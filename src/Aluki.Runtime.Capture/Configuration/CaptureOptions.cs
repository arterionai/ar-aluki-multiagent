namespace Aluki.Runtime.Capture.Configuration;

/// <summary>Bound from the <c>Capture</c> configuration section.</summary>
public sealed class CaptureOptions
{
    public const string SectionName = "Capture";

    public string SourceChannel { get; set; } = "whatsapp";

    public RetryOptions Retry { get; set; } = new();
}

public sealed class RetryOptions
{
    /// <summary>Maximum total attempts including the first (FR-017: max 5).</summary>
    public int MaxAttempts { get; set; } = 5;

    public int BaseDelayMilliseconds { get; set; } = 100;

    public int MaxDelayMilliseconds { get; set; } = 2000;
}
