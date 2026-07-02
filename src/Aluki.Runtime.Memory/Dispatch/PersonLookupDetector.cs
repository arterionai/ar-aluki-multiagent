using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Deterministic, accent-insensitive detector for person-lookup intent
/// ("¿Quién es Fer?", "qué sabes de Ana", "who is Bob"). Extracts the person
/// name from the original text (accents and casing preserved) so the reply can
/// display it verbatim. Pure — no I/O, no LLM.
/// </summary>
public static class PersonLookupDetector
{
    private static readonly string[] Triggers =
    [
        "quienes son ",
        "quien es ",
        "que sabes sobre ",
        "que sabes de ",
        "what do you know about ",
        "who is ",
    ];

    private static readonly char[] NameTrimChars =
        [' ', '?', '¿', '!', '¡', '.', ',', ';', ':', '"', '\'', '*'];

    /// <summary>
    /// True when the text expresses person-lookup intent; <paramref name="personName"/>
    /// is the queried name taken from the original text (accents/case preserved).
    /// Save-intent text (SB-013 triggers) never claims, regardless of phrasing.
    /// </summary>
    public static bool TryExtractLookup(string? text, out string personName)
    {
        personName = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Defense in depth: a save trigger wins even if a lookup phrase also appears
        // ("guarda que no sé quién es Fer" is a note, not a query).
        if (PersonNoteDetector.LooksLikePersonNote(text))
            return false;

        var (normalized, map) = NormalizeWithMap(text);

        foreach (var trigger in Triggers)
        {
            var index = normalized.IndexOf(trigger, StringComparison.Ordinal);
            if (index < 0)
                continue;

            var nameStartNormalized = index + trigger.Length;
            if (nameStartNormalized >= normalized.Length)
                return false;

            var originalStart = map[nameStartNormalized];
            var candidate = text[originalStart..].Trim(NameTrimChars);
            if (candidate.Length == 0)
                return false;

            personName = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lowercases and strips combining accents while tracking, for every normalized
    /// character, the index of the original character it came from — so trigger
    /// matches on the normalized text can be mapped back to the original string.
    /// </summary>
    private static (string Normalized, IReadOnlyList<int> Map) NormalizeWithMap(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var decomposed = text[i].ToString().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                    map.Add(i);
                }
            }
        }

        return (sb.ToString(), map);
    }
}
