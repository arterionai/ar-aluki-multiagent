namespace Aluki.Runtime.Reminders.Dispatch;

/// <summary>
/// Maps city/region mentions extracted from free-text recall answers to IANA timezone IDs.
/// Used by ReminderDomainAgent to display confirmation times in the user's local timezone.
/// </summary>
internal static class CityTimezoneMapper
{
    private static readonly (string[] Keywords, string Timezone)[] Map =
    [
        // US — Central (CDT/CST)
        (["houston", "dallas", "san antonio", "austin", "fort worth", "el paso", "corpus christi"], "America/Chicago"),
        // US — Eastern
        (["new york", "nyc", "miami", "atlanta", "boston", "philadelphia", "charlotte", "orlando"], "America/New_York"),
        // US — Pacific
        (["los angeles", "la", "san francisco", "seattle", "san diego", "portland", "las vegas"], "America/Los_Angeles"),
        // US — Mountain
        (["denver", "phoenix", "salt lake", "albuquerque"], "America/Denver"),
        // Mexico
        (["monterrey", "saltillo", "chihuahua", "hermosillo", "culiacan", "mazatlan"], "America/Monterrey"),
        (["tijuana", "mexicali", "ensenada"], "America/Tijuana"),
        (["cancun", "merida", "campeche"], "America/Cancun"),
        (["ciudad de mexico", "cdmx", "mexico city", "guadalajara", "puebla", "queretaro", "leon",
          "san luis potosi", "aguascalientes", "morelia", "toluca", "oaxaca", "veracruz"], "America/Mexico_City"),
    ];

    /// <summary>
    /// Scans <paramref name="text"/> for known city/region names and returns the matching
    /// IANA timezone ID, or <c>null</c> if no match is found.
    /// </summary>
    public static string? ResolveFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();
        foreach (var (keywords, tz) in Map)
        {
            foreach (var keyword in keywords)
            {
                if (lower.Contains(keyword))
                    return tz;
            }
        }
        return null;
    }
}
