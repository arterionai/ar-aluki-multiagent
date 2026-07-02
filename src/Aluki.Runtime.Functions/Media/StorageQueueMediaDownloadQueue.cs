using System.Text.Json;
using Aluki.Runtime.Capture.Media;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Functions.Media;

/// <summary>
/// Enqueues media download jobs onto an Azure Storage Queue consumed by the
/// queue-triggered media download function. Base64 encoding matches the Functions
/// queue trigger's default decoding. The QueueClient is cached (it is thread-safe)
/// and the queue is created lazily once — not once per message, which used to cost
/// an extra Storage round-trip on every media capture.
/// </summary>
public sealed class StorageQueueMediaDownloadQueue : IMediaDownloadQueue
{
    public const string QueueName = "whatsapp-media-download";

    private readonly QueueClient _client;
    private readonly SemaphoreSlim _createGate = new(1, 1);
    private volatile bool _queueEnsured;

    public StorageQueueMediaDownloadQueue(IConfiguration configuration)
    {
        var connectionString = configuration["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured for the media queue.");
        _client = new QueueClient(
            connectionString,
            QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    }

    public async Task EnqueueAsync(MediaDownloadJob job, CancellationToken cancellationToken)
    {
        if (!_queueEnsured)
        {
            await _createGate.WaitAsync(cancellationToken);
            try
            {
                if (!_queueEnsured)
                {
                    await _client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                    _queueEnsured = true;
                }
            }
            finally
            {
                _createGate.Release();
            }
        }

        await _client.SendMessageAsync(JsonSerializer.Serialize(job), cancellationToken);
    }
}
