using Aluki.Runtime.Calendar.Dispatch;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the deterministic scheduling-intent detector that gates the
/// WhatsApp calendar agent. Must catch genuine "create event" phrasing (es/en,
/// with or without accents) without stealing recall queries or chit-chat.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarSchedulingDetectorTests
{
    [Theory]
    [InlineData("Agéndame una cita con el dentista mañana a las 3pm")]
    [InlineData("agendame una reunion el lunes")]
    [InlineData("¿Me puedes agendar un evento para el viernes?")]
    [InlineData("Crea un evento para la junta de las 10")]
    [InlineData("créame una cita el martes")]
    [InlineData("ponme una cita con Juan el jueves")]
    [InlineData("Programa una llamada para las 5")]
    [InlineData("agrega un evento a mi calendario")]
    [InlineData("schedule a meeting tomorrow at 4pm")]
    [InlineData("can you book an appointment for me on Friday")]
    [InlineData("add an event to my calendar")]
    public void Detects_scheduling_intent(string text)
    {
        Assert.True(CalendarSchedulingDetector.LooksLikeScheduling(text));
    }

    [Theory]
    [InlineData("Hola, ¿cómo estás?")]
    [InlineData("¿Qué reunión tuve ayer?")]
    [InlineData("Recuérdame qué dijo Pedro sobre el proyecto")]
    [InlineData("gracias!")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_non_scheduling_text(string? text)
    {
        Assert.False(CalendarSchedulingDetector.LooksLikeScheduling(text));
    }

    [Theory]
    [InlineData("agéndame en Outlook una cita", "outlook")]
    [InlineData("crea un evento en mi microsoft", "outlook")]
    [InlineData("agrega un evento a google calendar", "google")]
    [InlineData("ponme una cita en gmail", "google")]
    [InlineData("agéndame una cita mañana", null)]
    public void Detects_provider_hint(string text, string? expected)
    {
        Assert.Equal(expected, CalendarSchedulingDetector.DetectProviderHint(text));
    }
}
