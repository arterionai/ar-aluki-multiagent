using System.Text.Json.Serialization;

namespace Aluki.Runtime.Memory;

public static class MemoryIntent
{
    public const string NoteToStore = "note_to_store";
    public const string RecallQuery = "recall_query";
}

public static class MemoryStatus
{
    public const string Accepted = "accepted";
    public const string DuplicateSuppressed = "duplicate_suppressed";
    public const string GroundedResult = "grounded_result";
    public const string LowConfidence = "low_confidence";
    public const string NoResult = "no_result";
    public const string Denied = "denied";
}

public static class MemoryErrorCode
{
    public const string InvalidPayload = "invalid_payload";
    public const string ScopeDenied = "scope_denied";
}

public static class MemoryAuditEventName
{
    public const string NoteAccepted = "memory.note_accepted";
    public const string NoteDuplicateSuppressed = "memory.note_duplicate_suppressed";
    public const string ScopeDenied = "memory.scope_denied";
    public const string RecallGrounded = "memory.recall_grounded";
    public const string RecallLowConfidence = "memory.recall_low_confidence";
    public const string RecallNoResult = "memory.recall_no_result";
    public const string PersonLookup = "memory.person_lookup";
}

public sealed record PrincipalScope(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("roles")] string[]? Roles);

public sealed record MemoryInteractionRequest(
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("source_channel")] string? SourceChannel,
    [property: JsonPropertyName("principal")] PrincipalScope? Principal,
    [property: JsonPropertyName("input_text")] string? InputText,
    [property: JsonPropertyName("source_identity")] string? SourceIdentity,
    [property: JsonPropertyName("requested_at_utc")] DateTimeOffset? RequestedAtUtc);

public sealed record MemoryInteractionResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("memory_artifact")] MemoryArtifactAck? MemoryArtifact = null,
    [property: JsonPropertyName("recall")] RecallResult? Recall = null);

public sealed record MemoryArtifactAck(
    [property: JsonPropertyName("canonical_chain_id")] Guid CanonicalChainId,
    [property: JsonPropertyName("chain_version")] int ChainVersion,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey);

public sealed record RecallResult(
    [property: JsonPropertyName("confidence")] string? Confidence,
    [property: JsonPropertyName("clarification_question")] string? ClarificationQuestion,
    [property: JsonPropertyName("no_result_reason")] string? NoResultReason,
    [property: JsonPropertyName("topic_groups")] IReadOnlyList<TopicGroup> TopicGroups,
    [property: JsonPropertyName("claims")] IReadOnlyList<RecallClaim> Claims);

public sealed record TopicGroup(
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("artifact_ids")] IReadOnlyList<Guid> ArtifactIds);

public sealed record RecallClaim(
    [property: JsonPropertyName("claim_id")] string ClaimId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confirmation_status")] string? ConfirmationStatus,
    [property: JsonPropertyName("citations")] IReadOnlyList<Citation> Citations);

public sealed record Citation(
    [property: JsonPropertyName("memory_artifact_id")] Guid MemoryArtifactId,
    [property: JsonPropertyName("provenance_ref")] string ProvenanceRef);

public sealed record MemoryError(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
