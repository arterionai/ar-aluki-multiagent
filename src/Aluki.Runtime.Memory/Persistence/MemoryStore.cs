using Aluki.Runtime.Capture.Persistence;
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
