namespace Aluki.Runtime.DelegatedReminders.Policies;

/// <summary>
/// Delivery retry backoff for delegated reminders (spec §FR-006):
/// transient failures retry with doubling backoff 1s, 2s, 4s, 8s, 16s — a total
/// window of 31 seconds across up to 5 attempts. Permanent failures never retry.
/// </summary>
public static class DelegatedReminderRetryPolicy
{
    private static readonly int[] BackoffTable = [1, 2, 4, 8, 16];

    /// <summary>Backoff in seconds after <paramref name="failedAttemptNumber"/> (1→1s, 2→2s, …, 5→16s).</summary>
    public static int BackoffSeconds(int failedAttemptNumber)
    {
        var index = Math.Max(failedAttemptNumber, 1) - 1;
        return index < BackoffTable.Length ? BackoffTable[index] : BackoffTable[^1];
    }

    /// <summary>Returns the next retry time, or null when retries are exhausted (attempt ≥ max).</summary>
    public static DateTimeOffset? NextRetry(DateTimeOffset now, int failedAttemptNumber, int maxAttempts)
    {
        if (failedAttemptNumber >= maxAttempts)
        {
            return null;
        }

        return now.AddSeconds(BackoffSeconds(failedAttemptNumber));
    }
}
