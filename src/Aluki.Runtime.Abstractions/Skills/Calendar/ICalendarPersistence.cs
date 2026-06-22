namespace Aluki.Runtime.Abstractions.Skills.Calendar;

public interface ICalendarConnectionRepository
{
    Task<CalendarConnectionRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarConnectionRecord>> GetAllActiveAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default);
    Task UpsertAsync(CalendarConnectionRecord record, CancellationToken ct = default);
}

public interface IOAuthCallbackStateRepository
{
    Task CreateAsync(OAuthCallbackStateRecord record, CancellationToken ct = default);
    Task<OAuthCallbackStateRecord?> GetByNonceAsync(string nonce, CancellationToken ct = default);
    Task MarkConsumedAsync(Guid id, DateTimeOffset consumedAt, CancellationToken ct = default);
    Task MarkRejectedAsync(Guid id, CancellationToken ct = default);
}

public interface IEventCreationRequestRepository
{
    Task CreateAsync(EventCreationRequestRecord record, CancellationToken ct = default);
    Task<EventCreationRequestRecord?> GetAsync(Guid id, CancellationToken ct = default);
}

public interface IDeduplicationRepository
{
    Task<DeduplicationRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string idempotencyKey, CancellationToken ct = default);
    Task CreateAsync(DeduplicationRecord record, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, DeduplicationStatus status, string? providerEventRef, CancellationToken ct = default);
}

public interface ICalendarOutcomeRepository
{
    Task CreateAsync(CalendarEventOutcomeRecord record, CancellationToken ct = default);
    Task<CalendarEventOutcomeRecord?> GetAsync(Guid id, CancellationToken ct = default);
}

public interface ICalendarAuditRepository
{
    Task AppendAsync(CalendarAuditRecord record, CancellationToken ct = default);
}

public sealed record CalendarConnectionRecord(
    Guid CalendarConnectionId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    CalendarProvider Provider,
    ConnectionStatus Status,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? DisconnectedAtUtc,
    string? ProviderAccountRef,
    bool DefaultForUser,
    string CorrelationId);

public sealed record OAuthCallbackStateRecord(
    Guid OAuthCallbackStateId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    CalendarProvider Provider,
    string StateNonce,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? UsedAtUtc,
    OAuthCallbackStatus Status,
    string CorrelationId);

public sealed record EventCreationRequestRecord(
    Guid EventCreationRequestId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    string? ProviderHint,
    string Title,
    string StartLocal,
    string? EndLocal,
    string CanonicalTimezone,
    TimezoneResolutionSource TimezoneResolutionSource,
    string NormalizedPayloadHash,
    DateTimeOffset RequestedAtUtc,
    string CorrelationId);

public sealed record DeduplicationRecord(
    Guid DeduplicationRecordId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    CalendarProvider Provider,
    string IdempotencyKey,
    DateTimeOffset WindowStartedAtUtc,
    DateTimeOffset WindowExpiresAtUtc,
    string FirstOutcomeRef,
    string? FirstProviderEventRef,
    DeduplicationStatus Status);

public sealed record CalendarEventOutcomeRecord(
    Guid CalendarEventOutcomeId,
    Guid EventCreationRequestId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    CalendarProvider Provider,
    CalendarOutcomeType OutcomeType,
    string OutcomeReference,
    string? ProviderEventReference,
    string? FinalTitle,
    DateTimeOffset? FinalStartUtc,
    DateTimeOffset? FinalEndUtc,
    string? FinalTimezone,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record CalendarAuditRecord(
    Guid CalendarAuditEventId,
    string EventName,
    Guid TenantId,
    Guid ContextId,
    Guid? UserId,
    CalendarProvider? Provider,
    string SkillName,
    string Result,
    string? OutcomeReference,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson);
