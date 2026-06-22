namespace Aluki.Runtime.Abstractions.Skills.Feedback;

public sealed record CaptureSuggestionRequest(
    Guid TenantId,
    Guid UserId,
    string SourceMessageId,
    string MessageText,
    string? PayloadHash = null);

public sealed record CaptureSuggestionResponse(
    string Outcome,
    Guid? SuggestionId,
    bool IsNew);

public static class CaptureSuggestionOutcome
{
    public const string Created = "created";
    public const string IdempotentNoop = "idempotent_noop";
    public const string WindowReopened = "window_reopened";
}

public sealed record LinkAttachmentRequest(
    Guid TenantId,
    Guid UserId,
    string SourceMessageId,
    string AttachmentType,
    string BlobUri,
    string MimeType,
    long FileSizeBytes,
    string ContentHash);

public sealed record LinkAttachmentResponse(
    string Outcome,
    Guid? AttachmentId,
    Guid? SuggestionId);

public static class LinkAttachmentOutcome
{
    public const string Linked = "linked";
    public const string NoActiveWindow = "no_active_window";
    public const string IdempotentNoop = "idempotent_noop";
    public const string LimitExceeded = "limit_exceeded";
    public const string ValidationFailed = "validation_failed";
}

public sealed record SuggestionRecord(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string State,
    string? TextContent,
    string? TextBlobUri,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ContextWindowExpiresAtUtc,
    int AttachmentCount,
    string? InboundMessageId,
    string? InboundPayloadHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
