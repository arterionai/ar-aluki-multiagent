using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aluki.Runtime.Extraction.Policies;

/// <summary>
/// Deterministic, AI-independent normalization + validation for receipt fiscal
/// fields (US3). Keeps the "never invent" guarantee: values that do not parse or
/// fail validation are reported as unnormalized so the orchestrator can withhold
/// or down-tier them rather than surfacing fabricated structure.
/// </summary>
public static partial class ReceiptNormalization
{
    // Mexican RFC: persona moral = 3 letters, persona física = 4 letters,
    // followed by a 6-digit date (YYMMDD) and a 3-char homoclave.
    [GeneratedRegex(@"^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex RfcPattern();

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd/MM/yy", "d/M/yy", "dd.MM.yyyy", "d.M.yyyy",
        "MM/dd/yyyy", "M/d/yyyy"
    };

    /// <summary>
    /// Validates and canonicalizes a Mexican RFC (uppercased, separators stripped).
    /// Returns false for absent or structurally invalid values.
    /// </summary>
    public static bool TryNormalizeRfc(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!RfcPattern().IsMatch(cleaned))
        {
            return false;
        }

        normalized = cleaned;
        return true;
    }

    /// <summary>
    /// Parses a monetary value from a number or a currency-formatted string
    /// (handles <c>$</c>, thousands separators, and either decimal convention).
    /// Negative or non-numeric inputs return false.
    /// </summary>
    public static bool TryNormalizeAmount(object? raw, out decimal value)
    {
        value = 0m;
        switch (raw)
        {
            case null:
                return false;
            case decimal d:
                return Accept(d, out value);
            case double db:
                return Accept((decimal)db, out value);
            case float f:
                return Accept((decimal)f, out value);
            case int i:
                return Accept(i, out value);
            case long l:
                return Accept(l, out value);
            case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDecimal(out var jd):
                return Accept(jd, out value);
            case JsonElement { ValueKind: JsonValueKind.String } js:
                return TryParseMoneyString(js.GetString(), out value);
            case string s:
                return TryParseMoneyString(s, out value);
            default:
                return TryParseMoneyString(raw.ToString(), out value);
        }
    }

    /// <summary>
    /// Parses a receipt date into an ISO <c>yyyy-MM-dd</c> string, trying the
    /// common Mexican (dd/MM/yyyy) and ISO/US conventions. Returns false when no
    /// format matches.
    /// </summary>
    public static bool TryNormalizeDate(string? raw, out string isoDate)
    {
        isoDate = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            isoDate = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        // Last resort: invariant/es-MX free parse (covers e.g. "15 mar 2026").
        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("es-MX"), new CultureInfo("en-US") })
        {
            if (DateOnly.TryParse(trimmed, culture, DateTimeStyles.None, out var loose))
            {
                isoDate = loose.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    private static bool Accept(decimal candidate, out decimal value)
    {
        value = decimal.Round(candidate, 2, MidpointRounding.AwayFromZero);
        return candidate >= 0m;
    }

    private static bool TryParseMoneyString(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // Keep digits and separators only (drops currency symbols, codes, spaces).
        var stripped = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (stripped.Length == 0)
        {
            return false;
        }

        var hasDot = stripped.Contains('.');
        var hasComma = stripped.Contains(',');
        string normalized;
        if (hasDot && hasComma)
        {
            // The right-most separator is the decimal point; the other groups thousands.
            normalized = stripped.LastIndexOf('.') > stripped.LastIndexOf(',')
                ? stripped.Replace(",", string.Empty)
                : stripped.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (hasComma)
        {
            // A lone comma with exactly two trailing digits is a decimal comma;
            // otherwise it is a thousands separator.
            var idx = stripped.LastIndexOf(',');
            normalized = stripped.Length - idx - 1 == 2
                ? stripped.Replace(',', '.')
                : stripped.Replace(",", string.Empty);
        }
        else
        {
            normalized = stripped;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return Accept(parsed, out value);
        }

        return false;
    }
}
