using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Shared accent/case normalization for the deterministic intent detectors.
/// </summary>
internal static class AccentInsensitiveText
{
    /// <summary>
    /// Lowercases and strips combining accents while tracking, for every normalized
    /// character, the index of the original character it came from — so trigger
    /// matches on the normalized text can be mapped back to the original string
    /// (preserving accents/casing in extracted names or topics).
    /// </summary>
    public static (string Normalized, IReadOnlyList<int> Map) NormalizeWithMap(string text)
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
