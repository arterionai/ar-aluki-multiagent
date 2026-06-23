using Aluki.Runtime.Abstractions.Skills.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.Feedback;

/// <summary>
/// Singleton adapter that resolves a scoped <see cref="FeedbackCaptureService"/>
/// per call so the sink can be injected into singleton domain agents.
/// </summary>
public sealed class FeedbackCaptureSink : IFeedbackCaptureSink
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeedbackCaptureSink> _logger;

    public FeedbackCaptureSink(IServiceScopeFactory scopeFactory, ILogger<FeedbackCaptureSink> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task TryCaptureAsync(Guid tenantId, Guid userId, string messageId, string messageText, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<FeedbackCaptureService>();
            await service.CaptureAsync(
                new CaptureSuggestionRequest(tenantId, userId, messageId, messageText),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feedback capture failed. message_id={MessageId}", messageId);
        }
    }
}
