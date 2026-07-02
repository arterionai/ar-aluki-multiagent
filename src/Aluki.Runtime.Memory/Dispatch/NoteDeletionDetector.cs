namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Deterministic, accent-insensitive detector for note-deletion intent
/// ("borra lo de Fer", "olvida lo de la galería", "forget about Bob"). Extracts
/// the topic from the original text (accents and casing preserved). Pure — no
/// I/O, no LLM.
/// </summary>
public static class NoteDeletionDetector
{
    private static readonly string[] Triggers =
    [
        "borra la nota de ",
        "borra lo de ",
        "elimina la nota de ",
        "elimina lo de ",
        "olvida lo de ",
        "olvida a ",
        "delete the note about ",
        "forget about ",
    ];

    private static readonly char[] TopicTrimChars =
        [' ', '?', '¿', '!', '¡', '.', ',', ';', ':', '"', '\'', '*'];

    /// <summary>
    /// True when the text expresses note-deletion intent; <paramref name="topic"/>
    /// is what to forget, taken from the original text (accents/case preserved).
    /// Save-intent text (SB-013 triggers) never claims.
    /// </summary>
    public static bool TryExtractDeletion(string? text, out string topic)
    {
        topic = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Defense in depth: a save trigger wins ("guarda que olvida lo de Fer" is a note).
        if (PersonNoteDetector.LooksLikePersonNote(text))
            return false;

        var (normalized, map) = AccentInsensitiveText.NormalizeWithMap(text);

        foreach (var trigger in Triggers)
        {
            var index = normalized.IndexOf(trigger, StringComparison.Ordinal);
            if (index < 0)
                continue;

            var topicStartNormalized = index + trigger.Length;
            if (topicStartNormalized >= normalized.Length)
                return false;

            var originalStart = map[topicStartNormalized];
            var candidate = text[originalStart..].Trim(TopicTrimChars);
            if (candidate.Length == 0)
                return false;

            topic = candidate;
            return true;
        }

        return false;
    }
}
