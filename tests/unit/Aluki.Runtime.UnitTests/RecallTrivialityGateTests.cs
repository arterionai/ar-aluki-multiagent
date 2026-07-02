using Aluki.Runtime.Memory.Recall;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class RecallTrivialityGateTests
{
    // --- Trivial: greetings / thanks / acks / emoji-only → skip recall ---

    [Theory]
    [InlineData("hola")]
    [InlineData("Hola!")]
    [InlineData("buenos días")]
    [InlineData("Buenas tardes")]
    [InlineData("buenas noches")]
    [InlineData("gracias")]
    [InlineData("Muchas gracias!")]
    [InlineData("mil gracias")]
    [InlineData("ok")]
    [InlineData("Ok gracias")]
    [InlineData("vale")]
    [InlineData("listo")]
    [InlineData("perfecto")]
    [InlineData("genial")]
    [InlineData("entendido")]
    [InlineData("jaja")]
    [InlineData("jajaja ok")]
    [InlineData("👍")]
    [InlineData("🙏🙏")]
    [InlineData("thanks")]
    [InlineData("thank you")]
    [InlineData("hello")]
    [InlineData("good morning")]
    [InlineData("hasta luego")]
    [InlineData("   ")]
    public void Trivial_messages_skip_recall(string text)
    {
        Assert.True(RecallTrivialityGate.ShouldSkipRecall(text));
    }

    // --- Hard negatives: anything that could need memory must run recall ---

    [Theory]
    [InlineData("¿quién es Fer?")]
    [InlineData("quien es Fer")]
    [InlineData("hola, qué guardé ayer?")]
    [InlineData("¿gracias a quién le debo el dinero?")]
    [InlineData("qué me recomiendas")]
    [InlineData("recuérdame que Fer es mi prima")]
    [InlineData("dónde dejé las llaves")]
    [InlineData("what did I save yesterday?")]
    [InlineData("hola necesito ayuda con algo")]
    [InlineData("el dentista")]
    [InlineData("Fer")]
    [InlineData("gracias por la nota sobre el doctor")]
    public void Substantive_messages_run_recall(string text)
    {
        Assert.False(RecallTrivialityGate.ShouldSkipRecall(text));
    }

    [Fact]
    public void Question_marks_always_force_recall_even_on_trivial_words()
    {
        Assert.False(RecallTrivialityGate.ShouldSkipRecall("hola?"));
        Assert.False(RecallTrivialityGate.ShouldSkipRecall("¿ok?"));
    }

    [Fact]
    public void More_than_four_words_always_runs_recall()
    {
        // All tokens are individually trivial but the message is long enough
        // to be conversational — stay conservative and run recall.
        Assert.False(RecallTrivialityGate.ShouldSkipRecall("hola buenas muchas gracias listo perfecto"));
    }

    [Fact]
    public void Null_or_empty_skips_recall()
    {
        Assert.True(RecallTrivialityGate.ShouldSkipRecall(null));
        Assert.True(RecallTrivialityGate.ShouldSkipRecall(""));
    }
}
