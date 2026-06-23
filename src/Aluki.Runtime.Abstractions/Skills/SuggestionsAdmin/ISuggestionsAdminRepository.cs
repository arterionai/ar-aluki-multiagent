namespace Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;

public interface ISuggestionsAdminRepository
{
    // Lazy-initializes queue entry if not present. Returns existing or newly created.
    Task<AdminQueueItem> GetOrCreateQueueEntryAsync(Guid tenantId, Guid suggestionId, Guid submitterUserId, string? summaryExcerpt, CancellationToken ct);

    Task<ListSuggestionsResponse> ListQueueAsync(ListSuggestionsRequest request, CancellationToken ct);

    Task<AdminQueueItem?> GetQueueItemAsync(Guid tenantId, Guid suggestionId, CancellationToken ct);

    // Returns auditId. record_hash = SHA256 of (tenant_id+suggestion_id+action_type+actor+old_value+new_value+sequence_no).
    Task<Guid> AppendAuditAsync(
        Guid tenantId, Guid suggestionId,
        string actorUserId, string actorRole, string actionType,
        object? oldValue, object? newValue, string reasonCode,
        CancellationToken ct);

    Task UpdateQueueItemAsync(
        Guid tenantId, Guid suggestionId,
        string? newStatus, string? newCategory, string? newPriority,
        string actorUserId,
        CancellationToken ct);

    // Returns (entitlementId, wasNew, isConflict). idempotency_key = SHA256(tenant+user+suggestion+rule+sourceEventId).
    Task<(Guid EntitlementId, bool WasNew, bool IsConflict)> UpsertEntitlementAsync(
        Guid tenantId, Guid submitterUserId, Guid suggestionId,
        string rewardRuleType, string sourceEventId,
        decimal grantAmount, string grantStatus,
        string policyVersion, object ruleMetadata,
        string idempotencyKey,
        CancellationToken ct);

    Task<Guid> AppendRewardDecisionAsync(
        Guid tenantId, string decisionType, string reason,
        object idempotencyBoundary, Guid? entitlementId, string correlationId,
        CancellationToken ct);

    Task<Guid> CreateNotificationDeliveryAsync(
        Guid tenantId, Guid entitlementId, Guid submitterUserId, string templateId,
        CancellationToken ct);

    // For reward notification sweep: claims due notifications (pending/retrying with next_attempt_at_utc <= now).
    Task<IReadOnlyList<(Guid DeliveryId, Guid EntitlementId, Guid SubmitterUserId, string TemplateId, int AttemptNo)>> ClaimDueNotificationsAsync(
        DateTimeOffset now, int batchSize, CancellationToken ct);

    Task UpdateNotificationDeliveryAsync(
        Guid deliveryId, string newState, int newAttemptNo,
        DateTimeOffset? nextAttemptAtUtc, string? errorCode, string? errorMessage,
        DateTimeOffset? deadLetterAtUtc, bool operatorReplayRequired,
        CancellationToken ct);
}
