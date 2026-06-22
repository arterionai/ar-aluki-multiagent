namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public static class YouTubeLinkOutcome
{
    public const string Enriched    = "enriched";
    public const string Partial     = "partial";
    public const string Degraded    = "degraded";
    public const string InvalidLink = "invalid_link";
}

public static class YouTubeLinkPersistenceAction
{
    public const string Created   = "created";
    public const string Refreshed = "refreshed";
    public const string None      = "none";
}

public static class YouTubeLinkEnrichmentState
{
    public const string Enriched = "enriched";
    public const string Partial  = "partial";
    public const string Degraded = "degraded";
}

public static class YouTubeLinkProvider
{
    public const string Primary   = "primary";
    public const string Secondary = "secondary";
    public const string None      = "none";
}

public static class YouTubeLinkConfidence
{
    public const string High   = "high";
    public const string Medium = "medium";
    public const string Low    = "low";
}

public static class YouTubeLinkAuditEventType
{
    public const string DetectionAttempted             = "detection_attempted";
    public const string NormalizationSucceeded         = "normalization_succeeded";
    public const string NormalizationFailed            = "normalization_failed";
    public const string EnrichmentPrimaryAttempted     = "enrichment_primary_attempted";
    public const string EnrichmentSecondaryAttempted   = "enrichment_secondary_attempted";
    public const string PersistenceCreated             = "persistence_created";
    public const string PersistenceRefreshed           = "persistence_refreshed";
    public const string PersistenceSkippedInvalid      = "persistence_skipped_invalid";
    public const string UserOutcomeEmitted             = "user_outcome_emitted";
}

public static class MetadataCompleteness
{
    public const string Full    = "full";
    public const string Partial = "partial";
    public const string None    = "none";
}
