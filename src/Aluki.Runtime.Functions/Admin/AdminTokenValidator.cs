// TODO: Replace API key validation with Azure AD JWT validation once an app registration
// is created. Use Admin:TenantId and Admin:ClientId config keys with
// Microsoft.IdentityModel.Tokens for proper Bearer JWT verification.

using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Functions.Admin;

/// <summary>
/// Validates admin API requests using a shared API key (MVP).
/// Future: validate Azure AD Bearer JWT tokens.
/// </summary>
public static class AdminTokenValidator
{
    /// <summary>
    /// Returns true if the Authorization header contains a valid Bearer token.
    /// MVP: compares to Admin:ApiKey config value.
    /// </summary>
    public static bool IsValid(string? authHeader, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(authHeader))
            return false;

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var expected = config["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        // Constant-time comparison to avoid timing attacks
        return CryptographicEquals(token, expected);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var result = 0;
        for (var i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }
}
