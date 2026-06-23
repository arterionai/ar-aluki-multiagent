using System.Text.Json;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Reminders.Dispatch;

public sealed record ReminderParseResult(
    bool Success,
    string? ReminderText = null,
    DateTimeOffset? ScheduledTimeUtc = null,
    string? Error = null);

/// <summary>
/// Uses the LLM to extract (reminder_text, scheduled_time_utc) from a natural-language
/// message. The caller supplies the current UTC time so relative expressions like
/// "en 2 minutos" or "mañana a las 3pm" can be resolved correctly.
/// </summary>
public sealed class ReminderIntentParser
{
    private const string SystemPrompt =
        """
        You are a reminder extraction assistant. Given a user message, extract two fields:

        1. "reminder_text": A concise action-oriented description of what the user wants to be
           reminded about (e.g. "leer el feedback", not the raw request phrase).
        2. "scheduled_time_utc": The ISO 8601 UTC timestamp when to fire the reminder.
           Interpret relative expressions ("en 2 minutos", "en 1 hora", "mañana a las 3pm",
           "in 5 minutes") relative to the current UTC time provided in the user prompt.
           Default local timezone for expressions without an explicit timezone: America/Mexico_City.

        Respond ONLY with a JSON object: {"reminder_text": "...", "scheduled_time_utc": "..."}
        If you cannot determine a clear future time, set "scheduled_time_utc" to null.
        Do not include markdown fences or any other text.
        """;

    private readonly IChatModelRouter _router;
    private readonly ILogger<ReminderIntentParser> _logger;

    public ReminderIntentParser(IChatModelRouter router, ILogger<ReminderIntentParser> logger)
    {
        _router = router;
        _logger = logger;
    }

    public async Task<ReminderParseResult> ParseAsync(
        string message,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var userPrompt = $"Current UTC time: {nowUtc:O}\n\nUser message: {message}";

        try
        {
            var raw = await _router.CompleteAsync(SystemPrompt, userPrompt, ct);
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

            if (string.IsNullOrWhiteSpace(text) || scheduledUtc is null)
                return new ReminderParseResult(false, Error: "Could not extract reminder text or scheduled time");

            return new ReminderParseResult(true, text.Trim(), scheduledUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReminderIntentParser failed. message={Message}", message);
            return new ReminderParseResult(false, Error: ex.Message);
        }
    }

    private static string StripFences(string s)
    {
        if (!s.StartsWith("```")) return s;
        var lines = s.Split('\n');
        // Drop first (```json or ```) and last (```) lines.
        return string.Join('\n', lines[1..^1]).Trim();
    }
}
