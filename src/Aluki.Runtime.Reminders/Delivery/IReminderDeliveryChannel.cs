using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Reminders.Delivery;

/// <summary>Immutable delivery request (mirrors reminder-delivery-contract.yaml).</summary>
public sealed record ReminderDeliveryRequest(
    Guid ReminderId,
    Guid TenantId,
    Guid UserId,
    Guid? ContextId,
    DateTimeOffset ScheduledTimeUtc,
    int AttemptNumber,
    string ReminderText,
    string DeliveryChannel,
    string Timezone,
    string CorrelationId);

/// <summary>Outcome of a delivery attempt.</summary>
public sealed record ReminderDeliveryResult(
    string Status,                 // DeliveryStatus.*
    string? NotificationId = null,
    string? FailureCategory = null,
    string? FailureMessage = null);

/// <summary>
/// Abstraction over the notification channel that physically delivers a reminder
/// (in-app/WhatsApp/SMS/email). Implementations MUST be idempotent-friendly: the
/// caller dedupes on <c>(reminder_id, scheduled_time_utc, attempt_number)</c>, so
/// the channel only needs to attempt the send and classify the outcome.
/// </summary>
public interface IReminderDeliveryChannel
{
    Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Default delivery channel used until a real outbound channel (WhatsApp send,
/// in-app push) is wired. It records the delivery as successful and logs it; the
/// persisted delivery attempt + audit row are the durable record. Swapping in a
/// real channel later requires no change to the reminder engine.
/// </summary>
public sealed class LoggingReminderDeliveryChannel : IReminderDeliveryChannel
{
    private readonly ILogger<LoggingReminderDeliveryChannel> _logger;

    public LoggingReminderDeliveryChannel(ILogger<LoggingReminderDeliveryChannel> logger)
    {
        _logger = logger;
    }

    public Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "reminder.delivered (stub channel={Channel}) reminder_id={ReminderId} attempt={Attempt} correlation_id={CorrelationId}",
            request.DeliveryChannel, request.ReminderId, request.AttemptNumber, request.CorrelationId);

        var notificationId = $"stub-{request.ReminderId:N}-{request.AttemptNumber}";
        return Task.FromResult(new ReminderDeliveryResult(DeliveryStatus.Delivered, notificationId));
    }
}
