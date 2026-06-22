using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.SuggestionsAdmin;

public sealed class RewardNotificationSweepService
{
    private readonly ISuggestionsAdminRepository _repo;
    private readonly ILogger<RewardNotificationSweepService> _logger;

    public RewardNotificationSweepService(ISuggestionsAdminRepository repo, ILogger<RewardNotificationSweepService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task ProcessDueNotificationsAsync(CancellationToken ct)
    {
        var due = await _repo.ClaimDueNotificationsAsync(DateTimeOffset.UtcNow, batchSize: 50, ct);
        var backoffMinutes = new[] { 1, 5, 15, 60, 360 };

        foreach (var (deliveryId, entitlementId, submitterUserId, templateId, attemptNo) in due)
        {
            var newAttemptNo = attemptNo + 1;
            if (newAttemptNo > 5)
            {
                await _repo.UpdateNotificationDeliveryAsync(deliveryId, DeliveryState.DeadLetter, newAttemptNo, null, "max_attempts", "Maximum delivery attempts exceeded", DateTimeOffset.UtcNow, true, ct);
                _logger.LogWarning("Reward notification dead-lettered. deliveryId={DeliveryId} entitlementId={EntitlementId}", deliveryId, entitlementId);
            }
            else
            {
                _logger.LogInformation("Reward notification delivery stub. deliveryId={DeliveryId} entitlementId={EntitlementId} attempt={Attempt}", deliveryId, entitlementId, newAttemptNo);
                var nextAttempt = DateTimeOffset.UtcNow.AddMinutes(backoffMinutes[newAttemptNo - 1]);
                await _repo.UpdateNotificationDeliveryAsync(deliveryId, DeliveryState.Retrying, newAttemptNo, nextAttempt, null, null, null, false, ct);
            }
        }
    }
}
