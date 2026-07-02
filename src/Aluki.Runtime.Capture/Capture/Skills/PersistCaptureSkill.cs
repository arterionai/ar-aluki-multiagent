using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Transactionally persists the inbound event, canonical unified message, and
/// (for image/audio) the media artifact, then links the idempotency record to
/// the canonical message (FR-002, FR-003, FR-004, FR-013, SC-003).
/// Skips all writes for duplicate-suppressed deliveries.
/// </summary>
public sealed class PersistCaptureSkill : CaptureSkill
{
    public const string SkillName = "capture.persist_capture";

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
                PayloadType: normalized.MessageKind,
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
                MessageKind: normalized.MessageKind,
                MessageText: normalized.MessageText,
                ForwardedFromRef: normalized.ForwardedFromRef,
                ProvenanceEventId: eventId,
                AcknowledgedAtUtc: now,
                CaptureStatus: CapturePersistedStatus.Accepted,
                // Provider receipt time, not insert time: persistence now runs in
                // parallel with (or after) reply generation, and conversation history
                // orders by created_at_utc — the inbound row must always sort before
                // the outbound reply it triggered.
                CreatedAtUtc: normalized.ReceivedAtUtc),
            cancellationToken);

        if (normalized.Media is { } media)
        {
            var mediaId = Guid.NewGuid();
            await uow.Media.InsertAsync(
                new MediaArtifactRow(
                    MediaId: mediaId,
                    TenantId: principal.TenantId,
                    ContextId: principal.ContextId,
                    MessageId: messageId,
                    MediaType: media.MediaType,
                    ContentType: media.ContentType,
                    ProviderMediaId: media.ProviderMediaId,
                    MediaRefUri: media.MediaRefUri,
                    ByteLength: media.ByteLength,
                    ProvenanceEventId: eventId,
                    CreatedAtUtc: now),
                cancellationToken);

            // Queue async binary download only when the provider gave us a media id
            // and we don't already have the binary reference.
            if (!string.IsNullOrWhiteSpace(media.ProviderMediaId) && string.IsNullOrWhiteSpace(media.MediaRefUri))
            {
                state.PersistedMedia = new PersistedMediaInfo(
                    mediaId, messageId, media.ProviderMediaId!, media.ContentType);
            }
        }

        await uow.Idempotency.LinkCanonicalAsync(state.IdempotencyId!.Value, messageId, cancellationToken);

        state.ProvenanceEventId = eventId;
        state.CanonicalMessageId = messageId;
        return Ok(state);
    }
}
