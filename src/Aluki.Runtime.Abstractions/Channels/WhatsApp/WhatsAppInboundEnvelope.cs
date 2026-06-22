using System.Text.Json.Serialization;

namespace Aluki.Runtime.Abstractions.Channels.WhatsApp;

public sealed record WhatsAppInboundEnvelope(
    [property: JsonPropertyName("provider_message_id")] string ProviderMessageId,
    [property: JsonPropertyName("source_channel")] string SourceChannel,
    [property: JsonPropertyName("sender")] SenderInfo Sender,
    [property: JsonPropertyName("context_metadata")] ContextMetadata? ContextMetadata,
    [property: JsonPropertyName("payload")] Payload Payload,
    [property: JsonPropertyName("occurred_at_utc")] DateTimeOffset OccurredAtUtc,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("phone_number_id")] string? PhoneNumberId = null
);

public sealed record SenderInfo(
    [property: JsonPropertyName("external_user_id")] string ExternalUserId,
    [property: JsonPropertyName("display_name")] string? DisplayName
);

public sealed record ContextMetadata(
    [property: JsonPropertyName("tenant_hint")] string? TenantHint,
    [property: JsonPropertyName("context_id")] string? ContextId
);

public sealed record Payload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("media")] MediaMetadata? Media,
    [property: JsonPropertyName("forwarded")] ForwardedMetadata? Forwarded,
    [property: JsonPropertyName("raw_envelope_ref")] string? RawEnvelopeRef
);

public sealed record MediaMetadata(
    [property: JsonPropertyName("media_type")] string? MediaType,
    [property: JsonPropertyName("content_type")] string? ContentType,
    [property: JsonPropertyName("provider_media_id")] string? ProviderMediaId,
    [property: JsonPropertyName("media_ref_uri")] string? MediaRefUri,
    [property: JsonPropertyName("byte_length")] long? ByteLength
);

public sealed record ForwardedMetadata(
    [property: JsonPropertyName("original_sender_ref")] string? OriginalSenderRef,
    [property: JsonPropertyName("original_message_time_utc")] DateTimeOffset? OriginalMessageTimeUtc
);