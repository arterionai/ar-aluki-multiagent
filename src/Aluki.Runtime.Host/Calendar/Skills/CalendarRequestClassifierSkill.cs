using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Aluki.Runtime.Host.Calendar.Skills;

/// <summary>
/// Parses a natural-language calendar create request into structured fields.
/// In production this skill would delegate to the model router; this implementation
/// uses deterministic regex patterns as a baseline that is always testable without LLM credentials.
/// </summary>
public sealed partial class CalendarRequestClassifierSkill
{
    public ClassifiedRequest Classify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ClassifiedRequest.Empty;

        var providerHint = ExtractProviderHint(input);
        var timezoneHint = ExtractTimezoneHint(input);
        var (startLocal, endLocal) = ExtractDateTimePair(input);
        var title = ExtractTitle(input, startLocal, endLocal);

        var normalized = NormalizeForHash(title, startLocal, timezoneHint, providerHint);

        return new ClassifiedRequest(
            Title: title,
            StartLocal: startLocal,
            EndLocal: endLocal,
            TimezoneHint: timezoneHint,
            ProviderHint: providerHint,
            NormalizedPayloadHash: ComputeHash(normalized));
    }

    // ── Extraction helpers ─────────────────────────────────────────────────

    private static string? ExtractProviderHint(string input)
    {
        if (OutlookPattern().IsMatch(input)) return "outlook";
        if (GooglePattern().IsMatch(input)) return "google";
        return null;
    }

    private static string? ExtractTimezoneHint(string input)
    {
        var match = TimezonePattern().Match(input);
        if (!match.Success) return null;
        return match.Groups["tz"].Value.Trim();
    }

    private static (string? start, string? end) ExtractDateTimePair(string input)
    {
        var match = DateTimeRangePattern().Match(input);
        if (match.Success)
        {
            return (match.Groups["start"].Value.Trim(), match.Groups["end"].Value.Trim());
        }

        var single = SingleDateTimePattern().Match(input);
        if (single.Success)
            return (single.Groups["dt"].Value.Trim(), null);

        return (null, null);
    }

    private static string? ExtractTitle(string input, string? startLocal, string? endLocal)
    {
        // Remove provider hints, timezone hints, and time expressions; remainder is the title
        var cleaned = input;
        cleaned = OutlookPattern().Replace(cleaned, "");
        cleaned = GooglePattern().Replace(cleaned, "");
        cleaned = TimezonePattern().Replace(cleaned, "");
        cleaned = DateTimeRangePattern().Replace(cleaned, "");
        cleaned = SingleDateTimePattern().Replace(cleaned, "");
        cleaned = LeadingVerbPattern().Replace(cleaned, "");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', ',', '.', '-');

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string NormalizeForHash(string? title, string? startLocal, string? timezoneHint, string? providerHint) =>
        $"{title?.ToLowerInvariant()}|{startLocal?.ToLowerInvariant()}|{timezoneHint?.ToLowerInvariant()}|{providerHint}";

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Regex patterns ─────────────────────────────────────────────────────

    [GeneratedRegex(@"\b(in\s+)?outlook(\s+calendar)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex OutlookPattern();

    [GeneratedRegex(@"\b(in\s+)?google(\s+calendar)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex GooglePattern();

    [GeneratedRegex(@"\b(?<tz>UTC|GMT|EST|EDT|CST|CDT|MST|MDT|PST|PDT|IST|CET|CEST|JST|AEST|Eastern\s+Time|Central\s+Time|Mountain\s+Time|Pacific\s+Time|[A-Z][a-z]+\/[A-Z][a-z_]+)\b")]
    private static partial Regex TimezonePattern();

    // "from 3pm to 5pm", "from 10:00 to 11:30"
    [GeneratedRegex(@"\bfrom\s+(?<start>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s+to\s+(?<end>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateTimeRangePattern();

    // "tomorrow at 3pm", "on Monday at 10:00", "June 25 at 2:30pm", "next week at noon"
    [GeneratedRegex(@"\b(?<dt>(?:today|tomorrow|next\s+\w+|(?:mon|tue|wed|thu|fri|sat|sun)\w*|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\w*\s+\d{1,2})(?:\s+at\s+\d{1,2}(?::\d{2})?\s*(?:am|pm)?)?|\d{1,2}(?::\d{2})?\s*(?:am|pm))\b", RegexOptions.IgnoreCase)]
    private static partial Regex SingleDateTimePattern();

    [GeneratedRegex(@"^\s*(schedule|create|add|book|set up|arrange|plan)\s+(a\s+|an\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingVerbPattern();
}

public sealed record ClassifiedRequest(
    string? Title,
    string? StartLocal,
    string? EndLocal,
    string? TimezoneHint,
    string? ProviderHint,
    string NormalizedPayloadHash)
{
    public static readonly ClassifiedRequest Empty = new(null, null, null, null, null, string.Empty);

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasStartTime => !string.IsNullOrWhiteSpace(StartLocal);
}
