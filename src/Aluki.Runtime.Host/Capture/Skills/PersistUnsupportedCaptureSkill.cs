using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Host.Capture.Skills;

/// <summary>
/// Accepted-but-unsupported fallback persistence (FR-010, FR-015). Persists a
/// minimal canonical artifact with unsupported classification, raw envelope
/// reference, scope, and provenance, without breaking session continuity.
/// </summary>
public sealed class PersistUnsupportedCaptureSkill : CaptureSkill
{
    public const string SkillName = "capture.persist_unsupported_capture";

    public override string Name => SkillName;

    public override async Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var state = GetState(context);
        if (state.IsDuplicate)
        {
            return Ok(state);
        }

        var uow = state.UnitOfWork
            ?? throw new InvalidOperationException("Persist skill requires an active unit of work.");
        var normalized = state.Normalized
            ?? throw new InvalidOperationException("Persist skill requires a normalized message.");
        var principal = state.Principal;
        var now = DateTimeOffset.UtcNow;

        var eventId = Guid.NewGuid();
        await uow.InboundEvents.InsertAsync(
            new InboundMessageEventRow(
                EventId: eventId,
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                SourceChannel: state.SourceChannel,
                ProviderMessageId: normalized.ProviderMessageId,
                ProviderAccountId: null,
                SenderExternalId: normalized.SenderExternalId,
                ReceivedAtUtc: normalized.ReceivedAtUtc,
                PayloadType: CapturePayloadType.Unsupported,
                RawEnvelopeRef: normalized.RawEnvelopeRef,
                CorrelationId: state.CorrelationId,
                CreatedAtUtc: now),
            cancellationToken);

        var messageId = Guid.NewGuid();
        await uow.Messages.InsertAsync(
            new UnifiedMessageArtifactRow(
                MessageId: messageId,
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                CreatedByUserId: principal.UserId,
                SourceChannel: state.SourceChannel,
                ProviderMessageId: normalized.ProviderMessageId,
                MessageKind: CapturePayloadType.Unsupported,
                MessageText: null,
                ForwardedFromRef: null,
                ProvenanceEventId: eventId,
                AcknowledgedAtUtc: now,
                CaptureStatus: CapturePersistedStatus.Unsupported,
                CreatedAtUtc: now),
            cancellationToken);

        await uow.Idempotency.LinkCanonicalAsync(state.IdempotencyId!.Value, messageId, cancellationToken);

        state.ProvenanceEventId = eventId;
        state.CanonicalMessageId = messageId;
        return Ok(state);
    }
}
