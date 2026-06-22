using NodaTime;
using NodaTime.TimeZones;

namespace Aluki.Runtime.Host.Calendar.Skills;

public sealed class CalendarTimezoneResolverSkill
{
    private static readonly IDateTimeZoneProvider TzProvider = DateTimeZoneProviders.Tzdb;

    // Well-known abbreviation → IANA mapping (non-exhaustive; extended per FR-004)
    private static readonly Dictionary<string, string> AbbreviationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = "UTC",
        ["GMT"] = "UTC",
        ["EST"] = "America/New_York",
        ["EDT"] = "America/New_York",
        ["Eastern Time"] = "America/New_York",
        ["CST"] = "America/Chicago",
        ["CDT"] = "America/Chicago",
        ["Central Time"] = "America/Chicago",
        ["MST"] = "America/Denver",
        ["MDT"] = "America/Denver",
        ["Mountain Time"] = "America/Denver",
        ["PST"] = "America/Los_Angeles",
        ["PDT"] = "America/Los_Angeles",
        ["Pacific Time"] = "America/Los_Angeles",
        ["IST"] = "Asia/Kolkata",
        ["CET"] = "Europe/Paris",
        ["CEST"] = "Europe/Paris",
        ["JST"] = "Asia/Tokyo",
        ["AEST"] = "Australia/Sydney",
    };

    /// <summary>
    /// Resolves the canonical IANA timezone from a hint string, or falls back to the
    /// profile default when the hint is null. Returns null when neither source resolves.
    /// </summary>
    public TimezoneResolution Resolve(string? timezoneHint, string? profileDefault)
    {
        if (!string.IsNullOrWhiteSpace(timezoneHint))
        {
            var ianaId = ResolveHint(timezoneHint!);
            if (ianaId is not null)
                return new TimezoneResolution(ianaId, Abstractions.Skills.Calendar.TimezoneResolutionSource.Request, DstAmbiguous: false);
        }

        if (!string.IsNullOrWhiteSpace(profileDefault))
        {
            var ianaId = ResolveHint(profileDefault!);
            if (ianaId is not null)
                return new TimezoneResolution(ianaId, Abstractions.Skills.Calendar.TimezoneResolutionSource.Profile, DstAmbiguous: false);
        }

        return TimezoneResolution.Unresolved;
    }

    /// <summary>
    /// Checks whether a local datetime string is ambiguous in the given IANA timezone
    /// (occurs twice due to DST fall-back transition).
    /// </summary>
    public bool IsDstAmbiguous(string ianaTimezone, string localDateTimeText)
    {
        var zone = TzProvider.GetZoneOrNull(ianaTimezone);
        if (zone is null) return false;

        if (!LocalDateTime.TryParse(localDateTimeText, out var localDt)) return false;

        var mapping = zone.MapLocal(localDt);
        return mapping.Count == 2; // two UTC instants = DST ambiguity
    }

    /// <summary>
    /// Converts a local datetime string + IANA timezone to UTC.
    /// Returns null when the local time is invalid (e.g. skipped by DST spring-forward).
    /// </summary>
    public DateTimeOffset? ToUtc(string ianaTimezone, string localDateTimeText)
    {
        var zone = TzProvider.GetZoneOrNull(ianaTimezone);
        if (zone is null) return null;

        if (!LocalDateTime.TryParse(localDateTimeText, out var localDt)) return null;

        var mapping = zone.MapLocal(localDt);
        if (mapping.Count == 0) return null; // skipped (spring-forward)

        // Prefer the first (earlier) instant for the non-ambiguous and DST-falling case
        var instant = mapping.First().ToInstant();
        return instant.ToDateTimeOffset();
    }

    private static string? ResolveHint(string hint)
    {
        if (AbbreviationMap.TryGetValue(hint, out var mapped))
            return mapped;

        // Try direct IANA lookup (e.g. "America/New_York" passed explicitly)
        try
        {
            var zone = TzProvider.GetZoneOrNull(hint);
            return zone?.Id;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TimezoneResolution(
    string? IanaId,
    Abstractions.Skills.Calendar.TimezoneResolutionSource Source,
    bool DstAmbiguous)
{
    public static readonly TimezoneResolution Unresolved =
        new(null, Abstractions.Skills.Calendar.TimezoneResolutionSource.Request, false);

    public bool IsResolved => !string.IsNullOrWhiteSpace(IanaId);
}
