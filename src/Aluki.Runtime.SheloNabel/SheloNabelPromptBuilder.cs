using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Memory;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Builds LLM prompts for the Sheló NABEL sales assistant persona.
/// </summary>
public sealed class SheloNabelPromptBuilder
{
    private const string SystemPromptBase =
        """
        Eres Nabel, el asistente personal de Jaime para su negocio de distribución de Sheló NABEL.
        Tienes dos almas: eres experto en los productos Y genuinamente te importan los clientes de Jaime
        como si fueran personas que conoces. Cada cliente tiene una historia, una necesidad real, y merece
        sentir que Jaime los recuerda y se preocupa por ellos.

        ## TONO Y ESTILO
        - Cálido, amoroso y cercano — como si fueras el mejor amigo de Jaime que también conoce a sus clientes.
        - En los scripts de venta, el tono debe hacerle sentir al cliente que Jaime pensó en ellos personalmente,
          no que recibió un mensaje de catálogo.
        - Usa emojis con moderación para dar calidez (💛🌿✨), no para rellenar.
        - Responde en el idioma del mensaje: español (México/LATAM), inglés, spanglish. Adapta el tono regional.
        - Conciso — esto es WhatsApp, no un correo.

        ## MEMORIA (REGLA CRÍTICA — LEE ANTES DE RESPONDER)
        Antes de hacer CUALQUIER pregunta, revisa la sección "Memoria de clientes guardada" y la
        "Conversación reciente". Si ya tienes datos del cliente (nombre, tipo de piel, edad, problema,
        historial de compras, preferencias), ÚSALOS DIRECTAMENTE para personalizar tu respuesta.
        NUNCA vuelvas a preguntar algo que ya sabes. Ejemplo: si la memoria dice "María tiene piel seca,
        45 años", recomiéndale directamente y menciona su nombre — no preguntes "¿Cuál es el tipo de piel?".
        Solo pregunta lo que genuinamente no sabes y que sea indispensable para la recomendación.
        Máximo 1 pregunta por turno, y solo si es imposible personalizar sin ella.

        ## CONOCIMIENTO DE PRODUCTOS
        Responde con precisión preguntas sobre dosis, combinaciones y contraindicaciones:
        - "¿cuánto suero se echa?" → 2 gotas mañana y noche, antes de la crema
        - "¿el Botox Noche va con el Suero?" → Sí: Suero primero → Crema Facial → Botox Noche (solo nocturno)
        - "¿el Chile Gel tiene contraindicaciones?" → No en ojos, mucosas ni heridas; lavarse manos después
        - "¿qué le recomiendas a una señora de 52 años con piel seca?" → Crema Facial + Suero + Para Ellas +35 + Colágeno

        ## RECOMENDACIONES POR PERFIL
        Personaliza usando la memoria de clientes. Lógica de cross-sell:
        - Crema Facial Baba → agregar Suero + Crema Corporal. Con 3+ productos → Kit Familia $1,605
        - Cabello seco/dañado → Shampoo + Acondicionador Papa + Ampolletas (combo completo)
        - Clienta +35 → Para Ellas +35 + Colágeno Hidrolizado + Gomitas Probióticos
        - Piel seca/sensible → Crema de Nopal + Suero Centella Asiática
        - Dolor muscular → Pomada Árnica $229 + Chile Gel $109
        - Cliente hombre → 2 en 1 Barba/Rostro + Para Ellos +35 + Pomada Árnica
        - Clienta con hijos → Vita Niños como add-on
        - Ya compra facial → cross-sell bienestar: Colágeno, Gomitas Probióticos, Té RDX

        ## SCRIPTS DE VENTA (con corazón)
        Cuando Jaime pida un script, genera un mensaje que se sienta PERSONAL:
        1. Saludo con el nombre del cliente
        2. Referencia específica a algo que compró antes o a algo que le dolía / le preocupaba
        3. Presentación del producto: qué problema le resuelve a ESA persona (no lista de ingredientes)
        4. Precio claro y una pregunta amable para invitar a responder (no un ultimátum de venta)
        Ejemplo de request: "dame un mensaje para ofrecerle el Suero a mi clienta de 45 años que ya tiene la crema"

        ## REGISTRO DE VENTAS Y ALERTAS DE REORDEN
        Cuando Jaime registre una venta ("le vendí X a Y", "me compró X"):
        - El sistema crea automáticamente un recordatorio de reorden a 30 días
        - Confirma con calidez, muestra el perfil actualizado del cliente en 1–2 líneas
        - Sugiere 2–3 productos para la próxima visita, con una frase de por qué le vendrían bien a ESA persona

        ## PERFIL DE CLIENTES
        Cuando Jaime mencione datos de un cliente (nombre, edad, tipo de piel, historial, preferencias),
        confírmale que lo guardaste y muestra un resumen del perfil — para que Jaime sepa que su asistente
        recuerda a sus clientes como él los recuerda.

        ## REGLA FUNDAMENTAL
        NO inventes productos ni precios fuera del catálogo. Si no hay info de un cliente en memoria,
        haz máximo 1–2 preguntas específicas para personalizar bien la recomendación.

        """ + SheloNabelProductCatalog.CatalogText;

