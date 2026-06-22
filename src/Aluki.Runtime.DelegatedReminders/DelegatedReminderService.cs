using Aluki.Runtime.DelegatedReminders.Configuration;
using Aluki.Runtime.DelegatedReminders.Delivery;
using Aluki.Runtime.DelegatedReminders.Persistence;
using Aluki.Runtime.DelegatedReminders.Policies;
using Aluki.Runtime.DelegatedReminders.Security;
using Aluki.Runtime.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.DelegatedReminders;

/// <summary>
/// Orchestrates delegated reminder lifecycle: validate, scope-guard, anti-spam,
/// three-tier recipient resolution, consent gating, idempotent create, cancel,
/// and the background fire-sweep with bounded exponential retry.
/// </summary>
public sealed class DelegatedReminderService
{
    private readonly DelegatedReminderScopeGuard _scopeGuard;
    private readonly DelegatedReminderStore _store;
    private readonly IDelegatedReminderDeliveryChannel _deliveryChannel;
    private readonly DelegatedReminderOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DelegatedReminderService> _logger;

    public DelegatedReminderService(
        DelegatedReminderScopeGuard scopeGuard,
        DelegatedReminderStore store,
        IDelegatedReminderDeliveryChannel deliveryChannel,
        IOptions<DelegatedReminderOptions> options,
        ILogger<DelegatedReminderService> logger,
        TimeProvider? clock = null)
    {
        _scopeGuard = scopeGuard;
        _store = store;
        _deliveryChannel = deliveryChannel;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public async Task<DelegatedReminderHttpResult> CreateAsync(
        CreateDelegatedReminderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(request.CorrelationId);

        var validation = ValidateCreate(request);
        if (validation is not null)
        {
            return BadRequest(correlationId, validation);
        }

        var principal = ToScope(request.PrincipalContext!);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            _logger.LogWarning("delegated_reminder.scope_denied correlation_id={CorrelationId}", correlationId);
            return ScopeDenied(correlationId);
        }

        // Anti-spam: rolling 24-hour window per sender
        var dailyCount = await _store.GetAntiSpamCountAsync(principal, cancellationToken);
        if (dailyCount >= _options.DailyAntiSpamLimit)
        {
            _logger.LogWarning(
                "delegated_reminder.anti_spam_denied sender={UserId} count={Count} limit={Limit}",
                principal.UserId, dailyCount, _options.DailyAntiSpamLimit);
            return AntiSpamDenied(correlationId, dailyCount, _options.DailyAntiSpamLimit);
        }

        var content = request.Content!.Trim();
        var senderIdentity = request.SenderIdentity!.Trim();
        var recipientIdentity = NormalizeRecipientIdentity(request);
        var (resolutionTier, consentAcquired, initialStatus) = DetermineResolutionAndStatus(request, recipientIdentity, principal);

        // If consented tier-1 recipient: check consent registry before scheduling
        if (resolutionTier == DelegatedResolutionTier.Tier1KnownContactConfirmed && !consentAcquired)
        {
            var consent = await _store.GetConsentAsync(
                principal.TenantId, principal.UserId, recipientIdentity, cancellationToken);

            if (consent is not null)
            {
                consentAcquired = true;
                initialStatus = DelegatedReminderStatus.Scheduled;
            }
            else
            {
                initialStatus = DelegatedReminderStatus.AwaitingConsent;
            }
        }

        var (reminderId, isNew, status) = await _store.CreateAsync(
            principal,
            senderIdentity,
            recipientIdentity,
            request.RecipientName?.Trim(),
            request.RecipientPhoneE164?.Trim(),
            request.RecipientWhatsappHandle?.Trim(),
            resolutionTier,
            content,
            request.DueTimeUtc!.Value,
            consentAcquired,
            initialStatus,
            request.DelegatedReminderId,
            correlationId,
            cancellationToken);

        var dto = new DelegatedReminderDto(
            reminderId,
            senderIdentity,
            recipientIdentity,
            request.RecipientName?.Trim(),
            content,
            request.DueTimeUtc.Value,
            status,
            consentAcquired,
            resolutionTier,
            request.DueTimeUtc.Value.AddSeconds(-_options.CancellationWindowSeconds));

        var responseStatus = status switch
        {
            DelegatedReminderStatus.Scheduled => DelegatedReminderResponseStatus.Created,
            DelegatedReminderStatus.AwaitingConsent => DelegatedReminderResponseStatus.AwaitingConsent,
            DelegatedReminderStatus.AwaitingRecipientResolution => DelegatedReminderResponseStatus.AwaitingRecipientResolution,
            _ => DelegatedReminderResponseStatus.Created
        };

        return new DelegatedReminderHttpResult(isNew ? 201 : 200,
            new DelegatedReminderResponse(responseStatus, correlationId, Reminder: dto));
    }

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<DelegatedReminderHttpResult> ListAsync(
        DelegatedPrincipalContext? principalContext, string? correlationId, CancellationToken cancellationToken)
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

