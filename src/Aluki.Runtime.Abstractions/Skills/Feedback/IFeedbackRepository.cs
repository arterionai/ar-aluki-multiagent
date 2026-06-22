namespace Aluki.Runtime.Abstractions.Skills.Feedback;

public interface IFeedbackRepository
{
    // Returns (suggestionId, isNew). isNew=false if same messageId+hash within 5-min window (idempotent replay).
    Task<(Guid SuggestionId, bool IsNew)> UpsertSuggestionAsync(
        Guid tenantId, Guid userId,
        string? textContent, string? inboundMessageId, string? inboundPayloadHash,
        DateTimeOffset contextWindowExpiresAtUtc,
        CancellationToken ct);

    // Returns active (non-expired, non-archived) suggestion window for user, or null.
    Task<SuggestionRecord?> GetActiveSuggestionAsync(Guid tenantId, Guid userId, CancellationToken ct);

    // Increments attachment_count and returns updated count. Returns -1 if suggestion not found.
    Task<int> IncrementAttachmentCountAsync(Guid suggestionId, Guid tenantId, CancellationToken ct);

    Task<Guid> AddAttachmentAsync(
        Guid tenantId, Guid suggestionId,
        string attachmentType, string blobUri, string mimeType,
        long fileSizeBytes, string contentHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct);

    // Returns true if a suggestion with same messageId+hash already exists within 5 minutes.
    Task<bool> IsIdempotentDuplicateAsync(
        Guid tenantId, string messageId, string payloadHash, DateTimeOffset since, CancellationToken ct);

    // Transition suggestion state. Returns false if suggestion not found or transition invalid.
    Task<bool> TransitionStateAsync(
        Guid suggestionId, Guid tenantId,
        string newState, string actor, string reason, CancellationToken ct);

    // For archival sweep: returns suggestions where state='sent_user' and state_transitioned_at_utc < cutoff.
    Task<IReadOnlyList<SuggestionRecord>> GetEligibleForArchivalAsync(
        DateTimeOffset cutoff, int batchSize, CancellationToken ct);
}
