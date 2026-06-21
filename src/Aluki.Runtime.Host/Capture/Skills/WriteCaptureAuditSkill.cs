using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Skills;
using Aluki.Runtime.Host.Observability;

namespace Aluki.Runtime.Host.Capture.Skills;

/// <summary>
/// Emits the lifecycle audit event for accepted, duplicate_suppressed, and
/// unsupported outcomes (FR-008, FR-016, SC-005, SC-008). Runs inside the active
/// capture transaction so the audit record is atomic with persistence.
/// </summary>
public sealed class WriteCaptureAuditSkill : CaptureSkill
{
    public const string SkillName = "capture.write_capture_audit";

    public override string Name => SkillName;

    public override async Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var state = GetState(context);
        var uow = state.UnitOfWork
            ?? throw new InvalidOperationException("Audit skill requires an active unit of work.");

        var (eventName, status) = ResolveEvent(state);

        await uow.Audit.InsertAsync(
            new CaptureAuditEventRow(
                AuditId: Guid.NewGuid(),
                TenantId: state.Principal.TenantId,
                ContextId: state.Principal.ContextId,
                UserId: state.Principal.UserId,
                SourceChannel: state.SourceChannel,
                EventName: eventName,
                EventStatus: status,
                CorrelationId: state.CorrelationId,
                ProviderMessageId: state.Envelope.ProviderMessageId,
                AttemptNumber: state.AttemptNumber,
                FailureCategory: null,
                PayloadRef: state.Normalized?.RawEnvelopeRef,
                OccurredAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return Ok(state);
    }

    private static (string EventName, string Status) ResolveEvent(CapturePipelineState state)
    {
        if (state.IsDuplicate)
        {
            return (CaptureAuditEvent.DuplicateSuppressed, CaptureObservability.Status.Suppressed);
        }

        if (state.IsUnsupported)
        {
            return (CaptureAuditEvent.UnsupportedPayload, CaptureObservability.Status.Success);
        }

        return (CaptureAuditEvent.Accepted, CaptureObservability.Status.Success);
    }
}
