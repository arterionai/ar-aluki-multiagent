namespace Aluki.Runtime.Reminders.Policies;

/// <summary>
/// Snooze duration policy (spec §Snooze Semantics). Presets are 5/15/30 min,
/// 1 hour, and next-day; the maximum is capped at 24 hours. Snooze reschedules
/// the same reminder instance to <c>now + duration</c>.
/// </summary>
public static class ReminderSnoozePolicy
{
    public static readonly IReadOnlyList<int> PresetSeconds =
        new[] { 300, 900, 1800, 3600, 86_400 };

    /// <summary>
    /// Clamps a requested snooze duration to (0, maxSeconds]. Non-positive values
    /// fall back to the smallest preset; values over the cap are capped.
    /// </summary>
    public static int ResolveDurationSeconds(int? requestedSeconds, int maxSeconds)
    {
        var cap = maxSeconds <= 0 ? 86_400 : maxSeconds;
        if (requestedSeconds is null or <= 0)
        {
            return Math.Min(PresetSeconds[0], cap);
        }

        return Math.Min(requestedSeconds.Value, cap);
    }

    /// <summary>Computes the new fire time for a snooze applied at <paramref name="now"/>.</summary>
    public static DateTimeOffset NextFireTime(DateTimeOffset now, int durationSeconds) =>
        now.AddSeconds(durationSeconds);
}
