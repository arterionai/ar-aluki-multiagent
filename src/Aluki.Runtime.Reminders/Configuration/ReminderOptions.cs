namespace Aluki.Runtime.Reminders.Configuration;

/// <summary>Bound from the <c>Reminders</c> configuration section.</summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Default free-tier active reminder limit per tenant.</summary>
    public int DefaultQuotaLimit { get; set; } = 10;

    /// <summary>Maximum snooze duration in seconds (24h cap per spec).</summary>
    public int MaxSnoozeSeconds { get; set; } = 86_400;

    /// <summary>Maximum delivery attempts before terminal failure (initial + retries).</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Hours after the due time before an undelivered reminder expires.</summary>
    public int OverdueExpiryHours { get; set; } = 24;

    /// <summary>Max reminders processed per sweep pass (bounds work per tick).</summary>
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>
    /// Local hour (0–23) assumed when the user gives a date/day but no time
    /// ("recuérdame mañana pagar mis tarjetas" → 09:00 local).
    /// </summary>
    public int DefaultHourLocal { get; set; } = 9;

    /// <summary>Local hour assumed for "en la mañana" / "in the morning".</summary>
    public int MorningHourLocal { get; set; } = 9;

    /// <summary>Local hour assumed for "al mediodía" / "at noon".</summary>
    public int MiddayHourLocal { get; set; } = 12;

    /// <summary>Local hour assumed for "en la tarde" / "in the afternoon".</summary>
    public int AfternoonHourLocal { get; set; } = 16;

    /// <summary>Local hour assumed for "en la noche" / "tonight" / "in the evening".</summary>
    public int EveningHourLocal { get; set; } = 20;
}
