using System.Text.Json.Serialization;

namespace Aluki.Runtime.Reminders;

/// <summary>Reminder type (data-model §reminders).</summary>
public static class ReminderType
{
    public const string OneShot = "one_shot";
    public const string Recurring = "recurring";
}

/// <summary>Reminder lifecycle states (data-model state machine).</summary>
public static class ReminderStatus
{
    public const string Scheduled = "scheduled";
    public const string Firing = "firing";
    public const string Delivered = "delivered";
    public const string DeliveryFailed = "delivery_failed";
    public const string ExpiredUndelivered = "expired_undelivered";
    public const string UserCancelled = "user_cancelled";
}

/// <summary>Delivery attempt outcome (delivery contract).</summary>
public static class DeliveryStatus
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string TransientFailure = "transient_failure";
    public const string PermanentFailure = "permanent_failure";
    public const string RetryScheduled = "retry_scheduled";
}

/// <summary>Delivery failure classification.</summary>
public static class DeliveryFailureCategory
{
    public const string NetworkTimeout = "network_timeout";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InvalidRecipient = "invalid_recipient";
    public const string RateLimited = "rate_limited";
    public const string Unknown = "unknown";
}

public static class ReminderCadence
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

public static class ReminderAuditEventType
{
    public const string Created = "created";
    public const string Scheduled = "scheduled";
    public const string Firing = "firing";
    public const string Delivered = "delivered";
    public const string DeliveryFailed = "delivery_failed";
    public const string Snoozed = "snoozed";
    public const string Cancelled = "cancelled";
    public const string QuotaChecked = "quota_checked";
    public const string QuotaBlocked = "quota_blocked";
    public const string ExpiredUndelivered = "expired_undelivered";
}

public static class ReminderResponseStatus
{
    public const string Created = "created";
    public const string QuotaExceeded = "quota_exceeded";
    public const string Snoozed = "snoozed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public static class ReminderErrorCode
{
    public const string InvalidPayload = "invalid_payload";
    public const string QuotaExceeded = "quota_exceeded";
    public const string ScopeDenied = "scope_denied";
    public const string NotFound = "not_found";
    public const string UnsupportedRecurrence = "unsupported_recurrence";
    public const string InternalError = "internal_error";
}

// ---- Requests -------------------------------------------------------------

public sealed record ReminderPrincipalContext(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid? ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId);

public sealed record ReminderRecurrenceInput(
    [property: JsonPropertyName("cadence")] string? Cadence,
    [property: JsonPropertyName("day_of_week")] string[]? DayOfWeek,
    [property: JsonPropertyName("day_of_month")] int? DayOfMonth,
    [property: JsonPropertyName("end_condition")] string? EndCondition,
    [property: JsonPropertyName("end_date_utc")] DateTimeOffset? EndDateUtc);

public sealed record CreateReminderRequest(
    [property: JsonPropertyName("reminder_id")] string? ReminderId,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("principal_context")] ReminderPrincipalContext? PrincipalContext,
    [property: JsonPropertyName("reminder_text")] string? ReminderText,
    [property: JsonPropertyName("scheduled_time_utc")] DateTimeOffset? ScheduledTimeUtc,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("reminder_type")] string? ReminderType,
    [property: JsonPropertyName("recurrence")] ReminderRecurrenceInput? Recurrence,
    [property: JsonPropertyName("delivery_channel")] string? DeliveryChannel);

public sealed record SnoozeReminderRequest(
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("principal_context")] ReminderPrincipalContext? PrincipalContext,
    [property: JsonPropertyName("snooze_duration_seconds")] int? SnoozeDurationSeconds);

public sealed record CancelReminderRequest(
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("principal_context")] ReminderPrincipalContext? PrincipalContext);

// ---- Responses ------------------------------------------------------------

public sealed record ReminderDto(
    [property: JsonPropertyName("reminder_id")] Guid ReminderId,
    [property: JsonPropertyName("reminder_text")] string ReminderText,
    [property: JsonPropertyName("scheduled_time_utc")] DateTimeOffset ScheduledTimeUtc,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("reminder_type")] string ReminderType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("snooze_count")] int SnoozeCount,
    [property: JsonPropertyName("delivery_channel")] string DeliveryChannel);

public sealed record ReminderResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("reminder")] ReminderDto? Reminder = null,
    [property: JsonPropertyName("quota")] QuotaInfo? Quota = null,
    [property: JsonPropertyName("error")] ReminderError? Error = null);

public sealed record QuotaInfo(
    [property: JsonPropertyName("active_count")] int ActiveCount,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("entitlement_tier")] string EntitlementTier);

public sealed record ReminderError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record ReminderListResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("reminders")] IReadOnlyList<ReminderDto> Reminders);

public sealed record ReminderErrorResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
