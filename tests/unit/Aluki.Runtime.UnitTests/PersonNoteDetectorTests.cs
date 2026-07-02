using Aluki.Runtime.Memory.Dispatch;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the deterministic person-note intent detector that gates the
/// PersonMemoryDomainAgent (priority 55). Must claim genuine person-note phrasing
/// (es/en, with or without accents) without stealing reminder intent or generic text.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PersonNoteDetectorTests
{
    // ── Positive: unconditional triggers ─────────────────────────────────────

    [Theory]
    [InlineData("guarda que Fer Amaro la conocí en galería")]
    [InlineData("Guarda que Juan es el dentista de los niños")]
    [InlineData("anota que María trabaja en TechCorp")]
    [InlineData("Anota que Carlos es mi vecino")]
    [InlineData("apunta que Ana me debe $500")]
    [InlineData("toma nota que Luis es el jefe de ventas")]
    [InlineData("nota que Sofía es la hermana de Pedro")]
    public void Unconditional_triggers_always_claim(string text)
    {
        Assert.True(PersonNoteDetector.LooksLikePersonNote(text));
    }

    // ── Positive: conditional trigger without temporal ────────────────────────

    [Theory]
    [InlineData("Recuérdame que Fer amaro la conocí en galería afuera de la casa de bluey")]
    [InlineData("recuerdame que Juan es el dentista")]
    [InlineData("Recuérdame que Ana es mi prima")]
    [InlineData("Remember that Sarah is my sister")]
    [InlineData("keep in mind that Bob is the sales manager")]
    public void Conditional_trigger_without_temporal_claims(string text)
    {
        Assert.True(PersonNoteDetector.LooksLikePersonNote(text));
    }

    // ── Negative: conditional trigger WITH temporal → falls to reminder ───────

    [Theory]
    [InlineData("Recuérdame que tengo reunión mañana a las 3")]
    [InlineData("recuérdame que debo llamar a Ana mañana")]
    [InlineData("recuérdame que el martes tengo cita médica")]
    [InlineData("recuérdame que esta tarde voy al gimnasio")]
    [InlineData("recuérdame que en 30 minutos empieza la llamada")]
    [InlineData("remember that tomorrow I have a meeting")]
    [InlineData("recuérdame que el viernes es el cumpleaños de Juan")]
    [InlineData("recuérdame que la proxima semana hay junta")]
    public void Conditional_trigger_with_temporal_does_not_claim(string text)
    {
        Assert.False(PersonNoteDetector.LooksLikePersonNote(text));
    }

    // ── Negative: reminder phrasing without "que" ─────────────────────────────

    [Theory]
    [InlineData("recuérdame llamar a Fer mañana")]
    [InlineData("recuérdame comprar leche")]
    [InlineData("avisame cuando llegue Juan")]
    [InlineData("ponme un recordatorio para las 5pm")]
    [InlineData("remind me to call Sarah")]
    public void Reminder_phrasing_without_que_does_not_claim(string text)
    {
        Assert.False(PersonNoteDetector.LooksLikePersonNote(text));
    }

    // ── Negative: unrelated text ──────────────────────────────────────────────

    [Theory]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData("¿Quién es Fer Amaro?")]
    [InlineData("agéndame una cita mañana")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Unrelated_text_does_not_claim(string? text)
    {
        Assert.False(PersonNoteDetector.LooksLikePersonNote(text));
    }

    // ── Accent and case insensitivity ─────────────────────────────────────────

    [Theory]
    [InlineData("GUARDA QUE Pedro es ingeniero")]
    [InlineData("RECUÉRDAME QUE Sofía es médica")]
    [InlineData("Recuerdame que (sin acento) Carlos es contador")]
    public void Detection_is_accent_and_case_insensitive(string text)
    {
        Assert.True(PersonNoteDetector.LooksLikePersonNote(text));
    }
}
