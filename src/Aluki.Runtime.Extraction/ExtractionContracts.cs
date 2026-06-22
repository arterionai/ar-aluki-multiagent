using System.Text.Json.Serialization;

namespace Aluki.Runtime.Extraction;

/// <summary>Supported extraction input modalities.</summary>
public static class ExtractionInputType
{
    public const string Audio = "audio";
    public const string Text = "text";
    public const string Image = "image";
}

/// <summary>Extraction result classification (one per job).</summary>
public static class ExtractionType
{
    public const string Transcription = "transcription";
    public const string TextSummary = "text_summary";
    public const string ReceiptOcr = "receipt_ocr";
}

/// <summary>Async job lifecycle states (data-model §Job lifecycle).</summary>
public static class ExtractionJobStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string CompletedSuccess = "completed_success";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string Failed = "failed";
}

/// <summary>Top-level response status (contract enum).</summary>
public static class ExtractionResponseStatus
{
    public const string Success = "success";
    public const string PartialSuccess = "partial_success";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

/// <summary>Per-field confidence tiers (clarification 2026-06-21).</summary>
public static class ConfidenceTier
{
    public const string High = "high";   // >= 0.85, accepted as-is
    public const string Medium = "medium"; // 0.70-0.84, flagged for review
    public const string Low = "low";     // < 0.70, uncertain, not surfaced without review
}

public static class ExtractionFieldType
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Entity = "entity";
    public const string DecisionItem = "decision_item";
    public const string Amount = "amount";
}

public static class ExtractionWarningCode
{
    public const string ConfidenceMedium = "confidence_medium";
    public const string ConfidenceLow = "confidence_low";
    public const string PartialResult = "partial_result";
    public const string OcrFallbackUsed = "ocr_fallback_used";
    public const string UnreadableFragment = "unreadable_fragment";
}

public static class ExtractionErrorCode
{
    public const string InvalidPayload = "invalid_payload";
    public const string UnsupportedFormat = "unsupported_format";
    public const string LanguageUndetectable = "language_undetectable";
    public const string OcrFailedAll = "ocr_failed_all";
    public const string ApiTimeout = "api_timeout";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
    public const string ScopeDenied = "scope_denied";
    public const string NotImplemented = "not_implemented";
}

public static class ExtractionAuditEventType
{
    public const string JobCreated = "job_created";
    public const string JobStarted = "job_started";
    public const string LanguageDetected = "language_detected";
    public const string ExtractionCompleted = "extraction_completed";
    public const string ExtractionFailed = "extraction_failed";
    public const string ConfidenceTierAssigned = "confidence_tier_assigned";
    public const string ManualReviewFlagged = "manual_review_flagged";
    public const string ResultPersisted = "result_persisted";
    public const string ErrorRecorded = "error_recorded";
    public const string ScopeDenied = "scope_denied";
    public const string DuplicateSuppressed = "duplicate_suppressed";
}

// ---- Request -------------------------------------------------------------

public sealed record ExtractionPrincipalContext(
    [property: JsonPropertyName("principal_id")] Guid PrincipalId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_type")] string? ContextType,
    [property: JsonPropertyName("context_id")] Guid? ContextId);

public sealed record ExtractionInput(
    [property: JsonPropertyName("input_type")] string? InputType,
    [property: JsonPropertyName("source")] string? Source,
    // Audio/image: base64 payload; Text: inline text.
    [property: JsonPropertyName("audio_data")] string? AudioData,
    [property: JsonPropertyName("encoding")] string? Encoding,
    [property: JsonPropertyName("duration_seconds")] int? DurationSeconds,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("image_data")] string? ImageData,
    [property: JsonPropertyName("image_type")] string? ImageType);

public sealed record ExtractionProcessingOptions(
    [property: JsonPropertyName("async_mode")] bool? AsyncMode,
    [property: JsonPropertyName("language_hint")] string? LanguageHint,
    [property: JsonPropertyName("confidence_threshold")] double? ConfidenceThreshold,
    [property: JsonPropertyName("include_raw")] bool? IncludeRaw);

public sealed record ExtractionRequest(
    [property: JsonPropertyName("extraction_id")] string? ExtractionId,
    [property: JsonPropertyName("tenant_id")] Guid? TenantId,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId,
    [property: JsonPropertyName("principal_context")] ExtractionPrincipalContext? PrincipalContext,
    [property: JsonPropertyName("extraction_input")] ExtractionInput? ExtractionInput,
    [property: JsonPropertyName("processing_options")] ExtractionProcessingOptions? ProcessingOptions);

// ---- Response ------------------------------------------------------------

public sealed record ExtractionFieldDto(
    [property: JsonPropertyName("field_name")] string FieldName,
    [property: JsonPropertyName("field_type")] string FieldType,
    [property: JsonPropertyName("extracted_value")] object? ExtractedValue,
    [property: JsonPropertyName("confidence_score")] double ConfidenceScore,
    [property: JsonPropertyName("confidence_tier")] string ConfidenceTier,
    [property: JsonPropertyName("confidence_justification")] string? ConfidenceJustification = null,
    [property: JsonPropertyName("source_segment_index")] int? SourceSegmentIndex = null,
    [property: JsonPropertyName("detected_language")] string? DetectedLanguage = null);

public sealed record ModelInfo(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model_name")] string ModelName,
    [property: JsonPropertyName("version")] string Version);

public sealed record ExtractionResults(
    [property: JsonPropertyName("extraction_type")] string ExtractionType,
    [property: JsonPropertyName("overall_confidence")] double OverallConfidence,
    [property: JsonPropertyName("extracted_fields")] IReadOnlyList<ExtractionFieldDto> ExtractedFields,
    [property: JsonPropertyName("raw_content")] object? RawContent,
    [property: JsonPropertyName("model_info")] ModelInfo? ModelInfo);

public sealed record WarningItem(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("affected_fields")] IReadOnlyList<string> AffectedFields);

public sealed record ErrorInfo(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record ProcessingMetadata(
    [property: JsonPropertyName("processing_time_ms")] int? ProcessingTimeMs,
    [property: JsonPropertyName("segment_count")] int SegmentCount,
    [property: JsonPropertyName("completion_pct")] double CompletionPct);

public sealed record AuditEventDto(
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("details")] IReadOnlyDictionary<string, object?>? Details = null);

public sealed record ExtractionResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("job_status")] string JobStatus,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("extraction_results")] ExtractionResults? ExtractionResults = null,
    [property: JsonPropertyName("warnings")] IReadOnlyList<WarningItem>? Warnings = null,
    [property: JsonPropertyName("error")] ErrorInfo? Error = null,
    [property: JsonPropertyName("status_url")] string? StatusUrl = null,
    [property: JsonPropertyName("audit_trail")] IReadOnlyList<AuditEventDto>? AuditTrail = null,
    [property: JsonPropertyName("processing_metadata")] ProcessingMetadata? ProcessingMetadata = null);

public sealed record JobStatusResponse(
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("job_status")] string JobStatus,
    [property: JsonPropertyName("completion_pct")] double CompletionPct,
    [property: JsonPropertyName("segment_count")] int SegmentCount,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record ExtractionErrorResponse(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
