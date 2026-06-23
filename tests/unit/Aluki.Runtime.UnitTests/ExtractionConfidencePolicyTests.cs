using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Policies;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class ExtractionConfidencePolicyTests
{
    private static ExtractionFieldDto Field(string name, double score) =>
        new(name, ExtractionFieldType.Text, name, score, ExtractionConfidencePolicy.TierFor(score));

    [Theory]
    [InlineData(0.85, ConfidenceTier.High)]
    [InlineData(0.95, ConfidenceTier.High)]
    [InlineData(0.84, ConfidenceTier.Medium)]
    [InlineData(0.70, ConfidenceTier.Medium)]
    [InlineData(0.699, ConfidenceTier.Low)]
    [InlineData(0.0, ConfidenceTier.Low)]
    public void TierFor_maps_score_to_tier_at_documented_boundaries(double score, string expected)
    {
        Assert.Equal(expected, ExtractionConfidencePolicy.TierFor(score));
    }

    [Fact]
    public void TierFor_clamps_out_of_range_scores()
    {
        Assert.Equal(ConfidenceTier.High, ExtractionConfidencePolicy.TierFor(1.7));
        Assert.Equal(ConfidenceTier.Low, ExtractionConfidencePolicy.TierFor(-0.5));
    }

    [Fact]
    public void Classify_high_only_is_success_with_no_warnings()
    {
        var result = ExtractionConfidencePolicy.Classify([Field("a", 0.9), Field("b", 0.88)]);

        Assert.Equal(ExtractionJobStatus.CompletedSuccess, result.JobStatus);
        Assert.Equal(ExtractionResponseStatus.Success, result.ResponseStatus);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.SurfacedFields.Count);
    }

    [Fact]
    public void Classify_medium_is_partial_success_and_still_surfaced()
    {
        var result = ExtractionConfidencePolicy.Classify([Field("a", 0.9), Field("b", 0.75)]);

        Assert.Equal(ExtractionJobStatus.CompletedWithWarnings, result.JobStatus);
        Assert.Equal(ExtractionResponseStatus.PartialSuccess, result.ResponseStatus);
        Assert.Contains(result.Warnings, w => w.Code == ExtractionWarningCode.ConfidenceMedium);
        Assert.Equal(2, result.SurfacedFields.Count); // medium is still surfaced (flagged)
    }

    [Fact]
    public void Classify_low_is_withheld_and_warned_no_fabrication()
    {
        var result = ExtractionConfidencePolicy.Classify([Field("a", 0.9), Field("low", 0.4)]);

        Assert.Equal(ExtractionJobStatus.CompletedWithWarnings, result.JobStatus);
        Assert.Contains(result.Warnings, w => w.Code == ExtractionWarningCode.ConfidenceLow);
        // Low-confidence field is not surfaced without explicit review.
        Assert.Single(result.SurfacedFields);
        Assert.Equal("a", result.SurfacedFields[0].FieldName);
    }

    [Fact]
    public void Classify_low_surfaced_when_caller_lowers_threshold()
    {
        var result = ExtractionConfidencePolicy.Classify([Field("low", 0.4)], callerThreshold: 0.3);

        Assert.Single(result.SurfacedFields);
        Assert.Equal("low", result.SurfacedFields[0].FieldName);
    }

    [Fact]
    public void OverallConfidence_is_mean_or_zero()
    {
        Assert.Equal(0.0, ExtractionConfidencePolicy.OverallConfidence([]));
        Assert.Equal(0.8, ExtractionConfidencePolicy.OverallConfidence([Field("a", 0.9), Field("b", 0.7)]), 3);
    }
}
