using System.Text.RegularExpressions;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Detects when the owner is asking for a sales / reorder summary report.
/// "¿cómo vamos?", "dame un resumen", "mis ventas", "reporte de ventas", etc.
/// </summary>
internal static partial class SheloNabelReportDetector
{
    [GeneratedRegex(
        @"c[oó]mo\s+vamos|resumen\s+(de\s+)?(ventas?|pedidos?|re[oó]rdenes?)|"
        + @"mis\s+ventas?|reporte\s+(de\s+)?(ventas?|clientes?)|"
        + @"cu[aá]nto\s+(vendí|hemos?\s+vendido)|"
        + @"estado\s+(del?\s+)?(negocio|mes)|"
        + @"how\s+(are\s+we\s+doing|did\s+we\s+do)|sales\s+report",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool LooksLikeReport(string text) =>
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
