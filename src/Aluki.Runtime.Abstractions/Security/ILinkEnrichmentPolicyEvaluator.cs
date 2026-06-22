namespace Aluki.Runtime.Abstractions.Security;

public sealed record PolicyEvaluationResult(string Decision, string ReasonCode);  // LinkPolicyDecision.*

public interface ILinkEnrichmentPolicyEvaluator
{
    // Evaluates whether the given URL may be enriched (outbound fetch permitted)
    PolicyEvaluationResult Evaluate(string canonicalUrl);
}
