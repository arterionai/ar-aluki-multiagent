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
}
