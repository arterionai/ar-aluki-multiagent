using Aluki.Runtime.Memory;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Policies;
using Aluki.Runtime.Reminders.Security;
using Aluki.Runtime.Reminders.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeZoneConverter;

namespace Aluki.Runtime.Reminders;

/// <summary>HTTP-shaped result for a reminder interaction.</summary>
public sealed record ReminderHttpResult(int StatusCode, object Body);

/// <summary>
/// Orchestrates reminder lifecycle operations: validate, scope-guard, quota-check,
/// idempotent create, snooze, cancel, list, and the background fire-sweep. Firing
/// is performed by a timer-triggered sweep (durable orchestration is a documented
/// follow-up); delivery goes through a pluggable <see cref="IReminderDeliveryChannel"/>.
/// </summary>
public sealed class ReminderService
{
    private const string DefaultTimezone = "America/Mexico_City";

    private readonly ReminderScopeGuard _scopeGuard;
    private readonly ReminderStore _store;
    private readonly IReminderDeliveryChannel _deliveryChannel;
    private readonly ReminderOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        ReminderScopeGuard scopeGuard,
        ReminderStore store,
        IReminderDeliveryChannel deliveryChannel,
        IOptions<ReminderOptions> options,
        ILogger<ReminderService> logger,
        TimeProvider? clock = null)
    {
        _scopeGuard = scopeGuard;
        _store = store;
        _deliveryChannel = deliveryChannel;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ReminderHttpResult> CreateAsync(CreateReminderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(request.CorrelationId);

        var validation = ValidateCreate(request);
        if (validation is not null)
        {
            return BadRequest(correlationId, validation);
        }

        var isRecurring = string.Equals(request.ReminderType, ReminderType.Recurring, StringComparison.OrdinalIgnoreCase)
            || request.Recurrence is not null;

        var timezone = ResolveTimezone(request.Timezone);
        var requestedUtc = request.ScheduledTimeUtc!.Value;
        var localTime = LocalTimeOfDay(requestedUtc, timezone) ?? TimeOnly.FromDateTime(requestedUtc.UtcDateTime);
        var channel = ResolveChannel(request.DeliveryChannel);
        var text = request.ReminderText!.Trim();

        // Build (and validate) the recurrence rule before any scope/DB work so bad
        // input fails fast with a 400.
        ResolvedRecurrence? recurrence = null;
        DateTimeOffset firstOccurrenceUtc = requestedUtc;
        if (isRecurring)
        {
            var (resolved, recurrenceError) = ResolveRecurrence(request.Recurrence, request.ReminderType, requestedUtc, localTime, timezone);
            if (recurrenceError is not null)
            {
                return BadRequest(correlationId, recurrenceError);
            }

            recurrence = resolved;
            firstOccurrenceUtc = recurrence!.FirstOccurrenceUtc;
        }

        var principal = ToScope(request.PrincipalContext!);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            _logger.LogWarning("reminder.scope_denied correlation_id={CorrelationId}", correlationId);
            return ScopeDenied(correlationId);
        }

        var quota = await _store.GetQuotaSnapshotAsync(principal, _options.DefaultQuotaLimit, cancellationToken);
        if (quota.ActiveCount >= quota.Limit)
        {
            return new ReminderHttpResult(409, new ReminderResponse(
                ReminderResponseStatus.QuotaExceeded, correlationId,
                Quota: new QuotaInfo(quota.ActiveCount, quota.Limit, quota.EntitlementTier),
                Error: new ReminderError(ReminderErrorCode.QuotaExceeded,
                    $"Active reminder limit reached ({quota.ActiveCount}/{quota.Limit}). Complete or cancel a reminder, or upgrade your plan.")));
        }

        var idempotencyKey = ReminderIdempotencyKey.Derive(request.ReminderId, principal.UserId, text, firstOccurrenceUtc);

        ReminderCreation creation;
        string reminderType;
        if (recurrence is not null)
        {
            reminderType = ReminderType.Recurring;
            creation = await _store.CreateRecurringAsync(
                principal, text, firstOccurrenceUtc, localTime, timezone, channel, quota.EntitlementTier,
                recurrence.Cadence, recurrence.DaysOfWeek, recurrence.DayOfMonth, recurrence.EndCondition,
                recurrence.EndDateUtc, idempotencyKey, correlationId, cancellationToken);
        }
        else
        {
            reminderType = ReminderType.OneShot;
            creation = await _store.CreateOneShotAsync(
                principal, text, firstOccurrenceUtc, localTime, timezone, channel, quota.EntitlementTier,
                idempotencyKey, correlationId, cancellationToken);
        }

        var dto = new ReminderDto(
            creation.ReminderId, text, firstOccurrenceUtc, timezone,
            reminderType, creation.Status, 0, channel);

        return new ReminderHttpResult(creation.IsNew ? 201 : 200, new ReminderResponse(
            ReminderResponseStatus.Created, correlationId, Reminder: dto,
            Quota: new QuotaInfo(quota.ActiveCount + (creation.IsNew ? 1 : 0), quota.Limit, quota.EntitlementTier)));
    }

    public async Task<ReminderHttpResult> SnoozeAsync(Guid reminderId, SnoozeReminderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(request.CorrelationId);
        if (request.PrincipalContext is not { } pc || pc.TenantId == Guid.Empty || pc.UserId == Guid.Empty)
        {
            return BadRequest(correlationId, "principal_context with tenant_id and user_id is required.");
        }

        var principal = ToScope(pc);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            return ScopeDenied(correlationId);
        }

        var duration = ReminderSnoozePolicy.ResolveDurationSeconds(request.SnoozeDurationSeconds, _options.MaxSnoozeSeconds);
        var newTime = ReminderSnoozePolicy.NextFireTime(_clock.GetUtcNow(), duration);

        var dto = await _store.SnoozeAsync(principal, reminderId, newTime, duration, cancellationToken);
        if (dto is null)
        {
            return NotFound(correlationId, "Reminder not found in scope or not snoozable.");
        }

        return new ReminderHttpResult(200, new ReminderResponse(ReminderResponseStatus.Snoozed, correlationId, Reminder: dto));
    }

    public async Task<ReminderHttpResult> CancelAsync(Guid reminderId, CancelReminderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(request.CorrelationId);
        if (request.PrincipalContext is not { } pc || pc.TenantId == Guid.Empty || pc.UserId == Guid.Empty)
        {
            return BadRequest(correlationId, "principal_context with tenant_id and user_id is required.");
        }

        var principal = ToScope(pc);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            return ScopeDenied(correlationId);
        }

        var cancelled = await _store.CancelAsync(principal, reminderId, cancellationToken);
        return cancelled
            ? new ReminderHttpResult(200, new ReminderResponse(ReminderResponseStatus.Cancelled, correlationId))
            : NotFound(correlationId, "Reminder not found in scope.");
    }

    public async Task<ReminderHttpResult> ListAsync(ReminderPrincipalContext? principalContext, string? correlationId, CancellationToken cancellationToken)
    {
        var cid = Correlation(correlationId);
        if (principalContext is not { } pc || pc.TenantId == Guid.Empty || pc.UserId == Guid.Empty)
        {
            return BadRequest(cid, "principal_context with tenant_id and user_id is required.");
        }

        var principal = ToScope(pc);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            return ScopeDenied(cid);
        }

        var reminders = await _store.ListAsync(principal, cancellationToken);
        return new ReminderHttpResult(200, new ReminderListResponse(cid, reminders));
    }

    /// <summary>
    /// Background sweep: claims due reminders and delivers them (or expires the
    /// overdue ones). Returns the number of reminders processed. Idempotent and
    /// safe to run concurrently (claim uses SKIP LOCKED).
    /// </summary>
    public async Task<int> FireDueAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var due = await _store.ClaimDueAsync(_options.SweepBatchSize, now, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var overdueCutoff = now.AddHours(-_options.OverdueExpiryHours);
        foreach (var reminder in due)
        {
            try
            {
                if (reminder.ScheduledTimeUtc < overdueCutoff)
                {
                    await _store.MarkExpiredAsync(reminder, cancellationToken);
                    continue;
                }

                var attemptNumber = reminder.AttemptCount + 1;
                var deliveryRequest = new ReminderDeliveryRequest(
                    reminder.ReminderId, reminder.TenantId, reminder.UserId, reminder.ContextId,
                    reminder.ScheduledTimeUtc, attemptNumber, reminder.ReminderText,
                    reminder.DeliveryChannel, reminder.Timezone,
                    reminder.CorrelationId ?? reminder.ReminderId.ToString("N"));

                var result = await _deliveryChannel.DeliverAsync(deliveryRequest, cancellationToken);
                var deliveredAt = _clock.GetUtcNow();

                if (result.Status == DeliveryStatus.Delivered)
                {
                    // Recurring reminders re-arm to their next occurrence; one-shots go terminal.
                    DateTimeOffset? next = null;
                    if (string.Equals(reminder.ReminderType, ReminderType.Recurring, StringComparison.Ordinal))
                    {
                        var context = await _store.GetRecurrenceContextAsync(reminder, cancellationToken);
                        if (context is not null)
                        {
                            next = ReminderRecurrenceCalculator.NextOccurrence(
                                context.Rule, context.LocalTime, reminder.Timezone, reminder.ScheduledTimeUtc);
                        }
                    }

                    await _store.RecordDeliveryOutcomeAsync(reminder, attemptNumber, result, deliveredAt, cancellationToken, rescheduleToUtc: next);
                    continue;
                }

                // Transient failures with attempts remaining are retried with backoff;
                // permanent failures and exhausted retries go terminal (delivery_failed).
                var retryAt = result.Status == DeliveryStatus.PermanentFailure
                    ? null
                    : ReminderRetryPolicy.NextRetry(deliveredAt, attemptNumber, _options.MaxDeliveryAttempts);

                await _store.RecordDeliveryOutcomeAsync(reminder, attemptNumber, result, deliveredAt, cancellationToken, nextRetryUtc: retryAt);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A single reminder's failure must not block the rest of the batch.
                _logger.LogError(ex, "reminder.fire_failed reminder_id={ReminderId}", reminder.ReminderId);
            }
        }

        return due.Count;
    }

    private string? ValidateCreate(CreateReminderRequest request)
    {
        if (request.PrincipalContext is not { } pc || pc.TenantId == Guid.Empty || pc.UserId == Guid.Empty)
        {
            return "principal_context with tenant_id and user_id is required.";
        }

        if (string.IsNullOrWhiteSpace(request.ReminderText))
        {
            return "reminder_text is required.";
        }

        var text = request.ReminderText.Trim();
        if (text.Length is < 1 or > 500)
        {
            return "reminder_text must be between 1 and 500 characters.";
        }

        if (request.ScheduledTimeUtc is not { } scheduled)
        {
            return "scheduled_time_utc is required.";
        }

        if (scheduled.ToUniversalTime() <= _clock.GetUtcNow())
        {
            return "scheduled_time_utc must be in the future.";
        }

        if (!string.IsNullOrWhiteSpace(request.Timezone) && !TZConvert.TryGetTimeZoneInfo(request.Timezone, out _))
        {
            return $"timezone '{request.Timezone}' is not a valid IANA identifier.";
        }

        return null;
    }

    private sealed record ResolvedRecurrence(
        string Cadence, string[]? DaysOfWeek, int? DayOfMonth,
        string? EndCondition, DateTimeOffset? EndDateUtc, DateTimeOffset FirstOccurrenceUtc);

    /// <summary>
    /// Validates the recurrence request, deriving the day-of-week / day-of-month
    /// from the requested start when not supplied, and computes the first
    /// occurrence on or after the start. Returns an error message on invalid input.
    /// </summary>
    private (ResolvedRecurrence? Resolved, string? Error) ResolveRecurrence(
        ReminderRecurrenceInput? input, string? reminderType, DateTimeOffset requestedUtc, TimeOnly localTime, string timezone)
    {
        var cadence = (input?.Cadence ?? string.Empty).Trim().ToLowerInvariant();
        if (cadence is not (ReminderCadence.Daily or ReminderCadence.Weekly or ReminderCadence.Monthly))
        {
            return (null, "recurrence.cadence must be one of daily, weekly, monthly.");
        }

        TZConvert.TryGetTimeZoneInfo(timezone, out var tz);
        var localStart = TimeZoneInfo.ConvertTime(requestedUtc.ToUniversalTime(), tz!);

        string[]? days = null;
        int? dayOfMonth = null;

        if (cadence == ReminderCadence.Weekly)
        {
            if (input?.DayOfWeek is { Length: > 0 } provided)
            {
                var normalized = new List<string>();
                foreach (var d in provided)
                {
                    if (!ReminderRecurrenceCalculator.TryParseDay(d, out var dow))
                    {
                        return (null, $"recurrence.day_of_week contains an invalid day: '{d}'.");
                    }

                    normalized.Add(Abbrev(dow));
                }

                days = normalized.Distinct().ToArray();
            }
            else
            {
                // Derive the weekday from the requested start.
                days = new[] { Abbrev(localStart.DayOfWeek) };
            }
        }
        else if (cadence == ReminderCadence.Monthly)
        {
            if (input?.DayOfMonth is { } dom)
            {
                if (dom is < 1 or > 31)
                {
                    return (null, "recurrence.day_of_month must be between 1 and 31.");
                }

                dayOfMonth = dom;
            }
            else
            {
                dayOfMonth = localStart.Day;
            }
        }

        var endCondition = input?.EndCondition?.Trim().ToLowerInvariant();
        if (endCondition is not (null or "never" or "until_date"))
        {
            return (null, "recurrence.end_condition must be 'never' or 'until_date'.");
        }

        DateTimeOffset? endDate = null;
        if (endCondition == "until_date")
        {
            if (input?.EndDateUtc is not { } end || end.ToUniversalTime() <= requestedUtc.ToUniversalTime())
            {
                return (null, "recurrence.end_date_utc must be after the start when end_condition is 'until_date'.");
            }

            endDate = end;
        }

        var rule = new ReminderRecurrence(cadence, days, dayOfMonth, endCondition, endDate);
        var first = ReminderRecurrenceCalculator.FirstOccurrenceOnOrAfter(rule, localTime, timezone, requestedUtc);
        if (first is null)
        {
            return (null, "Could not compute a recurrence occurrence for the given rule.");
        }

        return (new ResolvedRecurrence(cadence, days, dayOfMonth, endCondition, endDate, first.Value), null);
    }

    private static string Abbrev(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        _ => "Sun"
    };

    private static string ResolveTimezone(string? timezone) =>
        string.IsNullOrWhiteSpace(timezone) ? DefaultTimezone : timezone!.Trim();

    private static string ResolveChannel(string? channel) =>
        string.IsNullOrWhiteSpace(channel) ? "in_app" : channel!.Trim().ToLowerInvariant();

    private static TimeOnly? LocalTimeOfDay(DateTimeOffset scheduledUtc, string timezone)
    {
        if (!TZConvert.TryGetTimeZoneInfo(timezone, out var tz))
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(scheduledUtc.ToUniversalTime(), tz);
        return TimeOnly.FromTimeSpan(local.TimeOfDay);
    }

    private static PrincipalScope ToScope(ReminderPrincipalContext pc) =>
        new(pc.TenantId, pc.ContextId ?? Guid.Empty, pc.UserId, Roles: null);

    private static string Correlation(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId!;

    private static ReminderHttpResult BadRequest(string correlationId, string message) =>
        new(400, new ReminderErrorResponse(correlationId, ReminderErrorCode.InvalidPayload, message));

    private static ReminderHttpResult ScopeDenied(string correlationId) =>
        new(403, new ReminderErrorResponse(correlationId, ReminderErrorCode.ScopeDenied, "Principal is not authorized for the requested scope."));

    private static ReminderHttpResult NotFound(string correlationId, string message) =>
        new(404, new ReminderErrorResponse(correlationId, ReminderErrorCode.NotFound, message));
}
