using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Aluki.Runtime.Host.Skills.YouTubeLinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-008B YouTube Link Save and Classification integration tests: capture,
/// idempotency, deduplication, invalid URLs, metadata fallback, and tenant
/// isolation. Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class YouTubeLinkCaptureIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public YouTubeLinkCaptureIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IServiceProvider BuildServices(
        IPrimaryYouTubeMetadataProvider? primary = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] = _fixture.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        // Register stub providers before AddYouTubeLinkCapture so that any
        // TryAdd registrations inside the extension do not overwrite them.
        services.AddSingleton<IPrimaryYouTubeMetadataProvider>(
            primary ?? new StubPrimaryProvider());
        services.AddSingleton<ISecondaryYouTubeMetadataProvider>(
            new StubSecondaryProvider());
        services.AddSingleton<IYouTubeClassificationProvider>(
            new StubClassificationProvider());

        services.AddYouTubeLinkCapture(config);

        return services.BuildServiceProvider();
    }

    private YouTubeLinkCaptureService BuildCaptureService(
        IPrimaryYouTubeMetadataProvider? primary = null) =>
        BuildServices(primary)
            .CreateScope().ServiceProvider
            .GetRequiredService<YouTubeLinkCaptureService>();

    private static CaptureYoutubeLinksRequest MakeRequest(
        SeededPrincipal seed,
        string text,
        string? messageId = null) =>
        new(
            TenantId: seed.TenantId,
            ContextId: seed.ContextId,
            PrincipalId: seed.UserId.ToString(),
            MessageId: messageId ?? Guid.NewGuid().ToString("N"),
            MessageText: text);

    private async Task<int> CountSavedLinkArtifactsAsync(Guid tenantId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*) from saved_link_artifacts where tenant_id = @t;",
            conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Valid_youtube_url_creates_artifact()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        var response = await svc.CaptureAsync(
            MakeRequest(seed, "Check https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        Assert.Single(response.Outcomes);
        var outcome = response.Outcomes[0];
        Assert.Equal(YouTubeLinkOutcome.Enriched, outcome.Outcome);
        Assert.Equal(YouTubeLinkPersistenceAction.Created, outcome.PersistenceAction);
        Assert.True(outcome.Persisted);
        Assert.Equal(1, await CountSavedLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Duplicate_same_message_id_refreshes_not_duplicates()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();
        var messageId = Guid.NewGuid().ToString("N");
        var request = MakeRequest(seed, "https://www.youtube.com/watch?v=dQw4w9WgXcQ", messageId);

        var r1 = await svc.CaptureAsync(request, CancellationToken.None);
        var r2 = await svc.CaptureAsync(request, CancellationToken.None);

        Assert.Equal(YouTubeLinkPersistenceAction.Created, r1.Outcomes[0].PersistenceAction);
        Assert.Equal(YouTubeLinkPersistenceAction.Refreshed, r2.Outcomes[0].PersistenceAction);
        Assert.Equal(1, await CountSavedLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Same_video_different_message_refreshes_artifact()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        var r1 = await svc.CaptureAsync(
            MakeRequest(seed, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        var r2 = await svc.CaptureAsync(
            MakeRequest(seed, "https://youtu.be/dQw4w9WgXcQ"),
            CancellationToken.None);

        Assert.Equal(YouTubeLinkPersistenceAction.Created, r1.Outcomes[0].PersistenceAction);
        Assert.Equal(YouTubeLinkPersistenceAction.Refreshed, r2.Outcomes[0].PersistenceAction);
        Assert.Equal(1, await CountSavedLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Invalid_youtube_url_returns_invalid_link()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        // A YouTube hostname without a valid video ID (channel URL).
        var response = await svc.CaptureAsync(
            MakeRequest(seed, "hello https://www.youtube.com/channel/UCabc"),
            CancellationToken.None);

        Assert.Single(response.Outcomes);
        var outcome = response.Outcomes[0];
        Assert.Equal(YouTubeLinkOutcome.InvalidLink, outcome.Outcome);
        Assert.False(outcome.Persisted);
        Assert.Equal(0, await CountSavedLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Non_youtube_url_produces_no_outcomes()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        var response = await svc.CaptureAsync(
            MakeRequest(seed, "https://example.com/page"),
            CancellationToken.None);

        Assert.Empty(response.Outcomes);
        Assert.Equal(0, await CountSavedLinkArtifactsAsync(seed.TenantId));
    }

    [Fact]
    public async Task Primary_fails_secondary_succeeds_returns_partial()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();

        // Override the secondary to return metadata, primary returns null.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] = _fixture.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IPrimaryYouTubeMetadataProvider>(new FailingPrimaryProvider());
        services.AddSingleton<ISecondaryYouTubeMetadataProvider>(new StubSecondaryWithDataProvider());
        services.AddSingleton<IYouTubeClassificationProvider>(new StubClassificationProvider());
        services.AddYouTubeLinkCapture(config);
        var svc = services.BuildServiceProvider()
            .CreateScope().ServiceProvider
            .GetRequiredService<YouTubeLinkCaptureService>();

        var response = await svc.CaptureAsync(
            MakeRequest(seed, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        Assert.Single(response.Outcomes);
        Assert.Equal(YouTubeLinkOutcome.Partial, response.Outcomes[0].Outcome);
    }

    [Fact]
    public async Task Both_providers_fail_returns_degraded()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService(primary: new FailingPrimaryProvider());

        var response = await svc.CaptureAsync(
            MakeRequest(seed, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        Assert.Single(response.Outcomes);
        var outcome = response.Outcomes[0];
        Assert.Equal(YouTubeLinkOutcome.Degraded, outcome.Outcome);
        Assert.True(outcome.Persisted);
    }

    [Fact]
    public async Task Different_tenants_get_isolated_artifacts()
    {
        if (!_fixture.Available) return;

        var seed1 = await _fixture.SeedPrincipalAsync();
        var seed2 = await _fixture.SeedPrincipalAsync();
        var svc = BuildCaptureService();

        await svc.CaptureAsync(
            MakeRequest(seed1, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        await svc.CaptureAsync(
            MakeRequest(seed2, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CancellationToken.None);

        // Each tenant should have exactly one artifact — they do not share rows.
        Assert.Equal(1, await CountSavedLinkArtifactsAsync(seed1.TenantId));
        Assert.Equal(1, await CountSavedLinkArtifactsAsync(seed2.TenantId));
    }
}

// ── Stub providers ─────────────────────────────────────────────────────────────

/// <summary>
/// Returns a fixed full metadata result. Simulates the happy-path primary provider.
/// </summary>
internal sealed class StubPrimaryProvider : IPrimaryYouTubeMetadataProvider
{
    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct) =>
        Task.FromResult<YouTubeMetadataResult?>(new YouTubeMetadataResult(
            Title: "Test Video",
            DescriptionSnippet: "A test video description.",
            ChannelName: "Test Channel",
            PublishedAt: DateTimeOffset.UtcNow.AddDays(-30),
            IsPartial: false));
}

/// <summary>
/// Returns null — simulates a secondary provider that is unavailable.
/// Used when both providers should fail (degraded path).
/// </summary>
internal sealed class StubSecondaryProvider : ISecondaryYouTubeMetadataProvider
{
    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct) =>
        Task.FromResult<YouTubeMetadataResult?>(null);
}

/// <summary>
/// Secondary provider that returns a partial metadata result.
/// Used for the primary-fails-secondary-succeeds test.
/// </summary>
internal sealed class StubSecondaryWithDataProvider : ISecondaryYouTubeMetadataProvider
{
    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct) =>
        Task.FromResult<YouTubeMetadataResult?>(new YouTubeMetadataResult(
            Title: "Partial Video",
            DescriptionSnippet: null,
            ChannelName: null,
            PublishedAt: null,
            IsPartial: true));
}

/// <summary>
/// Returns null — simulates a primary provider that fails or is unavailable.
/// </summary>
internal sealed class FailingPrimaryProvider : IPrimaryYouTubeMetadataProvider
{
    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct) =>
        Task.FromResult<YouTubeMetadataResult?>(null);
}

/// <summary>
/// Returns a low-confidence classification result. Simulates an AI classifier
/// that produces uncertain output.
/// </summary>
internal sealed class StubClassificationProvider : IYouTubeClassificationProvider
{
    public Task<YouTubeClassificationResult> ClassifyAsync(
        string videoId,
        string? title,
        string? description,
        CancellationToken ct) =>
        Task.FromResult(new YouTubeClassificationResult(
            Category: "general",
            Tags: ["stub"],
            Summary: "Stub classification summary.",
            ConfidenceLabel: YouTubeLinkConfidence.Low,
            CategoryUncertain: true,
            TagsUncertain: true,
            SummaryUncertain: true,
            ConfidenceScore: 0.50m));
}
