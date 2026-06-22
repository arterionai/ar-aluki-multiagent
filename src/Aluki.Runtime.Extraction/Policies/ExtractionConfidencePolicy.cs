namespace Aluki.Runtime.Extraction.Policies;

/// <summary>
/// Deterministic per-field confidence tiering and surfacing policy
/// (clarification 2026-06-21):
///   High   >= 0.85  accepted as-is.
///   Medium 0.70-0.84 flagged for review.
///   Low    < 0.70   marked uncertain, NOT surfaced without user review.
/// No fabrication: low-confidence fields are suppressed from the surfaced
/// result set but still persisted with provenance for later review.
/// </summary>
public static class ExtractionConfidencePolicy
{
    public const double HighThreshold = 0.85;
    public const double MediumThreshold = 0.70;

    /// <summary>Maps a [0,1] score to its tier. Scores are clamped to range.</summary>
    public static string TierFor(double score)
    {
        var clamped = Math.Clamp(score, 0.0, 1.0);
        if (clamped >= HighThreshold)
        {
            return ConfidenceTier.High;
        }

        return clamped >= MediumThreshold ? ConfidenceTier.Medium : ConfidenceTier.Low;
    }

    /// <summary>
    /// A field is surfaced (returned to the caller without explicit review) when
    /// its score is at or above the medium threshold (0.70). Low-confidence fields
    /// are withheld unless the caller explicitly lowered the threshold to opt in.
    /// </summary>
    public static bool IsSurfaced(double score, double callerThreshold = MediumThreshold)
    {
        var s = Math.Clamp(score, 0.0, 1.0);
        return s >= MediumThreshold || s >= Math.Clamp(callerThreshold, 0.0, 1.0);
    }

    /// <summary>
    /// Classifies a set of fields into surfaced fields, the derived job status,
    /// and the warnings to emit. Medium/low presence downgrades a success job to
    /// <c>completed_with_warnings</c>.
    /// </summary>
    public static ConfidenceClassification Classify(
        IReadOnlyList<ExtractionFieldDto> fields,
        double callerThreshold = MediumThreshold)
    {
        var surfaced = new List<ExtractionFieldDto>(fields.Count);
        var mediumFields = new List<string>();
        var lowFields = new List<string>();

        foreach (var field in fields)
        {
            switch (field.ConfidenceTier)
            {
                case ConfidenceTier.High:
                    surfaced.Add(field);
                    break;
                case ConfidenceTier.Medium:
                    mediumFields.Add(field.FieldName);
                    surfaced.Add(field);
                    break;
                default: // low
                    lowFields.Add(field.FieldName);
                    // Withheld from surfaced set unless the caller lowered the bar.
                    if (IsSurfaced(field.ConfidenceScore, callerThreshold))
                    {
                        surfaced.Add(field);
                    }

                    break;
            }
        }

        var warnings = new List<WarningItem>();
        if (mediumFields.Count > 0)
        {
            warnings.Add(new WarningItem(
                ExtractionWarningCode.ConfidenceMedium,
                "One or more fields have medium confidence and are flagged for review.",
                mediumFields));
        }

        if (lowFields.Count > 0)
        {
            warnings.Add(new WarningItem(
                ExtractionWarningCode.ConfidenceLow,
                "One or more fields are low confidence and were not surfaced without review.",
                lowFields));
        }

        var hasWarnings = mediumFields.Count > 0 || lowFields.Count > 0;
        var jobStatus = hasWarnings
            ? ExtractionJobStatus.CompletedWithWarnings
            : ExtractionJobStatus.CompletedSuccess;
        var responseStatus = hasWarnings
            ? ExtractionResponseStatus.PartialSuccess
            : ExtractionResponseStatus.Success;

        return new ConfidenceClassification(surfaced, warnings, jobStatus, responseStatus);
    }

    /// <summary>Mean of field scores, or 0 when there are none.</summary>
    public static double OverallConfidence(IReadOnlyList<ExtractionFieldDto> fields)
    {
        if (fields.Count == 0)
        {
            return 0.0;
        }

        var sum = 0.0;
        foreach (var field in fields)
        {
            sum += Math.Clamp(field.ConfidenceScore, 0.0, 1.0);
        }

        return sum / fields.Count;
    }
}

public sealed record ConfidenceClassification(
    IReadOnlyList<ExtractionFieldDto> SurfacedFields,
    IReadOnlyList<WarningItem> Warnings,
    string JobStatus,
    string ResponseStatus);
