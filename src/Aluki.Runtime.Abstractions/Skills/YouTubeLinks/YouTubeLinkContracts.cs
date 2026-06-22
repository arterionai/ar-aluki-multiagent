namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public sealed record CaptureYoutubeLinksRequest(
    Guid TenantId,
    Guid ContextId,
    string PrincipalId,
    string MessageId,
    string MessageText,
    ConfiguredProviders? Providers = null);

public sealed record ConfiguredProviders(string Primary, string Secondary);

public sealed record CaptureYoutubeLinksResponse(
    string MessageId,
    DateTimeOffset ProcessedAt,
    IReadOnlyList<LinkOutcome> Outcomes);

public sealed record LinkOutcome(
    string SourceUrl,
    string Outcome,
    bool Persisted,
    string ConfidenceLabel,
    string? CanonicalVideoId = null,
    string? CanonicalUrl = null,
    string? PersistenceAction = null,
    EnrichmentPayload? Enrichment = null,
    ClassificationPayload? Classification = null,
    string? UserMessage = null);

public sealed record EnrichmentPayload(
    string ProviderUsed,
    string MetadataCompleteness,
    string? Title = null,
    string? DescriptionSnippet = null,
    string? ChannelName = null,
    DateTimeOffset? PublishedAt = null);

public sealed record ClassificationPayload(
    string ConfidenceLabel,
    string? Category = null,
    IReadOnlyList<string>? Tags = null,
    string? Summary = null,
    IReadOnlyList<string>? UncertainFields = null);
