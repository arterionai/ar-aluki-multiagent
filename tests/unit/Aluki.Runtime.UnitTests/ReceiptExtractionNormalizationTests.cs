using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Policies;
using Aluki.Runtime.Extraction.Providers;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// SB-004 US3: deterministic receipt normalization/validation and the structured
/// → text-only fallback mapping. AI-independent; no network or DB.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReceiptExtractionNormalizationTests
{
    [Theory]
    [InlineData("OXX970814HS9", "OXX970814HS9")]   // persona moral (12)
    [InlineData("SACL900101AB1", "SACL900101AB1")] // persona física (13)
    [InlineData("oxx970814hs9", "OXX970814HS9")]   // lowercase normalized
    [InlineData(" OXX-970814-HS9 ", "OXX970814HS9")] // separators stripped
    public void Valid_rfc_is_normalized(string raw, string expected)
    {
        Assert.True(ReceiptNormalization.TryNormalizeRfc(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("NOTANRFC")]          // too short / wrong shape
    [InlineData("OXX9708HS9")]        // missing date digits
    [InlineData("123456789012")]      // no leading letters
    public void Invalid_rfc_is_rejected(string? raw)
    {
        Assert.False(ReceiptNormalization.TryNormalizeRfc(raw, out _));
    }

    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]   // es-MX decimal comma
    [InlineData("58.00", 58.00)]
    [InlineData("MXN 99", 99.00)]
    [InlineData("1,500", 1500.00)]      // thousands comma, no decimals
    public void Money_strings_normalize(string raw, double expected)
    {
        Assert.True(ReceiptNormalization.TryNormalizeAmount(raw, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void Numeric_amount_is_accepted_and_rounded()
    {
        Assert.True(ReceiptNormalization.TryNormalizeAmount(12.349, out var value));
        Assert.Equal(12.35m, value);
    }

    [Theory]
    [InlineData("not money")]
    [InlineData("-5.00")]   // negative is not a valid receipt amount
    [InlineData(null)]
    public void Invalid_amount_is_rejected(object? raw)
    {
        Assert.False(ReceiptNormalization.TryNormalizeAmount(raw, out _));
    }

    [Theory]
    [InlineData("2026-03-15", "2026-03-15")]
    [InlineData("15/03/2026", "2026-03-15")] // dd/MM/yyyy (es-MX)
    [InlineData("15-03-2026", "2026-03-15")]
    [InlineData("15/03/26", "2026-03-15")]
    public void Dates_normalize_to_iso(string raw, string expected)
    {
        Assert.True(ReceiptNormalization.TryNormalizeDate(raw, out var iso));
        Assert.Equal(expected, iso);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    public void Invalid_date_is_rejected(string raw)
    {
        Assert.False(ReceiptNormalization.TryNormalizeDate(raw, out _));
    }

    [Fact]
    public void Structured_mapping_drops_unparseable_amount_and_downtiers_invalid_rfc()
    {
        var candidates = new[]
        {
            new ReceiptFieldCandidate("vendor", "text", "OXXO", 0.95),
            new ReceiptFieldCandidate("total", "amount", "garbage", 0.95),
            new ReceiptFieldCandidate("rfc", "text", "NOTARFC", 0.95),
        };

        var facts = ReceiptExtractionPolicy.MapStructured(candidates, "es-MX");

        Assert.Contains(facts, f => f.FieldName == "vendor");
        // Unparseable amount is dropped (no fabricated number).
        Assert.DoesNotContain(facts, f => f.FieldName == "total");
        // Present-but-invalid RFC kept for review, below the surfacing bar.
        var rfc = Assert.Single(facts, f => f.FieldName == "rfc");
        Assert.True(rfc.Confidence <= ReceiptExtractionPolicy.UnvalidatedConfidence);
    }

    [Fact]
    public void Text_fallback_recovers_capped_fields_from_raw_text()
    {
        const string raw = "FARMACIA GUADALAJARA\nTOTAL $58.00\nFECHA 12/04/2026\nRFC FGU081016SQ4";

        var facts = ReceiptExtractionPolicy.ParseRawText(raw, "es-MX");

        Assert.Contains(facts, f => f.FieldName == "vendor");
        Assert.Contains(facts, f => f.FieldName == "total");
        Assert.Contains(facts, f => f.FieldName == "date");
        Assert.Contains(facts, f => f.FieldName == "rfc");
        // Fallback-recovered fields are flagged (never high confidence).
        Assert.All(facts, f => Assert.True(f.Confidence <= ReceiptExtractionPolicy.FallbackConfidenceCap));
    }

    [Fact]
    public void Text_fallback_on_empty_input_returns_nothing()
    {
        Assert.Empty(ReceiptExtractionPolicy.ParseRawText(null, "es-MX"));
        Assert.Empty(ReceiptExtractionPolicy.ParseRawText("   ", "es-MX"));
    }

    [Fact]
    public void Unreadable_structured_json_is_reported_not_fabricated()
    {
        var result = FoundryReceiptOcrProvider.ParseStructured("this is not json");
        Assert.False(result.Readable);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Structured_json_parses_fields_and_readable_flag()
    {
        const string json = """
        {"readable": true, "raw_text": "OXXO ...", "fields": {
            "vendor": {"value": "OXXO", "confidence": 0.95},
            "total": {"value": 123.45, "currency": "MXN", "confidence": 0.9},
            "rfc": {"value": "OXX970814HS9", "confidence": 0.88},
            "date": null
        }}
        """;

        var result = FoundryReceiptOcrProvider.ParseStructured(json);

        Assert.True(result.Readable);
        Assert.Contains(result.Fields, f => f.FieldName == "vendor");
        Assert.Contains(result.Fields, f => f.FieldName == "total" && f.Currency == "MXN");
        Assert.Contains(result.Fields, f => f.FieldName == "rfc");
        // Null field is omitted, not fabricated.
        Assert.DoesNotContain(result.Fields, f => f.FieldName == "date");
    }
}
