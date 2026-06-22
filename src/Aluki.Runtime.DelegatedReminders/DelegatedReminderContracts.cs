using System.Text.Json.Serialization;

namespace Aluki.Runtime.DelegatedReminders;

// ── Status enums ────────────────────────────────────────────────────────────

public static class DelegatedReminderStatus
{
    public const string Draft = "draft";
    public const string AwaitingRecipientResolution = "awaiting_recipient_resolution";
    public const string AwaitingConsent = "awaiting_consent";
    public const string Scheduled = "scheduled";
    public const string DeliveryInProgress = "delivery_in_progress";
    public const string Delivered = "delivered";
    public const string DeliveryFailedTerminal = "delivery_failed_terminal";
    public const string Cancelled = "cancelled";
}

public static class DelegatedResolutionTier
{
    public const string Tier1KnownContactConfirmed = "tier1_known_contact_confirmed";
    public const string Tier2PhoneOnlyNeedsCapture = "tier2_phone_only_needs_capture";
    public const string Tier3UnknownNeedsClarification = "tier3_unknown_needs_clarification";
}

public static class DelegatedConsentStatus
{
    public const string OptedIn = "opted_in";
    public const string OptedOut = "opted_out";
    public const string Revoked = "revoked";
}

public static class DelegatedDeliveryStatus
{
    public const string Delivered = "delivered";
    public const string TransientFailure = "transient_failure";
    public const string PermanentFailure = "permanent_failure";
}

public static class DelegatedDeliveryFailureCategory
{
    public const string TransientExhausted = "transient_exhausted";
    public const string PermanentInvalidRecipient = "permanent_invalid_recipient";
    public const string PermanentPermission = "permanent_permission";
    public const string SystemError = "system_error";
}

public static class DelegatedAuditEventType
{
    public const string Created = "delegated_reminder.created";
    public const string RecipientResolved = "delegated_reminder.recipient_resolved";
    public const string ConsentAcquired = "delegated_reminder.consent_acquired";
    public const string DeliveryStarted = "delegated_reminder.delivery_started";
    public const string DeliverySucceeded = "delegated_reminder.delivery_succeeded";
    public const string DeliveryFailedTerminal = "delegated_reminder.delivery_failed_terminal";
    public const string Cancelled = "delegated_reminder.cancelled";
}

public static class DelegatedReminderErrorCode
{
    public const string InvalidPayload = "invalid_payload";
    public const string ScopeDenied = "scope_denied";
    public const string AntiSpamDenied = "anti_spam_denied";
    public const string NotFound = "not_found";
    public const string CancellationWindowExpired = "cancellation_window_expired";
    public const string RecallNotAllowed = "recall_not_allowed";
    public const string InternalError = "internal_error";
}

public static class DelegatedReminderResponseStatus
{
    public const string Created = "created";
    public const string AwaitingRecipientResolution = "awaiting_recipient_resolution";
    public const string AwaitingConsent = "awaiting_consent";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

// ── Request records ─────────────────────────────────────────────────────────

public sealed record DelegatedPrincipalContext(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid? ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId);

public sealed record CreateDelegatedReminderRequest(
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("delegated_reminder_id")] string? DelegatedReminderId,
    [property: JsonPropertyName("principal_context")] DelegatedPrincipalContext? PrincipalContext,
    [property: JsonPropertyName("sender_identity")] string? SenderIdentity,
    [property: JsonPropertyName("recipient_identity")] string? RecipientIdentity,
    [property: JsonPropertyName("recipient_name")] string? RecipientName,
    [property: JsonPropertyName("recipient_phone_e164")] string? RecipientPhoneE164,
    [property: JsonPropertyName("recipient_whatsapp_handle")] string? RecipientWhatsappHandle,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("due_time_utc")] DateTimeOffset? DueTimeUtc);

public sealed record CancelDelegatedReminderRequest(
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("principal_context")] DelegatedPrincipalContext? PrincipalContext);

// ── Response records ────────────────────────────────────────────────────────

public sealed record DelegatedReminderDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sender_identity")] string SenderIdentity,
    [property: JsonPropertyName("recipient_identity")] string RecipientIdentity,
    [property: JsonPropertyName("recipient_display_name")] string? RecipientDisplayName,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("due_time_utc")] DateTimeOffset DueTimeUtc,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("consent_acquired")] bool ConsentAcquired,
    [property: JsonPropertyName("resolution_tier")] string? ResolutionTier,
    [property: JsonPropertyName("cancel_deadline_utc")] DateTimeOffset CancelDeadlineUtc);

public sealed record DelegatedReminderResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("reminder")] DelegatedReminderDto? Reminder = null,
    [property: JsonPropertyName("error")] DelegatedReminderError? Error = null);

public sealed record DelegatedReminderListResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("reminders")] IReadOnlyList<DelegatedReminderDto> Reminders);

public sealed record DelegatedReminderError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record DelegatedReminderErrorResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>HTTP-shaped result for a delegated reminder interaction.</summary>
public sealed record DelegatedReminderHttpResult(int StatusCode, object Body);
