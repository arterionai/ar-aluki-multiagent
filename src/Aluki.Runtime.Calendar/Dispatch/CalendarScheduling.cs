using System.Globalization;
using System.Text;
using Aluki.Runtime.Abstractions.Skills.Calendar;

namespace Aluki.Runtime.Calendar.Dispatch;

/// <summary>
/// Deterministic, accent-insensitive detection of "schedule a calendar event" intent
/// from free text, plus provider-hint extraction. Pure (no I/O) so it is unit-testable
/// and cheap enough to run inside an agent's <c>ClaimsIntent</c> guard.
/// </summary>
public static class CalendarSchedulingDetector
{
    // Conservative create-intent phrases (normalized: lowercase, accents stripped).
    // Tight enough to avoid stealing recall queries like "¿qué reunión tuve ayer?".
    private static readonly string[] Triggers =
    [
        "agendame", "agendar", "agendale", "agenda una", "agenda un",
        "crea un evento", "crear evento", "creame un evento", "crea una cita",
        "creame una cita", "crea una reunion", "agrega un evento", "agrega una cita",
        "anade un evento", "anade una cita", "pon una cita", "ponme una cita",
        "pon un evento", "ponme un evento", "programa una", "programa un", "programame",
        "schedule a", "schedule an", "book a meeting", "book an appointment",
        "set up a meeting", "create an event", "create a meeting", "add an event",
        "en mi calendario", "a mi calendario", "to my calendar", "on my calendar",
    ];

    public static bool LooksLikeScheduling(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);
        return Triggers.Any(normalized.Contains);
    }

    /// <summary>Returns "outlook"/"google" if the text names a provider, else null.</summary>
    public static string? DetectProviderHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var n = Normalize(text);
        if (n.Contains("outlook") || n.Contains("microsoft") || n.Contains("office")) return "outlook";
        if (n.Contains("google") || n.Contains("gmail") || n.Contains("gcal")) return "google";
        return null;
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

/// <summary>
/// Composes the Spanish WhatsApp replies the calendar agent sends for each outcome.
/// Pure functions for unit-testability.
/// </summary>
public static class CalendarSchedulingReply
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string ProviderName(CalendarProvider provider) => provider switch
    {
        CalendarProvider.Outlook => "Outlook",
        CalendarProvider.Google => "Google Calendar",
        _ => provider.ToString(),
    };

    public static string ConnectPrompt(IReadOnlyList<(CalendarProvider Provider, string Url)> links)
    {
        if (links.Count == 0)
            return "El calendario no está disponible por ahora. Inténtalo más tarde. 🗓️";

        var sb = new StringBuilder();
        sb.Append("Para agendar tu cita primero necesito conectar tu calendario. ");
        sb.Append("Abre este enlace seguro, revisa los permisos y, si estás de acuerdo, conéctalo:\n");
        foreach (var (provider, url) in links)
            sb.Append($"\n• {ProviderName(provider)}: {url}");
        sb.Append("\n\nCuando termines, vuelve aquí y vuelve a pedirme que agende. 🔒");
        return sb.ToString();
    }

    public static string Confirmation(string? title, DateTimeOffset? startUtc, string? timezone)
    {
        var what = string.IsNullOrWhiteSpace(title) ? "tu evento" : $"«{title}»";
        var when = FormatLocal(startUtc, timezone);
        return when is null
            ? $"✅ Listo, agendé {what} en tu calendario."
            : $"✅ Listo, agendé {what} para el {when}.";
    }

    public static string Clarification(string? question) =>
        string.IsNullOrWhiteSpace(question)
            ? "Para agendarlo necesito un poco más de detalle (¿qué día y a qué hora?)."
            : question;

    public static string Denied() =>
        "No tienes permiso para agendar en este contexto.";

    public static string Failed() =>
        "No pude agendar la cita en este momento. Inténtalo de nuevo en un momento. 🙏";

    private static string? FormatLocal(DateTimeOffset? startUtc, string? timezone)
    {
        if (startUtc is null) return null;
        try
        {
            if (!string.IsNullOrWhiteSpace(timezone))
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                var local = TimeZoneInfo.ConvertTime(startUtc.Value, tz);
                return local.ToString("dddd d 'de' MMMM, HH:mm", Es);
            }
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        return startUtc.Value.UtcDateTime.ToString("dddd d 'de' MMMM, HH:mm 'UTC'", Es);
    }
}
