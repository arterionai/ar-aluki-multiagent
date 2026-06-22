using System.Text.RegularExpressions;

namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public static class YouTubeUrlCanonicalizer
{
    // Matches exactly 11 chars: alphanumeric, underscore, or hyphen.
    private static readonly Regex VideoIdPattern =
        new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    // Matches any YouTube hostname variant.
    private static readonly Regex YouTubeHostPattern =
        new(@"^(www\.|m\.)?youtube\.com$|^youtu\.be$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extracts a YouTube video ID from the given URL.
    /// Handles watch?v=, youtu.be/, /shorts/, /embed/, and mobile URLs.
    /// Returns null if the URL is not a recognisable YouTube video URL or the
    /// extracted ID fails validation.
    /// </summary>
    public static string? TryExtractVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Prepend a scheme if missing so Uri.TryCreate succeeds.
        var normalised = url.Trim();
        if (!normalised.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalised.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalised = "https://" + normalised;
        }

        if (!Uri.TryCreate(normalised, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();

        if (!YouTubeHostPattern.IsMatch(host))
            return null;

        string? candidateId = null;

        if (host == "youtu.be")
        {
            // https://youtu.be/VIDEO_ID[?...]
            candidateId = ExtractFirstPathSegment(uri.AbsolutePath);
        }
        else
        {
            // youtube.com variants
            var path = uri.AbsolutePath.TrimEnd('/');

            if (path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                // /shorts/VIDEO_ID
                candidateId = ExtractSegmentAfterPrefix(path, "/shorts/");
            }
            else if (path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
            {
                // /embed/VIDEO_ID
                candidateId = ExtractSegmentAfterPrefix(path, "/embed/");
            }
            else
            {
                // /watch?v=VIDEO_ID  (or /watch?... on m.youtube.com)
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                candidateId = query["v"];
            }
        }

        if (candidateId is null)
            return null;

        // Strip any extra path segments or query parameters that may have been
        // included in the extracted segment.
        var slashIdx = candidateId.IndexOf('/');
        if (slashIdx >= 0)
            candidateId = candidateId[..slashIdx];

        return IsValidVideoId(candidateId) ? candidateId : null;
    }

    /// <summary>Returns "https://www.youtube.com/watch?v={videoId}".</summary>
    public static string ToCanonicalUrl(string videoId) =>
        $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>
    /// Splits <paramref name="text"/> on whitespace and returns every token that
    /// is a recognisable YouTube video URL (i.e. TryExtractVideoId succeeds).
    /// </summary>
    public static IReadOnlyList<string> ExtractYoutubeUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryExtractVideoId(token) is not null)
                results.Add(token);
        }

        return results;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool IsValidVideoId(string? id) =>
        id is not null && VideoIdPattern.IsMatch(id);

    private static string? ExtractFirstPathSegment(string path)
    {
        var trimmed = path.TrimStart('/');
        if (string.IsNullOrEmpty(trimmed))
            return null;
        var slash = trimmed.IndexOf('/');
        return slash >= 0 ? trimmed[..slash] : trimmed;
    }

    private static string? ExtractSegmentAfterPrefix(string path, string prefix)
    {
        var idx = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var after = path[(idx + prefix.Length)..].TrimStart('/');
        if (string.IsNullOrEmpty(after))
            return null;
        var slash = after.IndexOf('/');
        return slash >= 0 ? after[..slash] : after;
    }
}
