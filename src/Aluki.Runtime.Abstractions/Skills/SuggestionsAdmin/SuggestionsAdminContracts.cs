namespace Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;

public sealed record AdminQueueItem(
    Guid Id,
    Guid SuggestionId,
    Guid TenantId,
    Guid SubmitterUserId,
    string AdminStatus,
    string? AdminCategory,
    string? AdminPriority,
    string? SummaryExcerpt,
    DateTimeOffset? LastAdminActionAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListSuggestionsRequest(
    Guid TenantId,
    string ActorUserId,
    string ActorRole,
    string? StatusFilter = null,
    string? CategoryFilter = null,
    string? PriorityFilter = null,
    string? SearchText = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "created_at_utc",
    string SortDir = "desc");

public sealed record ListSuggestionsResponse(IReadOnlyList<AdminQueueItem> Items, int TotalCount, int Page, int PageSize);

public sealed record TriageSuggestionRequest(
    Guid TenantId,
    Guid SuggestionId,
    string ActorUserId,
    string ActorRole,
    string? NewStatus = null,
    string? NewCategory = null,
    string? NewPriority = null,
    string? ReasonCode = null);

public sealed record TriageSuggestionResponse(string Outcome, Guid? AuditId = null, string? DeniedReason = null);

public static class TriageOutcome
{
    public const string Updated = "updated";
    public const string Denied = "denied";
    public const string NotFound = "not_found";
    public const string InvalidTransition = "invalid_transition";
}

public sealed record RewardDecisionRequest(
    Guid TenantId,
    Guid SubmitterUserId,
    Guid SuggestionId,
    string RewardRuleType,
    string SourceEventId,
    decimal GrantAmount,
    string PolicyVersion,
    string CorrelationId);

public sealed record RewardDecisionResponse(
    string DecisionType,
    Guid? EntitlementId,
    string Reason,
    bool IdempotentReplay);
