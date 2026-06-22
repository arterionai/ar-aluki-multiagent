using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Capture.Dispatch;

/// <summary>
/// Evaluates all registered <see cref="IDomainAgent"/> implementations in
/// deterministic priority order, selects at most one per dispatch cycle, contains
/// agent failures, and persists an immutable <see cref="DispatchAuditRecord"/> for
/// every cycle regardless of outcome (FR-016).
/// </summary>
public sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly IReadOnlyList<IDomainAgent> _agents;
    private readonly IDispatchAuditStore _auditStore;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        IEnumerable<IDomainAgent> agents,
        IDispatchAuditStore auditStore,
        ILogger<MessageDispatcher> logger)
    {
        // Sort once at construction time: priority asc → agent-id lexical asc → registered-at asc.
        _agents = agents
            .OrderBy(a => a.Priority)
            .ThenBy(a => a.AgentId, StringComparer.Ordinal)
            .ThenBy(a => a.RegisteredAt)
            .ToList();
        _auditStore = auditStore;
        _logger = logger;
    }

    public async Task<DispatchResult> DispatchAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var evaluated = new List<EvaluatedAgentEntry>();
        var claimants = new List<IDomainAgent>();

        // Evaluate all agents; contain guard exceptions (FR-010).
        foreach (var agent in _agents)
        {
            bool claimed;
            try
            {
                claimed = agent.ClaimsIntent(message, principal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Agent guard threw unexpectedly. agent_id={AgentId} message_id={MessageId}",
                    agent.AgentId, message.MessageId);
                claimed = false;
            }

            evaluated.Add(new EvaluatedAgentEntry(agent.AgentId, agent.Priority, claimed));
            if (claimed) claimants.Add(agent);
        }

        // Tie-break detection: multiple claimants at the same top priority.
        bool tieBreakApplied = false;
        string? tieBreakRationale = null;

        if (claimants.Count > 1)
        {
            var topPriority = claimants[0].Priority;
            var tied = claimants.Where(a => a.Priority == topPriority).ToList();
            if (tied.Count > 1)
            {
                tieBreakApplied = true;
                tieBreakRationale =
                    $"Priority {topPriority} claimed by [{string.Join(", ", tied.Select(a => a.AgentId))}]; " +
                    $"selected {tied[0].AgentId} by ascending lexical AgentId";
            }
        }

        var selected = claimants.FirstOrDefault();
        string outcome;
        bool fallbackUsed = false;
        string? fallbackReason = null;
        string? failureAgentId = null;
        object? failureDetails = null;

        if (selected == null)
        {
            outcome = DispatchOutcome.NoAgents;
            fallbackUsed = true;
            fallbackReason = "no_eligible_agent";
            _logger.LogInformation(
                "No domain agent claimed intent. message_id={MessageId} channel={Channel}",
                message.MessageId, message.ChannelType);
        }
        else
        {
            // Determine whether this is a fallback-priority agent (e.g., MemoryDomainAgent).
            fallbackUsed = selected.Priority == int.MaxValue && claimants.Count == 1;
            if (fallbackUsed)
                fallbackReason = "no_specific_agent_claimed";

            AgentHandleResult handleResult;
            try
            {
                handleResult = await selected.HandleAsync(message, principal, ct);
            }
            catch (Exception ex)
            {
                // Contain failure; do NOT fall back per FR-015.
                _logger.LogError(
                    ex,
                    "Domain agent threw during handling (contained). agent_id={AgentId} message_id={MessageId}",
                    selected.AgentId, message.MessageId);
                handleResult = new AgentHandleResult(false, ErrorCode: "contained_exception", ErrorMessage: ex.Message);
            }

            if (handleResult.Success)
            {
                outcome = fallbackUsed ? DispatchOutcome.Fallback : DispatchOutcome.Dispatched;
            }
            else
            {
                outcome = DispatchOutcome.ContainedFailure;
                failureAgentId = selected.AgentId;
                failureDetails = new { handleResult.ErrorCode, handleResult.ErrorMessage };
                _logger.LogError(
                    "Domain agent handling failed (contained). agent_id={AgentId} error={ErrorCode} message_id={MessageId}",
                    selected.AgentId, handleResult.ErrorCode, message.MessageId);
            }
        }

        var auditId = await _auditStore.AppendAsync(new DispatchAuditRecord(
            TenantId: principal.TenantId,
            CorrelationId: message.CorrelationId ?? string.Empty,
            UnifiedMessageId: message.MessageId,
            ChannelType: message.ChannelType,
            EvaluatedAgents: evaluated,
            SelectedAgentId: selected?.AgentId,
            FallbackUsed: fallbackUsed,
            FallbackReason: fallbackReason,
            TieBreakApplied: tieBreakApplied,
            TieBreakRationale: tieBreakRationale,
            Outcome: outcome,
            FailureAgentId: failureAgentId,
            FailureDetails: failureDetails,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            PrincipalUserId: principal.UserId), ct);

        return new DispatchResult(outcome, selected?.AgentId, fallbackUsed, fallbackReason, auditId);
    }
}
