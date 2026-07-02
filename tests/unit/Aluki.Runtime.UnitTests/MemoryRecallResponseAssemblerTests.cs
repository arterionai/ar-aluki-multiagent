using Aluki.Runtime.Memory.Recall;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class MemoryRecallResponseAssemblerTests
{
    private readonly MemoryRecallResponseAssembler _assembler = new(new TopicGroupingSkill());

    private static RecallCandidate C(string text, string channel = "whatsapp") =>
        new(Guid.NewGuid(), text, $"{channel}:{Guid.NewGuid():N}", 0.1, channel);

    [Fact]
    public void AssembleGroundedRaw_emits_one_confirmed_claim_per_candidate()
    {
        var a = C("Fer es mi prima de Monterrey");
        var b = C("Fer trabaja en el hospital");

        var result = _assembler.AssembleGroundedRaw([a, b]);

        Assert.Equal("confirmed", result.Confidence);
        Assert.Null(result.ClarificationQuestion);
        Assert.Null(result.NoResultReason);
        Assert.Equal(2, result.Claims.Count);
        Assert.Equal("Fer es mi prima de Monterrey", result.Claims[0].Text);
        Assert.Equal("Fer trabaja en el hospital", result.Claims[1].Text);
        Assert.All(result.Claims, claim => Assert.Equal("confirmed", claim.ConfirmationStatus));
    }

    [Fact]
    public void AssembleGroundedRaw_each_claim_carries_its_own_citation()
    {
        var a = C("nota uno");
        var b = C("nota dos");

        var result = _assembler.AssembleGroundedRaw([a, b]);

        var citationA = Assert.Single(result.Claims[0].Citations);
        var citationB = Assert.Single(result.Claims[1].Citations);
        Assert.Equal(a.ArtifactId, citationA.MemoryArtifactId);
        Assert.Equal(a.ProvenanceRef, citationA.ProvenanceRef);
        Assert.Equal(b.ArtifactId, citationB.MemoryArtifactId);
        Assert.Equal(b.ProvenanceRef, citationB.ProvenanceRef);
    }

    [Fact]
    public void AssembleGroundedRaw_groups_topics_like_synthesized_mode()
    {
        var evidence = new[]
        {
            C("cita con el dentista el martes"),
            C("recordatorio dentista martes 4pm")
        };

        var raw = _assembler.AssembleGroundedRaw(evidence);
        var synthesized = _assembler.AssembleGrounded("answer", evidence);

        Assert.Equal(synthesized.TopicGroups.Count, raw.TopicGroups.Count);
    }

    [Fact]
    public void AssembleGroundedRaw_null_content_becomes_empty_claim_text()
    {
        var candidate = new RecallCandidate(Guid.NewGuid(), null, "whatsapp:x", 0.1, "whatsapp");

        var result = _assembler.AssembleGroundedRaw([candidate]);

        Assert.Equal(string.Empty, Assert.Single(result.Claims).Text);
    }
}
