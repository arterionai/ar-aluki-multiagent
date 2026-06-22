using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

internal sealed class YouTubeLinkRepository : IYouTubeLinkRepository
{
    private readonly NpgsqlConnectionFactory _factory;

    public YouTubeLinkRepository(NpgsqlConnectionFactory factory) => _factory = factory;

    // ── Artifact ──────────────────────────────────────────────────────────────

    public async Task<(Guid Id, bool IsNew)> UpsertArtifactAsync(
        SavedLinkArtifactRecord record, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into saved_link_artifacts (
                id, tenant_id, context_id, principal_id,
                canonical_video_id, canonical_url, original_source_url,
                status, first_captured_at, last_refreshed_at, created_at, updated_at)
            values (
                @id, @tenant_id, @context_id, @principal_id,
                @canonical_video_id, @canonical_url, @original_source_url,
                'active', now(), now(), now(), now())
            on conflict (tenant_id, canonical_video_id)
            do update set
                last_refreshed_at   = now(),
                updated_at          = now(),
                original_source_url = excluded.original_source_url
            returning id, (xmax = 0) as is_new
            """, conn);

        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("principal_id", record.PrincipalId);
        cmd.Parameters.AddWithValue("canonical_video_id", record.CanonicalVideoId);
        cmd.Parameters.AddWithValue("canonical_url", record.CanonicalUrl);
        cmd.Parameters.AddWithValue("original_source_url", record.OriginalSourceUrl);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetGuid(0);
        var isNew = reader.GetBoolean(1);
        return (id, isNew);
    }

    // ── Enrichment ────────────────────────────────────────────────────────────

    public async Task SaveEnrichmentAsync(
        Guid savedLinkId, Guid tenantId,
        string state, string providerUsed,
        string? title, string? description, string? channel,
        DateTimeOffset? publishedAt, string? errorCode, int? latencyMs,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_enrichments (
                saved_link_id, tenant_id,
                enrichment_state, provider_used,
                title, description_snippet, channel_name,
                published_at, provider_error_code, provider_latency_ms,
                captured_at)
            values (
                @saved_link_id, @tenant_id,
                @enrichment_state, @provider_used,
                @title, @description_snippet, @channel_name,
                @published_at, @provider_error_code, @provider_latency_ms,
                now())
            """, conn);

        cmd.Parameters.AddWithValue("saved_link_id", savedLinkId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("enrichment_state", state);
        cmd.Parameters.AddWithValue("provider_used", providerUsed);
        cmd.Parameters.AddWithValue("title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description_snippet", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("channel_name", (object?)channel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("published_at", (object?)publishedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("provider_error_code", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("provider_latency_ms", (object?)latencyMs ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Classification ────────────────────────────────────────────────────────

    public async Task SaveClassificationAsync(
        Guid savedLinkId, Guid tenantId,
        string? category, string[] tags, string? summary,
        string confidenceLabel,
        bool categoryUncertain, bool tagsUncertain, bool summaryUncertain,
        decimal? confidenceScore,
        CancellationToken ct)
    {
        var tagsJson = JsonSerializer.Serialize(tags);

        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_classifications (
                saved_link_id, tenant_id,
                category, tags, summary,
                confidence_label,
                category_uncertain, tags_uncertain, summary_uncertain,
                confidence_score,
                classified_at)
            values (
                @saved_link_id, @tenant_id,
                @category, @tags::jsonb, @summary,
                @confidence_label,
                @category_uncertain, @tags_uncertain, @summary_uncertain,
                @confidence_score,
                now())
            """, conn);

        cmd.Parameters.AddWithValue("saved_link_id", savedLinkId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tags", tagsJson);
        cmd.Parameters.AddWithValue("summary", (object?)summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confidence_label", confidenceLabel);
        cmd.Parameters.AddWithValue("category_uncertain", categoryUncertain);
        cmd.Parameters.AddWithValue("tags_uncertain", tagsUncertain);
        cmd.Parameters.AddWithValue("summary_uncertain", summaryUncertain);
        cmd.Parameters.AddWithValue("confidence_score", (object?)confidenceScore ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public async Task WriteAuditAsync(
        Guid tenantId, Guid contextId, string principalId,
        string? messageId, string? canonicalVideoId,
        string eventType, string outcomeCode,
        object? details, CancellationToken ct)
    {
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details);

        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into link_capture_audit_events (
                tenant_id, context_id, principal_id,
                message_id, canonical_video_id,
                event_type, outcome_code,
                details, created_at)
            values (
                @tenant_id, @context_id, @principal_id,
                @message_id, @canonical_video_id,
                @event_type, @outcome_code,
                @details::jsonb, now())
            """, conn);

        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("principal_id", principalId);
        cmd.Parameters.AddWithValue("message_id", (object?)messageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("canonical_video_id", (object?)canonicalVideoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("event_type", eventType);
        cmd.Parameters.AddWithValue("outcome_code", outcomeCode);
        cmd.Parameters.AddWithValue("details", (object?)detailsJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
