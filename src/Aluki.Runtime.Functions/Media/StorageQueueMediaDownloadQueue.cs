using System.Text.Json;
using Aluki.Runtime.Capture.Media;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Functions.Media;

/// <summary>
/// Enqueues media download jobs onto an Azure Storage Queue consumed by the
/// queue-triggered media download function. Base64 encoding matches the Functions
/// queue trigger's default decoding.
/// </summary>
public sealed class StorageQueueMediaDownloadQueue : IMediaDownloadQueue
{
    public const string QueueName = "whatsapp-media-download";

    private readonly string _connectionString;

    public StorageQueueMediaDownloadQueue(IConfiguration configuration)
    {
        _connectionString = configuration["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured for the media queue.");
    }

    public async Task EnqueueAsync(MediaDownloadJob job, CancellationToken cancellationToken)
    {
        var client = new QueueClient(
            _connectionString,
            QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await client.SendMessageAsync(JsonSerializer.Serialize(job), cancellationToken);
    }
}
