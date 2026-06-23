namespace Aluki.Runtime.Abstractions.Governance;

/// <summary>
/// Evaluates an operation against tenant-configured policy rules.
/// Always returns a decision (defaults to allow when no rules match).
/// Persists an immutable record for every evaluation.
/// </summary>
public interface IPolicyDecisionEngine
{
    Task<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken ct);
}
