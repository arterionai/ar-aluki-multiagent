using Aluki.Runtime.Abstractions.Skills.Feedback;

namespace Aluki.Runtime.Conversation;

internal sealed class NoOpFeedbackCaptureSink : IFeedbackCaptureSink
{
    public Task TryCaptureAsync(Guid tenantId, Guid userId, string messageId, string messageText, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
