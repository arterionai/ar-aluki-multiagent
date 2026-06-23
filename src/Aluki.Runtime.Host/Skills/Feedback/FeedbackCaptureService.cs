using Aluki.Runtime.Abstractions.Skills.Feedback;

namespace Aluki.Runtime.Host.Skills.Feedback;

public sealed class FeedbackCaptureService
{
    private readonly IFeedbackRepository _repo;

    public FeedbackCaptureService(IFeedbackRepository repo) => _repo = repo;

    public async Task<CaptureSuggestionResponse> CaptureAsync(CaptureSuggestionRequest request, CancellationToken ct)
    {
        if (!HasSuggestionIntent(request.MessageText))
            return new CaptureSuggestionResponse("not_a_suggestion", null, false);

        var textContent = request.MessageText.Length <= 5120 ? request.MessageText : null;
        var payloadHash = request.PayloadHash ?? ComputePayloadHash(request.MessageText);
        var windowExpires = DateTimeOffset.UtcNow.AddMinutes(30);

        var (suggestionId, isNew) = await _repo.UpsertSuggestionAsync(
            request.TenantId, request.UserId, textContent,
            request.SourceMessageId, payloadHash,
            windowExpires, ct);

        if (!isNew)
            return new CaptureSuggestionResponse(CaptureSuggestionOutcome.IdempotentNoop, suggestionId, false);

        return new CaptureSuggestionResponse(CaptureSuggestionOutcome.Created, suggestionId, true);
    }

    public async Task<LinkAttachmentResponse> LinkAttachmentAsync(LinkAttachmentRequest request, CancellationToken ct)
    {
        if (!IsValidMimeType(request.AttachmentType, request.MimeType))
            return new LinkAttachmentResponse(LinkAttachmentOutcome.ValidationFailed, null, null);
        if (!IsWithinSizeLimit(request.AttachmentType, request.FileSizeBytes))
            return new LinkAttachmentResponse(LinkAttachmentOutcome.ValidationFailed, null, null);

        var suggestion = await _repo.GetActiveSuggestionAsync(request.TenantId, request.UserId, ct);
        if (suggestion == null)
            return new LinkAttachmentResponse(LinkAttachmentOutcome.NoActiveWindow, null, null);

        var newCount = await _repo.IncrementAttachmentCountAsync(suggestion.Id, request.TenantId, ct);
        if (newCount == -1)
            return new LinkAttachmentResponse(LinkAttachmentOutcome.LimitExceeded, null, null);

        var expiresAt = DateTimeOffset.UtcNow.AddDays(90);
        var attachmentId = await _repo.AddAttachmentAsync(
            request.TenantId, suggestion.Id,
            request.AttachmentType, request.BlobUri, request.MimeType,
            request.FileSizeBytes, request.ContentHash, expiresAt, ct);

        return new LinkAttachmentResponse(LinkAttachmentOutcome.Linked, attachmentId, suggestion.Id);
    }

    private static bool HasSuggestionIntent(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("suggestion") || lower.Contains("suggest") ||
               lower.Contains("idea") || lower.Contains("feature request") ||
               lower.Contains("feedback") || lower.Contains("would be nice") ||
               lower.Contains("could you add") || lower.Contains("please add") ||
               // Spanish
               lower.Contains("recomendaci") || lower.Contains("recomiendo") || lower.Contains("recomendaría") ||
               lower.Contains("sugerencia") || lower.Contains("sugiero") || lower.Contains("sugerir") ||
               lower.Contains("sería bueno") || lower.Contains("estaría bien") || lower.Contains("me gustaría que") ||
               lower.Contains("podrías agregar") || lower.Contains("podrías añadir") || lower.Contains("podrías incluir") ||
               lower.Contains("quiero proponer") || lower.Contains("tengo una propuesta") ||
               lower.Contains("solicitud") || lower.Contains("mejora");
    }

    private static string ComputePayloadHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }

    private static bool IsValidMimeType(string attachmentType, string mimeType) => attachmentType switch
    {
        AttachmentType.Audio => mimeType is "audio/mp4" or "audio/webm" or "audio/ogg" or "audio/mpeg",
        AttachmentType.Photo => mimeType is "image/jpeg" or "image/png",
        AttachmentType.Text => true,
        _ => false
    };

    private static bool IsWithinSizeLimit(string attachmentType, long fileSizeBytes) => attachmentType switch
    {
        AttachmentType.Audio => fileSizeBytes <= 50 * 1024 * 1024,
        AttachmentType.Photo => fileSizeBytes <= 10 * 1024 * 1024,
        AttachmentType.Text => fileSizeBytes <= 5 * 1024,
        _ => false
    };
}
