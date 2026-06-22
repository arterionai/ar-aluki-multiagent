using System.Text.RegularExpressions;
using Aluki.Runtime.Extraction.Providers;

namespace Aluki.Runtime.Extraction.Policies;

/// <summary>
/// Maps receipt OCR output into normalized <see cref="ExtractedFact"/> values and
/// implements the deterministic text-only fallback parser (US3). Fiscal fields
/// (vendor/total/subtotal/tax/date/RFC) are normalized and validated; values that
/// fail validation are kept at a sub-surfacing confidence so they are persisted
/// for review but never surfaced as fabricated facts.
/// </summary>
public static partial class ReceiptExtractionPolicy
{
    /// <summary>Confidence ceiling for fields recovered via the text-only fallback.</summary>
    public const double FallbackConfidenceCap = 0.80;

    /// <summary>Confidence assigned to present-but-unvalidated values (withheld, kept for review).</summary>
    public const double UnvalidatedConfidence = 0.60;

    private static readonly HashSet<string> AmountFields =
        new(StringComparer.OrdinalIgnoreCase) { "total", "subtotal", "tax", "amount", "tip" };

    [GeneratedRegex(@"[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RfcInText();

    [GeneratedRegex(@"\$?\s?\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyInText();

    [GeneratedRegex(@"\b(\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex DateInText();

    /// <summary>Maps structured OCR candidates into normalized facts.</summary>
    public static IReadOnlyList<ExtractedFact> MapStructured(
        IReadOnlyList<ReceiptFieldCandidate> candidates, string language)
    {
        var facts = new List<ExtractedFact>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var name = candidate.FieldName.Trim().ToLowerInvariant();
            var confidence = Math.Clamp(candidate.Confidence, 0.0, 1.0);

            if (AmountFields.Contains(name))
            {
                if (ReceiptNormalization.TryNormalizeAmount(candidate.Value, out var amount))
                {
                    facts.Add(new ExtractedFact(name, ExtractionFieldType.Amount,
                        new { value = amount, currency = candidate.Currency }, confidence, language));
                }
                // Unparseable amount: drop rather than surface a garbage number.
                continue;
            }

            switch (name)
            {
                case "date":
                    if (ReceiptNormalization.TryNormalizeDate(AsString(candidate.Value), out var iso))
                    {
                        facts.Add(new ExtractedFact("date", ExtractionFieldType.Date, iso, confidence, language));
                    }
                    else if (candidate.Value is not null)
                    {
                        facts.Add(new ExtractedFact("date", ExtractionFieldType.Date, AsString(candidate.Value),
                            Math.Min(confidence, UnvalidatedConfidence), language));
                    }

                    break;

                case "rfc":
                    if (ReceiptNormalization.TryNormalizeRfc(AsString(candidate.Value), out var rfc))
                    {
                        facts.Add(new ExtractedFact("rfc", ExtractionFieldType.Text, rfc, confidence, language));
                    }
                    else if (candidate.Value is not null)
                    {
                        // Present but invalid: keep for review, below the surfacing bar.
                        facts.Add(new ExtractedFact("rfc", ExtractionFieldType.Text, AsString(candidate.Value),
                            Math.Min(confidence, UnvalidatedConfidence), language));
                    }

                    break;

                case "vendor":
                    var vendor = AsString(candidate.Value);
                    if (!string.IsNullOrWhiteSpace(vendor))
                    {
                        facts.Add(new ExtractedFact("vendor", ExtractionFieldType.Text, vendor, confidence, language));
                    }

                    break;

                default:
                    // Unknown but present field: pass through as text for provenance.
                    var value = AsString(candidate.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        facts.Add(new ExtractedFact(name, ExtractionFieldType.Text, value, confidence, language));
                    }

                    break;
            }
        }

        return facts;
    }

    /// <summary>
    /// Deterministic text-only fallback: recovers vendor/total/date/RFC from raw
    /// OCR text. Recovered fields are capped at <see cref="FallbackConfidenceCap"/>
    /// (medium) so they are flagged for review, never accepted as high-confidence.
    /// </summary>
    public static IReadOnlyList<ExtractedFact> ParseRawText(string? rawText, string language)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Array.Empty<ExtractedFact>();
        }

        var facts = new List<ExtractedFact>();

        var rfcMatch = RfcInText().Match(rawText);
        if (rfcMatch.Success && ReceiptNormalization.TryNormalizeRfc(rfcMatch.Value, out var rfc))
        {
            facts.Add(new ExtractedFact("rfc", ExtractionFieldType.Text, rfc, FallbackConfidenceCap, language));
        }

        // Largest parseable money token is treated as the receipt total.
        decimal best = -1m;
        foreach (Match m in MoneyInText().Matches(rawText))
        {
            if (ReceiptNormalization.TryNormalizeAmount(m.Value, out var amount) && amount > best)
            {
                best = amount;
            }
        }

        if (best >= 0m)
        {
            facts.Add(new ExtractedFact("total", ExtractionFieldType.Amount,
                new { value = best, currency = (string?)null }, FallbackConfidenceCap, language));
        }

        var dateMatch = DateInText().Match(rawText);
        if (dateMatch.Success && ReceiptNormalization.TryNormalizeDate(dateMatch.Value, out var iso))
        {
            facts.Add(new ExtractedFact("date", ExtractionFieldType.Date, iso, FallbackConfidenceCap, language));
        }

        // Vendor heuristic: first non-empty line of the receipt header.
        var firstLine = rawText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Length > 1);
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            facts.Add(new ExtractedFact("vendor", ExtractionFieldType.Text, firstLine, FallbackConfidenceCap, language));
        }

        return facts;
    }

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString(),
        _ => value.ToString()
    };
}
