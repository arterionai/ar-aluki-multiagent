using System.Text.Json;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Reminders.Delivery;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.Reminders.Persistence;

public sealed record ReminderCreation(Guid ReminderId, bool IsNew, string Status);

public sealed record QuotaSnapshot(int ActiveCount, int Limit, string EntitlementTier);

/// <summary>A reminder claimed by the background sweep (already marked firing).</summary>
public sealed record ClaimedReminder(
    Guid ReminderId,
    Guid TenantId,
    Guid? ContextId,
    Guid UserId,
    string ReminderText,
    DateTimeOffset ScheduledTimeUtc,
    string Timezone,
    string DeliveryChannel,
    string? CorrelationId,
    string ReminderType,
    Guid? RecurrenceRuleId,
    int AttemptCount);

/// <summary>Recurrence rule + anchor local time for next-occurrence computation.</summary>
public sealed record RecurrenceContext(Time.ReminderRecurrence Rule, TimeOnly LocalTime);

/// <summary>
/// Scoped persistence for reminders. Applies the tenant/user session scope (RLS)
/// before every per-tenant write and reuses the shared connection factory.
/// Mirrors the SB-004 <c>ExtractionStore</c> idioms (transaction-local GUCs,
/// idempotent upsert with <c>(xmax = 0) as is_new</c>). The cross-tenant fire
/// sweep uses the SECURITY DEFINER <c>app.claim_due_reminders</c> function.
/// </summary>
public sealed class ReminderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlConnectionFactory _connectionFactory;

    public ReminderStore(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>Reads the tenant quota (creating a default row) and the active reminder count.</summary>
    public async Task<QuotaSnapshot> GetQuotaSnapshotAsync(
        PrincipalScope principal, int defaultLimit, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using (var ensure = new NpgsqlCommand(
            """
            insert into reminder_quotas (tenant_id, quota_limit)
            values (@tenant, @limit)
            on conflict (tenant_id) do nothing;
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue("tenant", principal.TenantId);
            ensure.Parameters.AddWithValue("limit", defaultLimit);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        int limit;
        string tier;
        await using (var read = new NpgsqlCommand(
            "select quota_limit, entitlement_tier from reminder_quotas where tenant_id = @tenant;",
            connection, transaction))
        {
            read.Parameters.AddWithValue("tenant", principal.TenantId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            limit = reader.GetInt32(0);
            tier = reader.GetString(1);
        }

        int activeCount;
        await using (var count = new NpgsqlCommand(
            """
            select count(*) from reminders
            where tenant_id = @tenant
              and status in ('scheduled', 'firing', 'delivery_failed')
              and deleted_at_utc is null;
            """, connection, transaction))
        {
            count.Parameters.AddWithValue("tenant", principal.TenantId);
            activeCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new QuotaSnapshot(activeCount, limit, tier);
    }

    /// <summary>
    /// Idempotently creates a one-shot reminder. A repeat submission with the same
    /// idempotency key returns the existing reminder (IsNew = false).
    /// </summary>
    public async Task<ReminderCreation> CreateOneShotAsync(
        PrincipalScope principal,
        string reminderText,
        DateTimeOffset scheduledTimeUtc,
        TimeOnly? originalLocalTime,
        string timezone,
        string deliveryChannel,
        string quotaTier,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        ReminderCreation creation;
        await using (var command = new NpgsqlCommand(
            """
            insert into reminders (
                tenant_id, context_id, user_id, reminder_text, scheduled_time_utc,
                original_time_local, timezone, reminder_type, delivery_channel,
                quota_tier, status, idempotency_key, correlation_id)
            values (
                @tenant, @context, @user, @text, @scheduled,
                @local_time, @tz, 'one_shot', @channel,
                @tier, 'scheduled', @idem, @correlation)
            on conflict (tenant_id, idempotency_key)
            do update set updated_at_utc = reminders.updated_at_utc
            returning reminder_id, status, (xmax = 0) as is_new;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("tenant", principal.TenantId);
            command.Parameters.AddWithValue("context", ContextParam(principal));
            command.Parameters.AddWithValue("user", principal.UserId);
            command.Parameters.AddWithValue("text", reminderText);
            command.Parameters.AddWithValue("scheduled", scheduledTimeUtc.ToUniversalTime());
            command.Parameters.Add(new NpgsqlParameter("local_time", NpgsqlDbType.Time)
            {
                Value = originalLocalTime is { } t ? t.ToTimeSpan() : DBNull.Value
            });
            command.Parameters.AddWithValue("tz", timezone);
            command.Parameters.AddWithValue("channel", deliveryChannel);
            command.Parameters.AddWithValue("tier", quotaTier);
            command.Parameters.AddWithValue("idem", idempotencyKey);
            command.Parameters.AddWithValue("correlation", correlationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            creation = new ReminderCreation(reader.GetGuid(0), reader.GetBoolean(2), reader.GetString(1));
        }

        if (creation.IsNew)
        {
            await WriteAuditAsync(connection, transaction, creation.ReminderId, principal,
                ReminderAuditEventType.Created, correlationId, null, cancellationToken);
            await WriteAuditAsync(connection, transaction, creation.ReminderId, principal,
                ReminderAuditEventType.Scheduled, correlationId,
                new { scheduled_time_utc = scheduledTimeUtc.ToUniversalTime() }, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return creation;
    }

    public async Task<IReadOnlyList<ReminderDto>> ListAsync(
        PrincipalScope principal, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select reminder_id, reminder_text, scheduled_time_utc, timezone,
                   reminder_type, status, snooze_count, delivery_channel
            from reminders
            where tenant_id = @tenant and user_id = @user and deleted_at_utc is null
            order by scheduled_time_utc;
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", principal.TenantId);
        command.Parameters.AddWithValue("user", principal.UserId);

        var results = new List<ReminderDto>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ReminderDto(
                    reader.GetGuid(0), reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7)));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return results;
    }

    /// <summary>
    /// Snoozes a reminder to <paramref name="newTimeUtc"/>: reschedules the same
    /// instance, increments snooze_count, returns to scheduled, audits. Returns the
    /// updated reminder, or null when not found in scope or not snoozable.
    /// </summary>
    public async Task<ReminderDto?> SnoozeAsync(
        PrincipalScope principal, Guid reminderId, DateTimeOffset newTimeUtc, int durationSeconds, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        ReminderDto? dto = null;
        await using (var command = new NpgsqlCommand(
            """
            update reminders
            set scheduled_time_utc = @new_time, snooze_count = snooze_count + 1,
                status = 'scheduled', updated_at_utc = now()
            where reminder_id = @id and user_id = @user and deleted_at_utc is null
              and status in ('scheduled', 'firing', 'delivery_failed')
            returning reminder_id, reminder_text, scheduled_time_utc, timezone,
                      reminder_type, status, snooze_count, delivery_channel;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("new_time", newTimeUtc.ToUniversalTime());
            command.Parameters.AddWithValue("id", reminderId);
            command.Parameters.AddWithValue("user", principal.UserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dto = new ReminderDto(
                    reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7));
            }
        }

        if (dto is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(connection, transaction, reminderId, principal,
            ReminderAuditEventType.Snoozed, null,
            new { snooze_duration_seconds = durationSeconds, new_scheduled_time_utc = newTimeUtc.ToUniversalTime() },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Soft-cancels a reminder. Returns false when not found in scope.</summary>
    public async Task<bool> CancelAsync(PrincipalScope principal, Guid reminderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        int affected;
        await using (var command = new NpgsqlCommand(
            """
            update reminders
            set status = 'user_cancelled', deleted_at_utc = now(), updated_at_utc = now()
            where reminder_id = @id and user_id = @user and deleted_at_utc is null;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", reminderId);
            command.Parameters.AddWithValue("user", principal.UserId);
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await WriteAuditAsync(connection, transaction, reminderId, principal,
            ReminderAuditEventType.Cancelled, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Cross-tenant claim of due reminders via the SECURITY DEFINER function. The
    /// returned reminders are already transitioned to <c>firing</c>.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedReminder>> ClaimDueAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select * from app.claim_due_reminders(@limit, @now);", connection);
        command.Parameters.AddWithValue("limit", batchSize);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());

        var results = new List<ClaimedReminder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ClaimedReminder(
                ReminderId: reader.GetGuid(0),
                TenantId: reader.GetGuid(1),
                ContextId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                UserId: reader.GetGuid(3),
                ReminderText: reader.GetString(4),
                ScheduledTimeUtc: reader.GetFieldValue<DateTimeOffset>(5),
                Timezone: reader.GetString(6),
                DeliveryChannel: reader.GetString(7),
                CorrelationId: reader.IsDBNull(8) ? null : reader.GetString(8),
                ReminderType: reader.GetString(9),
                RecurrenceRuleId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
                AttemptCount: reader.GetInt32(11)));
        }

        return results;
    }

    /// <summary>
    /// Records a delivery attempt and transitions the reminder, under its own
    /// (tenant, creator) scope. Idempotent on <c>(reminder_id, scheduled_time_utc,
    /// attempt_number)</c>. Outcomes:
    ///   delivered + <paramref name="rescheduleToUtc"/> ⇒ recurring re-arm (scheduled);
    ///   delivered ⇒ terminal delivered;
    ///   transient failure + <paramref name="nextRetryUtc"/> ⇒ arm a retry (stays firing);
    ///   else ⇒ terminal delivery_failed.
    /// </summary>
    public async Task RecordDeliveryOutcomeAsync(
        ClaimedReminder reminder,
        int attemptNumber,
        ReminderDeliveryResult result,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken,
        DateTimeOffset? rescheduleToUtc = null,
        DateTimeOffset? nextRetryUtc = null)
    {
        var principal = ScopeFor(reminder);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        var delivered = result.Status == DeliveryStatus.Delivered;
        var retry = !delivered && nextRetryUtc is not null;
        var reschedule = delivered && rescheduleToUtc is not null;

        var attemptStatus = delivered ? DeliveryStatus.Delivered
            : retry ? DeliveryStatus.RetryScheduled
            : result.Status == DeliveryStatus.PermanentFailure ? DeliveryStatus.PermanentFailure
            : DeliveryStatus.TransientFailure;

        await using (var attempt = new NpgsqlCommand(
            """
            insert into reminder_delivery_attempts (
                reminder_id, tenant_id, scheduled_time_utc, attempt_number, status,
                failure_category, failure_message, delivery_timestamp_utc,
                next_retry_time_utc, notification_id)
            values (
                @id, @tenant, @scheduled, @attempt, @status,
                @fail_cat, @fail_msg, @delivered_at, @next_retry, @notif)
            on conflict (reminder_id, scheduled_time_utc, attempt_number) do nothing;
            """, connection, transaction))
        {
            attempt.Parameters.AddWithValue("id", reminder.ReminderId);
            attempt.Parameters.AddWithValue("tenant", reminder.TenantId);
            attempt.Parameters.AddWithValue("scheduled", reminder.ScheduledTimeUtc.ToUniversalTime());
            attempt.Parameters.AddWithValue("attempt", attemptNumber);
            attempt.Parameters.AddWithValue("status", attemptStatus);
            attempt.Parameters.AddWithValue("fail_cat", (object?)result.FailureCategory ?? DBNull.Value);
            attempt.Parameters.AddWithValue("fail_msg", (object?)result.FailureMessage ?? DBNull.Value);
            attempt.Parameters.AddWithValue("delivered_at", delivered ? deliveredAt.ToUniversalTime() : (object)DBNull.Value);
            attempt.Parameters.AddWithValue("next_retry", retry ? nextRetryUtc!.Value.ToUniversalTime() : (object)DBNull.Value);
            attempt.Parameters.AddWithValue("notif", (object?)result.NotificationId ?? DBNull.Value);
            await attempt.ExecuteNonQueryAsync(cancellationToken);
        }

        var (sql, bind) = BuildReminderTransition(reschedule, delivered, retry);
        await using (var update = new NpgsqlCommand(sql, connection, transaction))
        {
            update.Parameters.AddWithValue("id", reminder.ReminderId);
            bind(update, rescheduleToUtc, nextRetryUtc, attemptNumber);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        // Audit the outcome.
        if (delivered)
        {
            await WriteAuditAsync(connection, transaction, reminder.ReminderId, principal,
                ReminderAuditEventType.Delivered, reminder.CorrelationId, null, cancellationToken);
            if (reschedule)
            {
                await WriteAuditAsync(connection, transaction, reminder.ReminderId, principal,
                    ReminderAuditEventType.Scheduled, reminder.CorrelationId,
                    new { next_occurrence_utc = rescheduleToUtc!.Value.ToUniversalTime() }, cancellationToken);
            }
        }
        else if (retry)
        {
            await WriteAuditAsync(connection, transaction, reminder.ReminderId, principal,
                ReminderAuditEventType.Firing, reminder.CorrelationId,
                new { attempt_number = attemptNumber, next_retry_utc = nextRetryUtc!.Value.ToUniversalTime(), failure_category = result.FailureCategory },
                cancellationToken);
        }
        else
        {
            await WriteAuditAsync(connection, transaction, reminder.ReminderId, principal,
                ReminderAuditEventType.DeliveryFailed, reminder.CorrelationId,
                new { failure_category = result.FailureCategory, attempt_number = attemptNumber }, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static (string Sql, Action<NpgsqlCommand, DateTimeOffset?, DateTimeOffset?, int> Bind) BuildReminderTransition(
        bool reschedule, bool delivered, bool retry)
    {
        if (reschedule)
        {
            return (
                """
                update reminders set status = 'scheduled', scheduled_time_utc = @next,
                    delivery_attempt_count = 0, next_retry_utc = null, updated_at_utc = now()
                where reminder_id = @id;
                """,
                (cmd, next, _, _) => cmd.Parameters.AddWithValue("next", next!.Value.ToUniversalTime()));
        }

        if (delivered)
        {
            return (
                """
                update reminders set status = 'delivered', next_retry_utc = null, updated_at_utc = now()
                where reminder_id = @id;
                """,
                (_, _, _, _) => { });
        }

        if (retry)
        {
            return (
                """
                update reminders set status = 'firing', next_retry_utc = @retry,
                    delivery_attempt_count = @count, updated_at_utc = now()
                where reminder_id = @id;
                """,
                (cmd, _, retryAt, count) =>
                {
                    cmd.Parameters.AddWithValue("retry", retryAt!.Value.ToUniversalTime());
                    cmd.Parameters.AddWithValue("count", count);
                });
        }

        return (
            """
            update reminders set status = 'delivery_failed', next_retry_utc = null, updated_at_utc = now()
            where reminder_id = @id;
            """,
            (_, _, _, _) => { });
    }

    /// <summary>
    /// Idempotently creates a recurring reminder: the reminder row, its recurrence
    /// rule, and the back-link, in one transaction. Returns the existing reminder on
    /// idempotent replay.
    /// </summary>
    public async Task<ReminderCreation> CreateRecurringAsync(
        PrincipalScope principal,
        string reminderText,
        DateTimeOffset firstOccurrenceUtc,
        TimeOnly originalLocalTime,
        string timezone,
        string deliveryChannel,
        string quotaTier,
        string cadence,
        string[]? daysOfWeek,
        int? dayOfMonth,
        string? endCondition,
        DateTimeOffset? endDateUtc,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        ReminderCreation creation;
        await using (var command = new NpgsqlCommand(
            """
            insert into reminders (
                tenant_id, context_id, user_id, reminder_text, scheduled_time_utc,
                original_time_local, timezone, reminder_type, delivery_channel,
                quota_tier, status, idempotency_key, correlation_id)
            values (
                @tenant, @context, @user, @text, @scheduled,
                @local_time, @tz, 'recurring', @channel,
                @tier, 'scheduled', @idem, @correlation)
            on conflict (tenant_id, idempotency_key)
            do update set updated_at_utc = reminders.updated_at_utc
            returning reminder_id, status, (xmax = 0) as is_new;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("tenant", principal.TenantId);
            command.Parameters.AddWithValue("context", ContextParam(principal));
            command.Parameters.AddWithValue("user", principal.UserId);
            command.Parameters.AddWithValue("text", reminderText);
            command.Parameters.AddWithValue("scheduled", firstOccurrenceUtc.ToUniversalTime());
            command.Parameters.Add(new NpgsqlParameter("local_time", NpgsqlDbType.Time) { Value = originalLocalTime.ToTimeSpan() });
            command.Parameters.AddWithValue("tz", timezone);
            command.Parameters.AddWithValue("channel", deliveryChannel);
            command.Parameters.AddWithValue("tier", quotaTier);
            command.Parameters.AddWithValue("idem", idempotencyKey);
            command.Parameters.AddWithValue("correlation", correlationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            creation = new ReminderCreation(reader.GetGuid(0), reader.GetBoolean(2), reader.GetString(1));
        }

        if (!creation.IsNew)
        {
            await transaction.CommitAsync(cancellationToken);
            return creation;
        }

        Guid ruleId;
        await using (var rule = new NpgsqlCommand(
            """
            insert into reminder_recurrence_rules (
                reminder_id, tenant_id, cadence, day_of_week, day_of_month,
                end_condition, end_date_utc)
            values (@reminder, @tenant, @cadence, @dow, @dom, @end_cond, @end_date)
            returning rule_id;
            """, connection, transaction))
        {
            rule.Parameters.AddWithValue("reminder", creation.ReminderId);
            rule.Parameters.AddWithValue("tenant", principal.TenantId);
            rule.Parameters.AddWithValue("cadence", cadence);
            rule.Parameters.Add(new NpgsqlParameter("dow", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = (object?)daysOfWeek ?? DBNull.Value
            });
            rule.Parameters.AddWithValue("dom", (object?)dayOfMonth ?? DBNull.Value);
            rule.Parameters.AddWithValue("end_cond", (object?)endCondition ?? DBNull.Value);
            rule.Parameters.AddWithValue("end_date", endDateUtc.HasValue ? endDateUtc.Value.ToUniversalTime() : (object)DBNull.Value);
            ruleId = (Guid)(await rule.ExecuteScalarAsync(cancellationToken))!;
        }

        await using (var link = new NpgsqlCommand(
            "update reminders set recurrence_rule_id = @rule where reminder_id = @id;", connection, transaction))
        {
            link.Parameters.AddWithValue("rule", ruleId);
            link.Parameters.AddWithValue("id", creation.ReminderId);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(connection, transaction, creation.ReminderId, principal,
            ReminderAuditEventType.Created, correlationId, new { reminder_type = "recurring", cadence }, cancellationToken);
        await WriteAuditAsync(connection, transaction, creation.ReminderId, principal,
            ReminderAuditEventType.Scheduled, correlationId,
            new { scheduled_time_utc = firstOccurrenceUtc.ToUniversalTime() }, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return creation;
    }

    /// <summary>
    /// Reads the recurrence rule + original local time for a recurring reminder,
    /// under its own (tenant, creator) scope. Returns null when there is no rule.
    /// </summary>
    public async Task<RecurrenceContext?> GetRecurrenceContextAsync(ClaimedReminder reminder, CancellationToken cancellationToken)
    {
        var principal = ScopeFor(reminder);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select r.original_time_local, rr.cadence, rr.day_of_week, rr.day_of_month,
                   rr.end_condition, rr.end_date_utc
            from reminders r
            join reminder_recurrence_rules rr on rr.rule_id = r.recurrence_rule_id
            where r.reminder_id = @id and rr.active = true;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", reminder.ReminderId);

        RecurrenceContext? context = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var localTime = reader.IsDBNull(0) ? new TimeOnly(9, 0) : TimeOnly.FromTimeSpan(reader.GetTimeSpan(0));
                var days = reader.IsDBNull(2) ? null : reader.GetFieldValue<string[]>(2);
                context = new RecurrenceContext(
                    new Time.ReminderRecurrence(
                        Cadence: reader.GetString(1),
                        DaysOfWeek: days,
                        DayOfMonth: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        EndCondition: reader.IsDBNull(4) ? null : reader.GetString(4),
                        EndDateUtc: reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)),
                    localTime);
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return context;
    }

    /// <summary>Marks an overdue reminder as expired_undelivered, under its own scope.</summary>
    public async Task MarkExpiredAsync(ClaimedReminder reminder, CancellationToken cancellationToken)
    {
        var principal = ScopeFor(reminder);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, principal.TenantId, principal.UserId, cancellationToken);

        await using (var update = new NpgsqlCommand(
            """
            update reminders set status = 'expired_undelivered', updated_at_utc = now()
            where reminder_id = @id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", reminder.ReminderId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(connection, transaction, reminder.ReminderId, principal,
            ReminderAuditEventType.ExpiredUndelivered, reminder.CorrelationId, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static PrincipalScope ScopeFor(ClaimedReminder reminder) =>
        new(reminder.TenantId, reminder.ContextId ?? Guid.Empty, reminder.UserId, Roles: null);

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reminderId,
        PrincipalScope principal,
        string eventType,
        string? correlationId,
        object? details,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reminder_audit_events (reminder_id, tenant_id, user_id, event_type, details, correlation_id)
            values (@id, @tenant, @user, @event_type, @details::jsonb, @correlation);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", reminderId);
        command.Parameters.AddWithValue("tenant", principal.TenantId);
        command.Parameters.AddWithValue("user", principal.UserId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Text)
        {
            Value = details is null ? "{}" : JsonSerializer.Serialize(details, JsonOptions)
        });
        command.Parameters.AddWithValue("correlation", (object?)correlationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ContextParam(PrincipalScope principal) =>
        principal.ContextId == Guid.Empty ? DBNull.Value : principal.ContextId;
}
