using System.Text.Json.Serialization;

namespace Aluki.Runtime.Abstractions.Channels.WhatsApp;

public static class CaptureStatus
{
    public const string Accepted = "accepted";
    public const string DuplicateSuppressed = "duplicate_suppressed";
    public const string AcceptedUnsupported = "accepted_unsupported";
    public const string Rejected = "rejected";
    public const string FailedTerminal = "failed_terminal";
}

public static class CaptureErrorCode
{
    public const string ScopeDenied = "scope_denied";
    public const string InvalidPayload = "invalid_payload";
    public const string RetryExhausted = "retry_exhausted";
}

public static class CaptureAuditEvent
{
    public const string Accepted = "capture.accepted";
    public const string DuplicateSuppressed = "capture.duplicate_suppressed";
    public const string UnsupportedPayload = "capture.unsupported_payload";
    public const string ScopeDenied = "capture.scope_denied";
    public const string FailedTerminal = "capture.failed_terminal";
}

public sealed record CaptureAck(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey,
    [property: JsonPropertyName("canonical_message_id")] Guid? CanonicalMessageId,
    [property: JsonPropertyName("audit_event")] string AuditEvent
);

public sealed record CaptureError(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("audit_event")] string? AuditEvent
);