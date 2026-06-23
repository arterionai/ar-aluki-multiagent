using Aluki.Runtime.Abstractions.Channels.WhatsApp;

namespace Aluki.Runtime.Capture.Failure;

/// <summary>HTTP-shaped result for a capture outcome.</summary>
public sealed record CaptureHttpResult(int StatusCode, object Body);

/// <summary>
/// Maps terminal and non-terminal capture outcomes to contract-compliant
/// responses (FR-007, FR-009, FR-017, SC-005, SC-009). Retry-exhausted flows
/// produce a controlled <c>retry_exhausted</c> failure with the correlation id
/// and never report a false success.
/// </summary>
public static class CaptureFailureMapper
{
    public static CaptureHttpResult Map(CaptureOutcome outcome) => outcome.Kind switch
    {
        CaptureOutcomeKind.Accepted => Accepted(outcome, CaptureStatus.Accepted, CaptureAuditEvent.Accepted),
        CaptureOutcomeKind.DuplicateSuppressed => Accepted(
            outcome, CaptureStatus.DuplicateSuppressed, CaptureAuditEvent.DuplicateSuppressed),
        CaptureOutcomeKind.AcceptedUnsupported => Accepted(
            outcome, CaptureStatus.AcceptedUnsupported, CaptureAuditEvent.UnsupportedPayload),
        CaptureOutcomeKind.ScopeDenied => new CaptureHttpResult(
            StatusCodes.Forbidden,
            new CaptureError(
                Status: CaptureStatus.Rejected,
                CorrelationId: outcome.CorrelationId,
                Code: CaptureErrorCode.ScopeDenied,
                Message: outcome.Message ?? "Capture scope denied.",
                AuditEvent: CaptureAuditEvent.ScopeDenied)),
        CaptureOutcomeKind.InvalidPayload => new CaptureHttpResult(
            StatusCodes.BadRequest,
            new CaptureError(
                Status: CaptureStatus.Rejected,
                CorrelationId: outcome.CorrelationId,
                Code: CaptureErrorCode.InvalidPayload,
                Message: outcome.Message ?? "Invalid inbound payload.",
                AuditEvent: null)),
        CaptureOutcomeKind.RetryExhausted => new CaptureHttpResult(
            StatusCodes.InternalServerError,
            new CaptureError(
                Status: CaptureStatus.FailedTerminal,
                CorrelationId: outcome.CorrelationId,
                Code: CaptureErrorCode.RetryExhausted,
                Message: outcome.Message ?? "Capture failed after exhausting retries.",
                AuditEvent: CaptureAuditEvent.FailedTerminal)),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, "Unhandled capture outcome.")
    };

    private static CaptureHttpResult Accepted(CaptureOutcome outcome, string status, string auditEvent) =>
        new(
            StatusCodes.Accepted,
            new CaptureAck(
                Status: status,
                CorrelationId: outcome.CorrelationId,
                IdempotencyKey: outcome.IdempotencyKey ?? string.Empty,
                CanonicalMessageId: outcome.CanonicalMessageId,
                AuditEvent: auditEvent));

    private static class StatusCodes
    {
        public const int Accepted = 202;
        public const int BadRequest = 400;
        public const int Forbidden = 403;
        public const int InternalServerError = 500;
    }
}
