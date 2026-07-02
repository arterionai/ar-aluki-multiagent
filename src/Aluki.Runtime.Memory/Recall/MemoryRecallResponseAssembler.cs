namespace Aluki.Runtime.Memory.Recall;

/// <summary>
/// SB-002 US3 (T033): assembles the recall response shape, mapping corroborated
/// evidence into claims with citations and grouping that evidence by topic. Topic
/// grouping spans channels (continuity is context-scoped, not channel-scoped), so
/// a single topic group can cite artifacts captured on different channels.
/// </summary>
public sealed class MemoryRecallResponseAssembler
{
    private readonly TopicGroupingSkill _topicGrouping;

    public MemoryRecallResponseAssembler(TopicGroupingSkill topicGrouping)
    {
        _topicGrouping = topicGrouping;
    }

    /// <summary>Builds a confirmed grounded result for corroborated evidence.</summary>
    public RecallResult AssembleGrounded(string answer, IReadOnlyList<RecallCandidate> relevant)
    {
        var citations = relevant.Select(c => new Citation(c.ArtifactId, c.ProvenanceRef)).ToList();
        var claim = new RecallClaim("claim-1", answer, "confirmed", citations);

        return new RecallResult(
            Confidence: "confirmed",
            ClarificationQuestion: null,
            NoResultReason: null,
            TopicGroups: _topicGrouping.Group(relevant),
            Claims: [claim]);
    }

    /// <summary>
    /// Builds a confirmed grounded result from the corroborated evidence verbatim —
    /// one claim per candidate, each with its own citation — without the synthesis
    /// LLM hop. Used on reply paths where a downstream completion re-reasons over
    /// the claims anyway (RecallSynthesisMode.Raw).
    /// </summary>
    public RecallResult AssembleGroundedRaw(IReadOnlyList<RecallCandidate> relevant)
    {
        var claims = relevant
            .Select((c, i) => new RecallClaim(
                $"claim-{i + 1}",
                c.ContentText ?? string.Empty,
                "confirmed",
                new List<Citation> { new(c.ArtifactId, c.ProvenanceRef) }))
            .ToList();

        return new RecallResult(
            Confidence: "confirmed",
            ClarificationQuestion: null,
            NoResultReason: null,
            TopicGroups: _topicGrouping.Group(relevant),
            Claims: claims);
    }

    /// <summary>Builds a low-confidence result for a single relevant artifact.</summary>
    public RecallResult AssembleLowConfidence(RecallCandidate single)
    {
        var citations = new List<Citation> { new(single.ArtifactId, single.ProvenanceRef) };
        var claim = new RecallClaim("claim-1", single.ContentText ?? string.Empty, "low_confidence", citations);

        return new RecallResult(
            Confidence: "low",
            ClarificationQuestion: "Solo encontré una nota relacionada; ¿puedes dar más detalle para confirmar?",
            NoResultReason: null,
            TopicGroups: _topicGrouping.Group([single]),
            Claims: [claim]);
    }
}
