namespace Aluki.Runtime.Abstractions.Channels.WhatsApp;

/// <summary>
/// Unified, channel-agnostic representation produced by the normalize skill and
/// consumed by the persistence/idempotency skills. Decouples downstream capture
/// logic from the raw provider envelope shape.
/// </summary>
public sealed record NormalizedCaptureMessage(
    string ProviderMessageId,
    string SourceChannel,
    string SenderExternalId,
    string MessageKind,
    bool IsSupported,
    string? MessageText,
    string? ForwardedFromRef,
    NormalizedMedia? Media,
    string RawEnvelopeRef,
    DateTimeOffset ReceivedAtUtc);

public sealed record NormalizedMedia(
    string MediaType,
    string ContentType,
    string? ProviderMediaId,
    string? MediaRefUri,
    long? ByteLength);

/// <summary>
/// Terminal capture pipeline outcome categories. Mapped to wire status/audit
/// events by the coordinator and endpoint.
/// </summary>
public enum CaptureOutcomeKind
{
    Accepted,
    DuplicateSuppressed,
    AcceptedUnsupported,
    ScopeDenied,
    InvalidPayload,
    RetryExhausted
}

/// <summary>
/// Result of running the capture pipeline for a single inbound event.
/// </summary>
public sealed record CaptureOutcome(
    CaptureOutcomeKind Kind,
    string CorrelationId,
    string? IdempotencyKey = null,
    Guid? CanonicalMessageId = null,
    string? AuditEvent = null,
    string? ErrorCode = null,
    string? Message = null,
    int AttemptCount = 0,
    string? FailureCategory = null);
