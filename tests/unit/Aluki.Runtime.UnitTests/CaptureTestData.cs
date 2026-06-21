using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills;
using Aluki.Runtime.Capture;

namespace Aluki.Runtime.UnitTests;

internal static class CaptureTestData
{
    public static PrincipalContext Principal(string correlationId = "corr-1") => new(
        UserId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        ContextId: Guid.NewGuid(),
        Roles: ["MEMBER"],
        SourceChannel: "whatsapp",
        CorrelationId: correlationId);

    public static WhatsAppInboundEnvelope TextEnvelope(string providerMessageId = "wamid-1", string text = "hola") =>
        new(
            ProviderMessageId: providerMessageId,
            SourceChannel: "whatsapp",
            Sender: new SenderInfo("5215555555555", "Tester"),
            ContextMetadata: null,
            Payload: new Payload("text", text, Media: null, Forwarded: null, RawEnvelopeRef: "blob://raw/1"),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: null);

    public static WhatsAppInboundEnvelope MediaEnvelope(string mediaType) =>
        new(
            ProviderMessageId: "wamid-media",
            SourceChannel: "whatsapp",
            Sender: new SenderInfo("5215555555555", "Tester"),
            ContextMetadata: null,
            Payload: new Payload(
                mediaType,
                Text: null,
                Media: new MediaMetadata(mediaType, "image/jpeg", "mid-1", "blob://media/1", 1024),
                Forwarded: null,
                RawEnvelopeRef: "blob://raw/2"),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: null);

    public static WhatsAppInboundEnvelope UnsupportedEnvelope() =>
        new(
            ProviderMessageId: "wamid-unsupported",
            SourceChannel: "whatsapp",
            Sender: new SenderInfo("5215555555555", "Tester"),
            ContextMetadata: null,
            Payload: new Payload("sticker", Text: null, Media: null, Forwarded: null, RawEnvelopeRef: null),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: null);

    public static WhatsAppInboundEnvelope ForwardedEnvelope() =>
        new(
            ProviderMessageId: "wamid-fwd",
            SourceChannel: "whatsapp",
            Sender: new SenderInfo("5215555555555", "Tester"),
            ContextMetadata: null,
            Payload: new Payload(
                "forwarded",
                Text: "fwd body",
                Media: null,
                Forwarded: new ForwardedMetadata("origin-sender", DateTimeOffset.UtcNow.AddMinutes(-5)),
                RawEnvelopeRef: "blob://raw/3"),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: null);

    public static SkillExecutionContext Context(CapturePipelineState state) => new(
        state.Principal,
        SkillName: "test",
        Input: new Dictionary<string, object?> { [CaptureInputKeys.State] = state },
        RequestedAtUtc: DateTimeOffset.UtcNow);
}
