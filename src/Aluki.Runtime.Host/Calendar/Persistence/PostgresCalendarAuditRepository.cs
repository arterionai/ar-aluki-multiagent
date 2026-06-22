using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Calendar.Persistence;

internal sealed class PostgresCalendarAuditRepository : ICalendarAuditRepository
{
    private readonly NpgsqlConnectionFactory _factory;

    public PostgresCalendarAuditRepository(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task AppendAsync(CalendarAuditRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into calendar_audit_events (
                calendar_audit_event_id, event_name, tenant_id, context_id, user_id,
                provider, skill_name, result, outcome_reference,
                correlation_id, occurred_at_utc, payload_json)
            values (
                @id, @event_name, @tenant_id, @context_id, @user_id,
                @provider, @skill_name, @result, @outcome_ref,
                @correlation_id, @occurred_at, @payload_json::jsonb)
            """, conn);
        cmd.Parameters.AddWithValue("id", record.CalendarAuditEventId);
        cmd.Parameters.AddWithValue("event_name", record.EventName);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", (object?)record.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("provider", record.Provider.HasValue ? (object)record.Provider.Value.ToString().ToLowerInvariant() : DBNull.Value);
        cmd.Parameters.AddWithValue("skill_name", record.SkillName);
        cmd.Parameters.AddWithValue("result", record.Result);
        cmd.Parameters.AddWithValue("outcome_ref", (object?)record.OutcomeReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        cmd.Parameters.AddWithValue("occurred_at", record.OccurredAtUtc);
        cmd.Parameters.AddWithValue("payload_json", record.PayloadJson);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
