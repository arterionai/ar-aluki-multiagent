namespace Aluki.Runtime.Memory.Skills;

/// <summary>
/// Deterministic intent split between storing a note and asking a recall query.
/// A trailing '?' or a leading interrogative marks a recall query; otherwise the
/// interaction is treated as a note to store.
/// </summary>
public sealed class MemoryIntentClassifierSkill
{
    private static readonly string[] QuestionStarters =
    [
        "que", "qué", "cual", "cuál", "cuales", "cuáles", "como", "cómo",
        "cuando", "cuándo", "donde", "dónde", "quien", "quién", "quienes", "quiénes",
        "por que", "por qué", "recuerda", "recuerdas", "sabes",
        "what", "how", "when", "where", "who", "which", "why",
        "do i", "did i", "have i", "is there", "are there"
    ];

    public string Classify(string? inputText)
    {
        var text = (inputText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return MemoryIntent.NoteToStore;
        }

        if (text.EndsWith('?') || text.EndsWith('?'))
        {
            return MemoryIntent.RecallQuery;
        }

        var lower = text.ToLowerInvariant();
        foreach (var starter in QuestionStarters)
        {
            if (lower == starter || lower.StartsWith(starter + " ", StringComparison.Ordinal))
            {
                return MemoryIntent.RecallQuery;
            }
        }

        return MemoryIntent.NoteToStore;
    }
}
