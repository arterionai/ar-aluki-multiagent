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
    public const string RetryScheduled = "capture.retry_scheduled";
    public const string FailedTerminal = "capture.failed_terminal";
}

public static class CapturePayloadType
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Audio = "audio";
    public const string Document = "document";
    public const string Forwarded = "forwarded";
    public const string Contact = "contact";
    public const string Unsupported = "unsupported";

    /// <summary>Media-bearing supported kinds whose binary can be downloaded.</summary>
    public static bool IsMedia(string? type) => type is Image or Audio or Document;

    public static bool IsSupported(string? type) => type is Text or Image or Audio or Document or Forwarded or Contact;

    public static bool IsKnown(string? type) => IsSupported(type) || type == Unsupported;
}

/// <summary>
/// Persisted capture_status values (see data-model.md). Distinct from the
/// acknowledgment <see cref="CaptureStatus"/> surfaced over the wire.
/// </summary>
public static class CapturePersistedStatus
{
    public const string Accepted = "accepted";
    public const string DuplicateSuppressed = "duplicate_suppressed";
    public const string Unsupported = "unsupported";
    public const string FailedTerminal = "failed_terminal";
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