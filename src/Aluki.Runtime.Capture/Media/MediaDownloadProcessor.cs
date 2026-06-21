using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Capture.Media;

/// <summary>
/// Reusable core of the async media pipeline: download the binary from Meta,
/// store it durably, and link the stored reference back to the media artifact.
/// </summary>
public sealed class MediaDownloadProcessor
{
    private readonly IMetaMediaClient _client;
    private readonly IMediaBlobStore _blobStore;
    private readonly IMediaRefUpdater _updater;
    private readonly ILogger<MediaDownloadProcessor> _logger;

    public MediaDownloadProcessor(
        IMetaMediaClient client,
        IMediaBlobStore blobStore,
        IMediaRefUpdater updater,
        ILogger<MediaDownloadProcessor> logger)
    {
        _client = client;
        _blobStore = blobStore;
        _updater = updater;
        _logger = logger;
    }

    public async Task ProcessAsync(MediaDownloadJob job, CancellationToken cancellationToken)
    {
        var content = await _client.DownloadAsync(job.ProviderMediaId, cancellationToken);

        // Deterministic, tenant-scoped blob path.
        var blobPath = $"{job.TenantId:D}/{job.MessageId:D}/{job.MediaId:D}";
        var contentType = string.IsNullOrWhiteSpace(content.ContentType) ? job.ContentType : content.ContentType;

        var uri = await _blobStore.UploadAsync(blobPath, content.Bytes, contentType, cancellationToken);
        await _updater.UpdateAsync(job.MediaId, uri, content.ByteLength, cancellationToken);

        _logger.LogInformation(
            "Media downloaded. media_id={MediaId} bytes={Bytes} uri={Uri}",
            job.MediaId,
            content.ByteLength,
            uri);
    }
}
