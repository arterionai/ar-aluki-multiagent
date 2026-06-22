using System.Security.Cryptography;
using System.Text;

namespace Aluki.Runtime.Abstractions.Skills.LinkCapture;

public static class LinkCanonicalization
{
    // Returns null for invalid/unsupported URLs (non-http/https, relative, etc.)
    public static string? TryCanonical(string raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        // Lowercase scheme+host; keep path and query as-is; drop fragment.
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty,
        };
        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path | UriComponents.Query,
            UriFormat.UriEscaped);
    }

    public static string ComputeHash(string canonicalUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl));
        return Convert.ToHexStringLower(bytes);
    }

    public static IReadOnlyList<string> ExtractUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token =>
                (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                Uri.TryCreate(token, UriKind.Absolute, out var u) &&
                u.IsAbsoluteUri &&
                u.Scheme is "http" or "https")
            .ToList();
    }
}
