using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Calendar.Persistence;

internal sealed class PostgresCalendarRepository :
    ICalendarConnectionRepository,
    IOAuthCallbackStateRepository,
    IEventCreationRequestRepository,
    IDeduplicationRepository,
    ICalendarOutcomeRepository
{
    private readonly NpgsqlConnectionFactory _factory;

    public PostgresCalendarRepository(NpgsqlConnectionFactory factory) => _factory = factory;

    // ── ICalendarConnectionRepository ──────────────────────────────────────

    public async Task<CalendarConnectionRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select calendar_connection_id, tenant_id, context_id, user_id, provider,
                   connection_status, connected_at_utc, disconnected_at_utc,
                   provider_account_ref, default_for_user, correlation_id
            from calendar_connections
            where tenant_id = @tenant_id and context_id = @context_id
              and user_id = @user_id and provider = @provider
              and connection_status = 'connected'
            limit 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("provider", provider.ToString().ToLowerInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConnection(reader) : null;
    }

    public async Task<IReadOnlyList<CalendarConnectionRecord>> GetAllActiveAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select calendar_connection_id, tenant_id, context_id, user_id, provider,
                   connection_status, connected_at_utc, disconnected_at_utc,
                   provider_account_ref, default_for_user, correlation_id
            from calendar_connections
            where tenant_id = @tenant_id and context_id = @context_id
              and user_id = @user_id and connection_status = 'connected'
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("user_id", userId);

        var results = new List<CalendarConnectionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadConnection(reader));
        return results;
    }

    public async Task UpsertAsync(CalendarConnectionRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into calendar_connections (
                calendar_connection_id, tenant_id, context_id, user_id, provider,
                connection_status, connected_at_utc, disconnected_at_utc,
                provider_account_ref, default_for_user, correlation_id, updated_at_utc)
            values (
                @id, @tenant_id, @context_id, @user_id, @provider,
                @status, @connected_at, @disconnected_at,
                @account_ref, @default_for_user, @correlation_id, now())
            on conflict (calendar_connection_id) do update set
                connection_status = excluded.connection_status,
                connected_at_utc = excluded.connected_at_utc,
                disconnected_at_utc = excluded.disconnected_at_utc,
                provider_account_ref = excluded.provider_account_ref,
                default_for_user = excluded.default_for_user,
                correlation_id = excluded.correlation_id,
                updated_at_utc = now()
            """, conn);
        cmd.Parameters.AddWithValue("id", record.CalendarConnectionId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider", record.Provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("status", record.Status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("connected_at", (object?)record.ConnectedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("disconnected_at", (object?)record.DisconnectedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("account_ref", (object?)record.ProviderAccountRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("default_for_user", record.DefaultForUser);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── IOAuthCallbackStateRepository ──────────────────────────────────────

    public async Task CreateAsync(OAuthCallbackStateRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into oauth_callback_states (
                oauth_callback_state_id, tenant_id, context_id, user_id, provider,
                state_nonce, issued_at_utc, expires_at_utc, used_at_utc, status, correlation_id)
            values (
                @id, @tenant_id, @context_id, @user_id, @provider,
                @nonce, @issued_at, @expires_at, @used_at, @status, @correlation_id)
            """, conn);
        cmd.Parameters.AddWithValue("id", record.OAuthCallbackStateId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider", record.Provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("nonce", record.StateNonce);
        cmd.Parameters.AddWithValue("issued_at", record.IssuedAtUtc);
        cmd.Parameters.AddWithValue("expires_at", record.ExpiresAtUtc);
        cmd.Parameters.AddWithValue("used_at", (object?)record.UsedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", record.Status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<OAuthCallbackStateRecord?> GetByNonceAsync(string nonce, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select oauth_callback_state_id, tenant_id, context_id, user_id, provider,
                   state_nonce, issued_at_utc, expires_at_utc, used_at_utc, status, correlation_id
            from oauth_callback_states
            where state_nonce = @nonce
            limit 1
            """, conn);
        cmd.Parameters.AddWithValue("nonce", nonce);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCallbackState(reader) : null;
    }

    public async Task MarkConsumedAsync(Guid id, DateTimeOffset consumedAt, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "update oauth_callback_states set status = 'consumed', used_at_utc = @used_at where oauth_callback_state_id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("used_at", consumedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRejectedAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "update oauth_callback_states set status = 'rejected' where oauth_callback_state_id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── IEventCreationRequestRepository ───────────────────────────────────

    public async Task CreateAsync(EventCreationRequestRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into event_creation_requests (
                event_creation_request_id, tenant_id, context_id, user_id,
                provider_hint, title, start_local, end_local,
                canonical_timezone, timezone_resolution_source,
                normalized_payload_hash, requested_at_utc, correlation_id)
            values (
                @id, @tenant_id, @context_id, @user_id,
                @provider_hint, @title, @start_local, @end_local,
                @canonical_timezone, @timezone_source,
                @payload_hash, @requested_at, @correlation_id)
            """, conn);
        cmd.Parameters.AddWithValue("id", record.EventCreationRequestId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider_hint", (object?)record.ProviderHint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("title", record.Title);
        cmd.Parameters.AddWithValue("start_local", record.StartLocal);
        cmd.Parameters.AddWithValue("end_local", (object?)record.EndLocal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("canonical_timezone", record.CanonicalTimezone);
        cmd.Parameters.AddWithValue("timezone_source", record.TimezoneResolutionSource.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("payload_hash", record.NormalizedPayloadHash);
        cmd.Parameters.AddWithValue("requested_at", record.RequestedAtUtc);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<EventCreationRequestRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select event_creation_request_id, tenant_id, context_id, user_id,
                   provider_hint, title, start_local, end_local,
                   canonical_timezone, timezone_resolution_source,
                   normalized_payload_hash, requested_at_utc, correlation_id
            from event_creation_requests
            where event_creation_request_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCreationRequest(reader) : null;
    }

    // ── IDeduplicationRepository ───────────────────────────────────────────

    public async Task<DeduplicationRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string idempotencyKey, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select deduplication_record_id, tenant_id, context_id, user_id, provider,
                   idempotency_key, window_started_at_utc, window_expires_at_utc,
                   first_outcome_ref, first_provider_event_ref, status
            from deduplication_records
            where tenant_id = @tenant_id and context_id = @context_id
              and user_id = @user_id and provider = @provider
              and idempotency_key = @key and window_expires_at_utc > now()
            limit 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("provider", provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadDedup(reader) : null;
    }

    public async Task CreateAsync(DeduplicationRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into deduplication_records (
                deduplication_record_id, tenant_id, context_id, user_id, provider,
                idempotency_key, window_started_at_utc, window_expires_at_utc,
                first_outcome_ref, first_provider_event_ref, status)
            values (
                @id, @tenant_id, @context_id, @user_id, @provider,
                @key, @window_start, @window_expires,
                @outcome_ref, @provider_event_ref, @status)
            """, conn);
        cmd.Parameters.AddWithValue("id", record.DeduplicationRecordId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider", record.Provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("key", record.IdempotencyKey);
        cmd.Parameters.AddWithValue("window_start", record.WindowStartedAtUtc);
        cmd.Parameters.AddWithValue("window_expires", record.WindowExpiresAtUtc);
        cmd.Parameters.AddWithValue("outcome_ref", record.FirstOutcomeRef);
        cmd.Parameters.AddWithValue("provider_event_ref", (object?)record.FirstProviderEventRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", record.Status.ToString().ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid id, DeduplicationStatus status, string? providerEventRef, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update deduplication_records
            set status = @status, first_provider_event_ref = coalesce(@provider_event_ref, first_provider_event_ref)
            where deduplication_record_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("provider_event_ref", (object?)providerEventRef ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── ICalendarOutcomeRepository ─────────────────────────────────────────

    public async Task CreateAsync(CalendarEventOutcomeRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into calendar_event_outcomes (
                calendar_event_outcome_id, event_creation_request_id, tenant_id,
                context_id, user_id, provider, outcome_type, outcome_reference,
                provider_event_reference, final_title, final_start_utc, final_end_utc,
                final_timezone, created_at_utc, correlation_id)
            values (
                @id, @request_id, @tenant_id,
                @context_id, @user_id, @provider, @outcome_type, @outcome_ref,
                @provider_event_ref, @final_title, @final_start, @final_end,
                @final_timezone, @created_at, @correlation_id)
            """, conn);
        cmd.Parameters.AddWithValue("id", record.CalendarEventOutcomeId);
        cmd.Parameters.AddWithValue("request_id", record.EventCreationRequestId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider", record.Provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("outcome_type", OutcomeTypeToString(record.OutcomeType));
        cmd.Parameters.AddWithValue("outcome_ref", record.OutcomeReference);
        cmd.Parameters.AddWithValue("provider_event_ref", (object?)record.ProviderEventReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("final_title", (object?)record.FinalTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("final_start", (object?)record.FinalStartUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("final_end", (object?)record.FinalEndUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("final_timezone", (object?)record.FinalTimezone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", record.CreatedAtUtc);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CalendarEventOutcomeRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select calendar_event_outcome_id, event_creation_request_id, tenant_id,
                   context_id, user_id, provider, outcome_type, outcome_reference,
                   provider_event_reference, final_title, final_start_utc, final_end_utc,
                   final_timezone, created_at_utc, correlation_id
            from calendar_event_outcomes
            where calendar_event_outcome_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadOutcome(reader) : null;
    }

    // ── Readers ────────────────────────────────────────────────────────────

    private static CalendarConnectionRecord ReadConnection(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
        ParseProvider(r.GetString(4)),
        ParseConnectionStatus(r.GetString(5)),
        r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
        r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.GetBoolean(9),
        r.GetString(10));

    private static OAuthCallbackStateRecord ReadCallbackState(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
        ParseProvider(r.GetString(4)),
        r.GetString(5),
        r.GetFieldValue<DateTimeOffset>(6),
        r.GetFieldValue<DateTimeOffset>(7),
        r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        ParseCallbackStatus(r.GetString(9)),
        r.GetString(10));

    private static EventCreationRequestRecord ReadCreationRequest(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.GetString(5), r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.GetString(8),
        ParseTimezoneSource(r.GetString(9)),
        r.GetString(10),
        r.GetFieldValue<DateTimeOffset>(11),
        r.GetString(12));

    private static DeduplicationRecord ReadDedup(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
        ParseProvider(r.GetString(4)),
        r.GetString(5),
        r.GetFieldValue<DateTimeOffset>(6),
        r.GetFieldValue<DateTimeOffset>(7),
        r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        ParseDedupStatus(r.GetString(10)));

    private static CalendarEventOutcomeRecord ReadOutcome(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3), r.GetGuid(4),
        ParseProvider(r.GetString(5)),
        ParseOutcomeType(r.GetString(6)),
        r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
        r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
        r.IsDBNull(12) ? null : r.GetString(12),
        r.GetFieldValue<DateTimeOffset>(13),
        r.GetString(14));

    private static CalendarProvider ParseProvider(string v) => v switch
    {
        "outlook" => CalendarProvider.Outlook,
        "google" => CalendarProvider.Google,
        _ => throw new InvalidOperationException($"Unknown provider: {v}")
    };

    private static ConnectionStatus ParseConnectionStatus(string v) => v switch
    {
        "connected" => ConnectionStatus.Connected,
        "disconnected" => ConnectionStatus.Disconnected,
        "revoked" => ConnectionStatus.Revoked,
        "failed" => ConnectionStatus.Failed,
        _ => throw new InvalidOperationException($"Unknown connection status: {v}")
    };

    private static OAuthCallbackStatus ParseCallbackStatus(string v) => v switch
    {
        "issued" => OAuthCallbackStatus.Issued,
        "consumed" => OAuthCallbackStatus.Consumed,
        "expired" => OAuthCallbackStatus.Expired,
        "rejected" => OAuthCallbackStatus.Rejected,
        _ => throw new InvalidOperationException($"Unknown callback status: {v}")
    };

    private static TimezoneResolutionSource ParseTimezoneSource(string v) => v switch
    {
        "request" => TimezoneResolutionSource.Request,
        "profile" => TimezoneResolutionSource.Profile,
        "clarified" => TimezoneResolutionSource.Clarified,
        _ => throw new InvalidOperationException($"Unknown timezone source: {v}")
    };

    private static DeduplicationStatus ParseDedupStatus(string v) => v switch
    {
        "in_progress" => DeduplicationStatus.InProgress,
        "created" => DeduplicationStatus.Created,
        "failed" => DeduplicationStatus.Failed,
        _ => throw new InvalidOperationException($"Unknown dedup status: {v}")
    };

    private static CalendarOutcomeType ParseOutcomeType(string v) => v switch
    {
        "created" => CalendarOutcomeType.Created,
        "previously_created" => CalendarOutcomeType.PreviouslyCreated,
        "clarification_required" => CalendarOutcomeType.ClarificationRequired,
        "reconnect_required" => CalendarOutcomeType.ReconnectRequired,
        "denied" => CalendarOutcomeType.Denied,
        "failed" => CalendarOutcomeType.Failed,
        _ => throw new InvalidOperationException($"Unknown outcome type: {v}")
    };

    private static string OutcomeTypeToString(CalendarOutcomeType t) => t switch
    {
        CalendarOutcomeType.Created => "created",
        CalendarOutcomeType.PreviouslyCreated => "previously_created",
        CalendarOutcomeType.ClarificationRequired => "clarification_required",
        CalendarOutcomeType.ReconnectRequired => "reconnect_required",
        CalendarOutcomeType.Denied => "denied",
        CalendarOutcomeType.Failed => "failed",
        _ => throw new InvalidOperationException($"Unknown outcome type: {t}")
    };
}
