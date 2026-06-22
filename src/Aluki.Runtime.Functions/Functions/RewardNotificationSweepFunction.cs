using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

public sealed class RewardNotificationSweepFunction
{
    private readonly RewardNotificationSweepService _service;
    private readonly ILogger<RewardNotificationSweepFunction> _logger;

    public RewardNotificationSweepFunction(RewardNotificationSweepService service, ILogger<RewardNotificationSweepFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    [Function("RewardNotificationSweep")]
    public async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        try
        {
            await _service.ProcessDueNotificationsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "reward_notification.sweep failed");
        }
    }
}
