using System.Text.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;

namespace Aluki.Runtime.Capture.Channels.WhatsApp;

/// <summary>
/// Maps a Meta WhatsApp Cloud API webhook payload into zero or more
/// <see cref="WhatsAppInboundEnvelope"/> values (one per inbound message).
/// Status/delivery notifications and non-message changes yield no envelopes.
/// Media payloads are mapped to metadata only (provider media id + content type);
/// binary download is handled asynchronously downstream.
/// </summary>
public static class MetaWebhookMapper
{
    public static IReadOnlyList<WhatsAppInboundEnvelope> Map(string json)
    {
        var envelopes = new List<WhatsAppInboundEnvelope>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return envelopes;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return envelopes;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value))
                {
                    continue;
                }

                if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                {
                    continue; // status/delivery updates have no "messages"
                }

                var contacts = value.TryGetProperty("contacts", out var c) ? c : default;

                foreach (var message in messages.EnumerateArray())
                {
                    var envelope = MapMessage(message, contacts);
                    if (envelope is not null)
                    {
                        envelopes.Add(envelope);
                    }
                }
            }
        }

        return envelopes;
    }

    private static WhatsAppInboundEnvelope? MapMessage(JsonElement message, JsonElement contacts)
    {
        var providerMessageId = GetString(message, "id");
        var from = GetString(message, "from");
        if (string.IsNullOrWhiteSpace(providerMessageId) || string.IsNullOrWhiteSpace(from))
        {
            return null;
        }

        var rawType = (GetString(message, "type") ?? string.Empty).ToLowerInvariant();
        var isForwarded = IsForwarded(message);

        string payloadType;
        string? text = null;
        MediaMetadata? media = null;
        ForwardedMetadata? forwarded = null;

        switch (rawType)
        {
            case "text":
                payloadType = isForwarded ? CapturePayloadType.Forwarded : CapturePayloadType.Text;
                text = message.TryGetProperty("text", out var t) ? GetString(t, "body") : null;
                if (isForwarded)
                {
                    forwarded = new ForwardedMetadata(OriginalSenderRef: null, OriginalMessageTimeUtc: null);
                }

                break;

            case "image":
                payloadType = CapturePayloadType.Image;
                media = MapMedia(message, "image", "image");
                break;

            case "audio":
            case "voice":
                payloadType = CapturePayloadType.Audio;
                media = MapMedia(message, rawType, "audio");
                break;

            case "document":
                payloadType = CapturePayloadType.Document;
                media = MapMedia(message, "document", "document");
                text = message.TryGetProperty("document", out var d) ? GetString(d, "caption") : null;
                break;

            default:
                // document, video, sticker, location, contacts, interactive, button, system, etc.
                payloadType = CapturePayloadType.Unsupported;
                break;
        }

        var payload = new Payload(
            Type: payloadType,
            Text: text,
            Media: media,
            Forwarded: forwarded,
            RawEnvelopeRef: providerMessageId);

        return new WhatsAppInboundEnvelope(
            ProviderMessageId: providerMessageId!,
            SourceChannel: "whatsapp",
            Sender: new SenderInfo(from!, ResolveDisplayName(contacts, from!)),
            ContextMetadata: null,
            Payload: payload,
            OccurredAtUtc: ResolveTimestamp(message),
            CorrelationId: null);
    }

    private static MediaMetadata MapMedia(JsonElement message, string property, string mediaType)
    {
        if (!message.TryGetProperty(property, out var m))
        {
            return new MediaMetadata(mediaType, "application/octet-stream", null, null, null);
        }

        return new MediaMetadata(
            MediaType: mediaType,
            ContentType: GetString(m, "mime_type") ?? "application/octet-stream",
            ProviderMediaId: GetString(m, "id"),
            MediaRefUri: null,
            ByteLength: null);
    }

    private static bool IsForwarded(JsonElement message)
    {
        if (!message.TryGetProperty("context", out var context))
        {
            return false;
        }

        return (context.TryGetProperty("forwarded", out var f) && f.ValueKind == JsonValueKind.True)
            || (context.TryGetProperty("frequently_forwarded", out var ff) && ff.ValueKind == JsonValueKind.True);
    }

    private static string? ResolveDisplayName(JsonElement contacts, string waId)
    {
        if (contacts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var contact in contacts.EnumerateArray())
        {
            if (GetString(contact, "wa_id") == waId &&
                contact.TryGetProperty("profile", out var profile))
            {
                return GetString(profile, "name");
            }
        }

        return null;
    }

    private static DateTimeOffset ResolveTimestamp(JsonElement message)
    {
        var ts = GetString(message, "timestamp");
        if (long.TryParse(ts, out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return DateTimeOffset.UtcNow;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
