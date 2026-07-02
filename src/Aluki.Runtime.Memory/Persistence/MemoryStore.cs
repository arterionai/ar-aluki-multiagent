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

        // RLS GUCs + query in one round-trip (hot reply path).
        await using var batch = new NpgsqlBatch(connection, transaction);
        batch.BatchCommands.Add(ScopedSessionContextSetter.CreateApplyBatchCommand(principal.TenantId, principal.UserId));

        var search = new NpgsqlBatchCommand(
            """
            select memory_artifact_id, content_text, provenance_ref, source_channel, (embedding <=> @q::vector) as dist
            from memory_artifact
            where context_id = @context and deleted_at_utc is null and embedding is not null
            order by embedding <=> @q::vector
            limit @k;
            """);
        search.Parameters.AddWithValue("context", principal.ContextId);
        search.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Text)
        {
            Value = Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(queryEmbedding)
        });
        search.Parameters.AddWithValue("k", topK);
        batch.BatchCommands.Add(search);

        var results = new List<RecallCandidate>(topK);
        await using (var reader = await batch.ExecuteReaderAsync(cancellationToken))
        {
            await reader.NextResultAsync(cancellationToken); // skip set_config result set
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecallCandidate(
                    ArtifactId: reader.GetGuid(0),
                    ContentText: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ProvenanceRef: reader.GetString(2),
                    Distance: reader.GetDouble(4),
                    SourceChannel: reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return results;
    }

    /// <summary>
    /// Soft-deletes the non-deleted artifacts within <paramref name="maxDistance"/>
    /// of the query embedding (closest first, capped at <paramref name="limit"/>)
    /// and writes the deletion audit in the same transaction. Returns the content
    /// texts of the deleted notes so the reply can echo exactly what was removed.
    /// </summary>
    public async Task<IReadOnlyList<string>> SoftDeleteRelevantAsync(
        PrincipalScope principal,
        float[] queryEmbedding,
        double maxDistance,
        int limit,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        var deleted = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            update memory_artifact
            set deleted_at_utc = now(), updated_at_utc = now()
            where memory_artifact_id in (
                select memory_artifact_id
                from memory_artifact
                where context_id = @context and deleted_at_utc is null and embedding is not null
                  and (embedding <=> @q::vector) <= @maxd
                order by embedding <=> @q::vector
                limit @k)
            returning content_text;
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("context", principal.ContextId);
            command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Text)
            {
                Value = Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(queryEmbedding)
            });
            command.Parameters.AddWithValue("maxd", maxDistance);
            command.Parameters.AddWithValue("k", limit);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                    deleted.Add(reader.GetString(0));
            }
        }

        if (deleted.Count > 0)
        {
            await WriteAuditAsync(
                connection,
                transaction,
                eventName: MemoryAuditEventName.NoteDeleted,
                tenantId: principal.TenantId,
                contextId: principal.ContextId,
                userId: principal.UserId,
                skillName: "NoteDeletionService",
                resultText: $"deleted_{deleted.Count}",
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
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

        // RLS GUCs + query in one round-trip (hot reply path).
        await using var batch = new NpgsqlBatch(connection, transaction);
        batch.BatchCommands.Add(ScopedSessionContextSetter.CreateApplyBatchCommand(principal.TenantId, principal.UserId));

        var existsQuery = new NpgsqlBatchCommand(
            """
            select exists (
                select 1 from memory_artifact
                where context_id = @context and deleted_at_utc is not null and embedding is not null
                  and (embedding <=> @q::vector) <= @maxd
            );
            """);
        existsQuery.Parameters.AddWithValue("context", principal.ContextId);
        existsQuery.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Text)
        {
            Value = Aluki.Runtime.Memory.Embeddings.AzureOpenAIEmbeddingClient.ToVectorLiteral(queryEmbedding)
        });
        existsQuery.Parameters.AddWithValue("maxd", maxDistance);
        batch.BatchCommands.Add(existsQuery);

        var hasDeleted = false;
        await using (var reader = await batch.ExecuteReaderAsync(cancellationToken))
        {
            await reader.NextResultAsync(cancellationToken); // skip set_config result set
            if (await reader.ReadAsync(cancellationToken))
            {
                hasDeleted = reader.GetBoolean(0);
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return hasDeleted;
    }

    /// <summary>Records a recall outcome audit under the principal's scope.</summary>
    public async Task WriteRecallAuditAsync(
        PrincipalScope principal,
        string eventName,
        string resultText,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // CancellationToken.None: recall audit is best-effort logging. Using the caller's
        // webhook ct here caused OperationCanceledException when the HTTP lifecycle closed
        // (~20s) before the audit DB write completed, crashing the domain agent's catch block.
        await using var connection = await _connectionFactory.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        // RLS GUCs + insert in one round-trip.
        await using var batch = new NpgsqlBatch(connection, transaction);
        batch.BatchCommands.Add(ScopedSessionContextSetter.CreateApplyBatchCommand(principal.TenantId, principal.UserId));

        var insert = new NpgsqlBatchCommand(
            """
            insert into memory_audit_event (
                event_name, tenant_id, context_id, user_id, skill_name, result, correlation_id)
            values (@event_name, @tenant_id, @context_id, @user_id, @skill_name, @result, @correlation_id);
            """);
        insert.Parameters.AddWithValue("event_name", eventName);
        insert.Parameters.AddWithValue("tenant_id", principal.TenantId);
        insert.Parameters.AddWithValue("context_id", (object?)principal.ContextId ?? DBNull.Value);
        insert.Parameters.AddWithValue("user_id", (object?)principal.UserId ?? DBNull.Value);
        insert.Parameters.AddWithValue("skill_name", "MemoryRecallSkill");
        insert.Parameters.AddWithValue("result", resultText);
        insert.Parameters.AddWithValue("correlation_id", correlationId);
        batch.BatchCommands.Add(insert);

        await batch.ExecuteNonQueryAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
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
