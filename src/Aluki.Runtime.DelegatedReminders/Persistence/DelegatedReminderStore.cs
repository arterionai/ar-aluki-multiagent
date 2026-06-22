using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.DelegatedReminders;
using Aluki.Runtime.DelegatedReminders.Delivery;
using Aluki.Runtime.Memory;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.DelegatedReminders.Persistence;

// ── Internal value types ────────────────────────────────────────────────────

public sealed record DelegatedReminderRecord(
    Guid Id,
    Guid TenantId,
    Guid SenderUserId,
    string SenderIdentity,
    string RecipientIdentity,
    string? RecipientDisplayName,
    string RoutingKey,
    string Content,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset CancelDeadlineUtc,
    string Status,
    bool ConsentAcquired,
    int DeliveryAttemptCount,
    string? CorrelationId,
    string? ResolutionTier);

public sealed record ClaimedDelegatedReminder(
    Guid Id,
    Guid TenantId,
    Guid SenderUserId,
    string SenderIdentity,
    string RecipientIdentity,
    string Content,
    DateTimeOffset DueTimeUtc,
    string? CorrelationId,
    int DeliveryAttemptCount);

public sealed record ConsentRecord(
    Guid Id,
    string ConsentStatus,
    string ConsentScope,
    DateTimeOffset? GrantedAtUtc);