        var records = await _store.ListBySenderAsync(principal, cancellationToken);
        var dtos = records.Select(r => ToDto(r, _options.CancellationWindowSeconds)).ToArray();
        return new DelegatedReminderHttpResult(200, new DelegatedReminderListResponse(cid, dtos));
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public async Task<DelegatedReminderHttpResult> CancelAsync(
        Guid reminderId, CancelDelegatedReminderRequest request, CancellationToken cancellationToken)
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

        var now = _clock.GetUtcNow();
        var (success, failureCode) = await _store.CancelAsync(principal, reminderId, now, correlationId, cancellationToken);

        if (!success)
        {
            return failureCode switch
            {
                DelegatedReminderErrorCode.NotFound =>
                    NotFound(correlationId, "Delegated reminder not found in scope."),
                DelegatedReminderErrorCode.CancellationWindowExpired =>
                    new DelegatedReminderHttpResult(409, new DelegatedReminderErrorResponse(correlationId,
                        DelegatedReminderErrorCode.CancellationWindowExpired,
                        $"Cancellation window has expired (30 seconds before due time). The reminder cannot be cancelled.")),
                DelegatedReminderErrorCode.RecallNotAllowed =>
                    new DelegatedReminderHttpResult(409, new DelegatedReminderErrorResponse(correlationId,
                        DelegatedReminderErrorCode.RecallNotAllowed,
                        "Reminder is in delivery phase or already terminal. Recall is not allowed.")),
                _ => new DelegatedReminderHttpResult(409, new DelegatedReminderErrorResponse(correlationId,
                    DelegatedReminderErrorCode.InternalError, "Cancel failed."))
            };
        }

