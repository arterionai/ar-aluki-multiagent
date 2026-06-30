using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Host.Skills.Feedback;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class FeedbackCaptureContractTests
{
    private static FeedbackCaptureService BuildService(StubFeedbackRepository? repo = null)
        => new(repo ?? new StubFeedbackRepository(), new KeywordStubFeedbackIntentDetector());

    private static CaptureSuggestionRequest MakeCaptureRequest(string text) =>
        new(TenantId: Guid.NewGuid(), UserId: Guid.NewGuid(), SourceMessageId: Guid.NewGuid().ToString(), MessageText: text);

    private static LinkAttachmentRequest MakeAttachRequest(
        Guid tenantId, Guid userId,
        string attachmentType = AttachmentType.Audio,
        string mimeType = "audio/mp4",
        long fileSizeBytes = 1024) =>
        new(TenantId: tenantId, UserId: userId,
            SourceMessageId: Guid.NewGuid().ToString(),
            AttachmentType: attachmentType,
            BlobUri: "https://blob.example.com/test.mp3",
            MimeType: mimeType,
            FileSizeBytes: fileSizeBytes,
            ContentHash: Guid.NewGuid().ToString("N"));

    private static SuggestionRecord MakeSuggestionRecord(Guid tenantId, Guid userId) =>
        new(Guid.NewGuid(), tenantId, userId, "captured", null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), 0,
            null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Suggestion_keyword_creates_suggestion()
    {
        var repo = new StubFeedbackRepository { UpsertResult = (Guid.NewGuid(), true) };
        var svc = BuildService(repo);

        var result = await svc.CaptureAsync(MakeCaptureRequest("I have a suggestion for you"), CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.Created, result.Outcome);
        Assert.True(result.IsNew);
    }

    [Fact]
    public async Task Idea_keyword_creates_suggestion()
    {
        var repo = new StubFeedbackRepository { UpsertResult = (Guid.NewGuid(), true) };
        var svc = BuildService(repo);

        var result = await svc.CaptureAsync(MakeCaptureRequest("Here's my idea for improving the UI"), CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.Created, result.Outcome);
        Assert.True(result.IsNew);
    }

    [Fact]
    public async Task Plain_text_returns_not_a_suggestion()
    {
        var svc = BuildService();

        var result = await svc.CaptureAsync(MakeCaptureRequest("Hello how are you"), CancellationToken.None);

        Assert.Equal("not_a_suggestion", result.Outcome);
        Assert.Null(result.SuggestionId);
        Assert.False(result.IsNew);
    }

    [Fact]
    public async Task Idempotent_capture_returns_noop()
    {
        var existingId = Guid.NewGuid();
        var repo = new StubFeedbackRepository { UpsertResult = (existingId, false) };
        var svc = BuildService(repo);

        var result = await svc.CaptureAsync(MakeCaptureRequest("I have a suggestion for dark mode"), CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.IdempotentNoop, result.Outcome);
        Assert.Equal(existingId, result.SuggestionId);
        Assert.False(result.IsNew);
    }

    [Fact]
    public async Task Audio_valid_mime_links_successfully()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new StubFeedbackRepository
        {
            ActiveSuggestion = MakeSuggestionRecord(tenantId, userId),
            AttachmentCountResult = 1
        };
        var svc = BuildService(repo);

        var result = await svc.LinkAttachmentAsync(
            MakeAttachRequest(tenantId, userId, AttachmentType.Audio, "audio/mp4", 1024),
            CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.Linked, result.Outcome);
        Assert.NotNull(result.AttachmentId);
    }

    [Fact]
    public async Task Audio_invalid_mime_returns_validation_failed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = BuildService();

        var result = await svc.LinkAttachmentAsync(
            MakeAttachRequest(tenantId, userId, AttachmentType.Audio, "audio/wav", 1024),
            CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Photo_over_size_limit_returns_validation_failed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = BuildService();

        var result = await svc.LinkAttachmentAsync(
            MakeAttachRequest(tenantId, userId, AttachmentType.Photo, "image/jpeg", 11 * 1024 * 1024),
            CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task No_active_window_returns_no_active_window()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new StubFeedbackRepository { ActiveSuggestion = null };
        var svc = BuildService(repo);

        var result = await svc.LinkAttachmentAsync(
            MakeAttachRequest(tenantId, userId),
            CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.NoActiveWindow, result.Outcome);
    }
}

internal sealed class KeywordStubFeedbackIntentDetector : IFeedbackIntentDetector
{
    public Task<bool> HasSuggestionIntentAsync(string text, CancellationToken ct)
    {
        var lower = text.ToLowerInvariant();
        var result = lower.Contains("suggestion") || lower.Contains("suggest") ||
                     lower.Contains("idea") || lower.Contains("feature request") ||
                     lower.Contains("feedback") || lower.Contains("sugerencia");
        return Task.FromResult(result);
    }
}

internal sealed class StubFeedbackRepository : IFeedbackRepository
{
    public (Guid Id, bool IsNew) UpsertResult { get; set; } = (Guid.NewGuid(), true);
    public SuggestionRecord? ActiveSuggestion { get; set; }
    public int AttachmentCountResult { get; set; } = 1;

    public Task<(Guid SuggestionId, bool IsNew)> UpsertSuggestionAsync(
        Guid tenantId, Guid userId, string? textContent,
        string? inboundMessageId, string? inboundPayloadHash,
        DateTimeOffset contextWindowExpiresAtUtc, CancellationToken ct)
        => Task.FromResult((UpsertResult.Id, UpsertResult.IsNew));

    public Task<SuggestionRecord?> GetActiveSuggestionAsync(Guid tenantId, Guid userId, CancellationToken ct)
        => Task.FromResult(ActiveSuggestion);

    public Task<int> IncrementAttachmentCountAsync(Guid suggestionId, Guid tenantId, CancellationToken ct)
        => Task.FromResult(AttachmentCountResult);

    public Task<Guid> AddAttachmentAsync(
        Guid tenantId, Guid suggestionId,
        string attachmentType, string blobUri, string mimeType,
        long fileSizeBytes, string contentHash,
        DateTimeOffset expiresAtUtc, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());

    public Task<bool> IsIdempotentDuplicateAsync(
        Guid tenantId, string messageId, string payloadHash, DateTimeOffset since, CancellationToken ct)
        => Task.FromResult(false);

    public Task<bool> TransitionStateAsync(
        Guid suggestionId, Guid tenantId, string newState, string actor, string reason, CancellationToken ct)
        => Task.FromResult(true);

    public Task<IReadOnlyList<SuggestionRecord>> GetEligibleForArchivalAsync(
        DateTimeOffset cutoff, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SuggestionRecord>>(Array.Empty<SuggestionRecord>());
}
