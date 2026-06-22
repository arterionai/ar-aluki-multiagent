using TimeZoneConverter;

namespace Aluki.Runtime.Reminders.Time;

/// <summary>Recurrence parameters needed to compute the next occurrence (US2).</summary>
public sealed record ReminderRecurrence(
    string Cadence,
    IReadOnlyList<string>? DaysOfWeek,
    int? DayOfMonth,
    string? EndCondition,
    DateTimeOffset? EndDateUtc);

/// <summary>
/// Deterministic, DST-safe next-occurrence calculator (SB-005 US2). Recurrence is
/// anchored to a wall-clock <see cref="TimeOnly"/> in the user's IANA timezone, so
/// a 9 AM daily reminder stays at 9 AM local through DST transitions even though
/// its UTC instant shifts. Progression is computed on LOCAL dates (not UTC
/// instants) to avoid the spring-forward pitfall where the next local day can be
/// earlier in UTC than the previous fire.
/// </summary>
public static class ReminderRecurrenceCalculator
{
    /// <summary>
    /// Next occurrence strictly after the local date of <paramref name="afterUtc"/>,
    /// at <paramref name="localTime"/> in <paramref name="timezone"/>. Returns null
    /// when the timezone is invalid, the rule is malformed, or the next occurrence
    /// would fall past an <c>until_date</c> end boundary.
    /// </summary>
    public static DateTimeOffset? NextOccurrence(
        ReminderRecurrence rule, TimeOnly localTime, string timezone, DateTimeOffset afterUtc)
    {
        if (!TZConvert.TryGetTimeZoneInfo(timezone, out var tz))
        {
            return null;
        }

        var afterLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(afterUtc.UtcDateTime, tz));

        var next = (rule.Cadence?.Trim().ToLowerInvariant()) switch
        {
            ReminderCadence.Daily => ToUtc(afterLocalDate.AddDays(1), localTime, tz),
            ReminderCadence.Weekly => NextWeekly(rule.DaysOfWeek, localTime, tz, afterLocalDate),
            ReminderCadence.Monthly => NextMonthly(rule.DayOfMonth, localTime, tz, afterLocalDate),
            _ => (DateTimeOffset?)null
        };

        if (next is { } occ && IsAfterEnd(rule, occ))
        {
            return null;
        }

