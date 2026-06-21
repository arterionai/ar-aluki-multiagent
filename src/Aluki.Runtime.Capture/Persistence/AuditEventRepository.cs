using Aluki.Runtime.Abstractions.Persistence;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

internal sealed class AuditEventRepository : IAuditEventRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public AuditEventRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<Guid> InsertAsync(CaptureAuditEventRow row, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into capture_audit_event (
                audit_id, tenant_id, context_id, user_id, source_channel, event_name,
                event_status, correlation_id, provider_message_id, attempt_number,
                failure_category, payload_ref, occurred_at_utc)
            values (
                @audit_id, @tenant_id, @context_id, @user_id, @source_channel, @event_name,
                @event_status, @correlation_id, @provider_message_id, @attempt_number,
                @failure_category, @payload_ref, @occurred_at_utc)
            returning audit_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("audit_id", row.AuditId);
        command.Parameters.AddWithValue("tenant_id", row.TenantId);
        command.Parameters.AddWithValue("context_id", (object?)row.ContextId ?? DBNull.Value);
        command.Parameters.AddWithValue("user_id", (object?)row.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_channel", row.SourceChannel);
        command.Parameters.AddWithValue("event_name", row.EventName);
        command.Parameters.AddWithValue("event_status", row.EventStatus);
        command.Parameters.AddWithValue("correlation_id", row.CorrelationId);
        command.Parameters.AddWithValue("provider_message_id", (object?)row.ProviderMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("attempt_number", (object?)row.AttemptNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("failure_category", (object?)row.FailureCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("payload_ref", (object?)row.PayloadRef ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_at_utc", row.OccurredAtUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }
}
