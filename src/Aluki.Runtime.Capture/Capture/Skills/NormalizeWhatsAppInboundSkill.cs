using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Maps a raw WhatsApp inbound envelope to the unified internal model and flags
/// unsupported content deterministically (FR-001, FR-010, FR-015).
/// </summary>
public sealed class NormalizeWhatsAppInboundSkill : CaptureSkill
{
    public const string SkillName = "capture.normalize_whatsapp_inbound";

    public override string Name => SkillName;

    public override Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var state = GetState(context);
        var envelope = state.Envelope;
        var payload = envelope.Payload;
        var type = (payload.Type ?? string.Empty).Trim().ToLowerInvariant();

        var isSupported = CapturePayloadType.IsSupported(type);
        var messageKind = isSupported ? type : CapturePayloadType.Unsupported;

        var rawEnvelopeRef = string.IsNullOrWhiteSpace(payload.RawEnvelopeRef)
            ? envelope.ProviderMessageId
            : payload.RawEnvelopeRef!;

        NormalizedMedia? media = null;
        if (isSupported && CapturePayloadType.IsMedia(type))
        {
            var source = payload.Media;
            media = new NormalizedMedia(
                MediaType: NormalizeMediaType(source?.MediaType, type),
                ContentType: string.IsNullOrWhiteSpace(source?.ContentType)
                    ? "application/octet-stream"
                    : source!.ContentType!,
                ProviderMediaId: source?.ProviderMediaId,
                MediaRefUri: source?.MediaRefUri,
                ByteLength: source?.ByteLength);
        }

        var normalized = new NormalizedCaptureMessage(
            ProviderMessageId: envelope.ProviderMessageId,
            SourceChannel: state.SourceChannel,
            SenderExternalId: envelope.Sender.ExternalUserId,
            MessageKind: messageKind,
            IsSupported: isSupported,
            MessageText: type is CapturePayloadType.Text or CapturePayloadType.Forwarded or CapturePayloadType.Document
                ? payload.Text
                : null,
            ForwardedFromRef: type == CapturePayloadType.Forwarded ? payload.Forwarded?.OriginalSenderRef : null,
            Media: media,
            RawEnvelopeRef: rawEnvelopeRef,
            ReceivedAtUtc: envelope.OccurredAtUtc);

        state.Normalized = normalized;
        return Task.FromResult(Ok(state));
    }

    private static string NormalizeMediaType(string? declared, string payloadType)
    {
        var value = (declared ?? string.Empty).Trim().ToLowerInvariant();
        return CapturePayloadType.IsMedia(value) ? value : payloadType;
    }
}
