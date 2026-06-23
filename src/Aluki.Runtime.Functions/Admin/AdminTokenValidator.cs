using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Aluki.Runtime.Functions.Admin;

public static class AdminTokenValidator
{
    private static readonly JwtSecurityTokenHandler Handler = new();
    private static IConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private static string? _lastDiscoveryUrl;
    private static readonly object Lock = new();

    public static async Task<bool> IsValidAsync(string? authHeader, IConfiguration config, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(authHeader)) { logger?.LogWarning("AdminTokenValidator: no Authorization header"); return false; }
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) { logger?.LogWarning("AdminTokenValidator: header not Bearer"); return false; }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token)) return false;

        var tenantId = config["Admin:TenantId"] ?? "7b1683c8-3607-4002-a544-89f96fa0ef3a";
        var clientId = config["Admin:ClientId"] ?? "1e1562a3-eb31-4b9d-beed-eeb8129961f9";
        var discoveryUrl = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";

        var mgr = GetConfigurationManager(discoveryUrl);
        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await mgr.GetConfigurationAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "AdminTokenValidator: OIDC discovery failed for {Url}", discoveryUrl);
            return false;
        }

        // Peek at the token header to log aud/iss without full validation
        try
        {
            var raw = Handler.ReadJwtToken(token);
            logger?.LogInformation("AdminTokenValidator: aud={Aud} iss={Iss} tid={Tid}",
                string.Join(",", raw.Audiences), raw.Issuer,
                raw.Claims.FirstOrDefault(c => c.Type == "tid")?.Value ?? "(none)");
        }
        catch { /* best-effort peek */ }

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            // Accept both v1 (sts.windows.net) and v2 (login.microsoftonline.com) issuers —
            // access tokens for custom API scopes (api://...) are issued by the v1 endpoint
            // even when acquired via MSAL with v2 authority.
            ValidIssuers = new[]
            {
                $"https://login.microsoftonline.com/{tenantId}/v2.0",
                $"https://sts.windows.net/{tenantId}/",
            },
            ValidateAudience = true,
            // Accept both ID tokens (aud=clientId) and access tokens (aud=api://clientId).
            ValidAudiences = new[] { clientId, $"api://{clientId}" },
            ValidateLifetime = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuerSigningKey = true,
        };

        try
        {
            var principal = Handler.ValidateToken(token, validationParams, out _);
            var tid = principal.FindFirst("tid")?.Value;
            var ok = string.Equals(tid, tenantId, StringComparison.OrdinalIgnoreCase);
            if (!ok) logger?.LogWarning("AdminTokenValidator: tid mismatch — got {Tid}, expected {TenantId}", tid, tenantId);
            return ok;
        }
        catch (Exception ex)
        {
            logger?.LogWarning("AdminTokenValidator: ValidateToken failed — {Msg}", ex.Message);
            return false;
        }
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> GetConfigurationManager(string discoveryUrl)
    {
        lock (Lock)
        {
            if (_configManager == null || _lastDiscoveryUrl != discoveryUrl)
            {
                _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    discoveryUrl,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = true }
                );
                _lastDiscoveryUrl = discoveryUrl;
            }
            return _configManager;
        }
    }
}
