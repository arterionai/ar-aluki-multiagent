using System.Diagnostics;
using System.Security.Cryptography;
using Aluki.Runtime.Extraction.Configuration;
using Aluki.Runtime.Extraction.Persistence;
using Aluki.Runtime.Extraction.Policies;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Extraction.Security;
using Aluki.Runtime.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Extraction;

/// <summary>HTTP-shaped result for an extraction interaction.</summary>
public sealed record ExtractionHttpResult(int StatusCode, object Body);

/// <summary>
/// Orchestrates an extraction request: validate, scope-guard, idempotent job
/// creation, route by modality (audio US1 / text US2), confidence-tier the
/// fields, and persist result + provenance + audit. Receipt OCR (image, US3) is
/// not yet implemented and returns a controlled <c>not_implemented</c> error.
/// Long-running durable orchestration is a documented follow-up; processing is
/// performed inline and the status endpoint reflects the persisted lifecycle.
/// </summary>
public sealed class ExtractionCoordinator
{
    private readonly ExtractionScopeGuard _scopeGuard;
    private readonly ExtractionStore _store;
    private readonly ITranscriptionProvider _transcription;
    private readonly IStructuredTextExtractionProvider _textExtraction;
    private readonly ExtractionOptions _options;
    private readonly ILogger<ExtractionCoordinator> _logger;

    public ExtractionCoordinator(
        ExtractionScopeGuard scopeGuard,
        ExtractionStore store,
        ITranscriptionProvider transcription,
        IStructuredTextExtractionProvider textExtraction,
        IOptions<ExtractionOptions> options,
        ILogger<ExtractionCoordinator> logger)
    {
        _scopeGuard = scopeGuard;
        _store = store;
        _transcription = transcription;
        _textExtraction = textExtraction;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionHttpResult> ProcessAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId!;

        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(correlationId, validation);
        }

