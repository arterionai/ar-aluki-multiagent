using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class SuggestionsAdminIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public SuggestionsAdminIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private IServiceProvider BuildRootProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSuggestionsAdmin(config);
        return services.BuildServiceProvider();
    }

    private SuggestionsAdminService BuildAdminService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<SuggestionsAdminService>();

    private RewardLedgerService BuildRewardService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<RewardLedgerService>();

    private async Task<Guid> SeedSuggestionAndQueueAsync(Guid tenantId, Guid userId, string status = "captured")
    {
        var suggestionId = Guid.NewGuid();
        await using var conn = await _fixture.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO suggestions (id, tenant_id, user_id, state, context_window_expires_at_utc, created_at_utc, updated_at_utc)
            VALUES (@id, @tenant_id, @user_id, 'captured', now() + interval '30 minutes', now(), now())
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", suggestionId);
            cmd.Parameters.AddWithValue("tenant_id", tenantId);
            cmd.Parameters.AddWithValue("user_id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd2 = new NpgsqlCommand(
            """
            INSERT INTO suggestion_admin_queue (id, suggestion_id, tenant_id, submitter_user_id, admin_status, created_at_utc, updated_at_utc)
            VALUES (gen_random_uuid(), @suggestion_id, @tenant_id, @user_id, @status, now(), now())
            """, conn))
        {
            cmd2.Parameters.AddWithValue("suggestion_id", suggestionId);
            cmd2.Parameters.AddWithValue("tenant_id", tenantId);
            cmd2.Parameters.AddWithValue("user_id", userId);
            cmd2.Parameters.AddWithValue("status", status);
            await cmd2.ExecuteNonQueryAsync();
        }

        return suggestionId;
    }

    private async Task<int> CountAuditRecordsAsync(Guid tenantId, Guid suggestionId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM suggestion_admin_audit_ledger WHERE tenant_id = @t AND suggestion_id = @s", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("s", suggestionId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task List_queue_returns_seeded_entries()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId);
        await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId);

        var result = await svc.ListQueueAsync(new ListSuggestionsRequest(
            TenantId: seed.TenantId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Reviewer), CancellationToken.None);

        Assert.True(result.TotalCount >= 2);
        Assert.True(result.Items.Count >= 2);
    }

    [Fact]
    public async Task Reviewer_can_move_to_under_review()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        var suggestionId = await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId, "captured");

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: seed.TenantId,
            SuggestionId: suggestionId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Reviewer,
            NewStatus: AdminStatus.UnderReview), CancellationToken.None);

        Assert.Equal(TriageOutcome.Updated, result.Outcome);
        Assert.NotNull(result.AuditId);
    }

    [Fact]
    public async Task Approver_can_accept()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        var suggestionId = await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId, "under_review");

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: seed.TenantId,
            SuggestionId: suggestionId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Approver,
            NewStatus: AdminStatus.Accepted), CancellationToken.None);

        Assert.Equal(TriageOutcome.Updated, result.Outcome);
        Assert.NotNull(result.AuditId);
    }

    [Fact]
    public async Task Auditor_cannot_mutate_returns_denied()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        var suggestionId = await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId, "captured");

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: seed.TenantId,
            SuggestionId: suggestionId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Auditor,
            NewStatus: AdminStatus.UnderReview), CancellationToken.None);

        Assert.Equal(TriageOutcome.Denied, result.Outcome);
        Assert.Equal("auditor_readonly", result.DeniedReason);
    }

    [Fact]
    public async Task Invalid_transition_returns_invalid_transition()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        var suggestionId = await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId, "accepted");

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: seed.TenantId,
            SuggestionId: suggestionId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Reviewer,
            NewStatus: AdminStatus.Archived), CancellationToken.None);

        Assert.Equal(TriageOutcome.InvalidTransition, result.Outcome);
    }

    [Fact]
    public async Task Audit_record_written_on_triage()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildAdminService();

        var suggestionId = await SeedSuggestionAndQueueAsync(seed.TenantId, seed.UserId, "captured");

        await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: seed.TenantId,
            SuggestionId: suggestionId,
            ActorUserId: seed.UserId.ToString(),
            ActorRole: AdminRole.Reviewer,
            NewStatus: AdminStatus.UnderReview), CancellationToken.None);

        var auditCount = await CountAuditRecordsAsync(seed.TenantId, suggestionId);
        Assert.True(auditCount >= 1);
    }

    [Fact]
    public async Task Reward_new_grant_returns_granted()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildRewardService();
        var suggestionId = Guid.NewGuid();

        var request = new RewardDecisionRequest(
            TenantId: seed.TenantId,
            SubmitterUserId: seed.UserId,
            SuggestionId: suggestionId,
            RewardRuleType: RewardRuleType.Base,
            SourceEventId: Guid.NewGuid().ToString(),
            GrantAmount: 10m,
            PolicyVersion: "1.0",
            CorrelationId: Guid.NewGuid().ToString());

        var result = await svc.ProcessRewardDecisionAsync(request, CancellationToken.None);

        Assert.Equal(DecisionType.Granted, result.DecisionType);
        Assert.False(result.IdempotentReplay);
        Assert.NotNull(result.EntitlementId);
    }

    [Fact]
    public async Task Reward_duplicate_replay_returns_duplicate()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildRewardService();
        var suggestionId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid().ToString();

        var request = new RewardDecisionRequest(
            TenantId: seed.TenantId,
            SubmitterUserId: seed.UserId,
            SuggestionId: suggestionId,
            RewardRuleType: RewardRuleType.Base,
            SourceEventId: sourceEventId,
            GrantAmount: 10m,
            PolicyVersion: "1.0",
            CorrelationId: Guid.NewGuid().ToString());

        await svc.ProcessRewardDecisionAsync(request, CancellationToken.None);

        var secondSvc = BuildRewardService();
        var second = await secondSvc.ProcessRewardDecisionAsync(
            request with { CorrelationId = Guid.NewGuid().ToString() }, CancellationToken.None);

        Assert.Equal(DecisionType.Duplicate, second.DecisionType);
        Assert.True(second.IdempotentReplay);
    }
}
