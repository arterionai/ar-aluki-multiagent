using Aluki.Runtime.Memory.Recall;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class TopicGroupingSkillTests
{
    private readonly TopicGroupingSkill _skill = new();

    private static RecallCandidate C(string text, string channel = "whatsapp") =>
        new(Guid.NewGuid(), text, $"{channel}:id", 0.1, channel);

    [Fact]
    public void Empty_evidence_yields_no_groups()
    {
        Assert.Empty(_skill.Group([]));
    }

    [Fact]
    public void Related_notes_collapse_into_one_topic()
    {
        var groups = _skill.Group([
            C("cita con el dentista el martes"),
            C("recordatorio dentista martes 4pm")
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.ArtifactIds.Count);
        Assert.Equal("dentista", group.Topic); // most frequent shared keyword
    }

    [Fact]
    public void Unrelated_notes_split_into_distinct_topics()
    {
        var groups = _skill.Group([
            C("cita con el dentista el martes"),
            C("dentista revision martes"),
            C("comprar pan leche huevos")
        ]);

        Assert.Equal(2, groups.Count);
        // Largest group first: the two dentist notes.
        Assert.Equal(2, groups[0].ArtifactIds.Count);
        Assert.Equal("dentista", groups[0].Topic);
        Assert.Single(groups[1].ArtifactIds);
    }

    [Fact]
    public void Grouping_is_deterministic_across_runs()
    {
        var evidence = new[]
        {
            C("proyecto alpha reunion"),
            C("alpha entrega viernes"),
            C("vacaciones playa agosto")
        };

        var first = _skill.Group(evidence);
        var second = _skill.Group(evidence);

        Assert.Equal(
            first.Select(g => (g.Topic, g.ArtifactIds.Count)),
            second.Select(g => (g.Topic, g.ArtifactIds.Count)));
    }

    [Fact]
    public void Accent_variants_cluster_together()
    {
        var groups = _skill.Group([
            C("reunión equipo lunes"),
            C("reunion equipo agenda")
        ]);

        Assert.Single(groups);
    }
}
