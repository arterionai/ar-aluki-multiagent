using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Memory;

namespace Aluki.Runtime.Conversation;

/// <summary>
/// Constructs the system and user prompts for the conversational LLM call,
/// grounding the response in retrieved memory claims and conversation history.
/// </summary>
public sealed class ConversationPromptBuilder
{
    private const string SystemPromptTemplate =
        """
        You are Aluki, a warm and helpful personal AI assistant. You communicate in the same
        language the user uses. You must ONLY answer based on the memory context and conversation
        history provided below — never invent facts that are not present in the context.
        If the context does not contain enough information to answer, say so honestly and offer
        to remember the information if the user shares it.
        Keep your replies concise and conversational, as if chatting over WhatsApp.
        """;

    public string BuildSystemPrompt(string? onboardingInstruction = null)
    {
        if (string.IsNullOrWhiteSpace(onboardingInstruction))
            return SystemPromptTemplate;

        return SystemPromptTemplate + "\n\n## Onboarding\n" + onboardingInstruction.Trim();
    }

    public string BuildUserPrompt(
        string userMessage,
        IReadOnlyList<ConversationTurn> history,
        RecallResult? recall)
    {
        var sb = new System.Text.StringBuilder();

        // --- Memory context ---
        if (recall?.Claims is { Count: > 0 } claims)
        {
            sb.AppendLine("## Memory context");
            foreach (var claim in claims)
            {
                sb.AppendLine($"- {claim.Text}");
            }

            sb.AppendLine();
        }

        // --- Conversation history ---
        if (history.Count > 0)
        {
            sb.AppendLine("## Recent conversation");
            foreach (var turn in history)
            {
                var speaker = turn.Direction == "inbound" ? "User" : "Aluki";
                sb.AppendLine($"{speaker}: {turn.Body}");
            }

            sb.AppendLine();
        }

        // --- Current user message ---
        sb.AppendLine("## Current message");
        sb.AppendLine(userMessage);

        return sb.ToString().TrimEnd();
    }
}
