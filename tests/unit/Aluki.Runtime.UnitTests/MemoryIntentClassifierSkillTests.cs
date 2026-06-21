using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Skills;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class MemoryIntentClassifierSkillTests
{
    private readonly MemoryIntentClassifierSkill _classifier = new();

    [Theory]
    [InlineData("¿Cuándo es la cita con el dentista?")]
    [InlineData("what did I say about the budget?")]
    [InlineData("Que compre en la tienda")] // leading interrogative
    [InlineData("recuerdas mi numero de vuelo")]
    public void Classifies_recall_queries(string input)
    {
        Assert.Equal(MemoryIntent.RecallQuery, _classifier.Classify(input));
    }

    [Theory]
    [InlineData("Comprar leche mañana")]
    [InlineData("La reunión es el martes a las 3pm")]
    [InlineData("Mi color favorito es el azul")]
    public void Classifies_notes(string input)
    {
        Assert.Equal(MemoryIntent.NoteToStore, _classifier.Classify(input));
    }

    [Fact]
    public void Empty_input_defaults_to_note()
    {
        Assert.Equal(MemoryIntent.NoteToStore, _classifier.Classify(""));
        Assert.Equal(MemoryIntent.NoteToStore, _classifier.Classify("   "));
    }
}
