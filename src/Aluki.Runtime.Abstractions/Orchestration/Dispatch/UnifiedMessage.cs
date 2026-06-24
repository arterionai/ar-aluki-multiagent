namespace Aluki.Runtime.Abstractions.Orchestration.Dispatch;

/// <summary>
/// Channel-agnostic normalized message representation passed to the domain agent
/// dispatcher. Produced by the inbound normalization skill for each supported channel.
/// </summary>
public sealed record UnifiedMessage(
    string MessageId,
    string ChannelType,
    string? Text,
    IReadOnlyList<UnifiedMediaRef> MediaRefs,
    DateTimeOffset ReceivedAtUtc,
    string? CorrelationId = null,
    string? SenderExternalId = null,
    string? PhoneNumberId = null,
    IReadOnlyList<UnifiedContactRef>? ContactRefs = null);

/// <summary>Reference to a media artifact attached to a <see cref="UnifiedMessage"/>.</summary>
public sealed record UnifiedMediaRef(
    string MediaId,
    string MediaKind,
    string? MimeType,
    long? FileSizeBytes);

/// <summary>A contact card attached to a <see cref="UnifiedMessage"/>. <c>WaId</c> is canonical when present.</summary>
public sealed record UnifiedContactRef(
    string? DisplayName,
    string? WaId,
    IReadOnlyList<string> Phones);

public static class ChannelType
{
    public const string WhatsApp = "whatsapp";
    public const string Sms = "sms";
    public const string Email = "email";
    public const string Internal = "internal";
}
