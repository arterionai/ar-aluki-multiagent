using Aluki.Runtime.Abstractions.Persistence;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

internal sealed class InboundEventRepository : IInboundEventRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public InboundEventRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<Guid> InsertAsync(InboundMessageEventRow row, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into inbound_message_event (
                event_id, tenant_id, context_id, source_channel, provider_message_id,
                provider_account_id, sender_external_id, received_at_utc, payload_type,
                raw_envelope_ref, correlation_id, created_at_utc)
            values (
                @event_id, @tenant_id, @context_id, @source_channel, @provider_message_id,
                @provider_account_id, @sender_external_id, @received_at_utc, @payload_type,
                @raw_envelope_ref, @correlation_id, @created_at_utc)
            returning event_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("event_id", row.EventId);
        command.Parameters.AddWithValue("tenant_id", row.TenantId);
        command.Parameters.AddWithValue("context_id", row.ContextId);
        command.Parameters.AddWithValue("source_channel", row.SourceChannel);
        command.Parameters.AddWithValue("provider_message_id", row.ProviderMessageId);
        command.Parameters.AddWithValue("provider_account_id", (object?)row.ProviderAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("sender_external_id", row.SenderExternalId);
        command.Parameters.AddWithValue("received_at_utc", row.ReceivedAtUtc);
        command.Parameters.AddWithValue("payload_type", row.PayloadType);
        command.Parameters.AddWithValue("raw_envelope_ref", row.RawEnvelopeRef);
        command.Parameters.AddWithValue("correlation_id", row.CorrelationId);
        command.Parameters.AddWithValue("created_at_utc", row.CreatedAtUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }
}
