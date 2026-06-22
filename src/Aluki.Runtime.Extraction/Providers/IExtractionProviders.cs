using Aluki.Runtime.Extraction.Policies;

namespace Aluki.Runtime.Extraction.Providers;

/// <summary>A transcribed audio segment with language and timing metadata.</summary>
public sealed record TranscriptionSegment(
    int SegmentIndex,
    string Text,
    int StartMs,
    int EndMs,
    string? DetectedLanguage,
    double LanguageConfidence);

/// <summary>Raw transcription output from an audio provider.</summary>
public sealed record TranscriptionOutput(
    string FullTranscription,
    IReadOnlyList<TranscriptionSegment> Segments,
    int AudioDurationMs,
    ModelInfo ModelInfo);

/// <summary>
/// Audio transcription provider (es-MX/en-US). Stays on Azure inference per the
/// runtime directive. Implementations may chunk long audio into segments.
/// </summary>
public interface ITranscriptionProvider
{
    Task<TranscriptionOutput> TranscribeAsync(
        byte[] audio,
        string encoding,
        string? languageHint,
        CancellationToken cancellationToken);
}

/// <summary>A structured fact extracted from text/transcription with a confidence.</summary>
public sealed record ExtractedFact(
    string FieldName,
    string FieldType,
    object? Value,
    double Confidence,
    string? DetectedLanguage = null,
    int? SourceSegmentIndex = null);

/// <summary>Structured extraction output for text/transcription content.</summary>
public sealed record StructuredExtractionOutput(
    string Summary,
    IReadOnlyList<ExtractedFact> Facts,
    ModelInfo ModelInfo);

/// <summary>
/// Extracts a summary plus structured action items / decisions / entities from
/// free text (or a transcription). Backed by Azure AI Foundry model-router.
/// </summary>
public interface IStructuredTextExtractionProvider
{
    Task<StructuredExtractionOutput> ExtractAsync(
        string text,
        LanguageResolution language,
        CancellationToken cancellationToken);
}
