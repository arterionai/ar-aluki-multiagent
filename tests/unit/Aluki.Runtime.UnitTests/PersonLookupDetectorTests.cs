using Aluki.Runtime.Memory.Dispatch;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the deterministic person-lookup intent detector that gates the
/// PersonLookupDomainAgent (priority 58). Must claim explicit "who is X" phrasing
/// (es/en, with or without accents) and extract the name verbatim from the original
/// text, without stealing save intent (SB-013) or generic conversation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PersonLookupDetectorTests
{
    // ── Positive: lookup triggers with name extraction ────────────────────────

    [Theory]
    [InlineData("¿Quién es Fer?", "Fer")]
    [InlineData("quien es Fer Amaro", "Fer Amaro")]
    [InlineData("¿Quiénes son los García?", "los García")]
    [InlineData("qué sabes de Ana", "Ana")]
    [InlineData("que sabes sobre Carlos Pérez", "Carlos Pérez")]
    [InlineData("who is Bob?", "Bob")]
    [InlineData("what do you know about Sarah Connor", "Sarah Connor")]
    public void Lookup_phrases_claim_and_extract_name(string text, string expectedName)
    {
        Assert.True(PersonLookupDetector.TryExtractLookup(text, out var name));
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Name_preserves_original_accents_and_casing()
    {
        Assert.True(PersonLookupDetector.TryExtractLookup("¿QUIÉN ES Sofía Núñez?", out var name));
        Assert.Equal("Sofía Núñez", name);
    }

    [Fact]
    public void Trigger_mid_sentence_claims()
    {
        Assert.True(PersonLookupDetector.TryExtractLookup("oye, ¿quién es Fer?", out var name));
        Assert.Equal("Fer", name);
    }

    [Fact]
    public void Trailing_punctuation_is_stripped_from_name()
    {
        Assert.True(PersonLookupDetector.TryExtractLookup("quien es Fer!?", out var name));
        Assert.Equal("Fer", name);
    }

    // ── Negative: save intent wins (SB-013 defense in depth) ──────────────────

    [Theory]
    [InlineData("guarda que no sé quién es Fer")]
    [InlineData("anota que quien es Fer no importa")]
    [InlineData("recuérdame que Fer es quien es")]
    public void Save_intent_never_claims_lookup(string text)
    {
        Assert.False(PersonLookupDetector.TryExtractLookup(text, out _));
    }

    // ── Negative: empty name after trigger ────────────────────────────────────

    [Theory]
    [InlineData("¿quién es?")]
    [InlineData("quien es ")]
    [InlineData("who is ?")]
    public void Trigger_without_name_does_not_claim(string text)
    {
        Assert.False(PersonLookupDetector.TryExtractLookup(text, out _));
    }

    // ── Negative: unrelated text ──────────────────────────────────────────────

    [Theory]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData("recuérdame llamar a Fer mañana")]
    [InlineData("agéndame una cita con Fer")]
    [InlineData("quienes vinieron ayer")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Unrelated_text_does_not_claim(string? text)
    {
        Assert.False(PersonLookupDetector.TryExtractLookup(text, out _));
    }
}
