using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.SuggestionsAdmin;

internal sealed class SuggestionsAdminRepository : ISuggestionsAdminRepository
{
    private readonly NpgsqlConnectionFactory _factory;
    private readonly ILogger<SuggestionsAdminRepository> _logger;

    public SuggestionsAdminRepository(NpgsqlConnectionFactory factory, ILogger<SuggestionsAdminRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<AdminQueueItem> GetOrCreateQueueEntryAsync(
        Guid tenantId, Guid suggestionId, Guid submitterUserId, string? summaryExcerpt, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO suggestion_admin_queue (id, suggestion_id, tenant_id, submitter_user_id, admin_status, summary_excerpt, created_at_utc, updated_at_utc)
            VALUES (gen_random_uuid(), @suggestion_id, @tenant_id, @submitter_user_id, 'captured', @summary_excerpt, now(), now())
            ON CONFLICT (suggestion_id) DO UPDATE SET updated_at_utc = now()
            RETURNING id, suggestion_id, tenant_id, submitter_user_id, admin_status, admin_category, admin_priority, summary_excerpt, last_admin_action_at_utc, created_at_utc, updated_at_utc
            """, conn);

        cmd.Parameters.AddWithValue("suggestion_id", suggestionId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("submitter_user_id", submitterUserId);
        cmd.Parameters.AddWithValue("summary_excerpt", (object?)summaryExcerpt ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadQueueItem(reader);
    }

    public async Task<ListSuggestionsResponse> ListQueueAsync(ListSuggestionsRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);

        var whereParts = new List<string> { "tenant_id = @tenant_id" };
        var countCmd = new NpgsqlCommand();
        var listCmd = new NpgsqlCommand();

        countCmd.Parameters.AddWithValue("tenant_id", request.TenantId);
        listCmd.Parameters.AddWithValue("tenant_id", request.TenantId);

        if (request.StatusFilter != null)
        {
            whereParts.Add("admin_status = @status");
            countCmd.Parameters.AddWithValue("status", request.StatusFilter);
            listCmd.Parameters.AddWithValue("status", request.StatusFilter);
        }
        if (request.CategoryFilter != null)
        {
            whereParts.Add("admin_category = @category");
            countCmd.Parameters.AddWithValue("category", request.CategoryFilter);
            listCmd.Parameters.AddWithValue("category", request.CategoryFilter);
        }
        if (request.PriorityFilter != null)
        {
            whereParts.Add("admin_priority = @priority");
            countCmd.Parameters.AddWithValue("priority", request.PriorityFilter);
            listCmd.Parameters.AddWithValue("priority", request.PriorityFilter);
        }
        if (request.SearchText != null)
        {
            whereParts.Add("(summary_excerpt ILIKE @search OR submitter_user_id::text ILIKE @search)");
            countCmd.Parameters.AddWithValue("search", "%" + request.SearchText + "%");
            listCmd.Parameters.AddWithValue("search", "%" + request.SearchText + "%");
        }

        var whereClause = string.Join(" AND ", whereParts);
        var orderClause = request.SortBy == "updated_at_utc" ? "ORDER BY updated_at_utc DESC" : "ORDER BY created_at_utc DESC";
        var offset = (request.Page - 1) * request.PageSize;

        countCmd.Connection = conn;
        countCmd.CommandText = $"SELECT COUNT(*) FROM suggestion_admin_queue WHERE {whereClause}";

        listCmd.Connection = conn;
        listCmd.CommandText = $"""
            SELECT id, suggestion_id, tenant_id, submitter_user_id, admin_status, admin_category, admin_priority, summary_excerpt, last_admin_action_at_utc, created_at_utc, updated_at_utc
            FROM suggestion_admin_queue
            WHERE {whereClause}
            {orderClause}
            LIMIT @page_size OFFSET @offset
            """;
        listCmd.Parameters.AddWithValue("page_size", request.PageSize);
        listCmd.Parameters.AddWithValue("offset", offset);

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var items = new List<AdminQueueItem>();
        await using var reader = await listCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(ReadQueueItem(reader));

        return new ListSuggestionsResponse(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<AdminQueueItem?> GetQueueItemAsync(Guid tenantId, Guid suggestionId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, suggestion_id, tenant_id, submitter_user_id, admin_status, admin_category, admin_priority, summary_excerpt, last_admin_action_at_utc, created_at_utc, updated_at_utc
            FROM suggestion_admin_queue
            WHERE tenant_id = @tenant_id AND suggestion_id = @suggestion_id
            LIMIT 1
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("suggestion_id", suggestionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadQueueItem(reader) : null;
    }

    public async Task<Guid> AppendAuditAsync(
        Guid tenantId, Guid suggestionId,
        string actorUserId, string actorRole, string actionType,
        object? oldValue, object? newValue, string reasonCode,
        CancellationToken ct)
    {
        var oldValueJson = oldValue != null ? JsonSerializer.Serialize(oldValue) : "";
        var newValueJson = newValue != null ? JsonSerializer.Serialize(newValue) : "";

        var hashInput = $"{tenantId}{suggestionId}{actionType}{actorUserId}{oldValueJson}{newValueJson}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var recordHash = Convert.ToHexStringLower(hashBytes);

        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO suggestion_admin_audit_ledger (id, tenant_id, suggestion_id, actor_user_id, actor_role, action_type, old_value, new_value, reason_code, record_hash, correction_of_audit_id, created_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @suggestion_id, @actor_user_id, @actor_role, @action_type, @old_value::jsonb, @new_value::jsonb, @reason_code, @record_hash, @correction_of_audit_id, now())
            RETURNING id
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("suggestion_id", suggestionId);
        cmd.Parameters.AddWithValue("actor_user_id", actorUserId);
        cmd.Parameters.AddWithValue("actor_role", actorRole);
        cmd.Parameters.AddWithValue("action_type", actionType);
        cmd.Parameters.AddWithValue("old_value", oldValue != null ? (object)oldValueJson : DBNull.Value);
        cmd.Parameters.AddWithValue("new_value", newValue != null ? (object)newValueJson : DBNull.Value);
        cmd.Parameters.AddWithValue("reason_code", reasonCode);
        cmd.Parameters.AddWithValue("record_hash", recordHash);
        cmd.Parameters.AddWithValue("correction_of_audit_id", DBNull.Value);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateQueueItemAsync(
        Guid tenantId, Guid suggestionId,
        string? newStatus, string? newCategory, string? newPriority,
        string actorUserId,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE suggestion_admin_queue
            SET admin_status = COALESCE(@new_status, admin_status),
                admin_category = COALESCE(@new_category, admin_category),
                admin_priority = COALESCE(@new_priority, admin_priority),
                last_admin_action_at_utc = now(),
                updated_at_utc = now()
            WHERE tenant_id = @tenant_id AND suggestion_id = @suggestion_id
            """, conn);

        cmd.Parameters.AddWithValue("new_status", (object?)newStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("new_category", (object?)newCategory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("new_priority", (object?)newPriority ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("suggestion_id", suggestionId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(Guid EntitlementId, bool WasNew, bool IsConflict)> UpsertEntitlementAsync(
        Guid tenantId, Guid submitterUserId, Guid suggestionId,
        string rewardRuleType, string sourceEventId,
        decimal grantAmount, string grantStatus,
        string policyVersion, object ruleMetadata,
        string idempotencyKey,
        CancellationToken ct)
    {
        var ruleMetadataJson = JsonSerializer.Serialize(ruleMetadata);

        await using var conn = await _factory.OpenAsync(ct);
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO reward_entitlement_ledger (id, tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id, grant_amount, grant_status, policy_version, rule_metadata, idempotency_key, granted_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @submitter_user_id, @suggestion_id, @reward_rule_type, @source_event_id, @grant_amount, @grant_status, @policy_version, @rule_metadata::jsonb, @idempotency_key, now())
            ON CONFLICT (tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id) DO NOTHING
            RETURNING id, (xmax = 0) AS is_new
            """, conn);

        insertCmd.Parameters.AddWithValue("tenant_id", tenantId);
        insertCmd.Parameters.AddWithValue("submitter_user_id", submitterUserId);
        insertCmd.Parameters.AddWithValue("suggestion_id", suggestionId);
        insertCmd.Parameters.AddWithValue("reward_rule_type", rewardRuleType);
        insertCmd.Parameters.AddWithValue("source_event_id", sourceEventId);
        insertCmd.Parameters.AddWithValue("grant_amount", grantAmount);
        insertCmd.Parameters.AddWithValue("grant_status", grantStatus);
        insertCmd.Parameters.AddWithValue("policy_version", policyVersion);
        insertCmd.Parameters.AddWithValue("rule_metadata", ruleMetadataJson);
        insertCmd.Parameters.AddWithValue("idempotency_key", idempotencyKey);

        await using var reader = await insertCmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var entitlementId = reader.GetGuid(0);
            return (entitlementId, true, false);
        }

        await reader.CloseAsync();

        await using var selectCmd = new NpgsqlCommand(
            """
            SELECT id, grant_amount, policy_version
            FROM reward_entitlement_ledger
            WHERE tenant_id = @tenant_id AND submitter_user_id = @submitter_user_id
              AND suggestion_id = @suggestion_id AND reward_rule_type = @reward_rule_type
              AND source_event_id = @source_event_id
            """, conn);

        selectCmd.Parameters.AddWithValue("tenant_id", tenantId);
        selectCmd.Parameters.AddWithValue("submitter_user_id", submitterUserId);
        selectCmd.Parameters.AddWithValue("suggestion_id", suggestionId);
        selectCmd.Parameters.AddWithValue("reward_rule_type", rewardRuleType);
        selectCmd.Parameters.AddWithValue("source_event_id", sourceEventId);

        await using var selectReader = await selectCmd.ExecuteReaderAsync(ct);
        if (!await selectReader.ReadAsync(ct))
            return (Guid.Empty, false, true);

        var existingId = selectReader.GetGuid(0);
        var existingAmount = selectReader.GetDecimal(1);
        var existingPolicyVersion = selectReader.GetString(2);

        var isConflict = existingAmount != grantAmount || existingPolicyVersion != policyVersion;
        return (existingId, false, isConflict);
    }

    public async Task<Guid> AppendRewardDecisionAsync(
        Guid tenantId, string decisionType, string reason,
        object idempotencyBoundary, Guid? entitlementId, string correlationId,
        CancellationToken ct)
    {
        var boundaryJson = JsonSerializer.Serialize(idempotencyBoundary);

        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reward_decision_record (id, tenant_id, decision_type, decision_reason, idempotency_boundary, entitlement_id, telemetry_correlation_id, created_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @decision_type, @decision_reason, @idempotency_boundary::jsonb, @entitlement_id, @telemetry_correlation_id, now())
            RETURNING id
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("decision_type", decisionType);
        cmd.Parameters.AddWithValue("decision_reason", reason);
        cmd.Parameters.AddWithValue("idempotency_boundary", boundaryJson);
        cmd.Parameters.AddWithValue("entitlement_id", (object?)entitlementId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("telemetry_correlation_id", correlationId);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<Guid> CreateNotificationDeliveryAsync(
        Guid tenantId, Guid entitlementId, Guid submitterUserId, string templateId,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reward_notification_delivery (id, tenant_id, entitlement_id, submitter_user_id, template_id, delivery_state, attempt_no, next_attempt_at_utc, created_at_utc, updated_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @entitlement_id, @submitter_user_id, @template_id, 'pending', 0, now() + interval '1 minute', now(), now())
            RETURNING id
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("entitlement_id", entitlementId);
        cmd.Parameters.AddWithValue("submitter_user_id", submitterUserId);
        cmd.Parameters.AddWithValue("template_id", templateId);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<(Guid DeliveryId, Guid EntitlementId, Guid SubmitterUserId, string TemplateId, int AttemptNo)>> ClaimDueNotificationsAsync(
        DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, entitlement_id, submitter_user_id, template_id, attempt_no
            FROM reward_notification_delivery
            WHERE delivery_state IN ('pending', 'retrying')
              AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= @now)
            ORDER BY next_attempt_at_utc ASC NULLS FIRST
            LIMIT @batch_size
            """, conn);

        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("batch_size", batchSize);

        var results = new List<(Guid, Guid, Guid, string, int)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }
        return results;
    }

    public async Task UpdateNotificationDeliveryAsync(
        Guid deliveryId, string newState, int newAttemptNo,
        DateTimeOffset? nextAttemptAtUtc, string? errorCode, string? errorMessage,
        DateTimeOffset? deadLetterAtUtc, bool operatorReplayRequired,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE reward_notification_delivery
            SET delivery_state = @new_state,
                attempt_no = @new_attempt_no,
                next_attempt_at_utc = @next_attempt_at_utc,
                last_error_code = @error_code,
                last_error_message = @error_message,
                dead_letter_at_utc = @dead_letter_at_utc,
                operator_replay_required = @operator_replay_required,
                updated_at_utc = now()
            WHERE id = @delivery_id
            """, conn);

        cmd.Parameters.AddWithValue("new_state", newState);
        cmd.Parameters.AddWithValue("new_attempt_no", newAttemptNo);
        cmd.Parameters.AddWithValue("next_attempt_at_utc", (object?)nextAttemptAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_code", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_message", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dead_letter_at_utc", (object?)deadLetterAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operator_replay_required", operatorReplayRequired);
        cmd.Parameters.AddWithValue("delivery_id", deliveryId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AdminQueueItem ReadQueueItem(NpgsqlDataReader r) => new(
        Id: r.GetGuid(0),
        SuggestionId: r.GetGuid(1),
        TenantId: r.GetGuid(2),
        SubmitterUserId: r.GetGuid(3),
        AdminStatus: r.GetString(4),
        AdminCategory: r.IsDBNull(5) ? null : r.GetString(5),
        AdminPriority: r.IsDBNull(6) ? null : r.GetString(6),
        SummaryExcerpt: r.IsDBNull(7) ? null : r.GetString(7),
        LastAdminActionAtUtc: r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(9),
        UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(10));
}
