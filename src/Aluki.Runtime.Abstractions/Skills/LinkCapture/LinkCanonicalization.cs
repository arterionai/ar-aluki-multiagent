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

    public static string? ExtractFirstUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var urls = ExtractUrls(text);
        return urls.Count > 0 ? urls[0] : null;
    }

    // Returns the text with the given URL removed, trimmed. Null if result is empty.
    public static string? ExtractLabelText(string text, string url)
    {
        var remainder = text.Replace(url, string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
    }

    // True when text contains a URL and the surrounding text is NOT a question.
    // Treat as "save this link" intent; bypass LLM topical response.
    public static bool IsLinkSaveIntent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var url = ExtractFirstUrl(text);
        if (url is null) return false;
        var remainder = ExtractLabelText(text, url) ?? string.Empty;
        return !remainder.Contains('?') && !remainder.Contains('¿');
    }
}
