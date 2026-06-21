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
