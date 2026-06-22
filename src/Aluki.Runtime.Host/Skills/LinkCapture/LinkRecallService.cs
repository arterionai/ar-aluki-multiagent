using Aluki.Runtime.Abstractions.Skills.LinkCapture;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

public sealed class LinkRecallService
{
    private readonly ILinkCaptureRepository _repo;

    public LinkRecallService(ILinkCaptureRepository repo) => _repo = repo;

    public async Task<RecallLinksResponse> RecallAsync(RecallLinksRequest request, CancellationToken ct)
    {
        // 1. Validate
        if (request.TenantId == Guid.Empty ||
            request.ContextScopeId == Guid.Empty ||
            request.PrincipalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Query))
        {
            return new RecallLinksResponse([]);
        }

        var limit = request.Limit > 0 ? request.Limit : 10;

        // 2. Search
        var artifacts = await _repo.SearchArtifactsAsync(
            request.TenantId, request.ContextScopeId, request.Query, limit, ct);

        // 3. Build recall items
        var items = new List<LinkRecallItem>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            var provenance = await _repo.GetFirstProvenanceAsync(request.TenantId, artifact.Id, ct);

            var description = !string.IsNullOrWhiteSpace(artifact.DescriptionText)
                ? artifact.DescriptionText
                : $"Link captured from {artifact.SourceChannel}";

            var provenanceRef = provenance is not null
                ? new LinkProvenanceRef(provenance.SourceMessageId, provenance.SourceChannel, provenance.SourceTimestampUtc)
                : new LinkProvenanceRef(string.Empty, artifact.SourceChannel, artifact.FirstCapturedAtUtc);

            items.Add(new LinkRecallItem(
                CanonicalUrl: artifact.CanonicalUrl,
                Description: description,
                EnrichmentStatus: artifact.EnrichmentStatus,
                EnrichmentReason: artifact.EnrichmentReasonCode,
                Provenance: provenanceRef));
        }

        return new RecallLinksResponse(items);
    }
}
