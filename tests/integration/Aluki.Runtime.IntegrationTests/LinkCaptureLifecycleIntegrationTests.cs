using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Aluki.Runtime.Host.Skills.LinkCapture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-009A link capture lifecycle integration tests: create, idempotency, upsert
/// merge, confirmation resolve/repeat/expired, and recall scope isolation.
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class LinkCaptureLifecycleIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public LinkCaptureLifecycleIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IServiceProvider BuildRootProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] = _fixture.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        // Register stubs before AddLinkCapture so that AddHttpClient's TryAdd does
        // not overwrite our NullHttpClientFactory, and the policy is ours from the start.
        services.AddSingleton<IHttpClientFactory, NullHttpClientFactory>();
        services.AddSingleton<ILinkEnrichmentPolicyEvaluator, AlwaysAllowPolicyEvaluator>();

        services.AddLinkCapture(config);

        return services.BuildServiceProvider();
    }

    // Services are Scoped — create a scope for each call.
    private LinkCaptureService BuildCaptureService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<LinkCaptureService>();

    private LinkConfirmationService BuildConfirmationService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<LinkConfirmationService>();

    private LinkRecallService BuildRecallService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<LinkRecallService>();

    private static CaptureLinkRequest MakeCaptureRequest(
        SeededPrincipal seed,
        string messageText = "Check this out https://example.com",
        string? sourceMessageId = null) =>
        new(
            TenantId: seed.TenantId,
            ContextScopeId: seed.ContextId,
            PrincipalId: seed.UserId,
            SourceChannel: "whatsapp",
            SourceMessageId: sourceMessageId ?? Guid.NewGuid().ToString("N"),
            SourceTimestampUtc: DateTimeOffset.UtcNow,
            MessageText: messageText);

    private async Task InsertPendingConfirmationAsync(
        Guid tenantId, Guid contextScopeId,
        string sessionId, string conversationId,
        DateTimeOffset expiresAtUtc)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_pending_confirmations (
                id, tenant_id, context_scope_id, session_id, conversation_id,
                state, expires_at_utc, created_at_utc)
            values (
                gen_random_uuid(), @tenant_id, @context_scope_id, @session_id, @conversation_id,
                'pending', @expires_at_utc, now())
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_scope_id", contextScopeId);
        cmd.Parameters.AddWithValue("session_id", sessionId);
        cmd.Parameters.AddWithValue("conversation_id", conversationId);
        cmd.Parameters.AddWithValue("expires_at_utc", expiresAtUtc);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountLinkArtifactsAsync(Guid tenantId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*) from link_artifacts where tenant_id = @t and is_active = true;",
            conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<string?> GetConfirmationStateAsync(Guid tenantId, string sessionId, string conversationId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            select state from link_pending_confirmations
            where tenant_id = @t and session_id = @s and conversation_id = @c
            order by created_at_utc desc limit 1
            """, conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("s", sessionId);
        cmd.Parameters.AddWithValue("c", conversationId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    // ── Capture ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_new_link_returns_created_outcome()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        var result = await svc.CaptureAsync(
            MakeCaptureRequest(seed, "See https://example.com/article"),
            CancellationToken.None);

        Assert.Equal(LinkCaptureOutcome.Created, result.Outcome);
        Assert.Single(result.Artifacts);
        Assert.Equal("https://example.com/article", result.Artifacts[0].CanonicalUrl);
    }

    [Fact]
    public async Task Duplicate_same_source_message_returns_idempotent_noop()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();
        var sourceMessageId = Guid.NewGuid().ToString("N");
        var request = MakeCaptureRequest(seed, "https://example.com/page", sourceMessageId);

        var r1 = await svc.CaptureAsync(request, CancellationToken.None);
        var r2 = await svc.CaptureAsync(request, CancellationToken.None);

        Assert.Equal(LinkCaptureOutcome.Created, r1.Outcome);
        Assert.Equal(LinkCaptureOutcome.IdempotentNoop, r2.Outcome);
        Assert.Equal(1, await CountLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Same_url_different_source_returns_upsert_merged()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();
        var url = "https://example.com/shared";

        var r1 = await svc.CaptureAsync(
            MakeCaptureRequest(seed, url, Guid.NewGuid().ToString("N")),
            CancellationToken.None);
        var r2 = await svc.CaptureAsync(
            MakeCaptureRequest(seed, url, Guid.NewGuid().ToString("N")),
            CancellationToken.None);

        Assert.Equal(LinkCaptureOutcome.Created, r1.Outcome);
        Assert.Equal(LinkCaptureOutcome.UpsertMerged, r2.Outcome);
        // One artifact — second capture merges a new provenance ref onto the same artifact
        Assert.Equal(1, await CountLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Invalid_url_in_message_returns_invalid_url()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        var result = await svc.CaptureAsync(
            MakeCaptureRequest(seed, "No links here, just plain text."),
            CancellationToken.None);

        Assert.Equal(LinkCaptureOutcome.InvalidUrl, result.Outcome);
        Assert.Empty(result.Artifacts);
    }

    // ── Confirmation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmation_resolve_yes_returns_resolved_yes()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid().ToString("N");

        await InsertPendingConfirmationAsync(
            seed.TenantId, seed.ContextId,
            sessionId, conversationId,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10));

        var svc = BuildConfirmationService();
        var result = await svc.ResolveAsync(new ResolveConfirmationRequest(
            TenantId: seed.TenantId,
            ContextScopeId: seed.ContextId,
            SessionId: sessionId,
            ConversationId: conversationId,
            PrincipalId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString("N"),
            Reply: "yes"),
            CancellationToken.None);

        Assert.Equal(LinkConfirmationOutcome.ResolvedYes, result.Outcome);
        Assert.True(result.SideEffectsApplied);
        Assert.NotNull(result.ConfirmationId);

        var state = await GetConfirmationStateAsync(seed.TenantId, sessionId, conversationId);
        Assert.Equal(LinkConfirmationState.ResolvedYes, state);
    }

    [Fact]
    public async Task Confirmation_resolve_twice_second_is_already_resolved()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid().ToString("N");

        await InsertPendingConfirmationAsync(
            seed.TenantId, seed.ContextId,
            sessionId, conversationId,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10));

        var svc = BuildConfirmationService();
        var firstReply = new ResolveConfirmationRequest(
            TenantId: seed.TenantId,
            ContextScopeId: seed.ContextId,
            SessionId: sessionId,
            ConversationId: conversationId,
            PrincipalId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString("N"),
            Reply: "yes");

        var r1 = await svc.ResolveAsync(firstReply, CancellationToken.None);
        // Second attempt: state is now resolved_yes, GetActivePendingAsync finds nothing
        var r2 = await svc.ResolveAsync(firstReply with { SourceMessageId = Guid.NewGuid().ToString("N") },
            CancellationToken.None);

        Assert.Equal(LinkConfirmationOutcome.ResolvedYes, r1.Outcome);
        Assert.True(r1.SideEffectsApplied);
        Assert.Equal(LinkConfirmationOutcome.NoActivePending, r2.Outcome);
        Assert.False(r2.SideEffectsApplied);
    }

    [Fact]
    public async Task Expired_confirmation_returns_expired()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid().ToString("N");

        // Insert with a past expiry — still state='pending' so GetActivePendingAsync will find it,
        // then the service checks ExpiresAtUtc and returns Expired.
        await InsertPendingConfirmationAsync(
            seed.TenantId, seed.ContextId,
            sessionId, conversationId,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));

        var svc = BuildConfirmationService();
        var result = await svc.ResolveAsync(new ResolveConfirmationRequest(
            TenantId: seed.TenantId,
            ContextScopeId: seed.ContextId,
            SessionId: sessionId,
            ConversationId: conversationId,
            PrincipalId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString("N"),
            Reply: "yes"),
            CancellationToken.None);

        Assert.Equal(LinkConfirmationOutcome.Expired, result.Outcome);
        Assert.False(result.SideEffectsApplied);
    }

    // ── Recall ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recall_returns_captured_links()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var url = "https://recall-test.example.com/article";

        await BuildCaptureService().CaptureAsync(
            MakeCaptureRequest(seed, url),
            CancellationToken.None);

        var result = await BuildRecallService().RecallAsync(new RecallLinksRequest(
            TenantId: seed.TenantId,
            ContextScopeId: seed.ContextId,
            PrincipalId: seed.UserId,
            Query: "recall-test.example.com"),
            CancellationToken.None);

        Assert.NotEmpty(result.Results);
        Assert.Contains(result.Results, r => r.CanonicalUrl == url);
    }

    [Fact]
    public async Task Recall_requires_context_scope_isolation()
    {
        if (!_fixture.Available) return;

        // Two independent principals — different tenants and context scopes
        var seedA = await _fixture.SeedPrincipalAsync();
        var seedB = await _fixture.SeedPrincipalAsync();

        var url = "https://isolation-test.example.com/page";

        // Capture under tenant/context A
        await BuildCaptureService().CaptureAsync(
            MakeCaptureRequest(seedA, url),
            CancellationToken.None);

        // Recall under tenant/context B — different tenant → no results
        var result = await BuildRecallService().RecallAsync(new RecallLinksRequest(
            TenantId: seedB.TenantId,
            ContextScopeId: seedB.ContextId,
            PrincipalId: seedB.UserId,
            Query: "isolation-test.example.com"),
            CancellationToken.None);

        Assert.Empty(result.Results);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>Policy evaluator that always allows enrichment — safe for integration tests.</summary>
internal sealed class AlwaysAllowPolicyEvaluator : ILinkEnrichmentPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(string canonicalUrl) =>
        new(LinkPolicyDecision.Allow, "allow_all");
}

/// <summary>
/// Minimal IHttpClientFactory stub whose clients always throw. Enrichment is
/// fire-and-forget so test assertions are never affected by this.
/// </summary>
internal sealed class NullHttpClientFactory : IHttpClientFactory
{
    public System.Net.Http.HttpClient CreateClient(string name) =>
        new(new ThrowingHttpMessageHandler());

    private sealed class ThrowingHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Integration tests must not make outbound HTTP calls.");
    }
}
