namespace Aluki.Runtime.Conversation;

public sealed class ConversationOptions
{
    public const string SectionName = "Conversation";

    public int HistoryWindowSize { get; set; } = 10;
    public int LlmTimeoutSeconds { get; set; } = 25;
    public string ErrorFallbackMessage { get; set; } =
        "Tuve un problema procesando tu mensaje, inténtalo de nuevo 🙏";
    public string AudioAcknowledgmentMessage { get; set; } =
        "¡Perfecto! Ahora escucho tu audio y en breve te respondo 🎧";
    public string NoMemoryMessageSuffix { get; set; } =
        " Si quieres, compárteme esa información y la guardo para ti.";

    public string OnboardingInstruction { get; set; } =
        """
        This is the user's FIRST message ever. Begin your reply with a brief, warm welcome (2-3 sentences)
        that explains what you can do: remember personal notes and information, answer questions based on
        what the user shares, set reminders, schedule calendar events, save links, and capture suggestions
        or improvements they want to share. After the welcome, naturally transition into answering their
        message. Keep it friendly and concise — WhatsApp style.
        """;
}
