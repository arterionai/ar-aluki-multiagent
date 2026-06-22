using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Timer-triggered sweep for SB-009A link capture. Every 5 minutes it expires
/// stale pending confirmations whose deadline has passed.
/// </summary>
public sealed class ExpireConfirmationsFunction
{
    private readonly ILinkCaptureRepository _repo;
    private readonly ILogger<ExpireConfirmationsFunction> _logger;

    public ExpireConfirmationsFunction(
        ILinkCaptureRepository repo, ILogger<ExpireConfirmationsFunction> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [Function("ExpireConfirmations")]
    public async Task RunAsync(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        try
        {
            var expired = await _repo.ExpireStaleConfirmationsAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (expired > 0)
            {
                _logger.LogInformation("link_capture.expire_confirmations expired={Expired}", expired);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "link_capture.expire_confirmations failed");
        }
    }
}
