using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Capture.Media;

/// <summary>Links a stored media binary reference and size to a media artifact.</summary>
public interface IMediaRefUpdater
{
    Task UpdateAsync(Guid mediaId, string mediaRefUri, long byteLength, CancellationToken cancellationToken);
}

/// <summary>
/// Updates a media artifact with its stored binary reference and size after an
/// asynchronous download. Runs as a system job (no interactive principal); the
/// configured connection is the schema owner, so the row is reachable without an
/// interactive RLS scope.
/// </summary>
public sealed class MediaRefUpdater : IMediaRefUpdater
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public MediaRefUpdater(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpdateAsync(
        Guid mediaId,
        string mediaRefUri,
        long byteLength,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            update media_artifact
            set media_ref_uri = @uri, byte_length = @len
            where media_id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("uri", mediaRefUri);
        command.Parameters.AddWithValue("len", byteLength);
        command.Parameters.AddWithValue("id", mediaId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
