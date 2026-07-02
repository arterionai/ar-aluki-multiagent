using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Memory.Recall;

/// <summary>
/// How corroborated evidence is turned into claims.
/// <para><see cref="Synthesized"/>: an extra LLM completion condenses the evidence into a
/// single answer — used where the recall result IS the user-visible payload (memory HTTP API).</para>
/// <para><see cref="Raw"/>: corroborated evidence is returned verbatim, one claim per
/// candidate — used on WhatsApp reply paths where a downstream LLM re-reasons over the
/// claims anyway, so the synthesis hop is redundant serial latency.</para>
/// </summary>
public enum RecallSynthesisMode
{
    Synthesized,
    Raw
}

public sealed record MemoryRecallOutcome(string Status, RecallResult Recall)
{
    /// <summary>
    /// Completion of the recall audit write (WORM). The write is started inside
    /// RecallAsync without being awaited; reply-path callers MUST await this after
    /// sending the user-visible reply and before returning from HandleAsync so the
    /// audit record is always persisted before the webhook returns 200. Never faults.
    /// </summary>
    public Task AuditCompletion { get; init; } = Task.CompletedTask;
}

public interface IMemoryRecallService
{
    Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal,
        string queryText,
        string correlationId,
        CancellationToken cancellationToken);

    Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal,
        string queryText,
        string correlationId,
        RecallSynthesisMode mode,
        CancellationToken cancellationToken)
        => RecallAsync(principal, queryText, correlationId, cancellationToken);
}

/// <summary>
/// Grounded recall (US2): embed the query, vector-search non-deleted in-scope
/// artifacts, apply the corroboration gate (>=2 to confirm), and assemble an answer
/// strictly from retrieved evidence — synthesized via the Foundry model-router or
/// returned as raw corroborated claims depending on <see cref="RecallSynthesisMode"/>.
/// Never fabricates: no/low evidence yields explicit no_result/low_confidence, and a
/// deletion gap is signaled distinctly.
/// </summary>
public sealed class MemoryRecallService : IMemoryRecallService
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

    public Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal,
        string queryText,
        string correlationId,
        CancellationToken cancellationToken)
        => RecallAsync(principal, queryText, correlationId, RecallSynthesisMode.Synthesized, cancellationToken);

    public async Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal,
        string queryText,
        string correlationId,
        RecallSynthesisMode mode,
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
            return NoResult(principal, "no_evidence", correlationId);
        }

        var candidates = await _store.SearchAsync(principal, queryEmbedding, _options.RecallTopK, cancellationToken);
        var corroboration = CorroborationPolicy.Evaluate(candidates, _options.RelevanceMaxDistance);

        if (corroboration.Decision == RecallDecision.None)
        {
            var hasDeleted = await _store.HasDeletedRelevantAsync(
                principal, queryEmbedding, _options.RelevanceMaxDistance, cancellationToken);
            return NoResult(principal, hasDeleted ? "deleted_evidence_gap" : "no_evidence", correlationId);
        }

        if (corroboration.Decision == RecallDecision.Low)
        {
            return new MemoryRecallOutcome(
                MemoryStatus.LowConfidence,
                _assembler.AssembleLowConfidence(corroboration.Relevant[0]))
            {
                AuditCompletion = StartRecallAudit(
                    principal, MemoryAuditEventName.RecallLowConfidence, MemoryStatus.LowConfidence, correlationId)
            };
        }

        var result = mode == RecallSynthesisMode.Raw
            ? _assembler.AssembleGroundedRaw(corroboration.Relevant)
            : _assembler.AssembleGrounded(
                await SynthesizeAsync(queryText, corroboration.Relevant, correlationId, cancellationToken),
                corroboration.Relevant);

        if (MemoryContinuityPolicy.IsCrossChannel(corroboration.Relevant))
        {
            _logger.LogInformation(
                "memory.recall_cross_channel channels={Channels} correlation_id={CorrelationId}",
                string.Join(",", MemoryContinuityPolicy.DistinctChannels(corroboration.Relevant)),
                correlationId);
        }

        return new MemoryRecallOutcome(MemoryStatus.GroundedResult, result)
        {
            AuditCompletion = StartRecallAudit(
                principal, MemoryAuditEventName.RecallGrounded, MemoryStatus.GroundedResult, correlationId)
        };
    }

    private MemoryRecallOutcome NoResult(
        PrincipalScope principal,
        string reason,
        string correlationId)
    {
        return new MemoryRecallOutcome(
            MemoryStatus.NoResult,
            new RecallResult(
                Confidence: null,
                ClarificationQuestion: null,
                NoResultReason: reason,
                TopicGroups: [],
                Claims: []))
        {
            AuditCompletion = StartRecallAudit(
                principal, MemoryAuditEventName.RecallNoResult, MemoryStatus.NoResult, correlationId)
        };
    }

    /// <summary>
    /// Starts the WORM recall-audit write without awaiting it so it overlaps with the
    /// downstream LLM call instead of preceding it. CancellationToken.None per the
    /// CancellationToken discipline (audit records must always be written); failures
    /// are logged, never thrown — the returned task never faults.
    /// </summary>
    private Task StartRecallAudit(
        PrincipalScope principal,
        string eventName,
        string status,
        string correlationId)
    {
        return Task.Run(async () =>
        {
            try
            {
                await _store.WriteRecallAuditAsync(principal, eventName, status, correlationId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Recall audit write failed. event={EventName} correlation_id={CorrelationId}",
                    eventName, correlationId);
            }
        });
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
