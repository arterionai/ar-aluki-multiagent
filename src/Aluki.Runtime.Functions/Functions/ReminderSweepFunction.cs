using Aluki.Runtime.Reminders;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Timer-triggered fire sweep (SB-005). Every minute it claims due reminders and
/// delivers them (or expires the overdue ones). This is the starter-baseline
/// scheduler; durable orchestration with per-reminder timers and retry backoff is
/// a documented follow-up. The claim is idempotent and concurrency-safe
/// (SKIP LOCKED), so overlapping runs do not double-fire.
/// </summary>
public sealed class ReminderSweepFunction
{
    private readonly ReminderService _service;
    private readonly ILogger<ReminderSweepFunction> _logger;

    public ReminderSweepFunction(ReminderService service, ILogger<ReminderSweepFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    [Function("ReminderSweep")]
    public async Task RunAsync(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        try
        {
            var processed = await _service.FireDueAsync(cancellationToken);
            if (processed > 0)
            {
                _logger.LogInformation("reminder.sweep processed={Processed}", processed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a sweep failure crash the host; the next tick retries.
            _logger.LogError(ex, "reminder.sweep failed");
        }
    }
}
