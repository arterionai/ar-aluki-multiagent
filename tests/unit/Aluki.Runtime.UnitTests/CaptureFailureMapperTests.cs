using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Host.Capture.Failure;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class CaptureFailureMapperTests
{
    [Fact]
    public void Accepted_maps_to_202_ack()
    {
        var outcome = new CaptureOutcome(
            CaptureOutcomeKind.Accepted, "corr-1", "tenant|whatsapp|wamid-1", Guid.NewGuid(), CaptureAuditEvent.Accepted);

        var result = CaptureFailureMapper.Map(outcome);

        Assert.Equal(202, result.StatusCode);
        var ack = Assert.IsType<CaptureAck>(result.Body);
        Assert.Equal(CaptureStatus.Accepted, ack.Status);
        Assert.Equal(CaptureAuditEvent.Accepted, ack.AuditEvent);
    }

    [Fact]
    public void DuplicateSuppressed_maps_to_202_with_duplicate_status()
    {
        var outcome = new CaptureOutcome(
            CaptureOutcomeKind.DuplicateSuppressed, "corr-1", "k", Guid.NewGuid(), CaptureAuditEvent.DuplicateSuppressed);

        var result = CaptureFailureMapper.Map(outcome);

        Assert.Equal(202, result.StatusCode);
        var ack = Assert.IsType<CaptureAck>(result.Body);
        Assert.Equal(CaptureStatus.DuplicateSuppressed, ack.Status);
    }

    [Fact]
    public void Unsupported_maps_to_202_accepted_unsupported()
    {
        var outcome = new CaptureOutcome(
            CaptureOutcomeKind.AcceptedUnsupported, "corr-1", "k", Guid.NewGuid(), CaptureAuditEvent.UnsupportedPayload);

        var result = CaptureFailureMapper.Map(outcome);

        var ack = Assert.IsType<CaptureAck>(result.Body);
        Assert.Equal(CaptureStatus.AcceptedUnsupported, ack.Status);
    }

    [Fact]
    public void ScopeDenied_maps_to_403_error()
    {
        var outcome = new CaptureOutcome(
            CaptureOutcomeKind.ScopeDenied, "corr-1", ErrorCode: CaptureErrorCode.ScopeDenied, Message: "denied");

        var result = CaptureFailureMapper.Map(outcome);

        Assert.Equal(403, result.StatusCode);
        var error = Assert.IsType<CaptureError>(result.Body);
        Assert.Equal(CaptureErrorCode.ScopeDenied, error.Code);
        Assert.Equal(CaptureAuditEvent.ScopeDenied, error.AuditEvent);
    }

    [Fact]
    public void InvalidPayload_maps_to_400_error()
    {
        var outcome = new CaptureOutcome(CaptureOutcomeKind.InvalidPayload, "corr-1", Message: "bad");

        var result = CaptureFailureMapper.Map(outcome);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<CaptureError>(result.Body);
        Assert.Equal(CaptureErrorCode.InvalidPayload, error.Code);
    }

    [Fact]
    public void RetryExhausted_maps_to_500_failed_terminal()
    {
        var outcome = new CaptureOutcome(
            CaptureOutcomeKind.RetryExhausted,
            "corr-1",
            ErrorCode: CaptureErrorCode.RetryExhausted,
            Message: "exhausted",
            AttemptCount: 5,
            FailureCategory: "transient");

        var result = CaptureFailureMapper.Map(outcome);

        Assert.Equal(500, result.StatusCode);
        var error = Assert.IsType<CaptureError>(result.Body);
        Assert.Equal(CaptureStatus.FailedTerminal, error.Status);
        Assert.Equal(CaptureErrorCode.RetryExhausted, error.Code);
        Assert.Equal(CaptureAuditEvent.FailedTerminal, error.AuditEvent);
    }
}