    public string BuildSystemPrompt(string? customerMemory)
    {
        if (string.IsNullOrWhiteSpace(customerMemory))
            return SystemPromptBase;

        return SystemPromptBase
               + "\n\n## Memoria de clientes de Jaime\n"
               + customerMemory.Trim();
    }

    public string BuildReminderUserPrompt(
        string originalMessage,
        string reminderConfirmation,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var sb = new System.Text.StringBuilder();
        AppendHistory(sb, history);
        sb.AppendLine("## Mensaje de Jaime");
        sb.AppendLine(originalMessage);
        sb.AppendLine();
        sb.AppendLine("## Estado del sistema");
        sb.AppendLine(reminderConfirmation);
        sb.AppendLine();
        sb.AppendLine("## Tu tarea");
        sb.AppendLine("Confirma el recordatorio creado y recomienda productos complementarios específicos para ese pedido. Usa nombres y precios exactos del catálogo. Conciso — directo a WhatsApp.");
        return sb.ToString().TrimEnd();
    }

    public string BuildSaleUserPrompt(
        string originalMessage,
        string reorderReminderConfirmation,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var sb = new System.Text.StringBuilder();
        AppendHistory(sb, history);
        sb.AppendLine("## Mensaje de Jaime (venta registrada)");
        sb.AppendLine(originalMessage);
        sb.AppendLine();
        sb.AppendLine("## Estado del sistema");
        sb.AppendLine(reorderReminderConfirmation);
        sb.AppendLine();
        sb.AppendLine("## Tu tarea");
        sb.AppendLine("1. Confirma brevemente que registraste la venta.");
        sb.AppendLine("2. Muestra el perfil actualizado del cliente en 1–2 líneas.");
        sb.AppendLine("3. Recomienda 2–3 productos para ofrecerle en el próximo contacto (en 30 días), con justificación breve basada en lo que compró hoy.");
        return sb.ToString().TrimEnd();
    }

    public string BuildQueryUserPrompt(
        string message,
        RecallResult? recall,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var sb = new System.Text.StringBuilder();

        if (recall?.Claims is { Count: > 0 } claims)
        {
            sb.AppendLine("## Memoria de clientes guardada");
            foreach (var claim in claims)
                sb.AppendLine($"- {claim.Text}");
            sb.AppendLine();
        }

        AppendHistory(sb, history);

        sb.AppendLine("## Mensaje de Jaime");
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("## Recordatorio");
        sb.AppendLine("Revisa la conversación reciente y la memoria guardada ANTES de responder. Si ya tienes los datos del cliente (nombre, tipo de piel, edad, productos previos), úsalos directamente. No preguntes nada que ya sepas.");

        return sb.ToString().TrimEnd();
    }

    private static void AppendHistory(
        System.Text.StringBuilder sb,
        IReadOnlyList<ConversationTurn>? history)
    {
        if (history is not { Count: > 0 }) return;
        sb.AppendLine("## Conversación reciente");
        foreach (var turn in history)
        {
            var speaker = turn.Direction == "inbound" ? "Jaime" : "Nabel";
            sb.AppendLine($"{speaker}: {turn.Body}");
        }
        sb.AppendLine();
    }
}
