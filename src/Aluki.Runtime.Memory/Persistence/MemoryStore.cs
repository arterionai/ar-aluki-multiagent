using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory.Recall;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.Memory.Persistence;

public sealed record MemoryCaptureResult(bool IsNew, Guid CanonicalChainId, int ChainVersion);

/// <summary>
/// Scoped persistence for personal memory. Applies the tenant/user session scope
/// (RLS) before every operation. Reuses the shared Npgsql connection factory.
/// </summary>
public sealed class MemoryStore
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public MemoryStore(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Idempotently captures a note as a canonical memory artifact and writes the
    /// lifecycle audit atomically. Repeat deliveries of the same source identity
    /// are suppressed (no extra canonical row).
    /// </summary>
    public async Task<MemoryCaptureResult> CaptureNoteAsync(
        PrincipalScope principal,
        string sourceChannel,
        string sourceIdentity,
        string? contentText,
        float[]? embedding,
        string provenanceRef,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        MemoryCaptureResult result;
        await using (var command = new NpgsqlCommand(
            """
            insert into memory_artifact (
                tenant_id, context_id, user_id, source_channel, source_identity,
                canonical_chain_id, chain_version, content_text, embedding,
                provenance_ref, correlation_id)
            values (
                @tenant_id, @context_id, @user_id, @source_channel, @source_identity,
                gen_random_uuid(), 1, @content_text, @embedding::vector,
                @provenance_ref, @correlation_id)
            on conflict (tenant_id, source_channel, source_identity)
            do update set updated_at_utc = now()
            returning canonical_chain_id, chain_version, (xmax = 0) as is_new;
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", principal.TenantId);
            command.Parameters.AddWithValue("context_id", principal.ContextId);
            command.Parameters.AddWithValue("user_id", principal.UserId);
            command.Parameters.AddWithValue("source_channel", sourceChannel);
            command.Parameters.AddWithValue("source_identity", sourceIdentity);
            command.Parameters.AddWithValue("content_text", (object?)contentText ?? DBNull.Value);
            var embeddingParam = command.Parameters.Add(new NpgsqlParameter("embedding", NpgsqlDbType.Text));
            embeddingParam.Value = embedding is null
                ? DBNull.Value
                : Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(embedding);
            command.Parameters.AddWithValue("provenance_ref", provenanceRef);
            command.Parameters.AddWithValue("correlation_id", correlationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            result = new MemoryCaptureResult(
                IsNew: reader.GetBoolean(2),
                CanonicalChainId: reader.GetGuid(0),
                ChainVersion: reader.GetInt32(1));
        }

        await WriteAuditAsync(
            connection,
            transaction,
            eventName: result.IsNew ? MemoryAuditEventName.NoteAccepted : MemoryAuditEventName.NoteDuplicateSuppressed,
            tenantId: principal.TenantId,
            contextId: principal.ContextId,
            userId: principal.UserId,
            skillName: "MemoryCaptureSkill",
            resultText: result.IsNew ? MemoryStatus.Accepted : MemoryStatus.DuplicateSuppressed,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Vector search over non-deleted, in-context memory artifacts ordered by
    /// cosine distance to the query embedding (RLS enforces tenant scope).
    /// </summary>
    public async Task<IReadOnlyList<RecallCandidate>> SearchAsync(
        PrincipalScope principal,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select memory_artifact_id, content_text, provenance_ref, (embedding <=> @q::vector) as dist
            from memory_artifact
            where context_id = @context and deleted_at_utc is null and embedding is not null
            order by embedding <=> @q::vector
            limit @k;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("context", principal.ContextId);
        command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Text)
        {
            Value = Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(queryEmbedding)
        });
        command.Parameters.AddWithValue("k", topK);

        var results = new List<RecallCandidate>(topK);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecallCandidate(
                    ArtifactId: reader.GetGuid(0),
                    ContentText: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ProvenanceRef: reader.GetString(2),
                    Distance: reader.GetDouble(3)));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return results;
    }

    /// <summary>
    /// True when deleted artifacts within relevance distance exist for the query —
    /// used to signal a deletion-caused evidence gap rather than no evidence.
    /// </summary>
    public async Task<bool> HasDeletedRelevantAsync(
        PrincipalScope principal,
        float[] queryEmbedding,
        double maxDistance,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1 from memory_artifact
                where context_id = @context and deleted_at_utc is not null and embedding is not null
                  and (embedding <=> @q::vector) <= @maxd
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("context", principal.ContextId);
        command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Text)
        {
            Value = Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(queryEmbedding)
        });
        command.Parameters.AddWithValue("maxd", maxDistance);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return result is bool b && b;
    }

    /// <summary>Records a recall outcome audit under the principal's scope.</summary>
    public async Task WriteRecallAuditAsync(
        PrincipalScope principal,
        string eventName,
        string resultText,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            eventName: eventName,
            tenantId: principal.TenantId,
            contextId: principal.ContextId,
            userId: principal.UserId,
            skillName: "MemoryRecallSkill",
            resultText: resultText,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Records a scope_denied audit under an audit-only tenant scope. Best-effort;
    /// if the tenant cannot satisfy the audit policy the failure is swallowed by
    /// the caller's logging.
    /// </summary>
    public async Task WriteScopeDeniedAuditAsync(
        PrincipalScope principal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            eventName: MemoryAuditEventName.ScopeDenied,
            tenantId: principal.TenantId,
            contextId: principal.ContextId,
            userId: principal.UserId,
            skillName: "MemoryScopeGuard",
            resultText: MemoryStatus.Denied,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventName,
        Guid tenantId,
        Guid? contextId,
        Guid? userId,
        string skillName,
        string resultText,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into memory_audit_event (
                event_name, tenant_id, context_id, user_id, skill_name, result, correlation_id)
            values (@event_name, @tenant_id, @context_id, @user_id, @skill_name, @result, @correlation_id);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_name", eventName);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("context_id", (object?)contextId ?? DBNull.Value);
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("skill_name", skillName);
        command.Parameters.AddWithValue("result", resultText);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
