using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Memory.Recall;

/// <summary>
/// Deterministic, accent-insensitive gate that lets reply paths skip semantic recall
/// (embedding + vector search + audit) for messages that can never have memory to
/// recall: pure greetings, thanks, acknowledgments and emoji-only messages.
/// Conservative by design — anything ambiguous runs recall. Pure (no I/O), mirroring
/// PersonNoteDetector so it is unit-testable and cheap.
/// </summary>
public static class RecallTrivialityGate
{
    private const int MaxTrivialTokens = 4;

    // Every token of the message must be in this set for recall to be skipped.
    private static readonly HashSet<string> TrivialTokens = new(StringComparer.Ordinal)
    {
        // Spanish greetings / farewells
        "hola", "buenas", "buenos", "dias", "tardes", "noches", "saludos",
        "adios", "chao", "chau", "hasta", "luego", "pronto", "nos", "vemos",
        // Spanish thanks / acks
        "gracias", "mil", "muchas", "vale", "va", "dale", "listo", "lista",
        "perfecto", "perfecta", "genial", "excelente", "entendido", "entendida",
        "enterado", "recibido", "anotado", "claro", "sale", "orale", "andale",
        "bien", "muy", "super", "bueno", "ok", "okay", "okey", "oki",
        // Laughter / filler
        "jaja", "jajaja", "jajajaja", "jeje", "jejeje", "xd",
        // English greetings / thanks / acks
        "hello", "hi", "hey", "thanks", "thank", "you", "ty", "thx",
        "good", "morning", "afternoon", "evening", "night", "bye", "goodbye",
        "cool", "great", "awesome", "nice", "sure", "yes", "yeah", "yep", "got", "it",
    };

    /// <summary>
    /// True when the message is trivially non-recallable (greeting/thanks/ack/emoji-only)
    /// and the embedding + vector search + recall audit can be skipped entirely.
    /// Any question mark (?, ¿) or unrecognized word forces recall.
    /// </summary>
    public static bool ShouldSkipRecall(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Contains('?') || text.Contains('¿')) return false;

        var tokens = Tokenize(text);

        // Emoji-only / punctuation-only messages have nothing to recall.
        if (tokens.Count == 0) return true;
        if (tokens.Count > MaxTrivialTokens) return false;

        return tokens.All(TrivialTokens.Contains);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue; // strip accents

            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
