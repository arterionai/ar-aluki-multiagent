using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

/// <summary>
/// Orchestrates YouTube link capture: URL extraction → deduplication → upsert
/// → best-effort enrichment (primary then secondary) → classification → audit.
/// </summary>
public sealed class YouTubeLinkCaptureService
{
    private readonly IYouTubeLinkRepository _repo;
    private readonly IPrimaryYouTubeMetadataProvider _primaryProvider;
    private readonly ISecondaryYouTubeMetadataProvider _secondaryProvider;
    private readonly IYouTubeClassificationProvider _classificationProvider;
    private readonly ILogger<YouTubeLinkCaptureService> _logger;

    public YouTubeLinkCaptureService(
        IYouTubeLinkRepository repo,
        IPrimaryYouTubeMetadataProvider primaryProvider,
        ISecondaryYouTubeMetadataProvider secondaryProvider,
        IYouTubeClassificationProvider classificationProvider,
        ILogger<YouTubeLinkCaptureService> logger)
    {
        _repo = repo;
        _primaryProvider = primaryProvider;
        _secondaryProvider = secondaryProvider;
        _classificationProvider = classificationProvider;
        _logger = logger;
    }

    public async Task<CaptureYoutubeLinksResponse> CaptureAsync(
        CaptureYoutubeLinksRequest request, CancellationToken ct)
    {
        // ── Validation ────────────────────────────────────────────────────────
        if (request.TenantId == Guid.Empty || request.ContextId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.PrincipalId) ||
            string.IsNullOrWhiteSpace(request.MessageId) ||
            string.IsNullOrWhiteSpace(request.MessageText))
        {
            _logger.LogWarning(
                "CaptureAsync called with invalid request (TenantId={TenantId}, ContextId={ContextId}, " +
                "PrincipalId={PrincipalId}, MessageId={MessageId}).",
                request.TenantId, request.ContextId, request.PrincipalId, request.MessageId);
            return new CaptureYoutubeLinksResponse(request.MessageId, DateTimeOffset.UtcNow, []);
        }

        // ── URL extraction ────────────────────────────────────────────────────
        var urls = YouTubeUrlCanonicalizer.ExtractYoutubeUrls(request.MessageText);

        await _repo.WriteAuditAsync(
            request.TenantId, request.ContextId, request.PrincipalId,
            request.MessageId, canonicalVideoId: null,
            YouTubeLinkAuditEventType.DetectionAttempted,
            outcomeCode: "detected",
            details: new { url_count = urls.Count },
            ct);

