using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Orchestration.Dispatch;

public interface IMessageDispatcher
{
    Task<DispatchResult> DispatchAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct);
}

public sealed record DispatchResult(
    string Outcome,
    string? SelectedAgentId,
    bool FallbackUsed,
    string? FallbackReason,
    Guid AuditEventId);

public static class DispatchOutcome
{
    public const string Dispatched = "dispatched";
    public const string Fallback = "fallback";
    public const string ContainedFailure = "contained_failure";
    public const string NoAgents = "no_agents";
}
