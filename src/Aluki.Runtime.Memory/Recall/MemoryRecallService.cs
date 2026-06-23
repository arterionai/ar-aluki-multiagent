using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Memory.Recall;

public sealed record MemoryRecallOutcome(string Status, RecallResult Recall);

/// <summary>
/// Grounded recall (US2): embed the query, vector-search non-deleted in-scope
/// artifacts, apply the corroboration gate (>=2 to confirm), and synthesize an
/// answer strictly from retrieved evidence via the Foundry model-router. Never
/// fabricates: no/low evidence yields explicit no_result/low_confidence, and a
/// deletion gap is signaled distinctly.
/// </summary>
public sealed class MemoryRecallService
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly MemoryStore _store;
    private readonly IChatModelRouter _router;
    private readonly MemoryRecallResponseAssembler _assembler;
    private readonly MemoryOptions _options;
    private readonly ILogger<MemoryRecallService> _logger;

    public MemoryRecallService(
        IEmbeddingClient embeddingClient,
        MemoryStore store,
        IChatModelRouter router,
        MemoryRecallResponseAssembler assembler,
        IOptions<MemoryOptions> options,
        ILogger<MemoryRecallService> logger)
    {
        _embeddingClient = embeddingClient;
        _store = store;
        _router = router;
        _assembler = assembler;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal,
        string queryText,
        string correlationId,
        CancellationToken cancellationToken)
    {
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingClient.EmbedAsync(queryText, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query embedding failed. correlation_id={CorrelationId}", correlationId);
            return await NoResultAsync(principal, "no_evidence", correlationId, cancellationToken);
        }

        var candidates = await _store.SearchAsync(principal, queryEmbedding, _options.RecallTopK, cancellationToken);
        var corroboration = CorroborationPolicy.Evaluate(candidates, _options.RelevanceMaxDistance);

        if (corroboration.Decision == RecallDecision.None)
        {
            var hasDeleted = await _store.HasDeletedRelevantAsync(
                principal, queryEmbedding, _options.RelevanceMaxDistance, cancellationToken);
            return await NoResultAsync(principal, hasDeleted ? "deleted_evidence_gap" : "no_evidence", correlationId, cancellationToken);
        }

        if (corroboration.Decision == RecallDecision.Low)
        {
            await _store.WriteRecallAuditAsync(
                principal, MemoryAuditEventName.RecallLowConfidence, MemoryStatus.LowConfidence, correlationId, cancellationToken);

            return new MemoryRecallOutcome(
                MemoryStatus.LowConfidence,
                _assembler.AssembleLowConfidence(corroboration.Relevant[0]));
        }

        var answer = await SynthesizeAsync(queryText, corroboration.Relevant, correlationId, cancellationToken);
        if (MemoryContinuityPolicy.IsCrossChannel(corroboration.Relevant))
        {
            _logger.LogInformation(
                "memory.recall_cross_channel channels={Channels} correlation_id={CorrelationId}",
                string.Join(",", MemoryContinuityPolicy.DistinctChannels(corroboration.Relevant)),
                correlationId);
        }

        await _store.WriteRecallAuditAsync(
            principal, MemoryAuditEventName.RecallGrounded, MemoryStatus.GroundedResult, correlationId, cancellationToken);

        return new MemoryRecallOutcome(
            MemoryStatus.GroundedResult,
            _assembler.AssembleGrounded(answer, corroboration.Relevant));
    }

    private async Task<MemoryRecallOutcome> NoResultAsync(
        PrincipalScope principal,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _store.WriteRecallAuditAsync(
            principal, MemoryAuditEventName.RecallNoResult, MemoryStatus.NoResult, correlationId, cancellationToken);

        return new MemoryRecallOutcome(
            MemoryStatus.NoResult,
            new RecallResult(
                Confidence: null,
                ClarificationQuestion: null,
                NoResultReason: reason,
                TopicGroups: [],
                Claims: []));
    }

    private async Task<string> SynthesizeAsync(
        string query,
        IReadOnlyList<RecallCandidate> relevant,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var evidence = string.Join(
                "\n",
                relevant.Select((c, i) => $"[{i + 1}] {c.ContentText}"));
            const string system =
                "Answer the user's question using ONLY the evidence notes provided. " +
                "Reply in the user's language, concisely. If the evidence is insufficient, " +
                "say you don't have enough information. Never invent facts not in the evidence.";
            var user = $"Question: {query}\n\nEvidence:\n{evidence}";
            var answer = (await _router.CompleteAsync(system, user, cancellationToken) ?? string.Empty).Trim();
            return answer.Length > 0 ? answer : (relevant[0].ContentText ?? string.Empty);
        }
        catch (Exception ex)
        {
            // Grounded fallback: surface the top evidence rather than failing recall.
            _logger.LogError(ex, "Recall synthesis failed. correlation_id={CorrelationId}", correlationId);
            return relevant[0].ContentText ?? string.Empty;
        }
    }
}
