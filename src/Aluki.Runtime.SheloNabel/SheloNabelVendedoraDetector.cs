using System.Text.RegularExpressions;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Detects when the owner wants to assign a client to a vendedora or add a
/// team member. Returns the raw assignment intent; the agent parses names/numbers.
/// </summary>
internal static partial class SheloNabelVendedoraDetector
{
    [GeneratedRegex(
        @"asigna\s+(a\s+)?|"
        + @"agrega\s+(a\s+)?.*\s+(como\s+)?(vendedora|mi\s+equipo)|"
        + @"a[nñ]ade\s+(a\s+)?.*\s+(como\s+)?vendedora|"
        + @"(este\s+cliente|esta\s+clienta?)\s+(es\s+de|para)\s+|"
        + @"cliente\s+(de|para)\s+\w+|"
        + @"add.*as\s+(a\s+)?sales\s+(rep|person)|assign.*to\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool LooksLikeAssignment(string text) =>
        Pattern().IsMatch(RemoveDiacritics(text));

    /// <summary>
    /// Attempts to extract a phone number from an assignment message.
    /// Returns the first E.164-ish digit sequence (7+ digits) found.
    /// </summary>
    public static string? ExtractPhone(string text)
    {
        var m = PhonePattern().Match(text);
        return m.Success ? m.Value.Replace(" ", "").Replace("-", "") : null;
    }

    [GeneratedRegex(@"\+?[\d][\d\s\-]{6,15}")]
    private static partial Regex PhonePattern();

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
