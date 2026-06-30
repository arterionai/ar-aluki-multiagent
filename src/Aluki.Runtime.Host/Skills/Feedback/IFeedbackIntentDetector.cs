namespace Aluki.Runtime.Host.Skills.Feedback;

public interface IFeedbackIntentDetector
{
    Task<bool> HasSuggestionIntentAsync(string text, CancellationToken ct);
}
