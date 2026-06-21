using Aluki.Runtime.Abstractions.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Persistence;

internal sealed class MessageArtifactRepository : IMessageArtifactRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public MessageArtifactRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<Guid> InsertAsync(UnifiedMessageArtifactRow row, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into unified_message_artifact (
                message_id, tenant_id, context_id, created_by_user_id, source_channel,
                provider_message_id, message_kind, message_text, forwarded_from_ref,
                provenance_event_id, acknowledged_at_utc, capture_status, created_at_utc)
            values (
                @message_id, @tenant_id, @context_id, @created_by_user_id, @source_channel,
                @provider_message_id, @message_kind, @message_text, @forwarded_from_ref,
                @provenance_event_id, @acknowledged_at_utc, @capture_status, @created_at_utc)
            returning message_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("message_id", row.MessageId);
        command.Parameters.AddWithValue("tenant_id", row.TenantId);
        command.Parameters.AddWithValue("context_id", row.ContextId);
        command.Parameters.AddWithValue("created_by_user_id", row.CreatedByUserId);
        command.Parameters.AddWithValue("source_channel", row.SourceChannel);
        command.Parameters.AddWithValue("provider_message_id", row.ProviderMessageId);
        command.Parameters.AddWithValue("message_kind", row.MessageKind);
        command.Parameters.AddWithValue("message_text", (object?)row.MessageText ?? DBNull.Value);
        command.Parameters.AddWithValue("forwarded_from_ref", (object?)row.ForwardedFromRef ?? DBNull.Value);
        command.Parameters.AddWithValue("provenance_event_id", row.ProvenanceEventId);
        command.Parameters.AddWithValue("acknowledged_at_utc", (object?)row.AcknowledgedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("capture_status", row.CaptureStatus);
        command.Parameters.AddWithValue("created_at_utc", row.CreatedAtUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    public async Task<UnifiedMessageArtifactRow?> GetByIdAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select message_id, tenant_id, context_id, created_by_user_id, source_channel,
                   provider_message_id, message_kind, message_text, forwarded_from_ref,
                   provenance_event_id, acknowledged_at_utc, capture_status, created_at_utc
            from unified_message_artifact
            where message_id = @message_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UnifiedMessageArtifactRow(
            MessageId: reader.GetGuid(0),
            TenantId: reader.GetGuid(1),
            ContextId: reader.GetGuid(2),
            CreatedByUserId: reader.GetGuid(3),
            SourceChannel: reader.GetString(4),
            ProviderMessageId: reader.GetString(5),
            MessageKind: reader.GetString(6),
            MessageText: reader.IsDBNull(7) ? null : reader.GetString(7),
            ForwardedFromRef: reader.IsDBNull(8) ? null : reader.GetString(8),
            ProvenanceEventId: reader.GetGuid(9),
            AcknowledgedAtUtc: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            CaptureStatus: reader.GetString(11),
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(12));
    }

    public async Task<int> CountByProviderAsync(
        Guid tenantId,
        string sourceChannel,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from unified_message_artifact
            where tenant_id = @tenant_id
              and source_channel = @source_channel
              and provider_message_id = @provider_message_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("source_channel", sourceChannel);
        command.Parameters.AddWithValue("provider_message_id", providerMessageId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
