using Aluki.Runtime.Memory.Recall;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class CorroborationPolicyTests
{
    private static RecallCandidate C(double distance) => new(Guid.NewGuid(), "x", "prov", distance);

    [Fact]
    public void Two_or_more_relevant_is_confirmed()
    {
        var result = CorroborationPolicy.Evaluate([C(0.1), C(0.2), C(0.9)], maxDistance: 0.6);

        Assert.Equal(RecallDecision.Confirmed, result.Decision);
        Assert.Equal(2, result.Relevant.Count); // 0.9 excluded
        Assert.Equal(0.1, result.Relevant[0].Distance); // ordered by distance
    }

    [Fact]
    public void Single_relevant_is_low()
    {
        var result = CorroborationPolicy.Evaluate([C(0.2), C(0.8)], maxDistance: 0.6);

        Assert.Equal(RecallDecision.Low, result.Decision);
        Assert.Single(result.Relevant);
    }

    [Fact]
    public void None_relevant_is_none()
    {
        var result = CorroborationPolicy.Evaluate([C(0.7), C(0.95)], maxDistance: 0.6);

        Assert.Equal(RecallDecision.None, result.Decision);
        Assert.Empty(result.Relevant);
    }

    [Fact]
    public void Empty_candidates_is_none()
    {
        var result = CorroborationPolicy.Evaluate([], maxDistance: 0.6);
        Assert.Equal(RecallDecision.None, result.Decision);
    }
}
