namespace Aluki.Runtime.Abstractions.Skills.Feedback;

/// <summary>
/// Best-effort sink for capturing user suggestions from conversation messages.
/// Never throws — failures are swallowed by the caller.
/// </summary>
public interface IFeedbackCaptureSink
{
    Task TryCaptureAsync(Guid tenantId, Guid userId, string messageId, string messageText, CancellationToken cancellationToken);
}
