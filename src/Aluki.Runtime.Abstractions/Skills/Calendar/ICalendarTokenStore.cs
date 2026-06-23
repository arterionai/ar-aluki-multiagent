namespace Aluki.Runtime.Abstractions.Skills.Calendar;

/// <summary>
/// Persists provider OAuth tokens for a connection. Implementations MUST store
/// <see cref="CalendarTokenRecord.AccessTokenCipher"/> / <see cref="CalendarTokenRecord.RefreshTokenCipher"/>
/// encrypted at rest — plaintext token material never reaches this boundary.
/// </summary>
public interface ICalendarTokenStore
{
    Task UpsertAsync(CalendarTokenRecord record, CancellationToken ct = default);

    Task<CalendarTokenRecord?> GetAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default);

    Task DeleteAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default);
}

public sealed record CalendarTokenRecord(
    Guid CalendarOAuthTokenId,
    Guid CalendarConnectionId,
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    CalendarProvider Provider,
    string AccessTokenCipher,
    string? RefreshTokenCipher,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string? Scope,
    string? TokenType,
    string CorrelationId);
