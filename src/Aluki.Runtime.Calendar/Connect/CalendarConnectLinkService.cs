using System.Security.Cryptography;
using System.Text;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Connect;

/// <summary>
/// Identity carried by a signed connect link.
/// </summary>
public sealed record CalendarConnectLinkPayload(
    Guid TenantId, Guid ContextId, Guid UserId, CalendarProvider Provider);

/// <summary>
/// Mints and validates the tamper-proof, short-lived links that a user clicks to
/// start a calendar connection. The link encodes who is connecting (tenant/context/
/// user/provider) and is HMAC-signed so it cannot be altered to connect on behalf of
/// someone else. The link points at the human-facing consent page, never directly at
/// the provider — the OAuth flow only begins after the user agrees.
/// </summary>
public interface ICalendarConnectLinkService
{
    /// <summary>URL of the consent page (…/api/calendar/connect/start?token=…).</summary>
    string CreateStartUrl(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider);

    bool TryValidateToken(string? token, out CalendarConnectLinkPayload payload);
}

public sealed class CalendarConnectLinkService : ICalendarConnectLinkService
{
    private readonly CalendarOptions _options;
    private readonly Lazy<byte[]> _signingKey;

    public CalendarConnectLinkService(IOptions<CalendarOptions> options)
    {
        _options = options.Value;
        // Resolve lazily so a missing key never breaks DI for hosts that don't connect.
        _signingKey = new Lazy<byte[]>(() => ResolveKey(_options));
    }

    public string CreateStartUrl(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider)
    {
        var expUnix = DateTimeOffset.UtcNow.AddMinutes(_options.ConnectLinkExpiryMinutes).ToUnixTimeSeconds();
        var payload = $"{tenantId:N}.{contextId:N}.{userId:N}.{(int)provider}.{expUnix}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = HMACSHA256.HashData(_signingKey.Value, payloadBytes);
        var token = $"{Base64Url(payloadBytes)}.{Base64Url(sig)}";

        var baseUrl = _options.CallbackBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/calendar/connect/start?token={token}";
    }

    public bool TryValidateToken(string? token, out CalendarConnectLinkPayload payload)
    {
        payload = default!;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return false;

        byte[] payloadBytes, sig;
        try
        {
            payloadBytes = FromBase64Url(token[..dot]);
            sig = FromBase64Url(token[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_signingKey.Value, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(sig, expected)) return false;

        var parts = Encoding.UTF8.GetString(payloadBytes).Split('.');
        if (parts.Length != 5) return false;

        if (!Guid.TryParseExact(parts[0], "N", out var tenantId) ||
            !Guid.TryParseExact(parts[1], "N", out var contextId) ||
            !Guid.TryParseExact(parts[2], "N", out var userId) ||
            !int.TryParse(parts[3], out var providerCode) ||
            !long.TryParse(parts[4], out var expUnix))
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix) return false; // expired
        if (!Enum.IsDefined(typeof(CalendarProvider), providerCode)) return false;

        payload = new CalendarConnectLinkPayload(tenantId, contextId, userId, (CalendarProvider)providerCode);
        return true;
    }

    private static byte[] ResolveKey(CalendarOptions options)
    {
        // Dedicated link-signing key, falling back to the token encryption key so there
        // is one fewer secret to configure. Both are base64-encoded.
        var raw = !string.IsNullOrWhiteSpace(options.LinkSigningKey)
            ? options.LinkSigningKey
            : options.TokenEncryptionKey;

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Calendar connect-link signing key is not configured. Set 'Calendar:LinkSigningKey' " +
                "or 'Calendar:TokenEncryptionKey' (base64).");

        return Convert.FromBase64String(raw);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
