using Aluki.Runtime.Extraction.Policies;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class ExtractionLanguageResolverTests
{
    [Fact]
    public void Single_confident_language_is_detected()
    {
        var result = ExtractionLanguageResolver.Resolve(
            [new LanguageSegment("es-MX", 0.9), new LanguageSegment("es-MX", 0.8)]);

        Assert.Equal("es-MX", result.DetectedLanguage);
        Assert.False(result.IsMixedLanguage);
        Assert.False(result.UsedRegionFallback);
    }

    [Fact]
    public void Mixed_language_uses_pair_notation_primary_first()
    {
        var result = ExtractionLanguageResolver.Resolve(
            [new LanguageSegment("en-US", 0.9), new LanguageSegment("es-MX", 0.9)]);

        Assert.True(result.IsMixedLanguage);
        Assert.Equal("es-MX:en-US", result.DetectedLanguage);
    }

    [Fact]
    public void Inconclusive_segments_fall_back_to_primary_region()
    {
        var result = ExtractionLanguageResolver.Resolve(
            [new LanguageSegment(null, 0.0), new LanguageSegment("es-MX", 0.2)]);

        Assert.Equal(ExtractionLanguageResolver.PrimaryRegion, result.DetectedLanguage);
        Assert.True(result.UsedRegionFallback);
    }

    [Fact]
    public void Inconclusive_segments_honor_explicit_hint_over_region()
    {
        var result = ExtractionLanguageResolver.Resolve(
            [new LanguageSegment(null, 0.0)], languageHint: "en-US");

        Assert.Equal("en-US", result.DetectedLanguage);
        Assert.False(result.UsedRegionFallback);
    }

    [Fact]
    public void Empty_segments_with_no_hint_fall_back_to_region()
    {
        var result = ExtractionLanguageResolver.Resolve(Array.Empty<LanguageSegment>());

        Assert.Equal(ExtractionLanguageResolver.PrimaryRegion, result.DetectedLanguage);
        Assert.True(result.UsedRegionFallback);
    }

    [Fact]
    public void Low_confidence_segment_tags_are_ignored()
    {
        // A confident es-MX plus a noisy below-threshold en-US ⇒ not mixed.
        var result = ExtractionLanguageResolver.Resolve(
            [new LanguageSegment("es-MX", 0.95), new LanguageSegment("en-US", 0.1)]);

        Assert.False(result.IsMixedLanguage);
        Assert.Equal("es-MX", result.DetectedLanguage);
    }
}
