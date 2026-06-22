namespace Aluki.Runtime.Abstractions.Skills.LinkCapture;

// ── Capture ──────────────────────────────────────────────────────────────────

public sealed record CaptureLinkRequest(
    Guid TenantId, Guid ContextScopeId, Guid PrincipalId,
    string SourceChannel, string SourceMessageId,
    DateTimeOffset SourceTimestampUtc, string MessageText,
    string? IdempotencyKey = null);

public sealed record CaptureLinkResponse(
    string Outcome,   // LinkCaptureOutcome.*
    IReadOnlyList<LinkArtifactSummary> Artifacts,
    PendingConfirmationSummary? PendingConfirmation = null);

public sealed record LinkArtifactSummary(
    Guid LinkArtifactId, string CanonicalUrl,
    string EnrichmentStatus, string? EnrichmentReason, string Description);

public sealed record PendingConfirmationSummary(
    Guid ConfirmationId, string State, DateTimeOffset ExpiresAtUtc);

// ── Confirmation ─────────────────────────────────────────────────────────────

public sealed record ResolveConfirmationRequest(
    Guid TenantId, Guid ContextScopeId,
    string SessionId, string ConversationId, Guid PrincipalId,
    string SourceMessageId, string Reply);  // "yes" or "no"

public sealed record ResolveConfirmationResponse(
    string Outcome,   // LinkConfirmationOutcome.*
    bool SideEffectsApplied,
    Guid? ConfirmationId = null,
    string? TerminalState = null);

// ── Recall ───────────────────────────────────────────────────────────────────

public sealed record RecallLinksRequest(
    Guid TenantId, Guid ContextScopeId, Guid PrincipalId,
    string Query, int Limit = 10);

public sealed record RecallLinksResponse(IReadOnlyList<LinkRecallItem> Results);

public sealed record LinkRecallItem(
    string CanonicalUrl, string Description,
    string EnrichmentStatus, string? EnrichmentReason,
    LinkProvenanceRef Provenance);

public sealed record LinkProvenanceRef(
    string SourceMessageId, string SourceChannel, DateTimeOffset CapturedAtUtc);
