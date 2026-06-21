using System.Text.Json;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Functions.Media;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Queue-triggered async media pipeline: downloads the WhatsApp media binary from
/// Meta, stores it in Blob, and links the reference to the media artifact. Queue
/// retries/poison handling provide reliability without blocking the capture ack.
/// </summary>
public sealed class MediaDownloadFunction
{
    private readonly MediaDownloadProcessor _processor;
    private readonly ILogger<MediaDownloadFunction> _logger;

    public MediaDownloadFunction(MediaDownloadProcessor processor, ILogger<MediaDownloadFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [Function("MediaDownload")]
    public async Task RunAsync(
        [QueueTrigger(StorageQueueMediaDownloadQueue.QueueName, Connection = "AzureWebJobsStorage")]
        string message,
        CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<MediaDownloadJob>(message)
            ?? throw new InvalidOperationException("Invalid media download job message.");

        _logger.LogInformation(
            "Processing media download. media_id={MediaId} provider_media_id={ProviderMediaId}",
            job.MediaId,
            job.ProviderMediaId);

        await _processor.ProcessAsync(job, cancellationToken);
    }
}
