using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory.Ingestion;

/// <summary>
/// Capture→memory bridge implementation: embeds the captured text and stores it
/// as a canonical memory artifact under the principal's scope. Idempotent via the
/// memory store's source-identity upsert (repeat deliveries are suppressed), and
/// resilient — an embedding failure still persists the artifact so recall can
/// backfill the vector later.
/// </summary>
public sealed class MemoryIngestionSink : IMemoryIngestionSink
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryIngestionSink> _logger;

    public MemoryIngestionSink(
        IEmbeddingClient embeddingClient,
        MemoryStore store,
        ILogger<MemoryIngestionSink> logger)
    {
        _embeddingClient = embeddingClient;
        _store = store;
        _logger = logger;
    }

    public async Task IngestAsync(MemoryIngestionItem item, CancellationToken cancellationToken)
    {
        var principal = new PrincipalScope(item.TenantId, item.ContextId, item.UserId, null);

        float[]? embedding = null;
        try
        {
            embedding = await _embeddingClient.EmbedAsync(item.ContentText, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Embedding failed during capture ingestion; persisting without vector. correlation_id={CorrelationId}",
                item.CorrelationId);
        }

        var result = await _store.CaptureNoteAsync(
            principal,
            item.SourceChannel,
            item.SourceIdentity,
            item.ContentText,
            embedding,
            item.ProvenanceRef,
            item.CorrelationId,
            cancellationToken);

        _logger.LogInformation(
            "memory.ingested_from_capture is_new={IsNew} channel={Channel} correlation_id={CorrelationId}",
            result.IsNew,
            item.SourceChannel,
            item.CorrelationId);
    }
}
