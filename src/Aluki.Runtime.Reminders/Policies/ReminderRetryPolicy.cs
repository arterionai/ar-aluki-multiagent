namespace Aluki.Runtime.Reminders.Policies;

/// <summary>
/// Delivery retry backoff (spec §Terminal Delivery Outcomes / delivery contract):
/// transient failures retry with exponential backoff 5s, 25s, 125s. Note: the
/// fire-sweep runs on a ~1 minute cadence, so sub-minute backoffs effectively
/// round up to the next sweep tick — the schedule is the lower bound, not exact
/// timing. Sub-minute precision is a Durable-Functions follow-up.
/// </summary>
public static class ReminderRetryPolicy
{
    private const double BaseSeconds = 5.0;
    private const double Factor = 5.0;

    /// <summary>Backoff in seconds after <paramref name="failedAttemptNumber"/> (1→5s, 2→25s, 3→125s).</summary>
    public static double BackoffSeconds(int failedAttemptNumber)
    {
        var n = Math.Max(failedAttemptNumber, 1);
        return BaseSeconds * Math.Pow(Factor, n - 1);
    }

    /// <summary>The next retry time, or null when retries are exhausted (attempt ≥ max).</summary>
    public static DateTimeOffset? NextRetry(DateTimeOffset now, int failedAttemptNumber, int maxAttempts)
    {
        if (failedAttemptNumber >= maxAttempts)
        {
            return null;
        }

        return now.AddSeconds(BackoffSeconds(failedAttemptNumber));
    }
}
