namespace Aluki.Runtime.Extraction.Providers;

/// <summary>No-op transcription provider used where Azure Whisper is not wired (e.g. the dev host).</summary>
public sealed class NullTranscriptionProvider : ITranscriptionProvider
{
    public Task<TranscriptionOutput> TranscribeAsync(
        byte[] audio,
        string encoding,
        string? languageHint,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Audio transcription is not configured in this environment.");
}
