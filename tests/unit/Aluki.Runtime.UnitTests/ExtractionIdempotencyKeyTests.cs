using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Policies;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class ExtractionIdempotencyKeyTests
{
    [Fact]
    public void Explicit_extraction_id_is_used_verbatim()
    {
        var key = ExtractionIdempotencyKey.Derive("  job-123  ", ExtractionInputType.Text, "payload");
        Assert.Equal("job-123", key);
    }

    [Fact]
    public void Derived_key_is_stable_for_same_input()
    {
        var a = ExtractionIdempotencyKey.Derive(null, ExtractionInputType.Text, "hello world");
        var b = ExtractionIdempotencyKey.Derive(null, ExtractionInputType.Text, "hello world");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Derived_key_differs_by_input_type_and_payload()
    {
        var text = ExtractionIdempotencyKey.Derive(null, ExtractionInputType.Text, "x");
        var audio = ExtractionIdempotencyKey.Derive(null, ExtractionInputType.Audio, "x");
        var other = ExtractionIdempotencyKey.Derive(null, ExtractionInputType.Text, "y");

        Assert.NotEqual(text, audio);
        Assert.NotEqual(text, other);
    }
}
