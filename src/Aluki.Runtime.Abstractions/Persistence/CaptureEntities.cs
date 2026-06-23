namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>Row model for <c>inbound_message_event</c>.</summary>
public sealed record InboundMessageEventRow(
    Guid EventId,
    Guid TenantId,
    Guid ContextId,
    string SourceChannel,
    string ProviderMessageId,
    string? ProviderAccountId,
    string SenderExternalId,
    DateTimeOffset ReceivedAtUtc,
    string PayloadType,
    string RawEnvelopeRef,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc);

/// <summary>Row model for <c>unified_message_artifact</c>.</summary>
public sealed record UnifiedMessageArtifactRow(
    Guid MessageId,
    Guid TenantId,
    Guid ContextId,
    Guid CreatedByUserId,
    string SourceChannel,
    string ProviderMessageId,
    string MessageKind,
    string? MessageText,
    string? ForwardedFromRef,
    Guid ProvenanceEventId,
    DateTimeOffset? AcknowledgedAtUtc,
    string CaptureStatus,
    DateTimeOffset CreatedAtUtc);

/// <summary>Row model for <c>media_artifact</c>.</summary>
public sealed record MediaArtifactRow(
    Guid MediaId,
    Guid TenantId,
    Guid ContextId,
    Guid MessageId,
    string MediaType,
    string ContentType,
    string? ProviderMediaId,
    string? MediaRefUri,
    long? ByteLength,
    Guid ProvenanceEventId,
    DateTimeOffset CreatedAtUtc);

/// <summary>Row model for <c>idempotency_record</c>.</summary>
public sealed record IdempotencyRecordRow(
    Guid IdempotencyId,
    Guid TenantId,
    string SourceChannel,
    string ProviderMessageId,
    Guid? CanonicalMessageId,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int DuplicateCount);

/// <summary>Result of an idempotency claim attempt.</summary>
public sealed record IdempotencyClaimResult(
    bool IsNew,
    Guid IdempotencyId,
    Guid? CanonicalMessageId,
    int DuplicateCount);

/// <summary>Row model for <c>capture_audit_event</c>.</summary>
public sealed record CaptureAuditEventRow(
    Guid AuditId,
    Guid TenantId,
    Guid? ContextId,
    Guid? UserId,
    string SourceChannel,
    string EventName,
    string EventStatus,
    string CorrelationId,
    string? ProviderMessageId,
    int? AttemptNumber,
    string? FailureCategory,
    string? PayloadRef,
    DateTimeOffset OccurredAtUtc);
