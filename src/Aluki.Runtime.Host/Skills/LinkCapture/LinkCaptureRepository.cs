using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

internal sealed class LinkCaptureRepository : ILinkCaptureRepository
{
    private readonly NpgsqlConnectionFactory _factory;

    public LinkCaptureRepository(NpgsqlConnectionFactory factory) => _factory = factory;

    // ── Artifact ─────────────────────────────────────────────────────────────

    public async Task<(Guid ArtifactId, bool IsNew)> UpsertArtifactAsync(LinkArtifactRecord record, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_artifacts (
                id, tenant_id, context_scope_id, created_by_principal_id,
                source_channel, canonical_url, url_hash,
                context_label, enrichment_status, enrichment_reason_code,
                description_text, site_name, title_text,
                first_captured_at_utc, last_upserted_at_utc, is_active)
            values (
                @id, @tenant_id, @context_scope_id, @created_by_principal_id,
                @source_channel, @canonical_url, @url_hash,
                @context_label, @enrichment_status, @enrichment_reason_code,
                @description_text, @site_name, @title_text,
                @first_captured_at_utc, now(), true)
            on conflict (tenant_id, url_hash) where is_active = true
            do update set last_upserted_at_utc = now()
            returning id, (xmax = 0) as is_new
            """, conn);

        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_scope_id", record.ContextScopeId);
        cmd.Parameters.AddWithValue("created_by_principal_id", record.CreatedByPrincipalId);
        cmd.Parameters.AddWithValue("source_channel", record.SourceChannel);
        cmd.Parameters.AddWithValue("canonical_url", record.CanonicalUrl);
        cmd.Parameters.AddWithValue("url_hash", record.UrlHash);
        cmd.Parameters.AddWithValue("context_label", (object?)record.ContextLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enrichment_status", record.EnrichmentStatus);
        cmd.Parameters.AddWithValue("enrichment_reason_code", (object?)record.EnrichmentReasonCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description_text", record.DescriptionText);
        cmd.Parameters.AddWithValue("site_name", (object?)record.SiteName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("title_text", (object?)record.TitleText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("first_captured_at_utc", record.FirstCapturedAtUtc);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var artifactId = reader.GetGuid(0);
        var isNew = reader.GetBoolean(1);
        return (artifactId, isNew);
    }

    public async Task<bool> TryAddProvenanceAsync(LinkProvenanceRecord record, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_provenance_refs (
                id, tenant_id, link_artifact_id,
                source_message_id, source_channel, source_timestamp_utc,
                captured_by_principal_id, context_label_snapshot, created_at_utc)
            values (
                @id, @tenant_id, @link_artifact_id,
                @source_message_id, @source_channel, @source_timestamp_utc,
                @captured_by_principal_id, @context_label_snapshot, now())
            on conflict (tenant_id, link_artifact_id, source_message_id) do nothing
            """, conn);

        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("link_artifact_id", record.LinkArtifactId);
        cmd.Parameters.AddWithValue("source_message_id", record.SourceMessageId);
        cmd.Parameters.AddWithValue("source_channel", record.SourceChannel);
        cmd.Parameters.AddWithValue("source_timestamp_utc", record.SourceTimestampUtc);
        cmd.Parameters.AddWithValue("captured_by_principal_id", record.CapturedByPrincipalId);
        cmd.Parameters.AddWithValue("context_label_snapshot", (object?)record.ContextLabelSnapshot ?? DBNull.Value);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<LinkArtifactRecord?> GetArtifactByHashAsync(Guid tenantId, string urlHash, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, tenant_id, context_scope_id, created_by_principal_id,
                   source_channel, canonical_url, url_hash,
                   context_label, enrichment_status, enrichment_reason_code,
                   description_text, site_name, title_text,
                   first_captured_at_utc, last_upserted_at_utc
            from link_artifacts
            where tenant_id = @tenant_id and url_hash = @url_hash and is_active = true
            limit 1
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("url_hash", urlHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadArtifact(reader) : null;
    }

    public async Task<IReadOnlyList<LinkArtifactRecord>> SearchArtifactsAsync(
        Guid tenantId, Guid contextScopeId, string query, int limit, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, tenant_id, context_scope_id, created_by_principal_id,
                   source_channel, canonical_url, url_hash,
                   context_label, enrichment_status, enrichment_reason_code,
                   description_text, site_name, title_text,
                   first_captured_at_utc, last_upserted_at_utc
            from link_artifacts
            where tenant_id = @tenant_id
              and context_scope_id = @context_scope_id
              and is_active = true
              and (canonical_url ilike '%' || @q || '%'
                   or context_label ilike '%' || @q || '%'
                   or description_text ilike '%' || @q || '%')
            order by last_upserted_at_utc desc
            limit @lim
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_scope_id", contextScopeId);
        cmd.Parameters.AddWithValue("q", query);
        cmd.Parameters.AddWithValue("lim", limit);

        var results = new List<LinkArtifactRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadArtifact(reader));
        return results;
    }

