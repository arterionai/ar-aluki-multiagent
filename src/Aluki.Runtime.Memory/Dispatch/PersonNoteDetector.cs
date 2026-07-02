using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Deterministic, accent-insensitive detection of "person note" intent from free text.
/// Pure (no I/O) so it is unit-testable and cheap enough to run inside ClaimsIntent.
///
/// Disambiguation rule vs. reminders:
///   "recuérdame [task] mañana"   → REMINDER  (no "que", falls to ReminderDomainAgent)
///   "recuérdame que [person] …"  → PERSON NOTE if no future temporal expression
///   "guarda / anota / apunta que" → always PERSON NOTE
/// </summary>
public static class PersonNoteDetector
{
    // Always treated as person-note intent regardless of temporal content.
    private static readonly string[] UnconditionalTriggers =
    [
        "guarda que",
        "anota que",
        "apunta que",
        "toma nota que",
        "nota que",
    ];

    // Treated as person-note intent only when no future temporal expression is detected.
    private static readonly string[] ConditionalTriggers =
    [
        "recuerdame que",
        "remember that",
        "keep in mind that",
    ];

    // Markers that signal a reminder delivery time, making "recuerdame que" a REMINDER.
    private static readonly string[] FutureTemporalMarkers =
    [
        "manana",           // mañana (normalized)
        "a las",
        "el lunes", "el martes", "el miercoles", "el jueves",
        "el viernes", "el sabado", "el domingo",
        "pasado manana",    // pasado mañana (normalized)
        "la proxima semana", "la semana que",
        "esta tarde", "esta noche",
        "dentro de",
        "hoy a las",
        // English
        "tomorrow", "tonight", "next week", "next month",
        // Unit words that imply a delay ("en 30 minutos", "in 2 hours")
        "minutos", "horas", "dias", "semanas",
        "minutes", "hours", "days", "weeks",
    ];

    public static bool LooksLikePersonNote(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);

        if (UnconditionalTriggers.Any(normalized.Contains))
            return true;

        if (ConditionalTriggers.Any(normalized.Contains))
            return !FutureTemporalMarkers.Any(normalized.Contains);

        return false;
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
