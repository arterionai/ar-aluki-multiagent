using System.Net.Http.Json;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Providers;

/// <summary>
/// Outcome of an OAuth token endpoint call. On failure, token fields are null and
/// <see cref="Error"/> carries the provider's error code (never the token material).
/// </summary>
public sealed record OAuthTokenResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresInSeconds,
    string? Scope,
    string? TokenType,
    string? Error)
{
    public static OAuthTokenResult Failed(string error) =>
        new(false, null, null, 0, null, null, error);
}

/// <summary>
/// Provider-specific OAuth code↔token exchange and refresh. Implementations talk to
/// the provider token endpoint using the confidential client secret. The authorization
/// <c>code</c> and resulting tokens are never logged.
/// </summary>
public interface IOAuthTokenExchanger
{
    CalendarProvider Provider { get; }

    Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default);

    Task<OAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Resolves a stable account identifier (e.g. email/UPN) for display/audit.</summary>
    Task<string?> ResolveAccountRefAsync(string accessToken, CancellationToken ct = default);
}

internal abstract class OAuthTokenExchangerBase
{
    protected readonly HttpClient Http;
    protected readonly CalendarOptions Options;
    private readonly ILogger _logger;

    protected OAuthTokenExchangerBase(HttpClient http, IOptions<CalendarOptions> options, ILogger logger)
    {
        Http = http;
        Options = options.Value;
        _logger = logger;
    }

    protected string RedirectUri => $"{Options.CallbackBaseUrl.TrimEnd('/')}/api/calendar/callback";

    protected async Task<OAuthTokenResult> PostTokenAsync(string tokenEndpoint, Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await Http.PostAsync(tokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body) ?? $"http_{(int)response.StatusCode}";
            _logger.LogWarning("OAuth token endpoint returned {Status} ({Error}).", (int)response.StatusCode, error);
            return OAuthTokenResult.Failed(error);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = GetString(root, "access_token");
            if (string.IsNullOrEmpty(accessToken))
                return OAuthTokenResult.Failed("missing_access_token");

            return new OAuthTokenResult(
                Success: true,
                AccessToken: accessToken,
                RefreshToken: GetString(root, "refresh_token"),
                ExpiresInSeconds: root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var s) ? s : 3600,
                Scope: GetString(root, "scope"),
                TokenType: GetString(root, "token_type"),
                Error: null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OAuth token response.");
            return OAuthTokenResult.Failed("invalid_token_response");
        }
    }

    protected static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? TryReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetString(doc.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class OutlookOAuthTokenExchanger : OAuthTokenExchangerBase, IOAuthTokenExchanger
{
    public OutlookOAuthTokenExchanger(HttpClient http, IOptions<CalendarOptions> options, ILogger<OutlookOAuthTokenExchanger> logger)
        : base(http, options, logger) { }

    public CalendarProvider Provider => CalendarProvider.Outlook;

    private string TokenEndpoint => $"https://login.microsoftonline.com/{Options.Outlook.TenantId}/oauth2/v2.0/token";

    public Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        PostTokenAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Options.Outlook.ClientId,
            ["client_secret"] = Options.Outlook.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = string.Join(" ", Options.Outlook.Scopes),
        }, ct);

    public Task<OAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Options.Outlook.ClientId,
            ["client_secret"] = Options.Outlook.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "refresh_token",
            ["scope"] = string.Join(" ", Options.Outlook.Scopes),
        }, ct);

    public async Task<string?> ResolveAccountRefAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        req.Headers.Authorization = new("Bearer", accessToken);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var me = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return GetString(me, "mail") ?? GetString(me, "userPrincipalName");
    }
}

internal sealed class GoogleOAuthTokenExchanger : OAuthTokenExchangerBase, IOAuthTokenExchanger
{
    public GoogleOAuthTokenExchanger(HttpClient http, IOptions<CalendarOptions> options, ILogger<GoogleOAuthTokenExchanger> logger)
        : base(http, options, logger) { }

    public CalendarProvider Provider => CalendarProvider.Google;

    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        PostTokenAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Options.Google.ClientId,
            ["client_secret"] = Options.Google.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
        }, ct);

    public Task<OAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Options.Google.ClientId,
            ["client_secret"] = Options.Google.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }, ct);

    public async Task<string?> ResolveAccountRefAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        req.Headers.Authorization = new("Bearer", accessToken);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var info = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return GetString(info, "email");
    }
}
