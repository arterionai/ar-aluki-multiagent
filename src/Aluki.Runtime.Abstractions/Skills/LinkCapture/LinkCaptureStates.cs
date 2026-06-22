namespace Aluki.Runtime.Abstractions.Skills.LinkCapture;

public static class LinkCaptureOutcome
{
    public const string Created = "created";
    public const string UpsertMerged = "upsert_merged";
    public const string IdempotentNoop = "idempotent_noop";
    public const string InvalidUrl = "invalid_url";
}

public static class LinkEnrichmentStatus
{
    public const string Pending = "pending";
    public const string Enriched = "enriched";
    public const string PolicyBlocked = "policy_blocked";
    public const string Timeout = "timeout";
    public const string Failed = "failed";
}

public static class LinkConfirmationState
{
    public const string Pending = "pending";
    public const string ResolvedYes = "resolved_yes";
    public const string ResolvedNo = "resolved_no";
    public const string Expired = "expired";
}

public static class LinkConfirmationOutcome
{
    public const string ResolvedYes = "resolved_yes";
    public const string ResolvedNo = "resolved_no";
    public const string Expired = "expired";
    public const string AlreadyResolved = "already_resolved";
    public const string NoActivePending = "no_active_pending";
}

public static class LinkPolicyDecision
{
    public const string Allow = "allow";
    public const string Block = "block";
}

public static class LinkAuditEventType
{
    public const string Captured = "captured";
    public const string UpsertMerged = "upsert_merged";
    public const string IdempotentNoop = "idempotent_noop";
    public const string ConfirmationResolved = "confirmation_resolved";
    public const string ConfirmationExpired = "confirmation_expired";
    public const string EnrichmentPolicyDecision = "enrichment_policy_decision";
    public const string EnrichmentTimeout = "enrichment_timeout";
    public const string EnrichmentFailed = "enrichment_failed";
    public const string EnrichmentSucceeded = "enrichment_succeeded";
}

public static class LinkActorType
{
    public const string User = "user";
    public const string System = "system";
}