    public async Task<LinkProvenanceRecord?> GetFirstProvenanceAsync(Guid tenantId, Guid artifactId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, tenant_id, link_artifact_id,
                   source_message_id, source_channel, source_timestamp_utc,
                   captured_by_principal_id, context_label_snapshot, created_at_utc
            from link_provenance_refs
            where tenant_id = @tenant_id and link_artifact_id = @link_artifact_id
            order by created_at_utc asc
            limit 1
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("link_artifact_id", artifactId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadProvenance(reader) : null;
    }

    public async Task UpdateEnrichmentAsync(
        Guid artifactId, string status, string? reasonCode,
        string descriptionText, string? siteName, string? titleText,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update link_artifacts
            set enrichment_status       = @status,
                enrichment_reason_code  = @reason_code,
                description_text        = @description_text,
                site_name               = @site_name,
                title_text              = @title_text
            where id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", artifactId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description_text", descriptionText);
        cmd.Parameters.AddWithValue("site_name", (object?)siteName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("title_text", (object?)titleText ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Confirmation ─────────────────────────────────────────────────────────

    public async Task<Guid> CreateConfirmationAsync(PendingConfirmationRecord record, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_pending_confirmations (
                id, tenant_id, context_scope_id, session_id, conversation_id,
                subject_link_artifact_id, state, expires_at_utc, created_at_utc)
            values (
                @id, @tenant_id, @context_scope_id, @session_id, @conversation_id,
                @subject_link_artifact_id, @state, @expires_at_utc, now())
            returning id
            """, conn);

        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_scope_id", record.ContextScopeId);
        cmd.Parameters.AddWithValue("session_id", record.SessionId);
        cmd.Parameters.AddWithValue("conversation_id", record.ConversationId);
        cmd.Parameters.AddWithValue("subject_link_artifact_id", (object?)record.SubjectLinkArtifactId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("state", record.State);
        cmd.Parameters.AddWithValue("expires_at_utc", record.ExpiresAtUtc);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<PendingConfirmationRecord?> GetActivePendingAsync(
        Guid tenantId, string sessionId, string conversationId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, tenant_id, context_scope_id, session_id, conversation_id,
                   subject_link_artifact_id, state, expires_at_utc,
                   resolved_at_utc, resolved_by_principal_id,
                   resolve_message_id, resolve_cause, created_at_utc
            from link_pending_confirmations
            where tenant_id = @tenant_id
              and session_id = @session_id
              and conversation_id = @conversation_id
              and state = 'pending'
            limit 1
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("session_id", sessionId);
        cmd.Parameters.AddWithValue("conversation_id", conversationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfirmation(reader) : null;
    }

    public async Task<bool> TryConsumeConfirmationAsync(
        Guid confirmationId, string newState,
        Guid resolvedByPrincipalId, string resolveMessageId,
        string resolveCause, DateTimeOffset resolvedAt,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update link_pending_confirmations
            set state                   = @new_state,
                resolved_at_utc         = @now,
                resolved_by_principal_id = @principal,
                resolve_message_id      = @msg_id,
                resolve_cause           = @cause
            where id = @id and state = 'pending'
            """, conn);

        cmd.Parameters.AddWithValue("id", confirmationId);
        cmd.Parameters.AddWithValue("new_state", newState);
        cmd.Parameters.AddWithValue("now", resolvedAt);
        cmd.Parameters.AddWithValue("principal", resolvedByPrincipalId);
        cmd.Parameters.AddWithValue("msg_id", resolveMessageId);
        cmd.Parameters.AddWithValue("cause", resolveCause);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<int> ExpireStaleConfirmationsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update link_pending_confirmations
            set state           = 'expired',
                resolve_cause   = 'timeout',
                resolved_at_utc = @now
            where state = 'pending' and expires_at_utc <= @now
            """, conn);

        cmd.Parameters.AddWithValue("now", now);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Enrichment ───────────────────────────────────────────────────────────

    public async Task<Guid> RecordEnrichmentAttemptAsync(
        Guid tenantId, Guid artifactId, int attemptNo,
        DateTimeOffset startedAt, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_enrichment_attempts (
                id, tenant_id, link_artifact_id, attempt_no, started_at_utc)
            values (gen_random_uuid(), @tenant_id, @artifact_id, @attempt_no, @started_at)
            returning id
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("artifact_id", artifactId);
        cmd.Parameters.AddWithValue("attempt_no", attemptNo);
        cmd.Parameters.AddWithValue("started_at", startedAt);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task CompleteEnrichmentAttemptAsync(
        Guid attemptId, string outcome, string? reasonCode,
        int durationMs, DateTimeOffset completedAt,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update link_enrichment_attempts
            set outcome         = @outcome,
                reason_code     = @reason_code,
                duration_ms     = @duration_ms,
                completed_at_utc = @completed_at
            where id = @id
            """, conn);

        cmd.Parameters.AddWithValue("id", attemptId);
        cmd.Parameters.AddWithValue("outcome", outcome);
        cmd.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("duration_ms", durationMs);
        cmd.Parameters.AddWithValue("completed_at", completedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Policy ───────────────────────────────────────────────────────────────

    public async Task RecordPolicyDecisionAsync(
        Guid tenantId, Guid artifactId, string decision,
        string reasonCode, string destinationHost,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_enrichment_policy_decisions (
                id, tenant_id, link_artifact_id, decision, reason_code, destination_host, decided_at_utc)
            values (gen_random_uuid(), @tenant_id, @artifact_id, @decision, @reason_code, @destination_host, now())
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("artifact_id", artifactId);
        cmd.Parameters.AddWithValue("decision", decision);
        cmd.Parameters.AddWithValue("reason_code", reasonCode);
        cmd.Parameters.AddWithValue("destination_host", destinationHost);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Audit ────────────────────────────────────────────────────────────────

    public async Task WriteAuditAsync(
        Guid tenantId, string entityType, Guid entityId,
        string eventType, string actorType, string? actorId,
        object payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload);

        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_audit_events (
                id, tenant_id, entity_type, entity_id, event_type,
                actor_type, actor_id, payload_json, event_time_utc)
            values (gen_random_uuid(), @tenant_id, @entity_type, @entity_id, @event_type,
                    @actor_type, @actor_id, @payload::jsonb, now())
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("entity_type", entityType);
        cmd.Parameters.AddWithValue("entity_id", entityId);
        cmd.Parameters.AddWithValue("event_type", eventType);
        cmd.Parameters.AddWithValue("actor_type", actorType);
        cmd.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload", payloadJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Readers ───────────────────────────────────────────────────────────────

    private static LinkArtifactRecord ReadArtifact(NpgsqlDataReader r) => new(
        Id: r.GetGuid(0),
        TenantId: r.GetGuid(1),
        ContextScopeId: r.GetGuid(2),
        CreatedByPrincipalId: r.GetGuid(3),
        SourceChannel: r.GetString(4),
        CanonicalUrl: r.GetString(5),
        UrlHash: r.GetString(6),
        ContextLabel: r.IsDBNull(7) ? null : r.GetString(7),
        EnrichmentStatus: r.GetString(8),
        EnrichmentReasonCode: r.IsDBNull(9) ? null : r.GetString(9),
        DescriptionText: r.GetString(10),
        SiteName: r.IsDBNull(11) ? null : r.GetString(11),
        TitleText: r.IsDBNull(12) ? null : r.GetString(12),
        FirstCapturedAtUtc: r.GetFieldValue<DateTimeOffset>(13),
        LastUpsertedAtUtc: r.GetFieldValue<DateTimeOffset>(14));

    private static LinkProvenanceRecord ReadProvenance(NpgsqlDataReader r) => new(
        Id: r.GetGuid(0),
        TenantId: r.GetGuid(1),
        LinkArtifactId: r.GetGuid(2),
        SourceMessageId: r.GetString(3),
        SourceChannel: r.GetString(4),
        SourceTimestampUtc: r.GetFieldValue<DateTimeOffset>(5),
        CapturedByPrincipalId: r.GetGuid(6),
        ContextLabelSnapshot: r.IsDBNull(7) ? null : r.GetString(7),
        CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(8));

    private static PendingConfirmationRecord ReadConfirmation(NpgsqlDataReader r) => new(
        Id: r.GetGuid(0),
        TenantId: r.GetGuid(1),
        ContextScopeId: r.GetGuid(2),
        SessionId: r.GetString(3),
        ConversationId: r.GetString(4),
        SubjectLinkArtifactId: r.IsDBNull(5) ? null : r.GetGuid(5),
        State: r.GetString(6),
        ExpiresAtUtc: r.GetFieldValue<DateTimeOffset>(7),
        ResolvedAtUtc: r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        ResolvedByPrincipalId: r.IsDBNull(9) ? null : r.GetGuid(9),
        ResolveMessageId: r.IsDBNull(10) ? null : r.GetString(10),
        ResolveCause: r.IsDBNull(11) ? null : r.GetString(11),
        CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(12));
}
