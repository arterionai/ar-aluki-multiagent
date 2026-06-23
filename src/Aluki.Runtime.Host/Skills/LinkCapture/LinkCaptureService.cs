using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

public sealed class LinkCaptureService
{
    private readonly ILinkCaptureRepository _repo;
    private readonly ILinkEnrichmentPolicyEvaluator _policy;
    private readonly LinkEnrichmentRunner _enrichmentRunner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LinkCaptureService> _logger;

    public LinkCaptureService(
        ILinkCaptureRepository repo,
        ILinkEnrichmentPolicyEvaluator policy,
        LinkEnrichmentRunner enrichmentRunner,
        IServiceScopeFactory scopeFactory,
        ILogger<LinkCaptureService> logger)
    {
        _repo = repo;
        _policy = policy;
        _enrichmentRunner = enrichmentRunner;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<CaptureLinkResponse> CaptureAsync(CaptureLinkRequest request, CancellationToken ct)
    {
        // 1. Validate
        if (request.TenantId == Guid.Empty ||
            request.ContextScopeId == Guid.Empty ||
            request.PrincipalId == Guid.Empty ||
            string.IsNullOrEmpty(request.SourceChannel) ||
            string.IsNullOrEmpty(request.SourceMessageId) ||
            request.MessageText is null)
        {
            return new CaptureLinkResponse(LinkCaptureOutcome.InvalidUrl, []);
        }

        // 2. Extract URLs
        var rawUrls = LinkCanonicalization.ExtractUrls(request.MessageText);
        if (rawUrls.Count == 0)
            return new CaptureLinkResponse(LinkCaptureOutcome.InvalidUrl, []);

        // 3. Process each URL
        var perUrlOutcomes = new List<(string Hash, string Outcome)>();

        foreach (var raw in rawUrls)
        {
            var canonical = LinkCanonicalization.TryCanonical(raw);
            if (canonical is null)
                continue;

            var hash = LinkCanonicalization.ComputeHash(canonical);
            var artifactId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var artifactRecord = new LinkArtifactRecord(
                Id: artifactId,
                TenantId: request.TenantId,
                ContextScopeId: request.ContextScopeId,
                CreatedByPrincipalId: request.PrincipalId,
                SourceChannel: request.SourceChannel,
                CanonicalUrl: canonical,
                UrlHash: hash,
                ContextLabel: null,
                EnrichmentStatus: LinkEnrichmentStatus.Pending,
                EnrichmentReasonCode: null,
                DescriptionText: string.Empty,
                SiteName: null,
                TitleText: null,
                FirstCapturedAtUtc: now,
                LastUpsertedAtUtc: now);

            var (resolvedArtifactId, isNew) = await _repo.UpsertArtifactAsync(artifactRecord, ct);

            var provenanceRecord = new LinkProvenanceRecord(
                Id: Guid.NewGuid(),
                TenantId: request.TenantId,
                LinkArtifactId: resolvedArtifactId,
                SourceMessageId: request.SourceMessageId,
                SourceChannel: request.SourceChannel,
                SourceTimestampUtc: request.SourceTimestampUtc,
                CapturedByPrincipalId: request.PrincipalId,
                ContextLabelSnapshot: null,
                CreatedAtUtc: now);

            var provenanceAdded = await _repo.TryAddProvenanceAsync(provenanceRecord, ct);

            string urlOutcome;
            string auditEventType;
            if (!isNew && !provenanceAdded)
            {
                urlOutcome = LinkCaptureOutcome.IdempotentNoop;
                auditEventType = LinkAuditEventType.IdempotentNoop;
            }
            else if (!isNew)
            {
                urlOutcome = LinkCaptureOutcome.UpsertMerged;
                auditEventType = LinkAuditEventType.UpsertMerged;
            }
            else
            {
                urlOutcome = LinkCaptureOutcome.Created;
                auditEventType = LinkAuditEventType.Captured;
            }

            await _repo.WriteAuditAsync(
                tenantId: request.TenantId,
                entityType: "link_artifact",
                entityId: resolvedArtifactId,
                eventType: auditEventType,
                actorType: LinkActorType.User,
                actorId: request.PrincipalId.ToString(),
                payload: new { canonical_url = canonical, url_hash = hash, source_message_id = request.SourceMessageId, outcome = urlOutcome },
                ct: ct);

            perUrlOutcomes.Add((hash, urlOutcome));

            // Fire-and-forget enrichment for newly created artifacts.
            // A dedicated scope is created so the background work outlives the request scope.
            if (isNew)
            {
                var tenantId = request.TenantId;
                var capturedArtifactId = resolvedArtifactId;
                var capturedCanonical = canonical;
                var scopeFactory = _scopeFactory;
                var policy = _policy;         // singleton — safe to capture directly
                var logger = _logger;         // singleton — safe to capture directly

                _ = Task.Run(async () =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var repo = scope.ServiceProvider.GetRequiredService<ILinkCaptureRepository>();
                    var runner = scope.ServiceProvider.GetRequiredService<LinkEnrichmentRunner>();

                    try
                    {
                        var policyResult = policy.Evaluate(capturedCanonical);
                        var host = new Uri(capturedCanonical).Host;

                        await repo.RecordPolicyDecisionAsync(
                            tenantId, capturedArtifactId,
                            policyResult.Decision, policyResult.ReasonCode, host,
                            CancellationToken.None);

                        if (policyResult.Decision == LinkPolicyDecision.Block)
                        {
                            await repo.UpdateEnrichmentAsync(
                                capturedArtifactId,
                                LinkEnrichmentStatus.PolicyBlocked,
                                policyResult.ReasonCode,
                                string.Empty, null, null,
                                CancellationToken.None);
                            return;
                        }

                        var startedAt = DateTimeOffset.UtcNow;
                        var attemptId = await repo.RecordEnrichmentAttemptAsync(
                            tenantId, capturedArtifactId, 1, startedAt, CancellationToken.None);

                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                            var fetchResult = await runner.FetchAsync(capturedCanonical, cts.Token);
                            var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

                            if (fetchResult is not null)
                            {
                                await repo.CompleteEnrichmentAttemptAsync(
                                    attemptId, "success", null, durationMs, DateTimeOffset.UtcNow, CancellationToken.None);

                                await repo.UpdateEnrichmentAsync(
                                    capturedArtifactId,
                                    LinkEnrichmentStatus.Enriched,
                                    null,
                                    fetchResult.Description,
                                    fetchResult.SiteName,
                                    fetchResult.Title,
                                    CancellationToken.None);

                                await repo.WriteAuditAsync(
                                    tenantId, "link_artifact", capturedArtifactId,
                                    LinkAuditEventType.EnrichmentSucceeded,
                                    LinkActorType.System, null,
                                    new { title = fetchResult.Title, site_name = fetchResult.SiteName },
                                    CancellationToken.None);
                            }
                            else
                            {
                                await repo.CompleteEnrichmentAttemptAsync(
                                    attemptId, "failed", null, durationMs, DateTimeOffset.UtcNow, CancellationToken.None);

                                await repo.UpdateEnrichmentAsync(
                                    capturedArtifactId,
                                    LinkEnrichmentStatus.Failed,
                                    null,
                                    string.Empty, null, null,
                                    CancellationToken.None);

                                await repo.WriteAuditAsync(
                                    tenantId, "link_artifact", capturedArtifactId,
                                    LinkAuditEventType.EnrichmentFailed,
                                    LinkActorType.System, null,
                                    new { url = capturedCanonical },
                                    CancellationToken.None);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                            await repo.CompleteEnrichmentAttemptAsync(
                                attemptId, "timeout", null, durationMs, DateTimeOffset.UtcNow, CancellationToken.None);

                            await repo.UpdateEnrichmentAsync(
                                capturedArtifactId,
                                LinkEnrichmentStatus.Timeout,
                                null,
                                string.Empty, null, null,
                                CancellationToken.None);

                            await repo.WriteAuditAsync(
                                tenantId, "link_artifact", capturedArtifactId,
                                LinkAuditEventType.EnrichmentTimeout,
                                LinkActorType.System, null,
                                new { url = capturedCanonical },
                                CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Enrichment background task failed for artifact {ArtifactId}", capturedArtifactId);
                    }
                });
            }
        }

        if (perUrlOutcomes.Count == 0)
            return new CaptureLinkResponse(LinkCaptureOutcome.InvalidUrl, []);

        // 4. Build summaries from fresh DB reads
        var summaries = new List<LinkArtifactSummary>();
        foreach (var (hash, _) in perUrlOutcomes)
        {
            var fresh = await _repo.GetArtifactByHashAsync(request.TenantId, hash, ct);
            if (fresh is null) continue;
            summaries.Add(new LinkArtifactSummary(
                LinkArtifactId: fresh.Id,
                CanonicalUrl: fresh.CanonicalUrl,
                EnrichmentStatus: fresh.EnrichmentStatus,
                EnrichmentReason: fresh.EnrichmentReasonCode,
                Description: fresh.DescriptionText));
        }

        // 5. Overall outcome
        string overallOutcome;
        if (perUrlOutcomes.Any(x => x.Outcome == LinkCaptureOutcome.Created))
            overallOutcome = LinkCaptureOutcome.Created;
        else if (perUrlOutcomes.Any(x => x.Outcome == LinkCaptureOutcome.UpsertMerged))
            overallOutcome = LinkCaptureOutcome.UpsertMerged;
        else
            overallOutcome = LinkCaptureOutcome.IdempotentNoop;

        return new CaptureLinkResponse(overallOutcome, summaries);
    }
}
