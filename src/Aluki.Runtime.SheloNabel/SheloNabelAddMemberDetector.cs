using System.Text.RegularExpressions;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Detects when the OWNER wants to add a new phone number as a member of the
/// Sheló NABEL org tenant.
/// "agrega al +52 55 1234 5678", "añade a este número al equipo", etc.
/// </summary>
internal static partial class SheloNabelAddMemberDetector
{
    [GeneratedRegex(
        @"agrega\s+(a\s+)?(\+?\d)|"
        + @"a[nñ]ade\s+(a\s+)?(\+?\d)|"
        + @"a[gñ]rega\s+este\s+n[uú]mero|"
        + @"(mete|s[uú]mame?)\s+(a\s+)?(\+?\d)|"
        + @"(dale\s+acceso|da\s+acceso)\s+(a\s+)?(\+?\d)|"
        + @"(invita|invite)\s+(a\s+)?(\+?\d)|"
        + @"(add|join)\s+(\+?\d).*team|"
        + @"nuevo\s+miembro|nueva\s+vendedora\s+(\+?\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool LooksLikeAddMember(string text) =>
        Pattern().IsMatch(RemoveDiacritics(text));

    /// <summary>
    /// Extracts the first phone-like digit sequence from the message and normalizes it.
    /// Returns digits only (no '+' or spaces), with country code prepended if needed.
    /// </summary>
    public static string? ExtractPhone(string text)
    {
        var m = PhonePattern().Match(text);
        if (!m.Success) return null;
        var digits = m.Value.Replace(" ", "").Replace("-", "").TrimStart('+');
        return NormalizePhone(digits);
    }

    /// <summary>
    /// Normalizes a digit-only phone string. Prepends Mexico country code (52) for
    /// 10-digit numbers that lack a country code prefix.
    /// </summary>
    public static string NormalizePhone(string digits)
    {
        // 10-digit number = Mexican local format (e.g. 5532229412) → 525532229412
        if (digits.Length == 10)
            return "52" + digits;
        return digits;
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
