using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
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

    public static async Task<bool> IsValidAsync(string? authHeader, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(authHeader)) return false;
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

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
        catch
        {
            return false;
        }

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            ValidateAudience = true,
            ValidAudience = $"api://{clientId}",
            ValidateLifetime = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuerSigningKey = true,
        };

        try
        {
            var principal = Handler.ValidateToken(token, validationParams, out _);
            var tid = principal.FindFirst("tid")?.Value;
            return string.Equals(tid, tenantId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
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
