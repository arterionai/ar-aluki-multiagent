using Aluki.Runtime.Capture.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class MediaDownloadProcessorTests
{
    private sealed class FakeClient(MetaMediaContent content) : IMetaMediaClient
    {
        public string? RequestedId { get; private set; }

        public Task<MetaMediaContent> DownloadAsync(string providerMediaId, CancellationToken cancellationToken)
        {
            RequestedId = providerMediaId;
            return Task.FromResult(content);
        }
    }

    private sealed class FakeBlobStore : IMediaBlobStore
    {
        public string? Path { get; private set; }
        public string? ContentType { get; private set; }
        public int Bytes { get; private set; }

        public Task<string> UploadAsync(string blobPath, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            Path = blobPath;
            ContentType = contentType;
            Bytes = content.Length;
            return Task.FromResult($"https://blob.example/{blobPath}");
        }
    }

    private sealed class FakeUpdater : IMediaRefUpdater
    {
        public Guid MediaId { get; private set; }
        public string? Uri { get; private set; }
        public long ByteLength { get; private set; }

        public Task UpdateAsync(Guid mediaId, string mediaRefUri, long byteLength, CancellationToken cancellationToken)
        {
            MediaId = mediaId;
            Uri = mediaRefUri;
            ByteLength = byteLength;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Downloads_uploads_and_links_reference()
    {
        var mediaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var job = new MediaDownloadJob(tenantId, Guid.NewGuid(), messageId, mediaId, "PMID", "image/jpeg");

        var client = new FakeClient(new MetaMediaContent(new byte[] { 1, 2, 3, 4 }, "image/png", 4));
        var blob = new FakeBlobStore();
        var updater = new FakeUpdater();
        var processor = new MediaDownloadProcessor(client, blob, updater, NullLogger<MediaDownloadProcessor>.Instance);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Equal("PMID", client.RequestedId);
        Assert.Equal($"{tenantId:D}/{messageId:D}/{mediaId:D}", blob.Path);
        Assert.Equal("image/png", blob.ContentType); // downloaded content type wins
        Assert.Equal(4, blob.Bytes);
        Assert.Equal(mediaId, updater.MediaId);
        Assert.Equal($"https://blob.example/{tenantId:D}/{messageId:D}/{mediaId:D}", updater.Uri);
        Assert.Equal(4, updater.ByteLength);
    }

    [Fact]
    public async Task Falls_back_to_job_content_type_when_download_has_none()
    {
        var job = new MediaDownloadJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PMID", "audio/ogg");
        var client = new FakeClient(new MetaMediaContent(new byte[] { 9 }, "", 1));
        var blob = new FakeBlobStore();
        var processor = new MediaDownloadProcessor(client, blob, new FakeUpdater(), NullLogger<MediaDownloadProcessor>.Instance);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Equal("audio/ogg", blob.ContentType);
    }
}
