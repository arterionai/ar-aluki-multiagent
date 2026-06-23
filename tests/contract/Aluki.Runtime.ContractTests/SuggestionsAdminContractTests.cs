using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class SuggestionsAdminContractTests
{
    private static SuggestionsAdminService BuildAdminService(ISuggestionsAdminRepository? repo = null)
    {
        var r = repo ?? new StubSuggestionsAdminRepository();
        return new SuggestionsAdminService(r, NullLogger<SuggestionsAdminService>.Instance);
    }

    private static RewardLedgerService BuildRewardService(ISuggestionsAdminRepository? repo = null)
    {
        var r = repo ?? new StubSuggestionsAdminRepository();
        return new RewardLedgerService(r, NullLogger<RewardLedgerService>.Instance);
    }

    [Fact]
    public void AdminRole_constants_match_expected_values()
    {
        Assert.Equal("AdminReviewer", AdminRole.Reviewer);
        Assert.Equal("AdminApprover", AdminRole.Approver);
        Assert.Equal("AdminAuditor", AdminRole.Auditor);
        Assert.Equal("System", AdminRole.System);
    }

    [Fact]
    public void AdminStatus_constants_match_expected_values()
    {
        Assert.Equal("captured", AdminStatus.Captured);
        Assert.Equal("under_review", AdminStatus.UnderReview);
        Assert.Equal("accepted", AdminStatus.Accepted);
        Assert.Equal("rejected", AdminStatus.Rejected);
        Assert.Equal("archived", AdminStatus.Archived);
    }

    [Fact]
    public async Task Reviewer_can_move_captured_to_under_review()
    {
        var stub = new StubSuggestionsAdminRepository
        {
            QueueItemToReturn = new AdminQueueItem(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                AdminStatus.Captured, null, null, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var svc = BuildAdminService(stub);

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: stub.QueueItemToReturn.TenantId,
            SuggestionId: stub.QueueItemToReturn.SuggestionId,
            ActorUserId: "reviewer-user",
            ActorRole: AdminRole.Reviewer,
            NewStatus: AdminStatus.UnderReview), CancellationToken.None);

        Assert.Equal(TriageOutcome.Updated, result.Outcome);
    }

    [Fact]
    public async Task Auditor_triage_returns_denied_without_db()
    {
        var stub = new StubSuggestionsAdminRepository
        {
            QueueItemToReturn = new AdminQueueItem(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                AdminStatus.Captured, null, null, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var svc = BuildAdminService(stub);

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: stub.QueueItemToReturn.TenantId,
            SuggestionId: stub.QueueItemToReturn.SuggestionId,
            ActorUserId: "auditor-user",
            ActorRole: AdminRole.Auditor,
            NewStatus: AdminStatus.UnderReview), CancellationToken.None);

        Assert.Equal(TriageOutcome.Denied, result.Outcome);
        Assert.Equal("auditor_readonly", result.DeniedReason);
    }

    [Fact]
    public async Task Invalid_transition_reviewer_accept_returns_invalid_transition()
    {
        var stub = new StubSuggestionsAdminRepository
        {
            QueueItemToReturn = new AdminQueueItem(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                AdminStatus.Captured, null, null, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var svc = BuildAdminService(stub);

        var result = await svc.TriageAsync(new TriageSuggestionRequest(
            TenantId: stub.QueueItemToReturn.TenantId,
            SuggestionId: stub.QueueItemToReturn.SuggestionId,
            ActorUserId: "reviewer-user",
            ActorRole: AdminRole.Reviewer,
            NewStatus: AdminStatus.Accepted), CancellationToken.None);

        Assert.Equal(TriageOutcome.InvalidTransition, result.Outcome);
    }

    [Fact]
    public async Task Reward_idempotency_key_is_stable_and_duplicate_returns_correct_outcome()
    {
        var entitlementId = Guid.NewGuid();
        var stub = new StubSuggestionsAdminRepository
        {
            UpsertResult = (entitlementId, false, false)
        };
        var svc = BuildRewardService(stub);

        var request = new RewardDecisionRequest(
            TenantId: Guid.NewGuid(),
            SubmitterUserId: Guid.NewGuid(),
            SuggestionId: Guid.NewGuid(),
            RewardRuleType: RewardRuleType.Base,
            SourceEventId: "evt-001",
            GrantAmount: 5m,
            PolicyVersion: "1.0",
            CorrelationId: "corr-001");

        var result = await svc.ProcessRewardDecisionAsync(request, CancellationToken.None);

        Assert.Equal(DecisionType.Duplicate, result.DecisionType);
        Assert.True(result.IdempotentReplay);
        Assert.Equal(entitlementId, result.EntitlementId);
    }

    [Fact]
    public async Task Reward_conflict_returns_conflict()
    {
        var stub = new StubSuggestionsAdminRepository
        {
            UpsertResult = (Guid.NewGuid(), false, true)
        };
        var svc = BuildRewardService(stub);

        var request = new RewardDecisionRequest(
            TenantId: Guid.NewGuid(),
            SubmitterUserId: Guid.NewGuid(),
            SuggestionId: Guid.NewGuid(),
            RewardRuleType: RewardRuleType.Base,
            SourceEventId: "evt-conflict",
            GrantAmount: 5m,
            PolicyVersion: "1.0",
            CorrelationId: "corr-002");

        var result = await svc.ProcessRewardDecisionAsync(request, CancellationToken.None);

        Assert.Equal(DecisionType.Conflict, result.DecisionType);
        Assert.False(result.IdempotentReplay);
        Assert.Null(result.EntitlementId);
    }

    [Fact]
    public async Task Reward_new_grant_returns_granted_outcome()
    {
        var stub = new StubSuggestionsAdminRepository
        {
            UpsertResult = (Guid.NewGuid(), true, false)
        };
        var svc = BuildRewardService(stub);

        var request = new RewardDecisionRequest(
            TenantId: Guid.NewGuid(),
            SubmitterUserId: Guid.NewGuid(),
            SuggestionId: Guid.NewGuid(),
            RewardRuleType: RewardRuleType.Quality,
            SourceEventId: "evt-new",
            GrantAmount: 15m,
            PolicyVersion: "1.0",
            CorrelationId: "corr-003");

        var result = await svc.ProcessRewardDecisionAsync(request, CancellationToken.None);

        Assert.Equal(DecisionType.Granted, result.DecisionType);
        Assert.False(result.IdempotentReplay);
        Assert.NotNull(result.EntitlementId);
    }

    [Fact]
    public void GrantStatus_constants_match_expected_values()
    {
        Assert.Equal("granted", GrantStatus.Granted);
        Assert.Equal("rejected", GrantStatus.Rejected);
        Assert.Equal("duplicate", GrantStatus.Duplicate);
        Assert.Equal("conflict", GrantStatus.Conflict);
        Assert.Equal("compensation", GrantStatus.Compensation);
    }
}

internal sealed class StubSuggestionsAdminRepository : ISuggestionsAdminRepository
{
    public AdminQueueItem? QueueItemToReturn { get; set; }
    public (Guid Id, bool WasNew, bool IsConflict) UpsertResult { get; set; } = (Guid.NewGuid(), true, false);

    public Task<AdminQueueItem> GetOrCreateQueueEntryAsync(Guid tenantId, Guid suggestionId, Guid submitterUserId, string? summaryExcerpt, CancellationToken ct)
    {
        var item = QueueItemToReturn ?? new AdminQueueItem(
            Guid.NewGuid(), suggestionId, tenantId, submitterUserId,
            AdminStatus.Captured, null, null, summaryExcerpt, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return Task.FromResult(item);
    }

    public Task<ListSuggestionsResponse> ListQueueAsync(ListSuggestionsRequest request, CancellationToken ct) =>
        Task.FromResult(new ListSuggestionsResponse([], 0, request.Page, request.PageSize));

    public Task<AdminQueueItem?> GetQueueItemAsync(Guid tenantId, Guid suggestionId, CancellationToken ct) =>
        Task.FromResult(QueueItemToReturn);

    public Task<Guid> AppendAuditAsync(Guid tenantId, Guid suggestionId, string actorUserId, string actorRole, string actionType, object? oldValue, object? newValue, string reasonCode, CancellationToken ct) =>
        Task.FromResult(Guid.NewGuid());

    public Task UpdateQueueItemAsync(Guid tenantId, Guid suggestionId, string? newStatus, string? newCategory, string? newPriority, string actorUserId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<(Guid EntitlementId, bool WasNew, bool IsConflict)> UpsertEntitlementAsync(Guid tenantId, Guid submitterUserId, Guid suggestionId, string rewardRuleType, string sourceEventId, decimal grantAmount, string grantStatus, string policyVersion, object ruleMetadata, string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(UpsertResult);

    public Task<Guid> AppendRewardDecisionAsync(Guid tenantId, string decisionType, string reason, object idempotencyBoundary, Guid? entitlementId, string correlationId, CancellationToken ct) =>
        Task.FromResult(Guid.NewGuid());

    public Task<Guid> CreateNotificationDeliveryAsync(Guid tenantId, Guid entitlementId, Guid submitterUserId, string templateId, CancellationToken ct) =>
        Task.FromResult(Guid.NewGuid());

    public Task<IReadOnlyList<(Guid DeliveryId, Guid EntitlementId, Guid SubmitterUserId, string TemplateId, int AttemptNo)>> ClaimDueNotificationsAsync(DateTimeOffset now, int batchSize, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(Guid, Guid, Guid, string, int)>>([]);

    public Task UpdateNotificationDeliveryAsync(Guid deliveryId, string newState, int newAttemptNo, DateTimeOffset? nextAttemptAtUtc, string? errorCode, string? errorMessage, DateTimeOffset? deadLetterAtUtc, bool operatorReplayRequired, CancellationToken ct) =>
        Task.CompletedTask;
}
