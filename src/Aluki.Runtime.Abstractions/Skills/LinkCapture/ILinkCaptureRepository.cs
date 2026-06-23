namespace Aluki.Runtime.Abstractions.Skills.LinkCapture;

public sealed record LinkArtifactRecord(
    Guid Id, Guid TenantId, Guid ContextScopeId, Guid CreatedByPrincipalId,
    string SourceChannel, string CanonicalUrl, string UrlHash,
    string? ContextLabel, string EnrichmentStatus, string? EnrichmentReasonCode,
    string DescriptionText, string? SiteName, string? TitleText,
    DateTimeOffset FirstCapturedAtUtc, DateTimeOffset LastUpsertedAtUtc);

public sealed record LinkProvenanceRecord(
    Guid Id, Guid TenantId, Guid LinkArtifactId,
    string SourceMessageId, string SourceChannel, DateTimeOffset SourceTimestampUtc,
    Guid CapturedByPrincipalId, string? ContextLabelSnapshot, DateTimeOffset CreatedAtUtc);

public sealed record PendingConfirmationRecord(
    Guid Id, Guid TenantId, Guid ContextScopeId,
    string SessionId, string ConversationId, Guid? SubjectLinkArtifactId,
    string State, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ResolvedAtUtc, Guid? ResolvedByPrincipalId,
    string? ResolveMessageId, string? ResolveCause, DateTimeOffset CreatedAtUtc);

public interface ILinkCaptureRepository
{
    // Returns (artifactId, isNew) — isNew=false means upsert_merged
    Task<(Guid ArtifactId, bool IsNew)> UpsertArtifactAsync(LinkArtifactRecord record, CancellationToken ct);

    // Returns false if provenance ref already exists (idempotent_noop for that source message)
    Task<bool> TryAddProvenanceAsync(LinkProvenanceRecord record, CancellationToken ct);

    Task<LinkArtifactRecord?> GetArtifactByHashAsync(Guid tenantId, string urlHash, CancellationToken ct);

    Task<IReadOnlyList<LinkArtifactRecord>> SearchArtifactsAsync(Guid tenantId, Guid contextScopeId, string query, int limit, CancellationToken ct);

    Task<LinkProvenanceRecord?> GetFirstProvenanceAsync(Guid tenantId, Guid artifactId, CancellationToken ct);

    Task UpdateEnrichmentAsync(Guid artifactId, string status, string? reasonCode, string descriptionText, string? siteName, string? titleText, CancellationToken ct);

    // ── Confirmation ─────────────────────────────────────────────────────────

    Task<Guid> CreateConfirmationAsync(PendingConfirmationRecord record, CancellationToken ct);

    // Returns null if no active pending exists
    Task<PendingConfirmationRecord?> GetActivePendingAsync(Guid tenantId, string sessionId, string conversationId, CancellationToken ct);

    // Atomic consume — returns true if this call won the race (first to resolve)
    Task<bool> TryConsumeConfirmationAsync(Guid confirmationId, string newState, Guid resolvedByPrincipalId, string resolveMessageId, string resolveCause, DateTimeOffset resolvedAt, CancellationToken ct);

    // Sweep: mark all expired pending confirmations
    Task<int> ExpireStaleConfirmationsAsync(DateTimeOffset now, CancellationToken ct);

    // ── Enrichment ───────────────────────────────────────────────────────────

    Task<Guid> RecordEnrichmentAttemptAsync(Guid tenantId, Guid artifactId, int attemptNo, DateTimeOffset startedAt, CancellationToken ct);
    Task CompleteEnrichmentAttemptAsync(Guid attemptId, string outcome, string? reasonCode, int durationMs, DateTimeOffset completedAt, CancellationToken ct);

    // ── Policy ───────────────────────────────────────────────────────────────

    Task RecordPolicyDecisionAsync(Guid tenantId, Guid artifactId, string decision, string reasonCode, string destinationHost, CancellationToken ct);

    // ── Audit ────────────────────────────────────────────────────────────────

    Task WriteAuditAsync(Guid tenantId, string entityType, Guid entityId, string eventType, string actorType, string? actorId, object payload, CancellationToken ct);
}
