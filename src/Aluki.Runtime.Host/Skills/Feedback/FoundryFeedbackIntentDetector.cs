using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.Feedback;

internal sealed class FoundryFeedbackIntentDetector(
    IChatModelRouter chatRouter,
    ILogger<FoundryFeedbackIntentDetector> logger) : IFeedbackIntentDetector
{
    private const string SystemPrompt =
        """
        You detect whether a user message is a product suggestion, feature request, or feedback.
        Respond with ONLY the word YES or NO. No explanation, no punctuation.
        YES = the message proposes an idea, suggests an improvement, requests a feature, or provides constructive feedback.
        NO = the message is a question, command, reminder, conversation, or anything else.
        """;

    public async Task<bool> HasSuggestionIntentAsync(string text, CancellationToken ct)
    {
        try
        {
            using var llmCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var response = await chatRouter.CompleteAsync(SystemPrompt, text, llmCts.Token);
            ct.ThrowIfCancellationRequested();
            return response.Trim().StartsWith("YES", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Feedback intent LLM call failed; falling back to keyword detection");
            return KeywordFallback(text);
        }
    }

    private static bool KeywordFallback(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("suggestion") || lower.Contains("suggest") ||
               lower.Contains("idea") || lower.Contains("feature request") ||
               lower.Contains("feedback") || lower.Contains("would be nice") ||
               lower.Contains("could you add") || lower.Contains("please add") ||
               lower.Contains("sugerencia") || lower.Contains("sugiero") ||
               lower.Contains("sería bueno") || lower.Contains("estaría bien") ||
               lower.Contains("me gustaría que") || lower.Contains("podrías agregar") ||
               lower.Contains("podrías añadir") || lower.Contains("podrías incluir") ||
               lower.Contains("quiero proponer") || lower.Contains("tengo una propuesta") ||
               lower.Contains("solicitud") || lower.Contains("mejora");
    }
}
