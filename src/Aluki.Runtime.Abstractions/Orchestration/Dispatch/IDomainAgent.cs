using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Orchestration.Dispatch;

/// <summary>
/// A domain agent owns deterministic intent guards and domain-specific handling
/// for a single domain boundary. Registered agents are evaluated in ascending
/// priority order; ties broken by <see cref="AgentId"/> lexical order then
/// <see cref="RegisteredAt"/> ascending.
/// </summary>
public interface IDomainAgent
{
    string AgentId { get; }

    /// <summary>Lower value = evaluated first. Use int.MaxValue for catch-all fallback agents.</summary>
    int Priority { get; }

    DateTimeOffset RegisteredAt { get; }

    /// <summary>
    /// Deterministic guard: returns true when this agent claims intent for the
    /// given message. Must not throw; exceptions are treated as non-claim.
    /// </summary>
    bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal);

    Task<AgentHandleResult> HandleAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct);
}

public sealed record AgentHandleResult(
    bool Success,
    string? OutcomeCode = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
