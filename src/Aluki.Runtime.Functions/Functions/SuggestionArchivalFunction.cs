using Aluki.Runtime.Abstractions.Skills.Feedback;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

public sealed class SuggestionArchivalFunction
{
    private readonly IFeedbackRepository _repo;
    private readonly ILogger<SuggestionArchivalFunction> _logger;

    public SuggestionArchivalFunction(IFeedbackRepository repo, ILogger<SuggestionArchivalFunction> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [Function("SuggestionArchival")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var batch = await _repo.GetEligibleForArchivalAsync(cutoff, batchSize: 100, cancellationToken);
        foreach (var s in batch)
        {
            await _repo.TransitionStateAsync(s.Id, s.TenantId, SuggestionState.Archived, SuggestionActor.System, SuggestionTransitionReason.AutoArchive90Days, cancellationToken);
        }
        _logger.LogInformation("SuggestionArchival: archived {Count} suggestions.", batch.Count);
    }
}
