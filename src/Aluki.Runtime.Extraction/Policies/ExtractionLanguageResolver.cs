namespace Aluki.Runtime.Extraction.Policies;

/// <summary>A language-tagged segment of transcribed/extracted content.</summary>
public sealed record LanguageSegment(string? Language, double Confidence);

public sealed record LanguageResolution(
    string DetectedLanguage,
    double LanguageConfidence,
    bool IsMixedLanguage,
    bool UsedRegionFallback);

/// <summary>
/// Deterministic language detection + region-fallback policy (clarifications
/// 2026-06-21):
///   - Attempt detection on the per-segment language tags.
///   - If inconclusive, fall back to the configured region preference
///     (es-MX primary, en-US secondary).
///   - Mixed-language (code-switching) inputs are merged with language-pair
///     notation (e.g. "es-MX:en-US"); switches are recorded in metadata.
/// </summary>
public static class ExtractionLanguageResolver
{
    public const string PrimaryRegion = "es-MX";
    public const string SecondaryRegion = "en-US";

    /// <summary>Confidence below which a segment tag is treated as inconclusive.</summary>
    public const double MinSegmentConfidence = 0.50;

    public static LanguageResolution Resolve(
        IReadOnlyList<LanguageSegment> segments,
        string? languageHint = null,
        string primaryRegion = PrimaryRegion,
        string secondaryRegion = SecondaryRegion)
    {
        // Collect distinct, confident language tags in first-seen order.
        var confident = new List<string>();
        var sumConfidence = 0.0;
        var confidentCount = 0;
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Language) || segment.Confidence < MinSegmentConfidence)
            {
                continue;
            }

            var lang = segment.Language!.Trim();
            sumConfidence += segment.Confidence;
            confidentCount++;
            if (!confident.Contains(lang, StringComparer.OrdinalIgnoreCase))
            {
                confident.Add(lang);
            }
        }

        if (confident.Count == 0)
        {
            // Inconclusive: honor an explicit hint, else fall back to region.
            if (!string.IsNullOrWhiteSpace(languageHint))
            {
                return new LanguageResolution(languageHint!.Trim(), 0.0, false, UsedRegionFallback: false);
            }

            return new LanguageResolution(primaryRegion, 0.0, false, UsedRegionFallback: true);
        }

        var avgConfidence = confidentCount > 0 ? sumConfidence / confidentCount : 0.0;

        if (confident.Count == 1)
        {
            return new LanguageResolution(confident[0], avgConfidence, false, UsedRegionFallback: false);
        }

        // Mixed-language: pair notation, primary region first when present.
        var ordered = OrderForPairNotation(confident, primaryRegion, secondaryRegion);
        return new LanguageResolution(string.Join(':', ordered), avgConfidence, true, UsedRegionFallback: false);
    }

    private static IReadOnlyList<string> OrderForPairNotation(
        IReadOnlyList<string> languages,
        string primaryRegion,
        string secondaryRegion)
    {
        // Stable ordering: primary region, then secondary region, then the rest
        // in first-seen order. Keeps pair notation deterministic ("es-MX:en-US").
        var result = new List<string>(languages.Count);
        void AddIfPresent(string region)
        {
            foreach (var lang in languages)
            {
                if (lang.Equals(region, StringComparison.OrdinalIgnoreCase) && !result.Contains(lang, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(lang);
                }
            }
        }

        AddIfPresent(primaryRegion);
        AddIfPresent(secondaryRegion);
        foreach (var lang in languages)
        {
            if (!result.Contains(lang, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(lang);
            }
        }

        return result;
    }
}