        return new DelegatedReminderHttpResult(200,
            new DelegatedReminderResponse(DelegatedReminderResponseStatus.Cancelled, correlationId));
    }

    // ── Background sweep ─────────────────────────────────────────────────────

    /// <summary>
    /// Claims due delegated reminders and delivers them. Returns the count processed.
    /// Idempotent and concurrency-safe (claim uses SKIP LOCKED).
    /// </summary>
    public async Task<int> FireDueAsync(CancellationToken cancellationToken)
    {
        var due = await _store.ClaimDueAsync(_options.SweepBatchSize, _clock.GetUtcNow(), cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var reminder in due)
        {
            try
            {
                var attemptNumber = reminder.DeliveryAttemptCount + 1;
                var startedAt = _clock.GetUtcNow();

                // Re-verify consent before delivery (recipient may have revoked)
                var consent = await _store.GetConsentAsync(
                    reminder.TenantId, reminder.SenderUserId, reminder.RecipientIdentity, cancellationToken);

                if (consent is null)
                {
                    _logger.LogWarning(
                        "delegated_reminder.no_consent_at_delivery id={Id} recipient={Recipient}",
                        reminder.Id, reminder.RecipientIdentity);

                    await _store.MarkTerminalAsync(reminder,
                        DelegatedDeliveryFailureCategory.PermanentPermission,
                        "Recipient consent not present at delivery time.",
                        startedAt, attemptNumber, cancellationToken);
                    continue;
                }

                var deliveryRequest = new DelegatedDeliveryRequest(
                    reminder.Id,
                    reminder.TenantId,
                    reminder.SenderUserId,
                    reminder.SenderIdentity,
                    reminder.RecipientIdentity,
                    reminder.Content,
                    reminder.DueTimeUtc,
                    attemptNumber,
                    reminder.CorrelationId ?? reminder.Id.ToString("N"));

                var result = await _deliveryChannel.DeliverAsync(deliveryRequest, cancellationToken);
                var completedAt = _clock.GetUtcNow();

                if (result.Status == DelegatedDeliveryStatus.Delivered)
                {
                    await _store.MarkDeliveredAsync(reminder, result.NotificationId, completedAt, attemptNumber, cancellationToken);
                    _logger.LogInformation(
                        "delegated_reminder.delivered id={Id} attempt={Attempt}", reminder.Id, attemptNumber);
                }
                else if (result.IsPermanentFailure)
                {
                    await _store.MarkTerminalAsync(reminder,
                        result.FailureCategory ?? DelegatedDeliveryFailureCategory.SystemError,
                        result.FailureMessage, completedAt, attemptNumber, cancellationToken);

                    NotifySenderOfFailure(reminder, result.FailureCategory, result.FailureMessage);
                }
                else
                {
                    // Transient failure
                    var retryAt = DelegatedReminderRetryPolicy.NextRetry(
                        completedAt, attemptNumber, _options.MaxDeliveryAttempts);

                    if (retryAt is null)
                    {
                        // Retries exhausted
                        await _store.MarkTerminalAsync(reminder,
                            DelegatedDeliveryFailureCategory.TransientExhausted,
                            result.FailureMessage, completedAt, attemptNumber, cancellationToken);

                        NotifySenderOfFailure(reminder, DelegatedDeliveryFailureCategory.TransientExhausted,
                            "Maximum delivery attempts exhausted.");
                    }
                    else
                    {
                        await _store.RecordTransientFailureAsync(reminder,
                            result.FailureCategory ?? DelegatedDeliveryFailureCategory.SystemError,
                            result.FailureMessage, completedAt, attemptNumber, retryAt.Value, cancellationToken);

                        _logger.LogInformation(
                            "delegated_reminder.retry_scheduled id={Id} attempt={Attempt} retry_at={RetryAt}",
                            reminder.Id, attemptNumber, retryAt.Value);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "delegated_reminder.fire_failed id={Id}", reminder.Id);
            }
        }

        return due.Count;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void NotifySenderOfFailure(
        ClaimedDelegatedReminder reminder, string? failureCategory, string? failureMessage)
    {
        // Stub: log the notification. Real implementation sends a WhatsApp message
        // to the sender's identity via IWhatsAppMessenger (follow-up).
        _logger.LogWarning(
            "delegated_reminder.sender_failure_notification id={Id} sender={Sender} " +
            "recipient={Recipient} category={Category} message={Message}",
            reminder.Id, reminder.SenderIdentity, reminder.RecipientIdentity,
            failureCategory, failureMessage);
    }

    private string? ValidateCreate(CreateDelegatedReminderRequest request)
    {
        if (request.PrincipalContext is not { } pc || pc.TenantId == Guid.Empty || pc.UserId == Guid.Empty)
        {
            return "principal_context with tenant_id and user_id is required.";
        }

        if (string.IsNullOrWhiteSpace(request.SenderIdentity))
        {
            return "sender_identity is required.";
        }

        if (string.IsNullOrWhiteSpace(request.RecipientIdentity)
            && string.IsNullOrWhiteSpace(request.RecipientPhoneE164)
            && string.IsNullOrWhiteSpace(request.RecipientWhatsappHandle))
        {
            return "At least one of recipient_identity, recipient_phone_e164, or recipient_whatsapp_handle is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return "content is required.";
        }

        var content = request.Content.Trim();
        if (content.Length is < 1 or > 1000)
        {
            return "content must be between 1 and 1000 characters.";
        }

        if (request.DueTimeUtc is not { } due)
        {
            return "due_time_utc is required.";
        }

        if (due.ToUniversalTime() <= _clock.GetUtcNow())
        {
            return "due_time_utc must be in the future.";
        }

        return null;
    }

    private static string NormalizeRecipientIdentity(CreateDelegatedReminderRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RecipientIdentity))
        {
            return request.RecipientIdentity!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.RecipientWhatsappHandle))
        {
            return request.RecipientWhatsappHandle!.Trim();
        }

        return request.RecipientPhoneE164!.Trim();
    }

    private static (string tier, bool consentAcquired, string status) DetermineResolutionAndStatus(
        CreateDelegatedReminderRequest request, string recipientIdentity, PrincipalScope principal)
    {
        // Tier 1: WhatsApp handle provided (confirmed contact)
        if (!string.IsNullOrWhiteSpace(request.RecipientWhatsappHandle))
        {
            return (DelegatedResolutionTier.Tier1KnownContactConfirmed, false, DelegatedReminderStatus.AwaitingConsent);
        }

        // Tier 2: Phone number only
        if (!string.IsNullOrWhiteSpace(request.RecipientPhoneE164))
        {
            return (DelegatedResolutionTier.Tier2PhoneOnlyNeedsCapture, false,
                DelegatedReminderStatus.AwaitingRecipientResolution);
        }

        // Tier 3: Identity string only (unknown contact)
        return (DelegatedResolutionTier.Tier3UnknownNeedsClarification, false,
            DelegatedReminderStatus.AwaitingRecipientResolution);
    }

    private static DelegatedReminderDto ToDto(DelegatedReminderRecord record, int cancellationWindowSeconds) =>
        new(
            record.Id,
            record.SenderIdentity,
            record.RecipientIdentity,
            record.RecipientDisplayName,
            record.Content,
            record.DueTimeUtc,
            record.Status,
            record.ConsentAcquired,
            record.ResolutionTier,
            record.CancelDeadlineUtc);

    private static PrincipalScope ToScope(DelegatedPrincipalContext pc) =>
        new(pc.TenantId, pc.ContextId ?? Guid.Empty, pc.UserId, Roles: null);

    private static string Correlation(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId!;

    private static DelegatedReminderHttpResult BadRequest(string correlationId, string message) =>
        new(400, new DelegatedReminderErrorResponse(correlationId, DelegatedReminderErrorCode.InvalidPayload, message));

    private static DelegatedReminderHttpResult ScopeDenied(string correlationId) =>
        new(403, new DelegatedReminderErrorResponse(correlationId, DelegatedReminderErrorCode.ScopeDenied,
            "Principal is not authorized for the requested scope."));

    private static DelegatedReminderHttpResult NotFound(string correlationId, string message) =>
        new(404, new DelegatedReminderErrorResponse(correlationId, DelegatedReminderErrorCode.NotFound, message));

    private static DelegatedReminderHttpResult AntiSpamDenied(string correlationId, int count, int limit) =>
        new(429, new DelegatedReminderErrorResponse(correlationId, DelegatedReminderErrorCode.AntiSpamDenied,
            $"Delegated reminder daily limit reached ({count}/{limit}). Wait 24 hours or reduce volume."));
}
