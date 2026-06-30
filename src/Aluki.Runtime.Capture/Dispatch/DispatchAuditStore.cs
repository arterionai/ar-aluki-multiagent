using System.Text.Json;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.Capture.Dispatch;

public sealed class DispatchAuditStore : IDispatchAuditStore
{
    private readonly NpgsqlConnectionFactory _factory;

    public DispatchAuditStore(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<Guid> AppendAsync(DispatchAuditRecord record, CancellationToken ct)
    {
        // CancellationToken.None: dispatch audit is a WORM record that must always be
        // written. Using the webhook ct here caused OperationCanceledException when the
        // HTTP lifecycle closed (~20s) and aborted the in-flight Npgsql insert.
        await using var conn = await _factory.OpenAsync(CancellationToken.None);
        await using var cmd = new NpgsqlCommand(
            """
            insert into dispatch_audit_events (
                tenant_id, correlation_id, unified_message_id, channel_type,
                evaluated_agents, selected_agent_id, fallback_used, fallback_reason,
                tie_break_applied, tie_break_rationale, outcome,
                failure_agent_id, failure_details, principal_user_id, dispatched_at_utc)
            values (
                @tenant_id, @correlation_id, @unified_message_id, @channel_type,
                @evaluated_agents, @selected_agent_id, @fallback_used, @fallback_reason,
                @tie_break_applied, @tie_break_rationale, @outcome,
                @failure_agent_id, @failure_details, @principal_user_id, @dispatched_at_utc)
            returning id;
            """,
            conn);

        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        cmd.Parameters.AddWithValue("unified_message_id", record.UnifiedMessageId);
        cmd.Parameters.AddWithValue("channel_type", record.ChannelType);
        cmd.Parameters.Add(new NpgsqlParameter("evaluated_agents", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(record.EvaluatedAgents)
        });
        cmd.Parameters.AddWithValue("selected_agent_id", (object?)record.SelectedAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fallback_used", record.FallbackUsed);
        cmd.Parameters.AddWithValue("fallback_reason", (object?)record.FallbackReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tie_break_applied", record.TieBreakApplied);
        cmd.Parameters.AddWithValue("tie_break_rationale", (object?)record.TieBreakRationale ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outcome", record.Outcome);
        cmd.Parameters.AddWithValue("failure_agent_id", (object?)record.FailureAgentId ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("failure_details", NpgsqlDbType.Jsonb)
        {
            Value = record.FailureDetails is null
                ? DBNull.Value
                : (object)JsonSerializer.Serialize(record.FailureDetails)
        });
        cmd.Parameters.AddWithValue("principal_user_id", (object?)record.PrincipalUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dispatched_at_utc", record.DispatchedAtUtc);

        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return (Guid)result!;
    }
}