/// <summary>
/// Scoped persistence for delegated reminders. Applies tenant/user session scope
/// (RLS) before every per-tenant write. The background sweep's claim uses a
/// SECURITY DEFINER function. Mirrors <c>ReminderStore</c> idioms.
/// </summary>
public sealed class DelegatedReminderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlConnectionFactory _connectionFactory;

    public DelegatedReminderStore(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── Anti-spam ────────────────────────────────────────────────────────────

    public async Task<int> GetAntiSpamCountAsync(
        PrincipalScope principal, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            select count(*)::int
            from delegated_reminders
            where tenant_id = @tenant
              and sender_user_id = @user
              and created_at_utc >= now() - interval '24 hours'
              and status not in ('cancelled');
            """, connection, tx);
        cmd.Parameters.AddWithValue("tenant", principal.TenantId);
        cmd.Parameters.AddWithValue("user", principal.UserId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public async Task<(Guid ReminderId, bool IsNew, string Status)> CreateAsync(
        PrincipalScope principal,
        string senderIdentity,
        string recipientIdentity,
        string? recipientDisplayName,
        string? recipientPhoneE164,
        string? recipientWhatsappHandle,
        string resolutionTier,
        string content,
        DateTimeOffset dueTimeUtc,
        bool consentAcquired,
        string initialStatus,
        string? externalId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var reminderId = string.IsNullOrWhiteSpace(externalId) || !Guid.TryParse(externalId, out var parsed)
            ? Guid.NewGuid()
            : parsed;

        var routingKey = BuildRoutingKey(principal.TenantId, recipientIdentity, senderIdentity);
        var idempotencyKey = DeriveIdempotencyKey(externalId, principal.UserId, recipientIdentity, dueTimeUtc, content);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        // Upsert recipient contact
        await using (var rc = new NpgsqlCommand(
            """
            insert into delegated_recipient_contact
                (tenant_id, sender_user_id, recipient_identity, recipient_name,
                 phone_e164, whatsapp_handle, resolution_tier, is_confirmed, last_confirmed_at_utc)
            values (@tenant, @sender, @recipient, @name, @phone, @handle, @tier, @confirmed, @confirmed_at)
            on conflict (tenant_id, sender_user_id, recipient_identity)
            do update set
                recipient_name = coalesce(excluded.recipient_name, delegated_recipient_contact.recipient_name),
                phone_e164 = coalesce(excluded.phone_e164, delegated_recipient_contact.phone_e164),
                whatsapp_handle = coalesce(excluded.whatsapp_handle, delegated_recipient_contact.whatsapp_handle),
                resolution_tier = excluded.resolution_tier,
                is_confirmed = excluded.is_confirmed or delegated_recipient_contact.is_confirmed,
                last_confirmed_at_utc = case when excluded.is_confirmed then excluded.last_confirmed_at_utc else delegated_recipient_contact.last_confirmed_at_utc end,
                updated_at_utc = now();
            """, connection, tx))
        {
            var isConfirmed = resolutionTier == DelegatedResolutionTier.Tier1KnownContactConfirmed;
            rc.Parameters.AddWithValue("tenant", principal.TenantId);
            rc.Parameters.AddWithValue("sender", principal.UserId);
            rc.Parameters.AddWithValue("recipient", recipientIdentity);
            rc.Parameters.AddWithValue("name", (object?)recipientDisplayName ?? DBNull.Value);
            rc.Parameters.AddWithValue("phone", (object?)recipientPhoneE164 ?? DBNull.Value);
            rc.Parameters.AddWithValue("handle", (object?)recipientWhatsappHandle ?? DBNull.Value);
            rc.Parameters.AddWithValue("tier", resolutionTier);
            rc.Parameters.AddWithValue("confirmed", isConfirmed);
            rc.Parameters.AddWithValue("confirmed_at", isConfirmed ? (object)DateTimeOffset.UtcNow : DBNull.Value);
            await rc.ExecuteNonQueryAsync(cancellationToken);
        }

        // Upsert reminder
        bool isNew;
        string status;
        await using (var insert = new NpgsqlCommand(
            """
            insert into delegated_reminders
                (id, tenant_id, sender_user_id, sender_identity,
                 recipient_identity, recipient_display_name, routing_key,
                 content, due_time_utc, status, consent_acquired,
                 idempotency_key, correlation_id)
            values
                (@id, @tenant, @sender, @sender_identity,
                 @recipient, @display_name, @routing_key,
                 @content, @due, @status, @consent,
                 @idem, @corr)
            on conflict (tenant_id, idempotency_key)
            do update set updated_at_utc = delegated_reminders.updated_at_utc
            returning id, (xmax = 0) as is_new, status;
            """, connection, tx))
        {
            insert.Parameters.AddWithValue("id", reminderId);
            insert.Parameters.AddWithValue("tenant", principal.TenantId);
            insert.Parameters.AddWithValue("sender", principal.UserId);
            insert.Parameters.AddWithValue("sender_identity", senderIdentity);
            insert.Parameters.AddWithValue("recipient", recipientIdentity);
            insert.Parameters.AddWithValue("display_name", (object?)recipientDisplayName ?? DBNull.Value);
            insert.Parameters.AddWithValue("routing_key", routingKey);
            insert.Parameters.AddWithValue("content", content);
            insert.Parameters.AddWithValue("due", dueTimeUtc);
            insert.Parameters.AddWithValue("status", initialStatus);
            insert.Parameters.AddWithValue("consent", consentAcquired);
            insert.Parameters.AddWithValue("idem", idempotencyKey);
            insert.Parameters.AddWithValue("corr", (object?)correlationId ?? DBNull.Value);

            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            reminderId = reader.GetGuid(0);
            isNew = reader.GetBoolean(1);
            status = reader.GetString(2);
        }

        // Audit: only on fresh creation
        if (isNew)
        {
            await RecordAuditAsync(connection, tx, principal.TenantId, reminderId,
                DelegatedAuditEventType.Created, "sender", principal.UserId.ToString(),
                new { status = initialStatus, recipient_identity = recipientIdentity, resolution_tier = resolutionTier },
                correlationId, cancellationToken);

            if (resolutionTier == DelegatedResolutionTier.Tier1KnownContactConfirmed)
            {
                await RecordAuditAsync(connection, tx, principal.TenantId, reminderId,
                    DelegatedAuditEventType.RecipientResolved, "system", null,
                    new { tier = resolutionTier, is_confirmed = true },
                    correlationId, cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
        return (reminderId, isNew, status);
    }

    // ── Consent ──────────────────────────────────────────────────────────────

    public async Task<ConsentRecord?> GetConsentAsync(
        Guid tenantId, Guid senderUserId, string recipientIdentity, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        // Consent check is SECURITY DEFINER-style read (no per-user GUC needed for lookup)
        await using var cmd = new NpgsqlCommand(
            """
            select id, consent_status, consent_scope, granted_at_utc
            from delegated_consent_registry
            where tenant_id = @tenant
              and recipient_identity = @recipient
              and (consent_scope = 'global'
                   or (consent_scope = 'sender_scoped' and sender_user_id = @sender))
              and consent_status = 'opted_in'
            limit 1;
            """, connection);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("recipient", recipientIdentity);
        cmd.Parameters.AddWithValue("sender", senderUserId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ConsentRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task UpsertConsentAsync(
        PrincipalScope principal,
        string recipientIdentity,
        string consentStatus,
        string consentScope,
        Guid? delegatedReminderId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var sourceEventId = Guid.NewGuid().ToString("N");

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            insert into delegated_consent_registry
                (tenant_id, recipient_identity, consent_scope, sender_user_id,
                 consent_status, granted_at_utc, policy_version, source_event_id)
            values
                (@tenant, @recipient, @scope, @sender,
                 @status, @granted, 'v1', @source)
            on conflict (tenant_id, recipient_identity, consent_scope,
                         coalesce(sender_user_id::text, '*'))
            do update set
                consent_status = excluded.consent_status,
                granted_at_utc = case when excluded.consent_status = 'opted_in' then excluded.granted_at_utc else delegated_consent_registry.granted_at_utc end,
                revoked_at_utc = case when excluded.consent_status in ('opted_out','revoked') then now() else null end,
                source_event_id = excluded.source_event_id,
                updated_at_utc = now();
            """, connection, tx);

        cmd.Parameters.AddWithValue("tenant", principal.TenantId);
        cmd.Parameters.AddWithValue("recipient", recipientIdentity);
        cmd.Parameters.AddWithValue("scope", consentScope);
        cmd.Parameters.AddWithValue("sender",
            consentScope == "sender_scoped" ? (object)principal.UserId : DBNull.Value);
        cmd.Parameters.AddWithValue("status", consentStatus);
        cmd.Parameters.AddWithValue("granted",
            consentStatus == DelegatedConsentStatus.OptedIn ? (object)DateTimeOffset.UtcNow : DBNull.Value);
        cmd.Parameters.AddWithValue("source", sourceEventId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (delegatedReminderId.HasValue && consentStatus == DelegatedConsentStatus.OptedIn)
        {
            // Promote reminder to scheduled if it was awaiting consent
            await using var promote = new NpgsqlCommand(
                """
                update delegated_reminders
                set status = 'scheduled', consent_acquired = true, updated_at_utc = now()
                where id = @id and tenant_id = @tenant and status = 'awaiting_consent';
                """, connection, tx);
            promote.Parameters.AddWithValue("id", delegatedReminderId.Value);
            promote.Parameters.AddWithValue("tenant", principal.TenantId);
            await promote.ExecuteNonQueryAsync(cancellationToken);

            await RecordAuditAsync(connection, tx, principal.TenantId, delegatedReminderId.Value,
                DelegatedAuditEventType.ConsentAcquired, "recipient", recipientIdentity,
                new { consent_scope = consentScope, consent_status = consentStatus },
                correlationId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    // ── Query ────────────────────────────────────────────────────────────────

    public async Task<DelegatedReminderRecord?> GetByIdAsync(
        PrincipalScope principal, Guid reminderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            select dr.id, dr.tenant_id, dr.sender_user_id, dr.sender_identity,
                   dr.recipient_identity, dr.recipient_display_name, dr.routing_key,
                   dr.content, dr.due_time_utc, dr.cancel_deadline_utc,
                   dr.status, dr.consent_acquired, dr.delivery_attempt_count,
                   dr.correlation_id, rc.resolution_tier
            from delegated_reminders dr
            left join delegated_recipient_contact rc
                on rc.tenant_id = dr.tenant_id
               and rc.sender_user_id = dr.sender_user_id
               and rc.recipient_identity = dr.recipient_identity
            where dr.id = @id and dr.tenant_id = @tenant;
            """, connection, tx);
        cmd.Parameters.AddWithValue("id", reminderId);
        cmd.Parameters.AddWithValue("tenant", principal.TenantId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadReminderRecord(reader);
    }

    public async Task<IReadOnlyList<DelegatedReminderRecord>> ListBySenderAsync(
        PrincipalScope principal, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            select dr.id, dr.tenant_id, dr.sender_user_id, dr.sender_identity,
                   dr.recipient_identity, dr.recipient_display_name, dr.routing_key,
                   dr.content, dr.due_time_utc, dr.cancel_deadline_utc,
                   dr.status, dr.consent_acquired, dr.delivery_attempt_count,
                   dr.correlation_id, rc.resolution_tier
            from delegated_reminders dr
            left join delegated_recipient_contact rc
                on rc.tenant_id = dr.tenant_id
               and rc.sender_user_id = dr.sender_user_id
               and rc.recipient_identity = dr.recipient_identity
            where dr.tenant_id = @tenant
              and dr.sender_user_id = @user
              and dr.status not in ('cancelled', 'delivered', 'delivery_failed_terminal')
            order by dr.due_time_utc;
            """, connection, tx);
        cmd.Parameters.AddWithValue("tenant", principal.TenantId);
        cmd.Parameters.AddWithValue("user", principal.UserId);

        var results = new List<DelegatedReminderRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadReminderRecord(reader));
        }

        return results;
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public async Task<(bool Success, string? FailureCode)> CancelAsync(
        PrincipalScope principal, Guid reminderId, DateTimeOffset now, string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, tx, principal.TenantId, principal.UserId, cancellationToken);

        // Load the reminder under RLS
        await using var load = new NpgsqlCommand(
            """
            select status, cancel_deadline_utc
            from delegated_reminders
            where id = @id and tenant_id = @tenant and sender_user_id = @user;
            """, connection, tx);
        load.Parameters.AddWithValue("id", reminderId);
        load.Parameters.AddWithValue("tenant", principal.TenantId);
        load.Parameters.AddWithValue("user", principal.UserId);

        string status;
        DateTimeOffset deadline;
        await using (var rdr = await load.ExecuteReaderAsync(cancellationToken))
        {
            if (!await rdr.ReadAsync(cancellationToken))
            {
                return (false, DelegatedReminderErrorCode.NotFound);
            }

            status = rdr.GetString(0);
            deadline = rdr.GetFieldValue<DateTimeOffset>(1);
        }

        if (status == DelegatedReminderStatus.DeliveryInProgress
            || status == DelegatedReminderStatus.Delivered
            || status == DelegatedReminderStatus.DeliveryFailedTerminal
            || status == DelegatedReminderStatus.Cancelled)
        {
            return (false, DelegatedReminderErrorCode.RecallNotAllowed);
        }

        if (now > deadline)
        {
            return (false, DelegatedReminderErrorCode.CancellationWindowExpired);
        }

        await using var cancel = new NpgsqlCommand(
            """
            update delegated_reminders
            set status = 'cancelled', updated_at_utc = now()
            where id = @id and tenant_id = @tenant;
            """, connection, tx);
        cancel.Parameters.AddWithValue("id", reminderId);
        cancel.Parameters.AddWithValue("tenant", principal.TenantId);
        await cancel.ExecuteNonQueryAsync(cancellationToken);

        await RecordAuditAsync(connection, tx, principal.TenantId, reminderId,
            DelegatedAuditEventType.Cancelled, "sender", principal.UserId.ToString(),
            new { reason = "sender_requested" },
            correlationId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return (true, null);
    }

    // ── Background sweep ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ClaimedDelegatedReminder>> ClaimDueAsync(
        int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "select id, tenant_id, sender_user_id, sender_identity, recipient_identity, " +
            "content, due_time_utc, correlation_id, delivery_attempt_count " +
            "from app.claim_due_delegated_reminders(@limit, @now);",
            connection);
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("now", now);

        var claimed = new List<ClaimedDelegatedReminder>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new ClaimedDelegatedReminder(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt32(8)));
        }

        return claimed;
    }

    public async Task MarkDeliveredAsync(
        ClaimedDelegatedReminder claimed,
        string? notificationId,
        DateTimeOffset completedAt,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        // No per-user GUC for sweep — update directly (reminder already in delivery_in_progress)
        await using (var upd = new NpgsqlCommand(
            """
            update delegated_reminders
            set status = 'delivered',
                delivery_attempt_count = @attempts,
                next_retry_utc = null,
                updated_at_utc = now()
            where id = @id;
            """, connection, tx))
        {
            upd.Parameters.AddWithValue("id", claimed.Id);
            upd.Parameters.AddWithValue("attempts", attemptNumber);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordDeliveryAttemptAsync(connection, tx, claimed, attemptNumber,
            completedAt, "delivered", null, null, null, cancellationToken);

        await RecordAuditNoRlsAsync(connection, tx, claimed.TenantId, claimed.Id,
            DelegatedAuditEventType.DeliverySucceeded, "system", null,
            new { attempt = attemptNumber, notification_id = notificationId },
            claimed.CorrelationId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task MarkTerminalAsync(
        ClaimedDelegatedReminder claimed,
        string failureCategory,
        string? failureDetail,
        DateTimeOffset completedAt,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var upd = new NpgsqlCommand(
            """
            update delegated_reminders
            set status = 'delivery_failed_terminal',
                delivery_attempt_count = @attempts,
                next_retry_utc = null,
                updated_at_utc = now()
            where id = @id;
            """, connection, tx))
        {
            upd.Parameters.AddWithValue("id", claimed.Id);
            upd.Parameters.AddWithValue("attempts", attemptNumber);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordDeliveryAttemptAsync(connection, tx, claimed, attemptNumber,
            completedAt, failureCategory, failureDetail, null, null, cancellationToken);

        await RecordAuditNoRlsAsync(connection, tx, claimed.TenantId, claimed.Id,
            DelegatedAuditEventType.DeliveryFailedTerminal, "system", null,
            new { attempt = attemptNumber, failure_category = failureCategory, failure_detail = failureDetail },
            claimed.CorrelationId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task RecordTransientFailureAsync(
        ClaimedDelegatedReminder claimed,
        string failureCategory,
        string? failureDetail,
        DateTimeOffset completedAt,
        int attemptNumber,
        DateTimeOffset nextRetryUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var upd = new NpgsqlCommand(
            """
            update delegated_reminders
            set delivery_attempt_count = @attempts,
                next_retry_utc = @retry,
                updated_at_utc = now()
            where id = @id;
            """, connection, tx))
        {
            upd.Parameters.AddWithValue("id", claimed.Id);
            upd.Parameters.AddWithValue("attempts", attemptNumber);
            upd.Parameters.AddWithValue("retry", nextRetryUtc);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordDeliveryAttemptAsync(connection, tx, claimed, attemptNumber,
            completedAt, "transient_failure", failureDetail,
            (int)(nextRetryUtc - completedAt).TotalSeconds, null, cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private static async Task RecordDeliveryAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedDelegatedReminder claimed,
        int attemptNumber,
        DateTimeOffset completedAt,
        string result,
        string? failureDetail,
        int? retryDelaySeconds,
        string? providerReference,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            insert into delegated_delivery_attempt
                (tenant_id, delegated_reminder_id, attempt_index,
                 scheduled_attempt_time_utc, started_at_utc, completed_at_utc,
                 result, retry_delay_seconds, failure_detail, provider_reference, correlation_id)
            values
                (@tenant, @reminder, @index, @scheduled, @started, @completed,
                 @result, @retry_delay, @detail, @provider, @corr)
            on conflict (tenant_id, delegated_reminder_id, attempt_index) do nothing;
            """, connection, transaction);

        cmd.Parameters.AddWithValue("tenant", claimed.TenantId);
        cmd.Parameters.AddWithValue("reminder", claimed.Id);
        cmd.Parameters.AddWithValue("index", attemptNumber);
        cmd.Parameters.AddWithValue("scheduled", claimed.DueTimeUtc);
        cmd.Parameters.AddWithValue("started", completedAt);
        cmd.Parameters.AddWithValue("completed", completedAt);
        cmd.Parameters.AddWithValue("result", result);
        cmd.Parameters.AddWithValue("retry_delay", (object?)retryDelaySeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("detail", (object?)failureDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("provider", (object?)providerReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("corr", (object?)claimed.CorrelationId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid reminderId,
        string eventType,
        string actorType,
        string? actorId,
        object? payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var payloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions);

        await using var cmd = new NpgsqlCommand(
            """
            insert into delegated_audit_event
                (tenant_id, delegated_reminder_id, event_type, actor_type, actor_id,
                 payload_json, correlation_id)
            values
                (@tenant, @reminder, @event, @actor_type, @actor_id, @payload::jsonb, @corr);
            """, connection, transaction);

        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("reminder", reminderId);
        cmd.Parameters.AddWithValue("event", eventType);
        cmd.Parameters.AddWithValue("actor_type", actorType);
        cmd.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload", payloadJson);
        cmd.Parameters.AddWithValue("corr", (object?)correlationId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Variant without RLS (for sweep path; uses direct connection, no GUC)
    private static Task RecordAuditNoRlsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid reminderId,
        string eventType,
        string actorType,
        string? actorId,
        object? payload,
        string? correlationId,
        CancellationToken cancellationToken)
        => RecordAuditAsync(connection, transaction, tenantId, reminderId,
            eventType, actorType, actorId, payload, correlationId, cancellationToken);

    private static DelegatedReminderRecord ReadReminderRecord(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetString(10),
            reader.GetBoolean(11),
            reader.GetInt32(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));

    private static string BuildRoutingKey(Guid tenantId, string recipientIdentity, string senderIdentity) =>
        $"{tenantId}|{recipientIdentity}|{senderIdentity}";

    private static string DeriveIdempotencyKey(
        string? externalId, Guid userId, string recipientIdentity,
        DateTimeOffset dueTimeUtc, string content)
    {
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            return externalId!.Trim();
        }

        var raw = $"{userId}:{recipientIdentity}:{dueTimeUtc.ToUnixTimeSeconds()}:{content.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
