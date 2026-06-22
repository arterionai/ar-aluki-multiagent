using Aluki.Runtime.Memory;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Policies;
using Aluki.Runtime.Reminders.Security;
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

        // Recurring reminders are scheduled in a later milestone (US2).
        if (string.Equals(request.ReminderType, ReminderType.Recurring, StringComparison.OrdinalIgnoreCase)
            || request.Recurrence is not null)
        {
            return new ReminderHttpResult(422, new ReminderResponse(
                ReminderResponseStatus.Failed, correlationId,
                Error: new ReminderError(ReminderErrorCode.UnsupportedRecurrence,
                    "Recurring reminders are not yet supported (SB-005 US2).")));
        }

        var principal = ToScope(request.PrincipalContext!);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            _logger.LogWarning("reminder.scope_denied correlation_id={CorrelationId}", correlationId);
            return ScopeDenied(correlationId);
        }

        var timezone = ResolveTimezone(request.Timezone);
        var scheduledUtc = request.ScheduledTimeUtc!.Value;

        var quota = await _store.GetQuotaSnapshotAsync(principal, _options.DefaultQuotaLimit, cancellationToken);
        if (quota.ActiveCount >= quota.Limit)
        {
            return new ReminderHttpResult(409, new ReminderResponse(
                ReminderResponseStatus.QuotaExceeded, correlationId,
                Quota: new QuotaInfo(quota.ActiveCount, quota.Limit, quota.EntitlementTier),
                Error: new ReminderError(ReminderErrorCode.QuotaExceeded,
                    $"Active reminder limit reached ({quota.ActiveCount}/{quota.Limit}). Complete or cancel a reminder, or upgrade your plan.")));
        }

        var localTime = LocalTimeOfDay(scheduledUtc, timezone);
        var idempotencyKey = ReminderIdempotencyKey.Derive(request.ReminderId, principal.UserId, request.ReminderText!.Trim(), scheduledUtc);

        var creation = await _store.CreateOneShotAsync(
            principal, request.ReminderText!.Trim(), scheduledUtc, localTime, timezone,
            ResolveChannel(request.DeliveryChannel), quota.EntitlementTier, idempotencyKey, correlationId, cancellationToken);

        var dto = new ReminderDto(
            creation.ReminderId, request.ReminderText!.Trim(), scheduledUtc, timezone,
            ReminderType.OneShot, creation.Status, 0, ResolveChannel(request.DeliveryChannel));

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

                var deliveryRequest = new ReminderDeliveryRequest(
                    reminder.ReminderId, reminder.TenantId, reminder.UserId, reminder.ContextId,
                    reminder.ScheduledTimeUtc, AttemptNumber: 1, reminder.ReminderText,
                    reminder.DeliveryChannel, reminder.Timezone,
                    reminder.CorrelationId ?? reminder.ReminderId.ToString("N"));

                var result = await _deliveryChannel.DeliverAsync(deliveryRequest, cancellationToken);
                await _store.RecordDeliveryOutcomeAsync(reminder, attemptNumber: 1, result, _clock.GetUtcNow(), cancellationToken);
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
