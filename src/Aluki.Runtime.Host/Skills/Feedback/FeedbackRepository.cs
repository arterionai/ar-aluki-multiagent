using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.Feedback;

internal sealed class FeedbackRepository : IFeedbackRepository
{
    private readonly NpgsqlConnectionFactory _factory;

    public FeedbackRepository(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<(Guid SuggestionId, bool IsNew)> UpsertSuggestionAsync(
        Guid tenantId, Guid userId,
        string? textContent, string? inboundMessageId, string? inboundPayloadHash,
        DateTimeOffset contextWindowExpiresAtUtc,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO suggestions (id, tenant_id, user_id, text_content, inbound_message_id, inbound_payload_hash, context_window_expires_at_utc, state, created_at_utc, updated_at_utc, state_transitioned_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @user_id, @text_content, @inbound_message_id, @inbound_payload_hash, @context_window_expires_at_utc, 'captured', now(), now(), now())
            ON CONFLICT (tenant_id, inbound_message_id, inbound_payload_hash) WHERE state IN ('captured', 'enriched') DO NOTHING
            RETURNING id, (xmax = 0) AS is_new
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("text_content", (object?)textContent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("inbound_message_id", (object?)inboundMessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("inbound_payload_hash", (object?)inboundPayloadHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("context_window_expires_at_utc", contextWindowExpiresAtUtc);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return (reader.GetGuid(0), reader.GetBoolean(1));
        }

        await reader.CloseAsync();
        await using var fallback = new NpgsqlCommand(
            """
            SELECT id FROM suggestions
            WHERE tenant_id = @tenant_id AND inbound_message_id = @inbound_message_id AND inbound_payload_hash = @inbound_payload_hash AND state IN ('captured','enriched')
            LIMIT 1
            """, conn);

        fallback.Parameters.AddWithValue("tenant_id", tenantId);
        fallback.Parameters.AddWithValue("inbound_message_id", (object?)inboundMessageId ?? DBNull.Value);
        fallback.Parameters.AddWithValue("inbound_payload_hash", (object?)inboundPayloadHash ?? DBNull.Value);

        var existingId = (Guid)(await fallback.ExecuteScalarAsync(ct))!;
        return (existingId, false);
    }

    public async Task<SuggestionRecord?> GetActiveSuggestionAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, user_id, state, text_content, text_blob_uri, captured_at_utc, context_window_expires_at_utc, attachment_count, inbound_message_id, inbound_payload_hash, created_at_utc, updated_at_utc
            FROM suggestions
            WHERE tenant_id = @tenant_id AND user_id = @user_id
              AND state NOT IN ('archived','sent_user')
              AND context_window_expires_at_utc > now()
            ORDER BY captured_at_utc DESC LIMIT 1
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("user_id", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSuggestion(reader) : null;
    }

    public async Task<int> IncrementAttachmentCountAsync(Guid suggestionId, Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE suggestions SET attachment_count = attachment_count + 1, updated_at_utc = now()
            WHERE id = @id AND tenant_id = @tenant_id AND attachment_count < 10
            RETURNING attachment_count
            """, conn);

        cmd.Parameters.AddWithValue("id", suggestionId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? -1 : Convert.ToInt32(result);
    }

    public async Task<Guid> AddAttachmentAsync(
        Guid tenantId, Guid suggestionId,
        string attachmentType, string blobUri, string mimeType,
        long fileSizeBytes, string contentHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO suggestion_attachments (id, tenant_id, suggestion_id, attachment_type, blob_uri, mime_type, file_size_bytes, content_hash, linked_at_utc, expires_at_utc, created_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @suggestion_id, @attachment_type, @blob_uri, @mime_type, @file_size_bytes, @content_hash, now(), @expires_at_utc, now())
            RETURNING id
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("suggestion_id", suggestionId);
        cmd.Parameters.AddWithValue("attachment_type", attachmentType);
        cmd.Parameters.AddWithValue("blob_uri", blobUri);
        cmd.Parameters.AddWithValue("mime_type", mimeType);
        cmd.Parameters.AddWithValue("file_size_bytes", fileSizeBytes);
        cmd.Parameters.AddWithValue("content_hash", contentHash);
        cmd.Parameters.AddWithValue("expires_at_utc", expiresAtUtc);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> IsIdempotentDuplicateAsync(
        Guid tenantId, string messageId, string payloadHash, DateTimeOffset since, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) > 0 FROM suggestions
            WHERE tenant_id = @tenant_id AND inbound_message_id = @message_id AND inbound_payload_hash = @payload_hash AND created_at_utc >= @since
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("message_id", messageId);
        cmd.Parameters.AddWithValue("payload_hash", payloadHash);
        cmd.Parameters.AddWithValue("since", since);

        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> TransitionStateAsync(
        Guid suggestionId, Guid tenantId,
        string newState, string actor, string reason, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);

        await using var selectCmd = new NpgsqlCommand(
            "SELECT state, state_transitioned_at_utc FROM suggestions WHERE id = @id AND tenant_id = @tenant_id",
            conn);
        selectCmd.Parameters.AddWithValue("id", suggestionId);
        selectCmd.Parameters.AddWithValue("tenant_id", tenantId);

        string priorState;
        int durationSeconds;
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return false;
            priorState = reader.GetString(0);
            var transitionedAt = reader.GetFieldValue<DateTimeOffset>(1);
            durationSeconds = (int)(DateTimeOffset.UtcNow - transitionedAt).TotalSeconds;
        }

        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE suggestions
            SET state = @new_state, state_transitioned_at_utc = now(), updated_at_utc = now(),
                archived_at_utc = CASE WHEN @new_state = 'archived' THEN now() ELSE archived_at_utc END
            WHERE id = @id AND tenant_id = @tenant_id AND state != 'archived'
            """, conn);

        updateCmd.Parameters.AddWithValue("id", suggestionId);
        updateCmd.Parameters.AddWithValue("tenant_id", tenantId);
        updateCmd.Parameters.AddWithValue("new_state", newState);

        var rowsAffected = await updateCmd.ExecuteNonQueryAsync(ct);
        if (rowsAffected == 0)
            return false;

        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO suggestion_state_transitions (id, tenant_id, suggestion_id, prior_state, new_state, actor, reason, duration_seconds, transitioned_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @id, @prior_state, @new_state, @actor, @reason, @duration_seconds, now())
            """, conn);

        insertCmd.Parameters.AddWithValue("tenant_id", tenantId);
        insertCmd.Parameters.AddWithValue("id", suggestionId);
        insertCmd.Parameters.AddWithValue("prior_state", priorState);
        insertCmd.Parameters.AddWithValue("new_state", newState);
        insertCmd.Parameters.AddWithValue("actor", actor);
        insertCmd.Parameters.AddWithValue("reason", reason);
        insertCmd.Parameters.AddWithValue("duration_seconds", durationSeconds);

        await insertCmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SuggestionRecord>> GetEligibleForArchivalAsync(
        DateTimeOffset cutoff, int batchSize, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, user_id, state, text_content, text_blob_uri, captured_at_utc, context_window_expires_at_utc, attachment_count, inbound_message_id, inbound_payload_hash, created_at_utc, updated_at_utc
            FROM suggestions WHERE state = 'sent_user' AND state_transitioned_at_utc < @cutoff
            LIMIT @batch_size
            """, conn);

        cmd.Parameters.AddWithValue("cutoff", cutoff);
        cmd.Parameters.AddWithValue("batch_size", batchSize);

        var results = new List<SuggestionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadSuggestion(reader));
        return results;
    }

    private static SuggestionRecord ReadSuggestion(NpgsqlDataReader r) => new(
        Id: r.GetGuid(0),
        TenantId: r.GetGuid(1),
        UserId: r.GetGuid(2),
        State: r.GetString(3),
        TextContent: r.IsDBNull(4) ? null : r.GetString(4),
        TextBlobUri: r.IsDBNull(5) ? null : r.GetString(5),
        CapturedAtUtc: r.GetFieldValue<DateTimeOffset>(6),
        ContextWindowExpiresAtUtc: r.GetFieldValue<DateTimeOffset>(7),
        AttachmentCount: r.GetInt32(8),
        InboundMessageId: r.IsDBNull(9) ? null : r.GetString(9),
        InboundPayloadHash: r.IsDBNull(10) ? null : r.GetString(10),
        CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(11),
        UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(12));
}
