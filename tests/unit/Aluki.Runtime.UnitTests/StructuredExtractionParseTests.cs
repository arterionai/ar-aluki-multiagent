using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Providers;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class StructuredExtractionParseTests
{
    [Fact]
    public void Parses_summary_actions_decisions_entities_amounts()
    {
        const string json = """
        {
          "summary": "Plan the launch.",
          "action_items": [{"action": "Email the team", "owner": "Ana", "due_date": "2026-07-01", "priority": "high", "confidence": 0.9}],
          "decisions": [{"statement": "Ship on Friday", "confidence": 0.8}],
          "entities": [{"name": "Ana", "entity_type": "person", "confidence": 0.95}],
          "amounts": [{"value": 1500.50, "currency": "MXN", "confidence": 0.7}],
          "dates": [{"value": "2026-07-01", "confidence": 0.65}]
        }
        """;

        var output = FoundryStructuredTextExtractionProvider.Parse(json, "es-MX");

        Assert.Equal("Plan the launch.", output.Summary);
        Assert.Contains(output.Facts, f => f.FieldName == "summary");
        Assert.Contains(output.Facts, f => f.FieldName == "action_item");
        Assert.Contains(output.Facts, f => f.FieldName == "decision" && f.FieldType == ExtractionFieldType.DecisionItem);
        Assert.Contains(output.Facts, f => f.FieldName == "entity" && f.FieldType == ExtractionFieldType.Entity);
        Assert.Contains(output.Facts, f => f.FieldName == "amount" && f.FieldType == ExtractionFieldType.Amount);
        Assert.Contains(output.Facts, f => f.FieldName == "date" && f.FieldType == ExtractionFieldType.Date);
    }

    [Fact]
    public void Tolerates_code_fences_and_surrounding_prose()
    {
        const string raw = "Here is the result:\n```json\n{\"summary\":\"ok\",\"action_items\":[]}\n```\nDone.";

        var output = FoundryStructuredTextExtractionProvider.Parse(raw, "en-US");

        Assert.Equal("ok", output.Summary);
    }

    [Fact]
    public void Garbage_returns_empty_extraction_no_fabrication()
    {
        var output = FoundryStructuredTextExtractionProvider.Parse("not json at all", "es-MX");

        Assert.Equal(string.Empty, output.Summary);
        Assert.Empty(output.Facts);
    }

    [Fact]
    public void Missing_confidence_defaults_to_medium()
    {
        const string json = """{"summary":"s","decisions":[{"statement":"d"}]}""";

        var output = FoundryStructuredTextExtractionProvider.Parse(json, "es-MX");

        var decision = Assert.Single(output.Facts, f => f.FieldName == "decision");
        Assert.Equal(0.75, decision.Confidence, 3);
    }
}