        var outcomes = new List<LinkOutcome>();
        var processedVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in urls)
        {
            // ── Video ID extraction ───────────────────────────────────────────
            var videoId = YouTubeUrlCanonicalizer.TryExtractVideoId(url);

            if (videoId is null)
            {
                await _repo.WriteAuditAsync(
                    request.TenantId, request.ContextId, request.PrincipalId,
                    request.MessageId, canonicalVideoId: null,
                    YouTubeLinkAuditEventType.NormalizationFailed,
                    outcomeCode: YouTubeLinkOutcome.InvalidLink,
                    details: new { source_url = url },
                    ct);

                outcomes.Add(new LinkOutcome(
                    SourceUrl: url,
                    Outcome: YouTubeLinkOutcome.InvalidLink,
                    Persisted: false,
                    ConfidenceLabel: YouTubeLinkConfidence.Low,
                    UserMessage: "URL could not be parsed as a YouTube video link."));
                continue;
            }

            // ── Deduplication within message ──────────────────────────────────
            if (!processedVideoIds.Add(videoId))
            {
                _logger.LogDebug(
                    "Skipping duplicate videoId={VideoId} in message {MessageId}.",
                    videoId, request.MessageId);
                continue;
            }

            await _repo.WriteAuditAsync(
                request.TenantId, request.ContextId, request.PrincipalId,
                request.MessageId, videoId,
                YouTubeLinkAuditEventType.NormalizationSucceeded,
                outcomeCode: "normalised",
                details: new { source_url = url, canonical_video_id = videoId },
                ct);

            var canonicalUrl = YouTubeUrlCanonicalizer.ToCanonicalUrl(videoId);

            // ── Upsert artifact ───────────────────────────────────────────────
            var artifactRecord = new SavedLinkArtifactRecord(
                Id: Guid.NewGuid(),
                TenantId: request.TenantId,
                ContextId: request.ContextId,
                PrincipalId: request.PrincipalId,
                CanonicalVideoId: videoId,
                CanonicalUrl: canonicalUrl,
                OriginalSourceUrl: url,
                Status: "active",
                FirstCapturedAt: DateTimeOffset.UtcNow,
                LastRefreshedAt: DateTimeOffset.UtcNow,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow);

            var (artifactId, isNew) = await _repo.UpsertArtifactAsync(artifactRecord, ct);

            var persistenceAction = isNew
                ? YouTubeLinkPersistenceAction.Created
                : YouTubeLinkPersistenceAction.Refreshed;

            // ── Enrichment chain ──────────────────────────────────────────────
            YouTubeMetadataResult? enrichmentResult = null;
            var enrichmentState = YouTubeLinkEnrichmentState.Degraded;
            var providerUsed = YouTubeLinkProvider.None;

            // Try primary
            await _repo.WriteAuditAsync(
                request.TenantId, request.ContextId, request.PrincipalId,
                request.MessageId, videoId,
                YouTubeLinkAuditEventType.EnrichmentPrimaryAttempted,
                outcomeCode: "attempted",
                details: new { provider = YouTubeLinkProvider.Primary },
                ct);

            try
            {
                enrichmentResult = await _primaryProvider.FetchAsync(videoId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Primary metadata provider threw for videoId={VideoId}.", videoId);
            }

            if (enrichmentResult is not null)
            {
                enrichmentState = enrichmentResult.IsPartial
                    ? YouTubeLinkEnrichmentState.Partial
                    : YouTubeLinkEnrichmentState.Enriched;
                providerUsed = YouTubeLinkProvider.Primary;
            }
            else
            {
                // Try secondary
                await _repo.WriteAuditAsync(
                    request.TenantId, request.ContextId, request.PrincipalId,
                    request.MessageId, videoId,
                    YouTubeLinkAuditEventType.EnrichmentSecondaryAttempted,
                    outcomeCode: "attempted",
                    details: new { provider = YouTubeLinkProvider.Secondary },
                    ct);

                try
                {
                    enrichmentResult = await _secondaryProvider.FetchAsync(videoId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Secondary metadata provider threw for videoId={VideoId}.", videoId);
                }

                if (enrichmentResult is not null)
                {
                    enrichmentState = YouTubeLinkEnrichmentState.Partial;
                    providerUsed = YouTubeLinkProvider.Secondary;
                }
                // else: both null → degraded / none (already set above)
            }

            // Persist enrichment record
            await _repo.SaveEnrichmentAsync(
                artifactId, request.TenantId,
                enrichmentState, providerUsed,
                enrichmentResult?.Title,
                enrichmentResult?.DescriptionSnippet,
                enrichmentResult?.ChannelName,
                enrichmentResult?.PublishedAt,
                enrichmentResult?.ErrorCode,
                latencyMs: null,
                ct);

            // ── Classification ────────────────────────────────────────────────
            YouTubeClassificationResult classification;
            try
            {
                classification = await _classificationProvider.ClassifyAsync(
                    videoId, enrichmentResult?.Title, enrichmentResult?.DescriptionSnippet, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Classification provider threw for videoId={VideoId}. Using low-confidence stub.", videoId);
                classification = new YouTubeClassificationResult(
                    Category: null, Tags: [], Summary: null,
                    ConfidenceLabel: YouTubeLinkConfidence.Low,
                    CategoryUncertain: true, TagsUncertain: true, SummaryUncertain: true,
                    ConfidenceScore: null);
            }

            await _repo.SaveClassificationAsync(
                artifactId, request.TenantId,
                classification.Category,
                classification.Tags,
                classification.Summary,
                classification.ConfidenceLabel,
                classification.CategoryUncertain,
                classification.TagsUncertain,
                classification.SummaryUncertain,
                classification.ConfidenceScore,
                ct);

            // ── Determine user-facing outcome ─────────────────────────────────
            var outcomeLabel = enrichmentState switch
            {
                YouTubeLinkEnrichmentState.Enriched => YouTubeLinkOutcome.Enriched,
                YouTubeLinkEnrichmentState.Partial   => YouTubeLinkOutcome.Partial,
                _                                    => YouTubeLinkOutcome.Degraded,
            };

            // ── Persistence audit ─────────────────────────────────────────────
            var persistenceAuditType = isNew
                ? YouTubeLinkAuditEventType.PersistenceCreated
                : YouTubeLinkAuditEventType.PersistenceRefreshed;

            await _repo.WriteAuditAsync(
                request.TenantId, request.ContextId, request.PrincipalId,
                request.MessageId, videoId,
                persistenceAuditType,
                outcomeCode: persistenceAction,
                details: new { artifact_id = artifactId, canonical_url = canonicalUrl },
                ct);

            await _repo.WriteAuditAsync(
                request.TenantId, request.ContextId, request.PrincipalId,
                request.MessageId, videoId,
                YouTubeLinkAuditEventType.UserOutcomeEmitted,
                outcomeCode: outcomeLabel,
                details: new { enrichment_state = enrichmentState, provider_used = providerUsed },
                ct);

            // ── Build enrichment payload ──────────────────────────────────────
            var enrichmentPayload = new EnrichmentPayload(
                ProviderUsed: providerUsed,
                MetadataCompleteness: DetermineMetadataCompleteness(enrichmentResult),
                Title: enrichmentResult?.Title,
                DescriptionSnippet: enrichmentResult?.DescriptionSnippet,
                ChannelName: enrichmentResult?.ChannelName,
                PublishedAt: enrichmentResult?.PublishedAt);

            // ── Build classification payload ──────────────────────────────────
            var uncertainFields = BuildUncertainFields(classification);
            var classificationPayload = new ClassificationPayload(
                ConfidenceLabel: classification.ConfidenceLabel,
                Category: classification.Category,
                Tags: classification.Tags.Length > 0 ? classification.Tags : null,
                Summary: classification.Summary,
                UncertainFields: uncertainFields.Count > 0 ? uncertainFields : null);

            outcomes.Add(new LinkOutcome(
                SourceUrl: url,
                Outcome: outcomeLabel,
                Persisted: true,
                ConfidenceLabel: classification.ConfidenceLabel,
                CanonicalVideoId: videoId,
                CanonicalUrl: canonicalUrl,
                PersistenceAction: persistenceAction,
                Enrichment: enrichmentPayload,
                Classification: classificationPayload));
        }

        return new CaptureYoutubeLinksResponse(request.MessageId, DateTimeOffset.UtcNow, outcomes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DetermineMetadataCompleteness(YouTubeMetadataResult? result)
    {
        if (result is null)
            return MetadataCompleteness.None;
        if (!result.IsPartial && result.Title is not null)
            return MetadataCompleteness.Full;
        if (result.IsPartial)
            return MetadataCompleteness.Partial;
        return MetadataCompleteness.None;
    }

    private static IReadOnlyList<string> BuildUncertainFields(YouTubeClassificationResult c)
    {
        var fields = new List<string>();
        if (c.CategoryUncertain) fields.Add("category");
        if (c.TagsUncertain)     fields.Add("tags");
        if (c.SummaryUncertain)  fields.Add("summary");
        return fields;
    }
}
