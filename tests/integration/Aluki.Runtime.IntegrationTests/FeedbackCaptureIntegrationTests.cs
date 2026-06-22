using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Host.Skills.Feedback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class FeedbackCaptureIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public FeedbackCaptureIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private IServiceProvider BuildRootProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddFeedbackCapture(config);
        return services.BuildServiceProvider();
    }

    private FeedbackCaptureService BuildService() =>
        BuildRootProvider().CreateScope().ServiceProvider.GetRequiredService<FeedbackCaptureService>();

    private async Task<int> CountSuggestionsAsync(Guid tenantId, Guid userId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM suggestions WHERE tenant_id = @t AND user_id = @u", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("u", userId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Suggestion_text_returns_created()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();

        var response = await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            MessageText: "I have a suggestion: please add dark mode"), CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.Created, response.Outcome);
        Assert.NotNull(response.SuggestionId);
        Assert.True(response.IsNew);
    }

    [Fact]
    public async Task Non_suggestion_text_returns_not_a_suggestion()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();

        var response = await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            MessageText: "Hello, how are you today?"), CancellationToken.None);

        Assert.Equal("not_a_suggestion", response.Outcome);
        Assert.Null(response.SuggestionId);
        Assert.False(response.IsNew);
    }

    [Fact]
    public async Task Duplicate_same_message_returns_idempotent_noop()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();
        var messageId = Guid.NewGuid().ToString();
        var request = new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: messageId,
            MessageText: "I have a suggestion for a new feature");

        var first = await svc.CaptureAsync(request, CancellationToken.None);
        var second = await svc.CaptureAsync(request, CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.Created, first.Outcome);
        Assert.Equal(CaptureSuggestionOutcome.IdempotentNoop, second.Outcome);
        Assert.Equal(first.SuggestionId, second.SuggestionId);
        Assert.Equal(1, await CountSuggestionsAsync(seed.TenantId, seed.UserId));
    }

    [Fact]
    public async Task Suggestion_stored_separate_from_memory()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();

        await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            MessageText: "Here is my feedback on the product"), CancellationToken.None);

        var count = await CountSuggestionsAsync(seed.TenantId, seed.UserId);
        Assert.True(count > 0);

        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM memory_artifact WHERE tenant_id = @t", conn);
        cmd.Parameters.AddWithValue("t", seed.TenantId);
        var memoryCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, memoryCount);
    }

    [Fact]
    public async Task Link_attachment_within_window_returns_linked()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();
        var messageId = Guid.NewGuid().ToString();

        var capture = await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: messageId,
            MessageText: "I have a suggestion with an audio attachment"), CancellationToken.None);

        Assert.Equal(CaptureSuggestionOutcome.Created, capture.Outcome);

        var attach = await svc.LinkAttachmentAsync(new LinkAttachmentRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: messageId,
            AttachmentType: AttachmentType.Audio,
            BlobUri: "https://blob.example.com/test.mp3",
            MimeType: "audio/mp4",
            FileSizeBytes: 1024,
            ContentHash: Guid.NewGuid().ToString("N")), CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.Linked, attach.Outcome);
        Assert.NotNull(attach.AttachmentId);
        Assert.Equal(capture.SuggestionId, attach.SuggestionId);
    }

    [Fact]
    public async Task Link_attachment_invalid_mime_returns_validation_failed()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();

        await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            MessageText: "I have a suggestion with audio"), CancellationToken.None);

        var attach = await svc.LinkAttachmentAsync(new LinkAttachmentRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            AttachmentType: AttachmentType.Audio,
            BlobUri: "https://blob.example.com/test.wav",
            MimeType: "audio/wav",
            FileSizeBytes: 1024,
            ContentHash: Guid.NewGuid().ToString("N")), CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.ValidationFailed, attach.Outcome);
    }

    [Fact]
    public async Task Link_attachment_exceeds_limit_returns_limit_exceeded()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();
        var messageId = Guid.NewGuid().ToString();

        await svc.CaptureAsync(new CaptureSuggestionRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: messageId,
            MessageText: "I have a suggestion with many attachments"), CancellationToken.None);

        var suggestion = await BuildRootProvider().CreateScope().ServiceProvider
            .GetRequiredService<IFeedbackRepository>()
            .GetActiveSuggestionAsync(seed.TenantId, seed.UserId, CancellationToken.None);
        Assert.NotNull(suggestion);

        await using var conn = await _fixture.OpenAsync();
        for (var i = 0; i < 10; i++)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO suggestion_attachments (id, tenant_id, suggestion_id, attachment_type, blob_uri, mime_type, file_size_bytes, content_hash, expires_at_utc, created_at_utc)
                VALUES (gen_random_uuid(), @tenant_id, @suggestion_id, 'audio', @uri, 'audio/mp4', 1024, @hash, now() + interval '90 days', now())
                """, conn);
            cmd.Parameters.AddWithValue("tenant_id", seed.TenantId);
            cmd.Parameters.AddWithValue("suggestion_id", suggestion.Id);
            cmd.Parameters.AddWithValue("uri", $"https://blob.example.com/test{i}.mp3");
            cmd.Parameters.AddWithValue("hash", Guid.NewGuid().ToString("N"));
            await cmd.ExecuteNonQueryAsync();
        }

        await using var updateCmd = new NpgsqlCommand(
            "UPDATE suggestions SET attachment_count = 10 WHERE id = @id", conn);
        updateCmd.Parameters.AddWithValue("id", suggestion.Id);
        await updateCmd.ExecuteNonQueryAsync();

        var eleventh = await svc.LinkAttachmentAsync(new LinkAttachmentRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: messageId,
            AttachmentType: AttachmentType.Audio,
            BlobUri: "https://blob.example.com/test11.mp3",
            MimeType: "audio/mp4",
            FileSizeBytes: 1024,
            ContentHash: Guid.NewGuid().ToString("N")), CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.LimitExceeded, eleventh.Outcome);
    }

    [Fact]
    public async Task Link_attachment_no_active_window_returns_no_active_window()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var svc = BuildService();

        var attach = await svc.LinkAttachmentAsync(new LinkAttachmentRequest(
            TenantId: seed.TenantId,
            UserId: seed.UserId,
            SourceMessageId: Guid.NewGuid().ToString(),
            AttachmentType: AttachmentType.Audio,
            BlobUri: "https://blob.example.com/test.mp3",
            MimeType: "audio/mp4",
            FileSizeBytes: 1024,
            ContentHash: Guid.NewGuid().ToString("N")), CancellationToken.None);

        Assert.Equal(LinkAttachmentOutcome.NoActiveWindow, attach.Outcome);
    }
}
