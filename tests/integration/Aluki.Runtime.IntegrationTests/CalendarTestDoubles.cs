using System.Net;
using System.Text;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Providers;
using Aluki.Runtime.Host.Calendar.Security;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Returns a fixed HTTP response and captures the last request, so provider adapters
/// can be exercised without real network access.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _json;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode status, string json)
    {
        _status = status;
        _json = json;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_json, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Configurable token exchanger for callback tests (no real OAuth round trip).</summary>
internal sealed class FakeOAuthTokenExchanger : IOAuthTokenExchanger
{
    private readonly OAuthTokenResult _result;
    private readonly string? _accountRef;

    public FakeOAuthTokenExchanger(
        CalendarProvider provider,
        OAuthTokenResult? result = null,
        string? accountRef = "user@example.com")
    {
        Provider = provider;
        _result = result ?? new OAuthTokenResult(true, "access-xyz", "refresh-xyz", 3600, "scope", "Bearer", null);
        _accountRef = accountRef;
    }

    public CalendarProvider Provider { get; }

    public Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default) => Task.FromResult(_result);

    public Task<OAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) => Task.FromResult(_result);

    public Task<string?> ResolveAccountRefAsync(string accessToken, CancellationToken ct = default) => Task.FromResult(_accountRef);
}

/// <summary>Token service that hands back a fixed access token (or none, to force reconnect).</summary>
internal sealed class FakeCalendarTokenService : ICalendarTokenService
{
    private readonly string? _accessToken;

    public int PersistCount { get; private set; }

    public FakeCalendarTokenService(string? accessToken = "access-xyz") => _accessToken = accessToken;

    public Task<ProviderTokenBoundary?> GetValidAccessTokenAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default) =>
        Task.FromResult(_accessToken is null ? null : ProviderTokenBoundary.Wrap(_accessToken));

    public Task PersistAsync(
        Guid connectionId, Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider,
        OAuthTokenResult tokens, string correlationId, CancellationToken ct = default)
    {
        PersistCount++;
        return Task.CompletedTask;
    }
}
