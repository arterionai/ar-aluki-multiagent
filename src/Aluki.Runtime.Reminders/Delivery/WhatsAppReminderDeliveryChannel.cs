using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Reminders.Delivery;

/// <summary>
/// Delivers a fired reminder to the user via WhatsApp. Routing (phoneNumberId + waId)
/// is encoded in the <c>delivery_channel</c> field by <see cref="Dispatch.ReminderDomainAgent"/>
/// as <c>whatsapp:{phoneNumberId}:{waId}</c>. Falls back to a permanent failure when
/// the channel string cannot be parsed.
/// </summary>
public sealed class WhatsAppReminderDeliveryChannel : IReminderDeliveryChannel
{
    private readonly IWhatsAppMessenger _messenger;
    private readonly ILogger<WhatsAppReminderDeliveryChannel> _logger;

    public WhatsAppReminderDeliveryChannel(
        IWhatsAppMessenger messenger,
        ILogger<WhatsAppReminderDeliveryChannel> logger)
    {
        _messenger = messenger;
        _logger = logger;
    }

    public async Task<ReminderDeliveryResult> DeliverAsync(
        ReminderDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var (phoneNumberId, waId) = ParseChannel(request.DeliveryChannel);
        if (phoneNumberId is null || waId is null)
        {
            _logger.LogWarning(
                "WhatsAppReminderDeliveryChannel: unrecognized channel='{Channel}' reminder_id={ReminderId}",
                request.DeliveryChannel, request.ReminderId);
            return new ReminderDeliveryResult(
                DeliveryStatus.PermanentFailure,
                FailureCategory: DeliveryFailureCategory.InvalidRecipient,
                FailureMessage: $"Cannot parse WhatsApp routing from '{request.DeliveryChannel}'");
        }

        var body = $"🔔 Recordatorio: {request.ReminderText}";

        try
        {
            await _messenger.SendTextMessageAsync(phoneNumberId, waId, body, cancellationToken);

            _logger.LogInformation(
                "WhatsAppReminderDeliveryChannel: delivered reminder_id={ReminderId} attempt={Attempt}",
                request.ReminderId, request.AttemptNumber);

            return new ReminderDeliveryResult(
                DeliveryStatus.Delivered,
                NotificationId: $"wa-{request.ReminderId:N}-{request.AttemptNumber}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WhatsAppReminderDeliveryChannel: send failed reminder_id={ReminderId} attempt={Attempt}",
                request.ReminderId, request.AttemptNumber);

            return new ReminderDeliveryResult(
                DeliveryStatus.TransientFailure,
                FailureCategory: DeliveryFailureCategory.ServiceUnavailable,
                FailureMessage: ex.Message);
        }
    }

    private static (string? phoneNumberId, string? waId) ParseChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return (null, null);
        var parts = channel.Split(':', 3);
        if (parts.Length == 3
            && string.Equals(parts[0], "whatsapp", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(parts[1])
            && !string.IsNullOrWhiteSpace(parts[2]))
        {
            return (parts[1], parts[2]);
        }
        return (null, null);
    }
}
