using Aluki.Runtime.Abstractions.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Persistence;

internal sealed class MediaArtifactRepository : IMediaArtifactRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public MediaArtifactRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<Guid> InsertAsync(MediaArtifactRow row, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into media_artifact (
                media_id, tenant_id, context_id, message_id, media_type, content_type,
                provider_media_id, media_ref_uri, byte_length, provenance_event_id, created_at_utc)
            values (
                @media_id, @tenant_id, @context_id, @message_id, @media_type, @content_type,
                @provider_media_id, @media_ref_uri, @byte_length, @provenance_event_id, @created_at_utc)
            returning media_id;
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("media_id", row.MediaId);
        command.Parameters.AddWithValue("tenant_id", row.TenantId);
        command.Parameters.AddWithValue("context_id", row.ContextId);
        command.Parameters.AddWithValue("message_id", row.MessageId);
        command.Parameters.AddWithValue("media_type", row.MediaType);
        command.Parameters.AddWithValue("content_type", row.ContentType);
        command.Parameters.AddWithValue("provider_media_id", (object?)row.ProviderMediaId ?? DBNull.Value);
        command.Parameters.AddWithValue("media_ref_uri", (object?)row.MediaRefUri ?? DBNull.Value);
        command.Parameters.AddWithValue("byte_length", (object?)row.ByteLength ?? DBNull.Value);
        command.Parameters.AddWithValue("provenance_event_id", row.ProvenanceEventId);
        command.Parameters.AddWithValue("created_at_utc", row.CreatedAtUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    public async Task<int> CountByMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select count(*) from media_artifact where message_id = @message_id;",
            _connection,
            _transaction);

        command.Parameters.AddWithValue("message_id", messageId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
