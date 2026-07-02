using System.Text.Json;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Reminders.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Reminders.Dispatch;

public sealed record ReminderParseResult(
    bool Success,
    string? ReminderText = null,
    DateTimeOffset? ScheduledTimeUtc = null,
    string? Error = null,
    bool TimeExplicit = true);

/// <summary>
/// Uses the LLM to extract (reminder_text, scheduled_time_utc, time_explicit) from a
/// natural-language message. The caller supplies the current UTC time so relative
/// expressions like "en 2 minutos" or "mañana a las 3pm" can be resolved correctly.
/// When the message gives a date/day but no time of day ("recuérdame mañana pagar mis
/// tarjetas"), a configurable default local hour is applied (Reminders:DefaultHourLocal,
/// 09:00 by default) and <c>TimeExplicit</c> is false so callers can tell the user the
/// hour was assumed.
/// </summary>
public sealed class ReminderIntentParser
{
    private const string SystemPromptTemplate =
        """
        You are a reminder extraction assistant. Given a user message, extract three fields:

        1. "reminder_text": A concise action-oriented description of what the user wants to be
           reminded about (e.g. "leer el feedback", not the raw request phrase).
        2. "scheduled_time_utc": The ISO 8601 UTC timestamp when to fire the reminder.
           Interpret relative expressions ("en 2 minutos", "en 1 hora", "mañana a las 3pm",
           "in 5 minutes") relative to the current UTC time provided in the user prompt.
           Default local timezone for expressions without an explicit timezone: {0}.
           IMPORTANT: The scheduled time MUST be in the future relative to the current UTC
           time. Pay close attention to the YEAR in the current UTC time — always use the
           current year or a future year, never a past year.
        3. "time_explicit": true when the user stated a concrete time of day ("a las 3pm",
           "9am") or a relative offset ("en 30 minutos", "in 2 hours"); false when you
           applied a default hour using the rules below.

        Default-hour rules (local time in {0}) — when the message gives a date or day but no
        concrete time, use these hours and set "time_explicit" to false:
        - Date/day with no time-of-day hint ("mañana", "el lunes", "tomorrow"): {1}.
        - "en la mañana" / "in the morning": {2}.
        - "al mediodía" / "at noon": {3}.
        - "en la tarde" / "in the afternoon": {4}.
        - "en la noche" / "esta noche" / "tonight" / "in the evening": {5}.

        Respond ONLY with a JSON object:
        {{"reminder_text": "...", "scheduled_time_utc": "...", "time_explicit": true}}
        Set "scheduled_time_utc" to null ONLY when the message contains no date, day, or
        time reference at all.
        Do not include markdown fences or any other text.
        """;

    private readonly IChatModelRouter _router;
    private readonly ILogger<ReminderIntentParser> _logger;
    private readonly ReminderOptions _options;

    public ReminderIntentParser(
        IChatModelRouter router,
        ILogger<ReminderIntentParser> logger,
        IOptions<ReminderOptions>? options = null)
    {
        _router = router;
        _logger = logger;
        _options = options?.Value ?? new ReminderOptions();
    }

    public async Task<ReminderParseResult> ParseAsync(
        string message,
        DateTimeOffset nowUtc,
        string userTimezone,
        CancellationToken ct)
    {
        var systemPrompt = string.Format(
            SystemPromptTemplate,
            userTimezone,
            FormatHour(_options.DefaultHourLocal),
            FormatHour(_options.MorningHourLocal),
            FormatHour(_options.MiddayHourLocal),
            FormatHour(_options.AfternoonHourLocal),
            FormatHour(_options.EveningHourLocal));
        var userPrompt = $"Current UTC time: {nowUtc:O}\n\nUser message: {message}";

        try
        {
            // Use a standalone token so the webhook's cancellation (client disconnect / host
            // lifecycle) doesn't abort the LLM call mid-flight.  The outer ct is still checked
            // after the call returns so a host shutdown still propagates cleanly.
            using var llmCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var raw = await _router.CompleteAsync(systemPrompt, userPrompt, llmCts.Token);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
                return new ReminderParseResult(false, Error: "LLM returned empty response");

            var json = StripFences(raw.Trim());

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var text = root.TryGetProperty("reminder_text", out var textEl) ? textEl.GetString() : null;

            DateTimeOffset? scheduledUtc = null;
            if (root.TryGetProperty("scheduled_time_utc", out var timeEl)
                && timeEl.ValueKind != JsonValueKind.Null)
            {
                if (DateTimeOffset.TryParse(timeEl.GetString(), out var parsed))
                    scheduledUtc = parsed.ToUniversalTime();
            }

            // Absent or malformed field defaults to true (backward compatible with older
            // model responses that only carry the two original fields).
            var timeExplicit = !root.TryGetProperty("time_explicit", out var explicitEl)
                               || explicitEl.ValueKind is not (JsonValueKind.False or JsonValueKind.True)
                               || explicitEl.GetBoolean();

            if (string.IsNullOrWhiteSpace(text) || scheduledUtc is null)
                return new ReminderParseResult(false, Error: "Could not extract reminder text or scheduled time");

            return new ReminderParseResult(true, text.Trim(), scheduledUtc, TimeExplicit: timeExplicit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReminderIntentParser failed. message={Message}", message);
            return new ReminderParseResult(false, Error: ex.Message);
        }
    }

    private static string FormatHour(int hour) => $"{hour:00}:00";

    private static string StripFences(string s)
    {
        if (!s.StartsWith("```")) return s;
        var lines = s.Split('\n');
        // Drop first (```json or ```) and last (```) lines.
        return string.Join('\n', lines[1..^1]).Trim();
    }
}
