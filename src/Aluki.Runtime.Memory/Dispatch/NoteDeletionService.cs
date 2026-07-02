using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Note deletion for WhatsApp commands (SB-016): embeds the topic and soft-deletes
/// the matching artifacts within <see cref="MemoryOptions.DeleteMaxDistance"/>.
/// </summary>
public interface INoteDeletionService
{
    /// <summary>
    /// Soft-deletes the notes matching the topic (closest first, capped) and
    /// returns the deleted note texts so the reply can echo what was removed.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteNotesAsync(
        PrincipalScope scope,
        string topic,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class NoteDeletionService : INoteDeletionService
{
    private const int MaxDeletesPerCommand = 5;

    private readonly IEmbeddingClient _embeddingClient;
    private readonly MemoryStore _store;
    private readonly MemoryOptions _options;

    public NoteDeletionService(
        IEmbeddingClient embeddingClient,
        MemoryStore store,
        IOptions<MemoryOptions> options)
    {
        _embeddingClient = embeddingClient;
        _store = store;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<string>> DeleteNotesAsync(
        PrincipalScope scope,
        string topic,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Embedding is on the reply path but must not inherit the webhook ct
        // (CancellationToken discipline) — bounded by its own timeout instead.
        float[] embedding;
        using (var embedCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            embedding = await _embeddingClient.EmbedAsync(topic, embedCts.Token);
        }
        cancellationToken.ThrowIfCancellationRequested();

        // CancellationToken.None: the delete + its WORM audit commit atomically and
        // must not be torn by the webhook lifecycle once started.
        return await _store.SoftDeleteRelevantAsync(
            scope, embedding, _options.DeleteMaxDistance, MaxDeletesPerCommand, correlationId, CancellationToken.None);
    }
}
