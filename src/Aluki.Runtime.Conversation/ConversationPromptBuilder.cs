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

        IMPORTANT — capabilities and how to decline:
        - You have NO internet access, NO ability to search the web, look up songs, find articles,
          check prices, or access any external service. When a user asks you to search or find
          something external, say clearly that you cannot browse the internet or search external
          sources — do NOT say you lack "memory" or "space", as that confuses users into thinking
          you have a storage problem.
        - You CAN remember personal notes and information the user shares with you directly in chat.
        - When you lack context to answer, invite the user to share that information so you can
          save it for future reference — but only after making clear WHY you cannot answer now
          (i.e., no internet access, not a memory/storage limitation).
        - NEVER use the word "memoria" (or "memory" in English) when explaining that you cannot
          search the internet. Use phrases like "no tengo acceso a internet" or "no puedo buscar
          fuera de lo que me compartes".

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
