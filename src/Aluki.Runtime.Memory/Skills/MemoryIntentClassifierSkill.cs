using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aluki.Runtime.Memory.Skills;

/// <summary>
/// Splits intent between storing a note and asking a recall query. Primary path
/// is an Azure (Foundry model-router) classification; a deterministic heuristic
/// serves as a fast, resilient fallback when the model is unavailable/ambiguous.
/// </summary>
public sealed class MemoryIntentClassifierSkill
{
    private const string SystemPrompt =
        "You classify a user's personal-assistant message. Reply with exactly one word: " +
        "NOTE if the user is stating/storing information, or QUERY if the user is asking to " +
        "recall previously stored information. Only output NOTE or QUERY.";

    private static readonly string[] QuestionStarters =
    [
        "que", "qué", "cual", "cuál", "cuales", "cuáles", "como", "cómo",
        "cuando", "cuándo", "donde", "dónde", "quien", "quién", "quienes", "quiénes",
        "por que", "por qué", "recuerda", "recuerdas", "sabes",
        "what", "how", "when", "where", "who", "which", "why",
        "do i", "did i", "have i", "is there", "are there"
    ];

    private readonly IChatModelRouter? _router;
    private readonly ILogger _logger;

    public MemoryIntentClassifierSkill(IChatModelRouter? router = null, ILogger<MemoryIntentClassifierSkill>? logger = null)
    {
        _router = router;
        _logger = logger ?? NullLogger<MemoryIntentClassifierSkill>.Instance;
    }

    /// <summary>Model-first classification with deterministic fallback.</summary>
    public async Task<string> ClassifyAsync(string? inputText, CancellationToken cancellationToken)
    {
        var text = (inputText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return MemoryIntent.NoteToStore;
        }

        if (_router is not null)
        {
            try
            {
                var answer = (await _router.CompleteAsync(SystemPrompt, text, cancellationToken) ?? string.Empty)
                    .Trim().ToUpperInvariant();
                if (answer.Contains("QUERY", StringComparison.Ordinal))
                {
                    return MemoryIntent.RecallQuery;
                }

                if (answer.Contains("NOTE", StringComparison.Ordinal))
                {
                    return MemoryIntent.NoteToStore;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Intent classification via model-router failed; falling back to heuristic.");
            }
        }

        return Classify(text);
    }

    /// <summary>Deterministic heuristic classification (fallback + offline tests).</summary>
    public string Classify(string? inputText)
    {
        var text = (inputText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return MemoryIntent.NoteToStore;
        }

        if (text.EndsWith('?') || text.EndsWith('?'))
        {
            return MemoryIntent.RecallQuery;
        }

        var lower = text.ToLowerInvariant();
        foreach (var starter in QuestionStarters)
        {
            if (lower == starter || lower.StartsWith(starter + " ", StringComparison.Ordinal))
            {
                return MemoryIntent.RecallQuery;
            }
        }

        return MemoryIntent.NoteToStore;
    }
}
