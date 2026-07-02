using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Direct note search for explicit person lookups (SB-014). Deliberately bypasses
/// the corroboration gate of <c>MemoryRecallService</c>: a single self-authored
/// note is ground truth for "¿Quién es X?" and must be returned, not withheld.
/// </summary>
public interface IPersonLookupService
{
    /// <summary>
    /// Returns the relevant saved notes for the person name, best match first
    /// (empty when none are within relevance distance). Writes the recall audit.
    /// </summary>
    Task<IReadOnlyList<string>> FindNotesAsync(
        PrincipalScope scope,
        string personName,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class PersonLookupService : IPersonLookupService
{
    private const int SearchTopK = 8;
    private const int MaxNotes = 5;

    private readonly IEmbeddingClient _embeddingClient;
    private readonly MemoryStore _store;
    private readonly MemoryOptions _options;
    private readonly ILogger<PersonLookupService> _logger;

    public PersonLookupService(
        IEmbeddingClient embeddingClient,
        MemoryStore store,
        IOptions<MemoryOptions> options,
        ILogger<PersonLookupService> logger)
    {
        _embeddingClient = embeddingClient;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> FindNotesAsync(
        PrincipalScope scope,
        string personName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> notes;
        try
        {
            // Embedding is on the reply path but must not inherit the webhook ct
            // (CancellationToken discipline) — bounded by its own timeout instead.
            float[] embedding;
            using (var embedCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                embedding = await _embeddingClient.EmbedAsync(personName, embedCts.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await _store.SearchAsync(scope, embedding, SearchTopK, cancellationToken);
            notes = candidates
                .Where(c => c.Distance <= _options.RelevanceMaxDistance && !string.IsNullOrWhiteSpace(c.ContentText))
                .OrderBy(c => c.Distance)
                .Take(MaxNotes)
                .Select(c => c.ContentText!.Trim())
                .ToList();
        }
        catch (Exception)
        {
            await WriteAuditBestEffortAsync(scope, "person_lookup_error", correlationId);
            throw;
        }

        await WriteAuditBestEffortAsync(
            scope,
            notes.Count > 0 ? "person_lookup_answered" : "person_lookup_no_notes",
            correlationId);
        return notes;
    }

    private async Task WriteAuditBestEffortAsync(PrincipalScope scope, string result, string correlationId)
    {
        try
        {
            // CancellationToken.None: WORM audit must be written regardless of webhook lifecycle.
            await _store.WriteRecallAuditAsync(scope, MemoryAuditEventName.PersonLookup, result, correlationId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PersonLookupService audit write failed. correlation_id={CorrelationId}", correlationId);
        }
    }
}
