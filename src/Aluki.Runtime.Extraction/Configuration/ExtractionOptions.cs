namespace Aluki.Runtime.Extraction.Configuration;

/// <summary>Bound from the <c>Extraction</c> configuration section.</summary>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>Default per-field confidence threshold for surfacing (0.70).</summary>
    public double DefaultConfidenceThreshold { get; set; } = 0.70;

    /// <summary>Maximum inline text length accepted for a text extraction request.</summary>
    public int MaxTextLength { get; set; } = 100_000;

    /// <summary>Maximum decoded input payload size (bytes) for audio/image.</summary>
    public int MaxInputBytes { get; set; } = 25 * 1024 * 1024;
}
