using Aluki.Runtime.Reminders;
using Aluki.Runtime.Reminders.Time;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// SB-005 US2: timezone-aware, DST-safe next-occurrence calculation. New York is
/// used for DST transitions (Mexico_City has had no DST since 2022).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReminderRecurrenceCalculatorTests
{
    private static readonly TimeOnly NineAm = new(9, 0);

    private static ReminderRecurrence Daily(string? end = null, DateTimeOffset? endDate = null) =>
        new(ReminderCadence.Daily, null, null, end, endDate);

    [Fact]
    public void Daily_advances_one_local_day()
    {
        // 2026-07-01 09:00 America/Mexico_City (UTC-6) = 15:00Z -> next day 15:00Z.
        var after = DateTimeOffset.Parse("2026-07-01T15:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(Daily(), NineAm, "America/Mexico_City", after);
        Assert.Equal(DateTimeOffset.Parse("2026-07-02T15:00:00Z"), next);
    }

    [Fact]
    public void Daily_holds_local_time_across_spring_forward()
    {
        // 2026-03-07 09:00 EST (UTC-5) = 14:00Z. Spring forward is 2026-03-08.
        // Next must stay 09:00 local = 09:00 EDT (UTC-4) = 13:00Z (UTC shifts, local holds).
        var after = DateTimeOffset.Parse("2026-03-07T14:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(Daily(), NineAm, "America/New_York", after);
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T13:00:00Z"), next);
    }

    [Fact]
    public void Daily_holds_local_time_across_fall_back()
    {
        // 2026-10-31 09:00 EDT (UTC-4) = 13:00Z. Fall back is 2026-11-01.
        // Next stays 09:00 local = 09:00 EST (UTC-5) = 14:00Z.
        var after = DateTimeOffset.Parse("2026-10-31T13:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(Daily(), NineAm, "America/New_York", after);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T14:00:00Z"), next);
    }

    [Fact]
    public void Weekly_picks_next_matching_weekday()
    {
        // 2026-07-06 is a Monday. Rule Mon/Thu, fired Monday -> next Thursday 07-09.
        var rule = new ReminderRecurrence(ReminderCadence.Weekly, new[] { "Mon", "Thu" }, null, null, null);
        var afterMonday = DateTimeOffset.Parse("2026-07-06T13:00:00Z"); // 09:00 EDT
        var next = ReminderRecurrenceCalculator.NextOccurrence(rule, NineAm, "America/New_York", afterMonday);
        Assert.Equal(DateTimeOffset.Parse("2026-07-09T13:00:00Z"), next);
    }

    [Fact]
    public void Weekly_wraps_to_next_week()
    {
        // Rule Mon only, fired Monday -> next Monday (07-13).
        var rule = new ReminderRecurrence(ReminderCadence.Weekly, new[] { "Monday" }, null, null, null);
        var afterMonday = DateTimeOffset.Parse("2026-07-06T13:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(rule, NineAm, "America/New_York", afterMonday);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T13:00:00Z"), next);
    }

    [Fact]
    public void Monthly_clamps_day_31_to_last_day_of_february()
    {
        // dom=31, fired 2026-01-31 -> next is the last day of Feb (2026-02-28).
        var rule = new ReminderRecurrence(ReminderCadence.Monthly, null, 31, null, null);
        var afterJan31 = DateTimeOffset.Parse("2026-01-31T15:00:00Z"); // 09:00 Mexico_City
        var next = ReminderRecurrenceCalculator.NextOccurrence(rule, NineAm, "America/Mexico_City", afterJan31);
        Assert.Equal(DateTimeOffset.Parse("2026-02-28T15:00:00Z"), next);
    }

    [Fact]
    public void Monthly_advances_to_same_day_next_month()
    {
        var rule = new ReminderRecurrence(ReminderCadence.Monthly, null, 15, null, null);
        var afterJul15 = DateTimeOffset.Parse("2026-07-15T15:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(rule, NineAm, "America/Mexico_City", afterJul15);
        Assert.Equal(DateTimeOffset.Parse("2026-08-15T15:00:00Z"), next);
    }

    [Fact]
    public void Until_date_end_boundary_stops_recurrence()
    {
        var end = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        var after = DateTimeOffset.Parse("2026-07-01T15:00:00Z");
        var next = ReminderRecurrenceCalculator.NextOccurrence(Daily("until_date", end), NineAm, "America/Mexico_City", after);
        Assert.Null(next); // next would be 2026-07-02T15:00Z, past the end boundary
    }

    [Fact]
    public void Invalid_timezone_returns_null()
    {
        Assert.Null(ReminderRecurrenceCalculator.NextOccurrence(Daily(), NineAm, "Not/AZone", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void First_occurrence_aligns_weekly_to_requested_day()
    {
        // Start Mon 2026-07-06, weekly Wednesday -> first occurrence Wed 07-08.
        var rule = new ReminderRecurrence(ReminderCadence.Weekly, new[] { "Wed" }, null, null, null);
        var start = DateTimeOffset.Parse("2026-07-06T13:00:00Z");
        var first = ReminderRecurrenceCalculator.FirstOccurrenceOnOrAfter(rule, NineAm, "America/New_York", start);
        Assert.Equal(DateTimeOffset.Parse("2026-07-08T13:00:00Z"), first);
    }

    [Theory]
    [InlineData("mon", DayOfWeek.Monday)]
    [InlineData("Tuesday", DayOfWeek.Tuesday)]
    [InlineData("WED", DayOfWeek.Wednesday)]
    [InlineData("sunday", DayOfWeek.Sunday)]
    public void Day_parsing_is_case_and_length_tolerant(string token, DayOfWeek expected)
    {
        Assert.True(ReminderRecurrenceCalculator.TryParseDay(token, out var dow));
        Assert.Equal(expected, dow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("funday")]
    public void Day_parsing_rejects_invalid(string token)
    {
        Assert.False(ReminderRecurrenceCalculator.TryParseDay(token, out _));
    }
}
