using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.DelegatedReminders.Delivery;

/// <summary>
/// Delivers delegated reminders over WhatsApp.
/// RecipientIdentity format: "whatsapp:{phoneNumberId}:{waId}"
/// </summary>
public sealed class WhatsAppDelegatedReminderDeliveryChannel(
    IWhatsAppMessenger messenger,
    ILogger<WhatsAppDelegatedReminderDeliveryChannel> logger) : IDelegatedReminderDeliveryChannel
{
    public async Task<DelegatedDeliveryResult> DeliverAsync(DelegatedDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseWhatsAppIdentity(request.RecipientIdentity, out var phoneNumberId, out var waId))
        {
            logger.LogWarning(
                "delegated_reminder.delivery_failed: unsupported recipient identity format. id={Id} recipient={Recipient}",
                request.DelegatedReminderId, request.RecipientIdentity);
            return new DelegatedDeliveryResult(
                DelegatedDeliveryStatus.PermanentFailure,
                IsPermanentFailure: true,
                FailureCategory: "unsupported_identity",
                FailureMessage: $"Recipient identity is not a whatsapp address: {request.RecipientIdentity}");
        }

        try
        {
            // CancellationToken.None: user-facing delivery must not be skipped due to sweep timer lifecycle.
            await messenger.SendTextMessageAsync(phoneNumberId, waId, request.Content, CancellationToken.None);

            logger.LogInformation(
                "delegated_reminder.delivered id={Id} recipient={Recipient} attempt={Attempt} correlation_id={CorrelationId}",
                request.DelegatedReminderId, request.RecipientIdentity, request.AttemptNumber, request.CorrelationId);

            return new DelegatedDeliveryResult(
                DelegatedDeliveryStatus.Delivered,
                NotificationId: $"wa-{request.DelegatedReminderId:N}-{request.AttemptNumber}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "delegated_reminder.delivery_failed id={Id} recipient={Recipient} attempt={Attempt}",
                request.DelegatedReminderId, request.RecipientIdentity, request.AttemptNumber);
            return new DelegatedDeliveryResult(
                DelegatedDeliveryStatus.TransientFailure,
                IsPermanentFailure: false,
                FailureCategory: "send_error",
                FailureMessage: ex.Message);
        }
    }

    private static bool TryParseWhatsAppIdentity(string identity, out string phoneNumberId, out string waId)
    {
        phoneNumberId = string.Empty;
        waId = string.Empty;

        // Expected format: "whatsapp:{phoneNumberId}:{waId}"
        if (!identity.StartsWith("whatsapp:", StringComparison.Ordinal))
            return false;

        var parts = identity.Split(':', 3);
        if (parts.Length != 3 || string.IsNullOrEmpty(parts[1]) || string.IsNullOrEmpty(parts[2]))
            return false;

        phoneNumberId = parts[1];
        waId = parts[2];
        return true;
    }
}
