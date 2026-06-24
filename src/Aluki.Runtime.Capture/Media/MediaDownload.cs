namespace Aluki.Runtime.Capture.Media;

/// <summary>
/// Work item to fetch a WhatsApp media binary and store it durably. Enqueued
/// after a media-bearing message is captured; processed asynchronously so the
/// synchronous capture ack stays within its latency budget.
/// </summary>
public sealed record MediaDownloadJob(
    Guid TenantId,
    Guid ContextId,
    Guid MessageId,
    Guid MediaId,
    string ProviderMediaId,
    string ContentType);

/// <summary>Enqueues media download work. Implementations are channel/infra specific.</summary>
public interface IMediaDownloadQueue
{
    Task EnqueueAsync(MediaDownloadJob job, CancellationToken cancellationToken);
}

/// <summary>No-op queue used where async media download is not wired (e.g. the dev host).</summary>
public sealed class NullMediaDownloadQueue : IMediaDownloadQueue
{
    public Task EnqueueAsync(MediaDownloadJob job, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>No-op media client used where Graph API download is not wired (e.g. the dev host).</summary>
public sealed class NullMetaMediaClient : IMetaMediaClient
{
    public Task<MetaMediaContent> DownloadAsync(string providerMediaId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Media download is not configured in this environment.");
}

/// <summary>Downloaded media payload.</summary>
public sealed record MetaMediaContent(byte[] Bytes, string ContentType, long ByteLength);

/// <summary>Fetches WhatsApp media binaries from the Meta Graph API.</summary>
public interface IMetaMediaClient
{
    Task<MetaMediaContent> DownloadAsync(string providerMediaId, CancellationToken cancellationToken);
}

/// <summary>Persists a media binary to durable object storage and returns its reference URI.</summary>
public interface IMediaBlobStore
{
    Task<string> UploadAsync(string blobPath, byte[] content, string contentType, CancellationToken cancellationToken);
}
