using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Orchestration;

public interface IAgentCoordinator
{
    Task<CoordinatorPlan> PlanAsync(
        PrincipalContext principal,
        IReadOnlyDictionary<string, object?> request,
        CancellationToken cancellationToken);
}

public sealed record CoordinatorPlan(
    string[] SkillSequence,
    bool RequiresLongRunningWorkflow,
    string? WorkflowName = null
);
