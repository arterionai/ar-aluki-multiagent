using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Timer sweep (every minute) that replays messages whose domain agent failed with
/// a system exception. Works alongside <see cref="ReminderSweepFunction"/> and
/// follows the same SKIP-LOCKED claim pattern via
/// <see cref="IDispatchRetryQueue.ClaimDueAsync"/>.
/// </summary>
public sealed class DispatchRetryFunction
{
    private readonly IDispatchRetryQueue _retryQueue;
    private readonly IMessageDispatcher _dispatcher;
    private readonly ILogger<DispatchRetryFunction> _logger;

    public DispatchRetryFunction(
        IDispatchRetryQueue retryQueue,
        IMessageDispatcher dispatcher,
        ILogger<DispatchRetryFunction> logger)
    {
        _retryQueue = retryQueue;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    [Function("DispatchRetry")]
    public async Task RunAsync(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DispatchRetryEntry> entries;
        try
        {
            entries = await _retryQueue.ClaimDueAsync(10, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "dispatch_retry.claim_failed — skipping tick");
            return;
        }

        if (entries.Count == 0) return;

        _logger.LogInformation("dispatch_retry.claimed count={Count}", entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                var result = await _dispatcher.DispatchAsync(
                    entry.Message, entry.Principal, cancellationToken);

                var succeeded = result.Outcome is
                    DispatchOutcome.Dispatched or DispatchOutcome.Fallback;

                if (succeeded)
                {
                    await _retryQueue.MarkSucceededAsync(entry.RetryId, cancellationToken);
                    _logger.LogInformation(
                        "dispatch_retry.succeeded retry_id={RetryId} agent={AgentId} attempt={Attempt}",
                        entry.RetryId, entry.FailedAgentId, entry.AttemptCount);
                }
                else
                {
                    var abandon = entry.AttemptCount >= 3;
                    await _retryQueue.MarkFailedAsync(
                        entry.RetryId, result.Outcome, abandon, cancellationToken);
                    _logger.LogWarning(
                        "dispatch_retry.{Disposition} retry_id={RetryId} agent={AgentId} attempt={Attempt}",
                        abandon ? "abandoned" : "rescheduled",
                        entry.RetryId, entry.FailedAgentId, entry.AttemptCount);
                }
            }
            catch (Exception ex)
            {
                var abandon = entry.AttemptCount >= 3;
                try
                {
                    await _retryQueue.MarkFailedAsync(
                        entry.RetryId, ex.Message, abandon, CancellationToken.None);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx,
                        "dispatch_retry.mark_failed_error retry_id={RetryId}", entry.RetryId);
                }

                _logger.LogError(ex,
                    "dispatch_retry.exception retry_id={RetryId} agent={AgentId} attempt={Attempt}",
                    entry.RetryId, entry.FailedAgentId, entry.AttemptCount);
            }
        }
    }
}
