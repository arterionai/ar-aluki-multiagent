using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Reminders.Dispatch;

/// <summary>
/// Deterministic, accent-insensitive detection of "set a reminder" intent from free text.
/// Pure (no I/O) so it is unit-testable and cheap enough to run inside ClaimsIntent.
/// </summary>
public static class ReminderSchedulingDetector
{
    private static readonly string[] Triggers =
    [
        // Spanish — imperative
        "recuerdame",
        "avisame",
        "ponme un recordatorio", "pon un recordatorio", "pon recordatorio",
        "crea un recordatorio", "crea recordatorio", "crear un recordatorio",
        "agenda un recordatorio", "programame un recordatorio",
        "mandame un recordatorio", "mandame recordatorio",
        "no olvides recordarme", "que no se me olvide",
        // Spanish — polite/interrogative forms ("puedes recordarme", "me recuerdes", etc.)
        "puedes recordarme", "podrias recordarme", "me puedes recordar",
        "me recuerdes", "me recuerdas",
        "recordarme que", "recordarme de",
        // English
        "remind me", "set a reminder", "set reminder",
        "create a reminder", "add a reminder", "create reminder",
        "can you remind me", "could you remind me",
    ];

    public static bool LooksLikeReminder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);
        return Triggers.Any(normalized.Contains);
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lowered.Length);
        foreach (var c in lowered)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }
}