        var principal = ToPrincipalScope(request.PrincipalContext!, request.TenantId);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            _logger.LogWarning("extraction.scope_denied. correlation_id={CorrelationId}", correlationId);
            return ScopeDenied(correlationId);
        }

        var input = request.ExtractionInput!;
        var inputType = input.InputType!.Trim().ToLowerInvariant();
        var inputSource = string.IsNullOrWhiteSpace(input.Source) ? "upload" : input.Source!;
        var (payloadDigest, inputSizeBytes, decodeError) = DescribeInput(input, inputType);
        if (decodeError is not null)
        {
            return BadRequest(correlationId, decodeError);
        }

        var idempotencyKey = ExtractionIdempotencyKey.Derive(request.ExtractionId, inputType, payloadDigest);
        var creation = await _store.CreateOrGetJobAsync(
            principal, inputType, inputSource, inputSizeBytes, idempotencyKey, correlationId, cancellationToken);

        if (!creation.IsNew)
        {
            // Idempotent replay: return the current lifecycle state without reprocessing.
            var existing = await _store.GetJobStatusAsync(principal, creation.JobId, cancellationToken);
            var existingStatus = existing?.JobStatus ?? creation.ExistingStatus;
            return Ok(new ExtractionResponse(
                Status: ResponseStatusFor(existingStatus),
                JobId: creation.JobId,
                JobStatus: existingStatus,
                CorrelationId: correlationId,
                StatusUrl: StatusUrl(creation.JobId)));
        }

        await _store.MarkProcessingAsync(principal, creation.JobId, cancellationToken);

        try
        {
            return inputType switch
            {
                ExtractionInputType.Audio => await ProcessAudioAsync(principal, creation.JobId, input, request.ProcessingOptions, correlationId, cancellationToken),
                ExtractionInputType.Text => await ProcessTextAsync(principal, creation.JobId, input, request.ProcessingOptions, correlationId, cancellationToken),
                ExtractionInputType.Image => await NotImplementedAsync(principal, creation.JobId, correlationId, cancellationToken),
                _ => await FailAsync(principal, creation.JobId, correlationId, ExtractionErrorCode.UnsupportedFormat, "Unsupported input_type.", cancellationToken)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed. job_id={JobId} correlation_id={CorrelationId}", creation.JobId, correlationId);
            return await FailAsync(principal, creation.JobId, correlationId, ExtractionErrorCode.InternalError, "Extraction failed.", cancellationToken);
        }
    }

    /// <summary>GET status endpoint backing (FR-008, SC-005).</summary>
    public async Task<ExtractionHttpResult> GetStatusAsync(
        ExtractionPrincipalContext? principalContext,
        Guid? tenantId,
        Guid jobId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var cid = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId!;
        if (principalContext is null || principalContext.PrincipalId == Guid.Empty)
        {
            return BadRequest(cid, "principal_context with principal_id is required.");
        }

        var principal = ToPrincipalScope(principalContext, tenantId);
        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            return ScopeDenied(cid);
        }

        var status = await _store.GetJobStatusAsync(principal, jobId, cancellationToken);
        return status is null
            ? new ExtractionHttpResult(404, new ExtractionErrorResponse(cid, ExtractionErrorCode.InvalidPayload, "Job not found in scope."))
            : Ok(status);
    }

    private async Task<ExtractionHttpResult> ProcessAudioAsync(
        PrincipalScope principal,
        Guid jobId,
        ExtractionInput input,
        ExtractionProcessingOptions? options,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        byte[] audio;
        try
        {
            audio = Convert.FromBase64String(input.AudioData!);
        }
        catch (FormatException)
        {
            return await FailAsync(principal, jobId, correlationId, ExtractionErrorCode.UnsupportedFormat, "audio_data is not valid base64.", cancellationToken);
        }

        var transcription = await _transcription.TranscribeAsync(
            audio, input.Encoding ?? "wav", options?.LanguageHint, cancellationToken);

        var language = ExtractionLanguageResolver.Resolve(
            transcription.Segments
                .Select(s => new LanguageSegment(s.DetectedLanguage, s.LanguageConfidence))
                .ToList(),
            options?.LanguageHint);

        var fields = new List<ExtractionFieldDto>();

        // The transcription itself is a provenance-bearing field.
        var transcriptionConfidence = AverageSegmentConfidence(transcription);
        fields.Add(BuildField(
            "transcription_text", ExtractionFieldType.Text, transcription.FullTranscription,
            transcriptionConfidence, language.DetectedLanguage, sourceSegmentIndex: null));

        // Structured action items / decisions / entities derived from the transcript.
        if (!string.IsNullOrWhiteSpace(transcription.FullTranscription))
        {
            var structured = await _textExtraction.ExtractAsync(transcription.FullTranscription, language, cancellationToken);
            fields.AddRange(structured.Facts.Select(f => MapFact(f, language.DetectedLanguage)));
        }

        var rawContent = new
        {
            full_transcription = transcription.FullTranscription,
            language = language.DetectedLanguage,
            language_confidence = language.LanguageConfidence,
            is_mixed_language = language.IsMixedLanguage,
            used_region_fallback = language.UsedRegionFallback,
            audio_duration_ms = transcription.AudioDurationMs,
            segments = transcription.Segments.Select(s => new
            {
                segment_index = s.SegmentIndex,
                text = s.Text,
                start_ms = s.StartMs,
                end_ms = s.EndMs,
                detected_language = s.DetectedLanguage
            })
        };

        return await FinalizeAsync(
            principal, jobId, correlationId, ExtractionType.Transcription, transcription.ModelInfo,
            rawContent, fields, language, transcription.Segments.Count, (int)stopwatch.ElapsedMilliseconds,
            options, cancellationToken);
    }

    private async Task<ExtractionHttpResult> ProcessTextAsync(
        PrincipalScope principal,
        Guid jobId,
        ExtractionInput input,
        ExtractionProcessingOptions? options,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var text = input.Text!;

        // Text inputs carry no per-segment language tags; resolution honors the
        // hint or falls back to the configured region.
        var language = ExtractionLanguageResolver.Resolve(
            Array.Empty<LanguageSegment>(), options?.LanguageHint);

        var structured = await _textExtraction.ExtractAsync(text, language, cancellationToken);
        var fields = structured.Facts.Select(f => MapFact(f, language.DetectedLanguage)).ToList();

        var rawContent = new
        {
            summary = structured.Summary,
            detected_language = language.DetectedLanguage,
            language_confidence = language.LanguageConfidence,
            is_mixed_language = language.IsMixedLanguage,
            original_length_chars = text.Length
        };

        return await FinalizeAsync(
            principal, jobId, correlationId, ExtractionType.TextSummary, structured.ModelInfo,
            rawContent, fields, language, segmentCount: 1, (int)stopwatch.ElapsedMilliseconds,
            options, cancellationToken);
    }

    private async Task<ExtractionHttpResult> FinalizeAsync(
        PrincipalScope principal,
        Guid jobId,
        string correlationId,
        string extractionType,
        ModelInfo modelInfo,
        object rawContent,
        IReadOnlyList<ExtractionFieldDto> allFields,
        LanguageResolution language,
        int segmentCount,
        int processingTimeMs,
        ExtractionProcessingOptions? options,
        CancellationToken cancellationToken)
    {
        var threshold = options?.ConfidenceThreshold ?? _options.DefaultConfidenceThreshold;
        var classification = ExtractionConfidencePolicy.Classify(allFields, threshold);
        var overall = ExtractionConfidencePolicy.OverallConfidence(allFields);

        // Persist the full field set (including low-confidence, for later review),
        // but surface only the classified set in the response.
        var persisted = new PersistedResult(extractionType, overall, modelInfo, rawContent, allFields);
        await _store.CompleteJobAsync(
            principal, jobId, persisted, classification.JobStatus,
            language.DetectedLanguage, language.LanguageConfidence,
            processingTimeMs, segmentCount, cancellationToken);

        var results = new ExtractionResults(
            ExtractionType: extractionType,
            OverallConfidence: overall,
            ExtractedFields: classification.SurfacedFields,
            RawContent: (options?.IncludeRaw ?? false) ? rawContent : null,
            ModelInfo: modelInfo);

        return Ok(new ExtractionResponse(
            Status: classification.ResponseStatus,
            JobId: jobId,
            JobStatus: classification.JobStatus,
            CorrelationId: correlationId,
            ExtractionResults: results,
            Warnings: classification.Warnings.Count > 0 ? classification.Warnings : null,
            StatusUrl: StatusUrl(jobId),
            ProcessingMetadata: new ProcessingMetadata(processingTimeMs, segmentCount, 100.0)));
    }

    private async Task<ExtractionHttpResult> NotImplementedAsync(
        PrincipalScope principal, Guid jobId, string correlationId, CancellationToken cancellationToken)
    {
        await _store.FailJobAsync(principal, jobId, ExtractionErrorCode.UnsupportedFormat,
            "Receipt OCR (image) extraction is not yet implemented (SB-004 US3).", cancellationToken);
        return new ExtractionHttpResult(501, new ExtractionResponse(
            Status: ExtractionResponseStatus.Failed,
            JobId: jobId,
            JobStatus: ExtractionJobStatus.Failed,
            CorrelationId: correlationId,
            Error: new ErrorInfo(ExtractionErrorCode.NotImplemented, "Receipt OCR (image) extraction is not yet implemented."),
            StatusUrl: StatusUrl(jobId)));
    }

    private async Task<ExtractionHttpResult> FailAsync(
        PrincipalScope principal, Guid jobId, string correlationId, string code, string message, CancellationToken cancellationToken)
    {
        await _store.FailJobAsync(principal, jobId, code, message, cancellationToken);
        return Ok(new ExtractionResponse(
            Status: ExtractionResponseStatus.Failed,
            JobId: jobId,
            JobStatus: ExtractionJobStatus.Failed,
            CorrelationId: correlationId,
            Error: new ErrorInfo(code, message),
            StatusUrl: StatusUrl(jobId)));
    }

    private static ExtractionFieldDto MapFact(ExtractedFact fact, string defaultLanguage) => BuildField(
        fact.FieldName, fact.FieldType, fact.Value, fact.Confidence,
        fact.DetectedLanguage ?? defaultLanguage, fact.SourceSegmentIndex);

    private static ExtractionFieldDto BuildField(
        string name, string type, object? value, double confidence, string? language, int? sourceSegmentIndex)
    {
        var clamped = Math.Clamp(confidence, 0.0, 1.0);
        var tier = ExtractionConfidencePolicy.TierFor(clamped);
        var justification = tier == ConfidenceTier.High
            ? null
            : $"Confidence {clamped:0.00} below high threshold ({ExtractionConfidencePolicy.HighThreshold:0.00}).";
        return new ExtractionFieldDto(name, type, value, clamped, tier, justification, sourceSegmentIndex, language);
    }

    private static double AverageSegmentConfidence(TranscriptionOutput transcription)
    {
        if (transcription.Segments.Count == 0)
        {
            return 0.0;
        }

        return transcription.Segments.Average(s => Math.Clamp(s.LanguageConfidence, 0.0, 1.0));
    }

    private string? Validate(ExtractionRequest request)
    {
        if (request.PrincipalContext is not { } pc || pc.PrincipalId == Guid.Empty || pc.TenantId == Guid.Empty)
        {
            return "principal_context with principal_id and tenant_id is required.";
        }

        if (request.ExtractionInput is not { } input || string.IsNullOrWhiteSpace(input.InputType))
        {
            return "extraction_input with input_type is required.";
        }

        var inputType = input.InputType.Trim().ToLowerInvariant();
        switch (inputType)
        {
            case ExtractionInputType.Audio when string.IsNullOrWhiteSpace(input.AudioData):
                return "audio_data is required for audio input.";
            case ExtractionInputType.Text when string.IsNullOrWhiteSpace(input.Text):
                return "text is required for text input.";
            case ExtractionInputType.Text when input.Text!.Length > _options.MaxTextLength:
                return $"text exceeds the maximum length of {_options.MaxTextLength} characters.";
            case ExtractionInputType.Image when string.IsNullOrWhiteSpace(input.ImageData):
                return "image_data is required for image input.";
            case ExtractionInputType.Audio:
            case ExtractionInputType.Text:
            case ExtractionInputType.Image:
                return null;
            default:
                return "input_type must be one of audio, text, image.";
        }
    }

    private (string PayloadDigest, int SizeBytes, string? Error) DescribeInput(ExtractionInput input, string inputType)
    {
        switch (inputType)
        {
            case ExtractionInputType.Text:
                var text = input.Text ?? string.Empty;
                return (Sha256Hex(text), text.Length, null);
            case ExtractionInputType.Audio:
            case ExtractionInputType.Image:
                var b64 = inputType == ExtractionInputType.Audio ? input.AudioData! : input.ImageData!;
                int size;
                try
                {
                    size = Convert.FromBase64String(b64).Length;
                }
                catch (FormatException)
                {
                    return (string.Empty, 0, $"{inputType}_data is not valid base64.");
                }

                if (size > _options.MaxInputBytes)
                {
                    return (string.Empty, 0, $"input exceeds the maximum size of {_options.MaxInputBytes} bytes.");
                }

                return (Sha256Hex(b64), size, null);
            default:
                return (string.Empty, 0, "Unsupported input_type.");
        }
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static PrincipalScope ToPrincipalScope(ExtractionPrincipalContext pc, Guid? topLevelTenantId)
    {
        var tenantId = pc.TenantId != Guid.Empty ? pc.TenantId : (topLevelTenantId ?? Guid.Empty);
        return new PrincipalScope(tenantId, pc.ContextId ?? Guid.Empty, pc.PrincipalId, Roles: null);
    }

    private static string ResponseStatusFor(string jobStatus) => jobStatus switch
    {
        ExtractionJobStatus.CompletedSuccess => ExtractionResponseStatus.Success,
        ExtractionJobStatus.CompletedWithWarnings => ExtractionResponseStatus.PartialSuccess,
        ExtractionJobStatus.Failed => ExtractionResponseStatus.Failed,
        _ => ExtractionResponseStatus.Pending
    };

    private static string StatusUrl(Guid jobId) => $"/api/extraction/jobs/{jobId}";

    private static ExtractionHttpResult Ok(object body) => new(200, body);

    private static ExtractionHttpResult BadRequest(string correlationId, string message) =>
        new(400, new ExtractionErrorResponse(correlationId, ExtractionErrorCode.InvalidPayload, message));

    private static ExtractionHttpResult ScopeDenied(string correlationId) =>
        new(403, new ExtractionErrorResponse(correlationId, ExtractionErrorCode.ScopeDenied, "Principal is not authorized for the requested scope."));
}
