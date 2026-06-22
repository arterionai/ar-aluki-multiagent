namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public sealed record SavedLinkArtifactRecord(
    Guid Id,
    Guid TenantId,
    Guid ContextId,
    string PrincipalId,
    string CanonicalVideoId,
    string CanonicalUrl,
    string OriginalSourceUrl,
    string Status,
    DateTimeOffset FirstCapturedAt,
    DateTimeOffset LastRefreshedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IYouTubeLinkRepository
{
    /// <summary>
    /// Upserts a saved link artifact by (tenant_id, canonical_video_id).
    /// Returns the persisted row id and whether this was a new insert.
    /// </summary>
    Task<(Guid Id, bool IsNew)> UpsertArtifactAsync(
        SavedLinkArtifactRecord record,
        CancellationToken ct);

    Task SaveEnrichmentAsync(
        Guid savedLinkId,
        Guid tenantId,
        string state,
        string providerUsed,
        string? title,
        string? description,
        string? channel,
        DateTimeOffset? publishedAt,
        string? errorCode,
        int? latencyMs,
        CancellationToken ct);

    Task SaveClassificationAsync(
        Guid savedLinkId,
        Guid tenantId,
        string? category,
        string[] tags,
        string? summary,
        string confidenceLabel,
        bool categoryUncertain,
        bool tagsUncertain,
        bool summaryUncertain,
        decimal? confidenceScore,
        CancellationToken ct);

    Task WriteAuditAsync(
        Guid tenantId,
        Guid contextId,
        string principalId,
        string? messageId,
        string? canonicalVideoId,
        string eventType,
        string outcomeCode,
        object? details,
        CancellationToken ct);
}
