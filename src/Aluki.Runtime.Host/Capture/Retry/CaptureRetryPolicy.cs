using Aluki.Runtime.Host.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aluki.Runtime.Host.Capture.Retry;

/// <summary>
/// Bounded exponential backoff policy for transient capture persistence failures
/// (FR-009, FR-017, SC-009). Caps total attempts (default 5), classifies
/// transient faults, and exposes per-attempt delay metadata.
/// </summary>
public sealed class CaptureRetryPolicy
{
    private readonly RetryOptions _options;

    public CaptureRetryPolicy(IOptions<CaptureOptions> options)
    {
        _options = options.Value.Retry;
    }

    /// <summary>Maximum total attempts including the first.</summary>
    public int MaxAttempts => Math.Max(1, _options.MaxAttempts);

    public bool HasAttemptsRemaining(int attemptNumber) => attemptNumber < MaxAttempts;

    /// <summary>
    /// Classifies whether a fault is transient and therefore retry-eligible.
    /// Permanent faults (e.g. constraint violations, invalid input) are not retried.
    /// </summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        PostgresException postgres => IsTransientSqlState(postgres.SqlState),
        NpgsqlException npgsql => npgsql.IsTransient,
        _ => false
    };

    /// <summary>Bounded exponential backoff: base * 2^(attempt-1), capped at max.</summary>
    public TimeSpan ComputeDelay(int attemptNumber)
    {
        var exponent = Math.Max(0, attemptNumber - 1);
        var baseMs = Math.Max(0, _options.BaseDelayMilliseconds);
        var maxMs = Math.Max(baseMs, _options.MaxDelayMilliseconds);

        // Guard against overflow for large exponents.
        var scaled = exponent >= 20
            ? (double)maxMs
            : baseMs * Math.Pow(2, exponent);

        var delayMs = Math.Min(scaled, maxMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static bool IsTransientSqlState(string? sqlState) => sqlState switch
    {
        // Class 40 — transaction rollback (serialization failure, deadlock).
        "40001" or "40P01" => true,
        // Class 08 — connection exceptions.
        "08000" or "08003" or "08006" or "08001" or "08004" => true,
        // 57P03 — cannot connect now (server starting up).
        "57P03" => true,
        _ => false
    };
}
