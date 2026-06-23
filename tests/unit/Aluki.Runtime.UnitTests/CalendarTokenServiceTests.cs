using System.Security.Cryptography;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Providers;
using Aluki.Runtime.Calendar.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for <see cref="CalendarTokenService"/> token lifecycle: returns a valid
/// token directly, refreshes when expired, and signals reconnect when no refresh is
/// possible (FR-008 token refresh flows).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarTokenServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Context = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static AesGcmCalendarTokenProtector Protector() =>
        new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public async Task Returns_stored_token_when_not_expired()
    {
        var protector = Protector();
        var store = new InMemoryTokenStore();
        await store.UpsertAsync(Record(protector, "good-token", expiresInMinutes: 30));
        var svc = new CalendarTokenService(store, protector,
            new[] { new RecordingExchanger(CalendarProvider.Outlook) }, NullLogger<CalendarTokenService>.Instance);

        var token = await svc.GetValidAccessTokenAsync(Tenant, Context, User, CalendarProvider.Outlook);

        Assert.NotNull(token);
        Assert.Equal("good-token", token!.Unwrap());
    }

    [Fact]
    public async Task Refreshes_when_expired_and_persists_new_token()
    {
        var protector = Protector();
        var store = new InMemoryTokenStore();
        await store.UpsertAsync(Record(protector, "stale-token", expiresInMinutes: -1, refresh: "refresh-1"));
        var exchanger = new RecordingExchanger(CalendarProvider.Outlook,
            new OAuthTokenResult(true, "fresh-token", "refresh-2", 3600, "s", "Bearer", null));
        var svc = new CalendarTokenService(store, protector, new[] { exchanger }, NullLogger<CalendarTokenService>.Instance);

        var token = await svc.GetValidAccessTokenAsync(Tenant, Context, User, CalendarProvider.Outlook);

        Assert.NotNull(token);
        Assert.Equal("fresh-token", token!.Unwrap());
        Assert.Equal("refresh-1", exchanger.LastRefreshToken); // used the stored refresh token
        // The refreshed token was re-encrypted and persisted.
        var persisted = await store.GetAsync(Tenant, Context, User, CalendarProvider.Outlook);
        Assert.Equal("fresh-token", protector.Unprotect(persisted!.AccessTokenCipher));
    }

    [Fact]
    public async Task Returns_null_when_expired_with_no_refresh_token()
    {
        var protector = Protector();
        var store = new InMemoryTokenStore();
        await store.UpsertAsync(Record(protector, "stale", expiresInMinutes: -1, refresh: null));
        var svc = new CalendarTokenService(store, protector,
            new[] { new RecordingExchanger(CalendarProvider.Outlook) }, NullLogger<CalendarTokenService>.Instance);

        var token = await svc.GetValidAccessTokenAsync(Tenant, Context, User, CalendarProvider.Outlook);

        Assert.Null(token);
    }

    [Fact]
    public async Task Returns_null_when_refresh_is_denied()
    {
        var protector = Protector();
        var store = new InMemoryTokenStore();
        await store.UpsertAsync(Record(protector, "stale", expiresInMinutes: -1, refresh: "refresh-1"));
        var exchanger = new RecordingExchanger(CalendarProvider.Outlook, OAuthTokenResult.Failed("invalid_grant"));
        var svc = new CalendarTokenService(store, protector, new[] { exchanger }, NullLogger<CalendarTokenService>.Instance);

        var token = await svc.GetValidAccessTokenAsync(Tenant, Context, User, CalendarProvider.Outlook);

        Assert.Null(token);
    }

    [Fact]
    public async Task Returns_null_when_no_token_stored()
    {
        var svc = new CalendarTokenService(new InMemoryTokenStore(), Protector(),
            new[] { new RecordingExchanger(CalendarProvider.Outlook) }, NullLogger<CalendarTokenService>.Instance);

        var token = await svc.GetValidAccessTokenAsync(Tenant, Context, User, CalendarProvider.Outlook);

        Assert.Null(token);
    }

    private static CalendarTokenRecord Record(
        ICalendarTokenProtector protector, string accessToken, int expiresInMinutes, string? refresh = "refresh-1") =>
        new(
            CalendarOAuthTokenId: Guid.NewGuid(),
            CalendarConnectionId: Guid.NewGuid(),
            TenantId: Tenant,
            ContextId: Context,
            UserId: User,
            Provider: CalendarProvider.Outlook,
            AccessTokenCipher: protector.Protect(accessToken),
            RefreshTokenCipher: refresh is null ? null : protector.Protect(refresh),
            AccessTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
            Scope: "s",
            TokenType: "Bearer",
            CorrelationId: "corr");

    private sealed class InMemoryTokenStore : ICalendarTokenStore
    {
        private readonly Dictionary<string, CalendarTokenRecord> _store = new();
        private static string Key(Guid t, Guid c, Guid u, CalendarProvider p) => $"{t}:{c}:{u}:{p}";

        public Task UpsertAsync(CalendarTokenRecord record, CancellationToken ct = default)
        {
            _store[Key(record.TenantId, record.ContextId, record.UserId, record.Provider)] = record;
            return Task.CompletedTask;
        }

        public Task<CalendarTokenRecord?> GetAsync(Guid t, Guid c, Guid u, CalendarProvider p, CancellationToken ct = default)
        {
            _store.TryGetValue(Key(t, c, u, p), out var r);
            return Task.FromResult(r);
        }

        public Task DeleteAsync(Guid t, Guid c, Guid u, CalendarProvider p, CancellationToken ct = default)
        {
            _store.Remove(Key(t, c, u, p));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExchanger : IOAuthTokenExchanger
    {
        private readonly OAuthTokenResult _refreshResult;
        public string? LastRefreshToken { get; private set; }

        public RecordingExchanger(CalendarProvider provider, OAuthTokenResult? refreshResult = null)
        {
            Provider = provider;
            _refreshResult = refreshResult ?? OAuthTokenResult.Failed("not_configured");
        }

        public CalendarProvider Provider { get; }

        public Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(_refreshResult);

        public Task<OAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            LastRefreshToken = refreshToken;
            return Task.FromResult(_refreshResult);
        }

        public Task<string?> ResolveAccountRefAsync(string accessToken, CancellationToken ct = default) =>
            Task.FromResult<string?>("user@example.com");
    }
}
