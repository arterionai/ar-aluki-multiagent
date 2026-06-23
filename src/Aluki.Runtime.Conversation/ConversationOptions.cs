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

    public string SecurityPrivacyResponse { get; set; } =
        """
        🔒 Tus datos están guardados en la nube de Microsoft Azure, en una base de datos privada que solo tú puedes ver. Nadie más tiene acceso a tus notas, recordatorios o conversaciones.

        Tus datos *no se comparten con terceros* ni se usan para entrenar modelos de IA. Si conectaste Google u Outlook, los permisos se guardan cifrados y solo se usan cuando tú pides crear un evento.

        Aluki está en beta privada, operada por Arterion AI. Antes del lanzamiento público publicaremos una política de privacidad completa.

        Si en algún momento quieres que eliminemos toda tu información, solo pídenoslo y lo hacemos. 🙏
        """;


        """
        This is the user's FIRST message ever. Begin your reply with a brief, warm welcome that:
        1. Introduces yourself as Aluki and mentions you are in a testing/beta phase — the team wants real
           people trying you out to make you better.
        2. Explains in 2-3 lines what you can do: remember personal notes and information, answer questions
           based on what the user shares, set reminders, and save links.
        3. Tells them how to give feedback or suggestions: they can just write naturally, for example
           "tengo una sugerencia: ..." or "sería bueno que..." and you will capture it automatically.
        4. Encourages them to share Aluki with friends or family who might find it useful — the more people
           try it, the better it gets.
        After the welcome, naturally transition into answering their message.
        Keep it friendly, warm and concise — WhatsApp style, use emojis sparingly.
        """;
}
