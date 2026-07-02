using System.ClientModel.Primitives;
using Aluki.Runtime.Memory.Chat;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Audio;

namespace Aluki.Runtime.Extraction.Providers;

/// <summary>
/// Audio transcription via an Azure OpenAI Whisper deployment (all inference
/// stays on Azure per the runtime directive). Requests verbose JSON so segment
/// timings are available; a per-segment confidence is derived from the model's
/// average log-probability. Configuration:
///   <c>Extraction:Transcription:Endpoint</c> / <c>:ApiKey</c> (fall back to
///   <c>AiExtraction:*</c>) and <c>Extraction:Transcription:Deployment</c>
///   (default <c>whisper</c>).
/// </summary>
public sealed class AzureOpenAiTranscriptionProvider : ITranscriptionProvider
{
    private static readonly ModelInfo WhisperModelInfo = new("Azure.OpenAI", "whisper", "v1");

    private readonly string _deployment;
    private readonly AzureOpenAIClient _client;

    public AzureOpenAiTranscriptionProvider(IConfiguration configuration)
    {
        var endpoint = configuration["Extraction:Transcription:Endpoint"]
            ?? configuration["AiExtraction:Endpoint"]
            ?? throw new InvalidOperationException("Extraction:Transcription:Endpoint (or AiExtraction:Endpoint) is not configured.");
        var apiKey = configuration["Extraction:Transcription:ApiKey"]
            ?? configuration["AiExtraction:ApiKey"]
            ?? throw new InvalidOperationException("Extraction:Transcription:ApiKey (or AiExtraction:ApiKey) is not configured.");
        _deployment = configuration["Extraction:Transcription:Deployment"] ?? "whisper";
        var options = new AzureOpenAIClientOptions
        {
            // Shared bounded-lifetime handler: avoids the stale-idle-connection stall
            // on the first transcription after an idle period (audio hot path).
            Transport = new HttpClientPipelineTransport(AzureAiSharedHttp.Client)
        };
        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), options);
    }

    public async Task<TranscriptionOutput> TranscribeAsync(
        byte[] audio,
        string encoding,
        string? languageHint,
        CancellationToken cancellationToken)
    {
        var audioClient = _client.GetAudioClient(_deployment);
        var options = new AudioTranscriptionOptions
        {
            ResponseFormat = AudioTranscriptionFormat.Verbose
        };
        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            // Whisper expects an ISO-639-1 code; pass the language part of a region tag.
            options.Language = languageHint!.Split('-')[0];
        }

        using var stream = new MemoryStream(audio, writable: false);
        var fileName = $"audio.{NormalizeEncoding(encoding)}";
        var result = await audioClient.TranscribeAudioAsync(stream, fileName, options, cancellationToken);
        var transcription = result.Value;

        var segments = new List<TranscriptionSegment>();
        if (transcription.Segments is { Count: > 0 })
        {
            var index = 0;
            foreach (var segment in transcription.Segments)
            {
                segments.Add(new TranscriptionSegment(
                    SegmentIndex: index++,
                    Text: segment.Text ?? string.Empty,
                    StartMs: (int)segment.StartTime.TotalMilliseconds,
                    EndMs: (int)segment.EndTime.TotalMilliseconds,
                    DetectedLanguage: transcription.Language,
                    LanguageConfidence: ConfidenceFromLogProb(segment.AverageLogProbability)));
            }
        }
        else
        {
            // No verbose segments: treat the whole transcription as one segment.
            var durationMs = (int)(transcription.Duration?.TotalMilliseconds ?? 0);
            segments.Add(new TranscriptionSegment(
                SegmentIndex: 0,
                Text: transcription.Text ?? string.Empty,
                StartMs: 0,
                EndMs: durationMs,
                DetectedLanguage: transcription.Language,
                LanguageConfidence: 0.8));
        }

        var totalDurationMs = (int)(transcription.Duration?.TotalMilliseconds
            ?? (segments.Count > 0 ? segments[^1].EndMs : 0));

        return new TranscriptionOutput(
            FullTranscription: transcription.Text ?? string.Empty,
            Segments: segments,
            AudioDurationMs: totalDurationMs,
            ModelInfo: WhisperModelInfo);
    }

    /// <summary>
    /// Maps Whisper's average token log-probability to a [0,1] confidence proxy
    /// (exp of the log-prob), clamped. Higher log-prob ⇒ higher confidence.
    /// </summary>
    private static double ConfidenceFromLogProb(double averageLogProbability)
    {
        var confidence = Math.Exp(averageLogProbability);
        return Math.Clamp(confidence, 0.0, 1.0);
    }

    private static string NormalizeEncoding(string encoding)
    {
        var e = (encoding ?? string.Empty).Trim().ToLowerInvariant();
        return e switch
        {
            "wav" or "mp3" or "flac" or "ogg" => e,
            _ => "wav"
        };
    }
}
