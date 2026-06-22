using System.Text.Json;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.Extraction.Persistence;

public sealed record JobCreation(Guid JobId, bool IsNew, string ExistingStatus);

public sealed record PersistedResult(
    string ExtractionType,
    double OverallConfidence,
    ModelInfo ModelInfo,
    object RawContent,
    IReadOnlyList<ExtractionFieldDto> Fields);

/// <summary>
/// Scoped persistence for AI extraction. Applies the tenant/user session scope
/// (RLS) before every write and reuses the shared Npgsql connection factory.
/// Mirrors the SB-002 <c>MemoryStore</c> idioms (transaction-local GUCs,
/// idempotent upsert with <c>(xmax = 0) as is_new</c>).
/// </summary>
public sealed class ExtractionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlConnectionFactory _connectionFactory;

    public ExtractionStore(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Idempotently creates a pending job. A repeat submission with the same
    /// <paramref name="idempotencyKey"/> returns the existing job (IsNew = false)
    /// so callers can return the cached lifecycle state (FR-010).
    /// </summary>
    public async Task<JobCreation> CreateOrGetJobAsync(
        PrincipalScope principal,
        string inputType,
        string inputSource,
        int inputSizeBytes,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        JobCreation creation;
        await using (var command = new NpgsqlCommand(
            """
            insert into extraction_job (
                tenant_id, context_id, created_by, job_status, input_type,
                input_source, input_size_bytes, segment_count, completion_pct,
                idempotency_key, correlation_id)
            values (
                @tenant_id, @context_id, @created_by, 'pending', @input_type,
                @input_source, @input_size_bytes, 0, 0,
                @idempotency_key, @correlation_id)
            on conflict (tenant_id, idempotency_key)
            do update set correlation_id = extraction_job.correlation_id
            returning extraction_job_id, job_status, (xmax = 0) as is_new;
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", principal.TenantId);
            command.Parameters.AddWithValue("context_id", ContextParam(principal));
            command.Parameters.AddWithValue("created_by", principal.UserId);
            command.Parameters.AddWithValue("input_type", inputType);
            command.Parameters.AddWithValue("input_source", inputSource);
            command.Parameters.AddWithValue("input_size_bytes", inputSizeBytes);
            command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            command.Parameters.AddWithValue("correlation_id", correlationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            creation = new JobCreation(
                JobId: reader.GetGuid(0),
                ExistingStatus: reader.GetString(1),
                IsNew: reader.GetBoolean(2));
        }

        if (creation.IsNew)
        {
            await WriteAuditAsync(connection, transaction, creation.JobId, principal,
                ExtractionAuditEventType.JobCreated, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return creation;
    }

    /// <summary>Transitions a job to <c>processing</c> and stamps <c>started_at</c>.</summary>
    public async Task MarkProcessingAsync(PrincipalScope principal, Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using (var command = new NpgsqlCommand(
            """
            update extraction_job
            set job_status = 'processing', started_at_utc = now()
            where extraction_job_id = @id and job_status = 'pending';
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("id", jobId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(connection, transaction, jobId, principal,
            ExtractionAuditEventType.JobStarted, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Atomically persists the result + fields, the language/processing metadata,
    /// and the terminal lifecycle status with audit (FR-005). Idempotent: a result
    /// already present for the job is left intact.
    /// </summary>
    public async Task CompleteJobAsync(
        PrincipalScope principal,
        Guid jobId,
        PersistedResult result,
        string terminalStatus,
        string detectedLanguage,
        double languageConfidence,
        int processingTimeMs,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        Guid resultId;
        await using (var command = new NpgsqlCommand(
            """
            insert into extraction_result (
                extraction_job_id, tenant_id, extraction_type, overall_confidence,
                model_provider, model_name, model_version, raw_content)
            values (
                @job_id, @tenant_id, @extraction_type, @overall_confidence,
                @provider, @model_name, @version, @raw_content::jsonb)
            on conflict (extraction_job_id) do nothing
            returning extraction_result_id;
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("job_id", jobId);
            command.Parameters.AddWithValue("tenant_id", principal.TenantId);
            command.Parameters.AddWithValue("extraction_type", result.ExtractionType);
            command.Parameters.AddWithValue("overall_confidence", result.OverallConfidence);
            command.Parameters.AddWithValue("provider", result.ModelInfo.Provider);
            command.Parameters.AddWithValue("model_name", result.ModelInfo.ModelName);
            command.Parameters.AddWithValue("version", result.ModelInfo.Version);
            command.Parameters.Add(new NpgsqlParameter("raw_content", NpgsqlDbType.Text)
            {
                Value = JsonSerializer.Serialize(result.RawContent, JsonOptions)
            });

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            if (scalar is null)
            {
                // Result already persisted (idempotent retry): finalize status only.
                await UpdateTerminalStatusAsync(connection, transaction, jobId, terminalStatus,
                    detectedLanguage, languageConfidence, processingTimeMs, segmentCount, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            resultId = (Guid)scalar;
        }

        foreach (var field in result.Fields)
        {
            await InsertFieldAsync(connection, transaction, resultId, principal.TenantId, field, cancellationToken);
        }

        await UpdateTerminalStatusAsync(connection, transaction, jobId, terminalStatus,
            detectedLanguage, languageConfidence, processingTimeMs, segmentCount, cancellationToken);

        await WriteAuditAsync(connection, transaction, jobId, principal,
            ExtractionAuditEventType.ResultPersisted, result.ExtractionType, cancellationToken);
        await WriteAuditAsync(connection, transaction, jobId, principal,
            ExtractionAuditEventType.ExtractionCompleted, terminalStatus, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Records a terminal failure with a controlled error category (FR-009/SC-002).</summary>
    public async Task FailJobAsync(
        PrincipalScope principal,
        Guid jobId,
        string errorCategory,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using (var command = new NpgsqlCommand(
            """
            update extraction_job
            set job_status = 'failed', error_category = @category, error_message = @message,
                completed_at_utc = now(), completion_pct = 1
            where extraction_job_id = @id;
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("id", jobId);
            command.Parameters.AddWithValue("category", errorCategory);
            command.Parameters.AddWithValue("message", errorMessage);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(connection, transaction, jobId, principal,
            ExtractionAuditEventType.ExtractionFailed, errorCategory, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Returns the current lifecycle state for a job, or null when out of scope.</summary>
    public async Task<JobStatusResponse?> GetJobStatusAsync(
        PrincipalScope principal,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select job_status, completion_pct, segment_count,
                   coalesce(completed_at_utc, started_at_utc, created_at_utc)
            from extraction_job
            where extraction_job_id = @id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", jobId);

        JobStatusResponse? response = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                response = new JobStatusResponse(
                    JobId: jobId,
                    JobStatus: reader.GetString(0),
                    CompletionPct: reader.GetDouble(1) * 100.0,
                    SegmentCount: reader.GetInt32(2),
                    UpdatedAt: reader.GetFieldValue<DateTimeOffset>(3));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return response;
    }

    private static async Task UpdateTerminalStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        string terminalStatus,
        string detectedLanguage,
        double languageConfidence,
        int processingTimeMs,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            update extraction_job
            set job_status = @status, completed_at_utc = now(), completion_pct = 1,
                detected_language = @lang, language_confidence = @lang_conf,
                processing_time_ms = @ptime, segment_count = @segments
            where extraction_job_id = @id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("status", terminalStatus);
        command.Parameters.AddWithValue("lang", (object?)detectedLanguage ?? DBNull.Value);
        command.Parameters.AddWithValue("lang_conf", languageConfidence);
        command.Parameters.AddWithValue("ptime", processingTimeMs);
        command.Parameters.AddWithValue("segments", segmentCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFieldAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid resultId,
        Guid tenantId,
        ExtractionFieldDto field,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into extraction_field (
                extraction_result_id, tenant_id, field_name, field_type, extracted_value,
                confidence_score, confidence_tier, confidence_justification,
                source_segment_index, detected_language)
            values (
                @result_id, @tenant_id, @field_name, @field_type, @value::jsonb,
                @score, @tier, @justification, @segment_index, @language);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("result_id", resultId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("field_name", field.FieldName);
        command.Parameters.AddWithValue("field_type", field.FieldType);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Text)
        {
            Value = JsonSerializer.Serialize(field.ExtractedValue, JsonOptions)
        });
        command.Parameters.AddWithValue("score", field.ConfidenceScore);
        command.Parameters.AddWithValue("tier", field.ConfidenceTier);
        command.Parameters.AddWithValue("justification", (object?)field.ConfidenceJustification ?? DBNull.Value);
        command.Parameters.AddWithValue("segment_index", (object?)field.SourceSegmentIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("language", (object?)field.DetectedLanguage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        PrincipalScope principal,
        string eventType,
        string? detail,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into extraction_audit_event (extraction_job_id, tenant_id, event_type, actor, details)
            values (@job_id, @tenant_id, @event_type, @actor, @details::jsonb);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("tenant_id", principal.TenantId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor", principal.UserId);
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Text)
        {
            Value = detail is null ? "{}" : JsonSerializer.Serialize(new { detail }, JsonOptions)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ContextParam(PrincipalScope principal) =>
        principal.ContextId == Guid.Empty ? DBNull.Value : principal.ContextId;
}
