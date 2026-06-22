namespace Aluki.Runtime.DelegatedReminders.Configuration;

/// <summary>Bound from the <c>DelegatedReminders</c> configuration section.</summary>
public sealed class DelegatedReminderOptions
{
    public const string SectionName = "DelegatedReminders";

    /// <summary>Max delegated reminders a sender may create in a rolling 24-hour window.</summary>
    public int DailyAntiSpamLimit { get; set; } = 10;

    /// <summary>Maximum delivery attempts (initial + retries) for transient failures.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Seconds before due time within which cancellation is still accepted.</summary>
    public int CancellationWindowSeconds { get; set; } = 30;

    /// <summary>Max delegated reminders processed per sweep pass.</summary>
    public int SweepBatchSize { get; set; } = 100;
}
