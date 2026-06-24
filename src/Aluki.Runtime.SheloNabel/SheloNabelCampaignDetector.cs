using System.Text.RegularExpressions;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Detects when the owner wants to run a reorder campaign —
/// "avisa a todos los clientes con reorden vencido", "campaña de reorden", etc.
/// </summary>
internal static partial class SheloNabelCampaignDetector
{
    [GeneratedRegex(
        @"campa[nñ]a\s+(de\s+)?(re[oó]rden|seguimiento)|"
        + @"avisa\s+(a\s+)?todos\s+(los\s+)?clientes?|"
        + @"manda\s+(mensaje|whatsapp)\s+(a\s+)?todos|"
        + @"re[oó]rdenes?\s+vencidas?|clientes?\s+(con\s+)?re[oó]rden\s+(vencida?|pendiente)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool LooksLikeCampaign(string text) =>
        Pattern().IsMatch(RemoveDiacritics(text));

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
