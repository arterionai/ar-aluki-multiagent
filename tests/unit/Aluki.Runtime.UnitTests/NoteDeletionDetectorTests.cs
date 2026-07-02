using Aluki.Runtime.Memory.Dispatch;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the deterministic note-deletion intent detector that gates the
/// NoteDeletionDomainAgent (priority 57). Must claim explicit deletion phrasing
/// (es/en) and extract the topic verbatim, without stealing save intent (SB-013)
/// or generic conversation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NoteDeletionDetectorTests
{
    // ── Positive: deletion triggers with topic extraction ─────────────────────

    [Theory]
    [InlineData("borra lo de Fer", "Fer")]
    [InlineData("Borra la nota de Juan Pérez", "Juan Pérez")]
    [InlineData("elimina lo de la galería", "la galería")]
    [InlineData("Elimina la nota de Ana", "Ana")]
    [InlineData("olvida lo de Carlos", "Carlos")]
    [InlineData("olvida a Sofía", "Sofía")]
    [InlineData("forget about Bob", "Bob")]
    [InlineData("delete the note about Sarah Connor", "Sarah Connor")]
    public void Deletion_phrases_claim_and_extract_topic(string text, string expectedTopic)
    {
        Assert.True(NoteDeletionDetector.TryExtractDeletion(text, out var topic));
        Assert.Equal(expectedTopic, topic);
    }

    [Fact]
    public void Topic_preserves_original_accents_and_casing()
    {
        Assert.True(NoteDeletionDetector.TryExtractDeletion("BORRA LO DE Sofía Núñez!", out var topic));
        Assert.Equal("Sofía Núñez", topic);
    }

    // ── Negative: save intent wins (SB-013 defense in depth) ──────────────────

    [Theory]
    [InlineData("guarda que hay que borrar lo de Fer")]
    [InlineData("anota que olvida lo de Fer no aplica")]
    public void Save_intent_never_claims_deletion(string text)
    {
        Assert.False(NoteDeletionDetector.TryExtractDeletion(text, out _));
    }

    // ── Negative: empty topic after trigger ───────────────────────────────────

    [Theory]
    [InlineData("borra lo de ")]
    [InlineData("forget about ?")]
    public void Trigger_without_topic_does_not_claim(string text)
    {
        Assert.False(NoteDeletionDetector.TryExtractDeletion(text, out _));
    }

    // ── Negative: unrelated text ──────────────────────────────────────────────

    [Theory]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData("¿Quién es Fer?")]
    [InlineData("recuérdame llamar a Fer mañana")]
    [InlineData("borra el pizarrón")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Unrelated_text_does_not_claim(string? text)
    {
        Assert.False(NoteDeletionDetector.TryExtractDeletion(text, out _));
    }
}
