namespace Aluki.Runtime.Abstractions.Conversation;

public sealed record ConversationTurn(
    string Body,
    string Direction,   // "inbound" | "outbound"
    DateTimeOffset CreatedAt);

public sealed record OutboundMessage(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string CorrelationMessageId,
    string Channel,
    string RecipientWaId,
    string Body,
    string Status,
    string? ErrorReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

public static class OutboundStatus
{
    public const string Delivered = "delivered";
    public const string ErrorFallback = "error_fallback";
    public const string Pending = "pending";
}
