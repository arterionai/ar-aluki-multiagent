namespace Aluki.Runtime.Memory.Recall;

/// <summary>A retrieved memory artifact considered as recall evidence.</summary>
public sealed record RecallCandidate(
    Guid ArtifactId,
    string? ContentText,
    string ProvenanceRef,
    double Distance,
    string SourceChannel = "");

public enum RecallDecision
{
    /// <summary>No relevant evidence.</summary>
    None,

    /// <summary>Exactly one relevant artifact — low confidence, ask to clarify.</summary>
    Low,

    /// <summary>Two or more relevant artifacts — claim can be confirmed.</summary>
    Confirmed
}

public sealed record CorroborationResult(RecallDecision Decision, IReadOnlyList<RecallCandidate> Relevant);

/// <summary>
/// Deterministic corroboration gate (FR-005/006): a confirmed claim requires at
/// least two distinct, in-scope, non-deleted corroborating artifacts. A single
/// artifact yields low confidence; none yields no result.
/// </summary>
public static class CorroborationPolicy
{
    public static CorroborationResult Evaluate(IReadOnlyList<RecallCandidate> candidates, double maxDistance)
    {
        var relevant = candidates
            .Where(c => c.Distance <= maxDistance)
            .OrderBy(c => c.Distance)
            .ToList();

        var decision = relevant.Count switch
        {
            >= 2 => RecallDecision.Confirmed,
            1 => RecallDecision.Low,
            _ => RecallDecision.None
        };

        return new CorroborationResult(decision, relevant);
    }
}
