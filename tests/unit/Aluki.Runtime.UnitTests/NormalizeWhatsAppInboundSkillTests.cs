using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Host.Capture;
using Aluki.Runtime.Host.Capture.Skills;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class NormalizeWhatsAppInboundSkillTests
{
    private readonly NormalizeWhatsAppInboundSkill _skill = new();

    [Fact]
    public async Task Normalizes_text_message()
    {
        var state = new CapturePipelineState(CaptureTestData.Principal(), CaptureTestData.TextEnvelope(), "corr-1");

        await _skill.ExecuteAsync(CaptureTestData.Context(state), CancellationToken.None);

        Assert.NotNull(state.Normalized);
        Assert.True(state.Normalized!.IsSupported);
        Assert.Equal(CapturePayloadType.Text, state.Normalized.MessageKind);
        Assert.Equal("hola", state.Normalized.MessageText);
        Assert.Null(state.Normalized.Media);
        Assert.False(state.IsUnsupported);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task Normalizes_media_message(string mediaType)
    {
        var state = new CapturePipelineState(
            CaptureTestData.Principal(),
            CaptureTestData.MediaEnvelope(mediaType),
            "corr-1");

        await _skill.ExecuteAsync(CaptureTestData.Context(state), CancellationToken.None);

        Assert.True(state.Normalized!.IsSupported);
        Assert.Equal(mediaType, state.Normalized.MessageKind);
        Assert.NotNull(state.Normalized.Media);
        Assert.Equal("image/jpeg", state.Normalized.Media!.ContentType);
    }

    [Fact]
    public async Task Flags_unsupported_payload()
    {
        var state = new CapturePipelineState(
            CaptureTestData.Principal(),
            CaptureTestData.UnsupportedEnvelope(),
            "corr-1");

        await _skill.ExecuteAsync(CaptureTestData.Context(state), CancellationToken.None);

        Assert.False(state.Normalized!.IsSupported);
        Assert.Equal(CapturePayloadType.Unsupported, state.Normalized.MessageKind);
        Assert.True(state.IsUnsupported);
        // Raw envelope reference falls back to provider message id when absent.
        Assert.Equal("wamid-unsupported", state.Normalized.RawEnvelopeRef);
    }

    [Fact]
    public async Task Normalizes_forwarded_message()
    {
        var state = new CapturePipelineState(
            CaptureTestData.Principal(),
            CaptureTestData.ForwardedEnvelope(),
            "corr-1");

        await _skill.ExecuteAsync(CaptureTestData.Context(state), CancellationToken.None);

        Assert.Equal(CapturePayloadType.Forwarded, state.Normalized!.MessageKind);
        Assert.Equal("origin-sender", state.Normalized.ForwardedFromRef);
    }
}
