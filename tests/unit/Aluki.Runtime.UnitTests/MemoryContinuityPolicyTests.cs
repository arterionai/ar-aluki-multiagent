using Aluki.Runtime.Memory.Recall;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class MemoryContinuityPolicyTests
{
    private static RecallCandidate C(string channel, string? provenance = null) =>
        new(Guid.NewGuid(), "x", provenance ?? $"{channel}:id", 0.1, channel);

    [Fact]
    public void Distinct_channels_are_ordered_and_deduplicated()
    {
        var channels = MemoryContinuityPolicy.DistinctChannels([
            C("whatsapp"), C("telegram"), C("whatsapp")
        ]);

        Assert.Equal(["telegram", "whatsapp"], channels);
    }

    [Fact]
    public void Multiple_channels_are_cross_channel()
    {
        Assert.True(MemoryContinuityPolicy.IsCrossChannel([C("whatsapp"), C("telegram")]));
    }

    [Fact]
    public void Single_channel_is_not_cross_channel()
    {
        Assert.False(MemoryContinuityPolicy.IsCrossChannel([C("whatsapp"), C("whatsapp")]));
    }

    [Fact]
    public void Channel_falls_back_to_provenance_when_missing()
    {
        var channels = MemoryContinuityPolicy.DistinctChannels([
            new RecallCandidate(Guid.NewGuid(), "x", "telegram:abc", 0.1, SourceChannel: "")
        ]);

        Assert.Equal(["telegram"], channels);
    }
}
