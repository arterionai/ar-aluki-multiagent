using Aluki.Runtime.Abstractions.Persistence;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

internal sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public IdempotencyRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<IdempotencyClaimResult> TryClaimAsync(
        Guid tenantId,
        string sourceChannel,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        // Atomic claim: insert on first delivery, otherwise bump duplicate_count.
        // (xmax = 0) is true only for the freshly inserted row, identifying the
        // canonical (new) delivery from a duplicate redelivery.
        await using var command = new NpgsqlCommand(
            """
            insert into idempotency_record (
                idempotency_id, tenant_id, source_channel, provider_message_id,
                first_seen_at_utc, last_seen_at_utc, duplicate_count)
            values (@id, @tenant_id, @source_channel, @provider_message_id, now(), now(), 0)
            on conflict (tenant_id, source_channel, provider_message_id)
            do update set last_seen_at_utc = now(),
                          duplicate_count = idempotency_record.duplicate_count + 1
            returning idempotency_id, canonical_message_id, duplicate_count, (xmax = 0) as is_new;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("source_channel", sourceChannel);
        command.Parameters.AddWithValue("provider_message_id", providerMessageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var idempotencyId = reader.GetGuid(0);
        var canonicalMessageId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var duplicateCount = reader.GetInt32(2);
        var isNew = reader.GetBoolean(3);

        return new IdempotencyClaimResult(isNew, idempotencyId, canonicalMessageId, duplicateCount);
    }

    public async Task LinkCanonicalAsync(
        Guid idempotencyId,
        Guid canonicalMessageId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            update idempotency_record
            set canonical_message_id = @canonical_message_id
            where idempotency_id = @idempotency_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("idempotency_id", idempotencyId);
        command.Parameters.AddWithValue("canonical_message_id", canonicalMessageId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
