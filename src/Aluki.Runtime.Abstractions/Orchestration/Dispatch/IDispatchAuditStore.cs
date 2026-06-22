namespace Aluki.Runtime.Abstractions.Orchestration.Dispatch;

public interface IDispatchAuditStore
{
    Task<Guid> AppendAsync(DispatchAuditRecord record, CancellationToken ct);
}

public sealed record DispatchAuditRecord(
    Guid TenantId,
    string CorrelationId,
    string UnifiedMessageId,
    string ChannelType,
    IReadOnlyList<EvaluatedAgentEntry> EvaluatedAgents,
    string? SelectedAgentId,
    bool FallbackUsed,
    string? FallbackReason,
    bool TieBreakApplied,
    string? TieBreakRationale,
    string Outcome,
    string? FailureAgentId,
    object? FailureDetails,
    DateTimeOffset DispatchedAtUtc,
    Guid? PrincipalUserId);

public sealed record EvaluatedAgentEntry(string AgentId, int Priority, bool Claimed, string? ClaimReason = null);
