using Microsoft.Extensions.Logging;
using Aluki.Runtime.DelegatedReminders;

namespace Aluki.Runtime.DelegatedReminders.Delivery;

/// <summary>Delivery request for a single delegated reminder attempt.</summary>
public sealed record DelegatedDeliveryRequest(
    Guid DelegatedReminderId,
    Guid TenantId,
    Guid SenderUserId,
    string SenderIdentity,
    string RecipientIdentity,
    string Content,
    DateTimeOffset DueTimeUtc,
    int AttemptNumber,
    string CorrelationId);

/// <summary>Outcome of a single delegated delivery attempt.</summary>
public sealed record DelegatedDeliveryResult(
    string Status,                      // DelegatedDeliveryStatus.*
    bool IsPermanentFailure = false,
    string? NotificationId = null,
    string? FailureCategory = null,
    string? FailureMessage = null);

/// <summary>
/// Abstraction over the channel that physically delivers a delegated reminder to
/// the recipient. Implementations MUST be idempotent-friendly; the caller
/// deduplicates on <c>(delegated_reminder_id, attempt_number)</c>.
/// </summary>
public interface IDelegatedReminderDeliveryChannel
{
    Task<DelegatedDeliveryResult> DeliverAsync(DelegatedDeliveryRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Stub delivery channel used until a real outbound WhatsApp channel is wired.
/// Records the delivery as successful and logs it; the persisted delivery
/// attempt and audit row are the durable record.
/// </summary>
public sealed class LoggingDelegatedReminderDeliveryChannel : IDelegatedReminderDeliveryChannel
{
    private readonly ILogger<LoggingDelegatedReminderDeliveryChannel> _logger;

    public LoggingDelegatedReminderDeliveryChannel(ILogger<LoggingDelegatedReminderDeliveryChannel> logger)
    {
        _logger = logger;
    }

    public Task<DelegatedDeliveryResult> DeliverAsync(DelegatedDeliveryRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "delegated_reminder.delivered (stub) id={Id} recipient={Recipient} attempt={Attempt} correlation_id={CorrelationId}",
            request.DelegatedReminderId, request.RecipientIdentity, request.AttemptNumber, request.CorrelationId);

        var notificationId = $"stub-{request.DelegatedReminderId:N}-{request.AttemptNumber}";
        return Task.FromResult(new DelegatedDeliveryResult(DelegatedDeliveryStatus.Delivered, NotificationId: notificationId));
    }
}