        return next;
    }

    /// <summary>
    /// Resolves the first occurrence at-or-after a requested start. Used at create
    /// time so the stored <c>scheduled_time_utc</c> aligns to the rule's day while
    /// honoring the inclusive start boundary.
    /// </summary>
    public static DateTimeOffset? FirstOccurrenceOnOrAfter(
        ReminderRecurrence rule, TimeOnly localTime, string timezone, DateTimeOffset startUtc)
    {
        if (!TZConvert.TryGetTimeZoneInfo(timezone, out var tz))
        {
            return null;
        }

        var startLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc.UtcDateTime, tz));

        var candidate = (rule.Cadence?.Trim().ToLowerInvariant()) switch
        {
            ReminderCadence.Daily => ToUtc(startLocalDate, localTime, tz),
            ReminderCadence.Weekly => FirstWeekly(rule.DaysOfWeek, localTime, tz, startLocalDate),
            ReminderCadence.Monthly => FirstMonthly(rule.DayOfMonth, localTime, tz, startLocalDate),
            _ => (DateTimeOffset?)null
        };

        // If the aligned candidate is before the requested start, roll to the next.
        if (candidate is { } c && c < startUtc)
        {
            return NextOccurrence(rule, localTime, timezone, c);
        }

        if (candidate is { } occ && IsAfterEnd(rule, occ))
        {
            return null;
        }

        return candidate;
    }

    private static DateTimeOffset? NextWeekly(IReadOnlyList<string>? days, TimeOnly lt, TimeZoneInfo tz, DateOnly afterDate)
    {
        var set = ParseDays(days);
        if (set.Count == 0)
        {
            return null;
        }

        for (var i = 1; i <= 7; i++)
        {
            var date = afterDate.AddDays(i);
            if (set.Contains(date.DayOfWeek))
            {
                return ToUtc(date, lt, tz);
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstWeekly(IReadOnlyList<string>? days, TimeOnly lt, TimeZoneInfo tz, DateOnly startDate)
    {
        var set = ParseDays(days);
        if (set.Count == 0)
        {
            return null;
        }

        for (var i = 0; i <= 7; i++)
        {
            var date = startDate.AddDays(i);
            if (set.Contains(date.DayOfWeek))
            {
                return ToUtc(date, lt, tz);
            }
        }

        return null;
    }

    private static DateTimeOffset? NextMonthly(int? dayOfMonth, TimeOnly lt, TimeZoneInfo tz, DateOnly afterDate)
    {
        if (dayOfMonth is null)
        {
            return null;
        }

        for (var i = 0; i <= 13; i++)
        {
            var date = MonthlyDate(afterDate.Year, afterDate.Month + i, dayOfMonth.Value);
            if (date > afterDate)
            {
                return ToUtc(date, lt, tz);
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstMonthly(int? dayOfMonth, TimeOnly lt, TimeZoneInfo tz, DateOnly startDate)
    {
        if (dayOfMonth is null)
        {
            return null;
        }

        for (var i = 0; i <= 13; i++)
        {
            var date = MonthlyDate(startDate.Year, startDate.Month + i, dayOfMonth.Value);
            if (date >= startDate)
            {
                return ToUtc(date, lt, tz);
            }
        }

        return null;
    }

    private static DateOnly MonthlyDate(int year, int monthOffset, int dayOfMonth)
    {
        // Normalize a possibly-overflowed month into (year, month).
        var zeroBased = monthOffset - 1;
        var y = year + Math.DivRem(zeroBased, 12, out var m);
        if (m < 0)
        {
            m += 12;
            y -= 1;
        }

        var month = m + 1;
        var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(y, month)); // 31 -> last day of month
        return new DateOnly(y, month, day);
    }

    /// <summary>
    /// Builds the UTC instant for a local wall-clock date+time, skipping the
    /// spring-forward gap (an invalid local time is rolled forward to the first
    /// valid instant). Ambiguous (fall-back) times resolve to the standard offset.
    /// </summary>
    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly localTime, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);
        var guard = 0;
        while (tz.IsInvalidTime(local) && guard++ < 3)
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
    }

    private static bool IsAfterEnd(ReminderRecurrence rule, DateTimeOffset occurrence) =>
        string.Equals(rule.EndCondition, "until_date", StringComparison.OrdinalIgnoreCase)
        && rule.EndDateUtc is { } end && occurrence > end;

    private static HashSet<DayOfWeek> ParseDays(IReadOnlyList<string>? days)
    {
        var set = new HashSet<DayOfWeek>();
        if (days is null)
        {
            return set;
        }

        foreach (var day in days)
        {
            if (TryParseDay(day, out var dow))
            {
                set.Add(dow);
            }
        }

        return set;
    }

    /// <summary>Parses a day token (full name or 3-letter prefix, case-insensitive).</summary>
    public static bool TryParseDay(string? value, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var t = value.Trim().ToLowerInvariant();
        DayOfWeek? parsed = t switch
        {
            _ when t.StartsWith("mon") => DayOfWeek.Monday,
            _ when t.StartsWith("tue") => DayOfWeek.Tuesday,
            _ when t.StartsWith("wed") => DayOfWeek.Wednesday,
            _ when t.StartsWith("thu") => DayOfWeek.Thursday,
            _ when t.StartsWith("fri") => DayOfWeek.Friday,
            _ when t.StartsWith("sat") => DayOfWeek.Saturday,
            _ when t.StartsWith("sun") => DayOfWeek.Sunday,
            _ => null
        };

        if (parsed is null)
        {
            return false;
        }

        dayOfWeek = parsed.Value;
        return true;
    }
}
