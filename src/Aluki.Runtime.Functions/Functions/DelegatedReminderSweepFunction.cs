using Aluki.Runtime.DelegatedReminders;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Timer-triggered delivery sweep for SB-006 delegated reminders. Every minute
/// it claims due reminders and delivers them (with retry backoff for transient
/// failures). The claim is idempotent and concurrency-safe (SKIP LOCKED), so
/// overlapping runs do not double-deliver.
/// </summary>
public sealed class DelegatedReminderSweepFunction
{
    private readonly DelegatedReminderService _service;
    private readonly ILogger<DelegatedReminderSweepFunction> _logger;

    public DelegatedReminderSweepFunction(
        DelegatedReminderService service, ILogger<DelegatedReminderSweepFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    [Function("DelegatedReminderSweep")]
    public async Task RunAsync(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        try
        {
            var processed = await _service.FireDueAsync(cancellationToken);
            if (processed > 0)
            {
                _logger.LogInformation("delegated_reminder.sweep processed={Processed}", processed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "delegated_reminder.sweep failed");
        }
    }
}
