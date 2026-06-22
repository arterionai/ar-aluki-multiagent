using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Security;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Calendar.Providers;

/// <summary>
/// Resolves a usable provider access token for a connection, transparently refreshing
/// it when expired. Returns <c>null</c> when the user must reconnect (no token, no
/// refresh token, or the refresh was denied/revoked). Token material is only ever
/// surfaced wrapped in a <see cref="ProviderTokenBoundary"/>.
/// </summary>
public interface ICalendarTokenService
{
    Task<ProviderTokenBoundary?> GetValidAccessTokenAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default);

    /// <summary>Encrypts and persists tokens obtained from a code exchange/refresh.</summary>
    Task PersistAsync(
        Guid connectionId, Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider,
        OAuthTokenResult tokens, string correlationId, CancellationToken ct = default);
}

public sealed class CalendarTokenService : ICalendarTokenService
{
    // Refresh slightly ahead of expiry so in-flight calls don't race the boundary.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    private readonly ICalendarTokenStore _store;
    private readonly ICalendarTokenProtector _protector;
    private readonly IEnumerable<IOAuthTokenExchanger> _exchangers;
    private readonly ILogger<CalendarTokenService> _logger;

    public CalendarTokenService(
        ICalendarTokenStore store,
        ICalendarTokenProtector protector,
        IEnumerable<IOAuthTokenExchanger> exchangers,
        ILogger<CalendarTokenService> logger)
    {
        _store = store;
        _protector = protector;
        _exchangers = exchangers;
        _logger = logger;
    }

    public async Task<ProviderTokenBoundary?> GetValidAccessTokenAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default)
    {
        var record = await _store.GetAsync(tenantId, contextId, userId, provider, ct);
        if (record is null)
        {
            _logger.LogInformation("No stored token for {Provider}; reconnect required.", provider);
            return null;
        }

        if (DateTimeOffset.UtcNow < record.AccessTokenExpiresAtUtc - ExpirySkew)
            return ProviderTokenBoundary.Wrap(_protector.Unprotect(record.AccessTokenCipher));

        // Access token expired (or about to) — attempt a refresh.
        if (string.IsNullOrEmpty(record.RefreshTokenCipher))
        {
            _logger.LogInformation("{Provider} token expired with no refresh token; reconnect required.", provider);
            return null;
        }

        var exchanger = _exchangers.FirstOrDefault(e => e.Provider == provider);
        if (exchanger is null) return null;

        var refreshToken = _protector.Unprotect(record.RefreshTokenCipher);
        var refreshed = await exchanger.RefreshAsync(refreshToken, ct);
        if (!refreshed.Success || string.IsNullOrEmpty(refreshed.AccessToken))
        {
            _logger.LogWarning("{Provider} token refresh failed ({Error}); reconnect required.", provider, refreshed.Error);
            return null;
        }

        await PersistAsync(record.CalendarConnectionId, tenantId, contextId, userId, provider, refreshed, record.CorrelationId, ct);
        return ProviderTokenBoundary.Wrap(refreshed.AccessToken);
    }

    public async Task PersistAsync(
        Guid connectionId, Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider,
        OAuthTokenResult tokens, string correlationId, CancellationToken ct = default)
    {
        if (!tokens.Success || string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("Cannot persist an unsuccessful token result.");

        var record = new CalendarTokenRecord(
            CalendarOAuthTokenId: Guid.NewGuid(),
            CalendarConnectionId: connectionId,
            TenantId: tenantId,
            ContextId: contextId,
            UserId: userId,
            Provider: provider,
            AccessTokenCipher: _protector.Protect(tokens.AccessToken),
            // Providers may omit refresh_token on refresh; the store keeps the prior one.
            RefreshTokenCipher: string.IsNullOrEmpty(tokens.RefreshToken) ? null : _protector.Protect(tokens.RefreshToken),
            AccessTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresInSeconds),
            Scope: tokens.Scope,
            TokenType: tokens.TokenType,
            CorrelationId: correlationId);

        await _store.UpsertAsync(record, ct);
    }
}
