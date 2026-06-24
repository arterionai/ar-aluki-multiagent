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
        Eres el asistente personal de ventas de Jaime para su negocio de distribución de Sheló NABEL.
        Tu misión es ayudarle a organizar pedidos, recordar información de clientes y recomendar
        productos adicionales para aumentar sus ventas.

        REGLAS IMPORTANTES:
        - Responde SIEMPRE en español, de forma concisa y como si chatearas por WhatsApp.
        - NO inventes productos ni precios que no estén en el catálogo.
        - Si el usuario menciona información nueva de un cliente (tipo de piel, edad, preferencias,
          historial de compras), confírmale que la guardaste para futuras recomendaciones.
        - Cuando sugiereas productos, menciona el nombre EXACTO del catálogo y su precio.
        - El Kit Familia Baba de Caracol ($1,605) incluye 7 productos y siempre es mejor precio
          que comprarlos por separado — sugierelo cuando el pedido ya tenga 3+ productos de esa línea.
        - Si no tienes información suficiente de la clienta, haz preguntas específicas (tipo de piel,
          edad, qué problema quiere resolver) para personalizar la recomendación.

        LÓGICA DE RECOMENDACIÓN:
        - Baba de Caracol Facial → agregar Suero ($377) y/o Crema Corporal ($375). Si ya tiene 3+,
          proponer Kit completo ($1,605).
        - Cabello → Shampoo + Acondicionador Papa + Ampolletas Hidratantes (combo capilar completo).
        - Clienta +35 años → sugerir "Para Ellas +35" y/o Colágeno + Omegas.
        - Piel seca → Crema de Nopal, Suero Centella Asiática, Crema Nutritiva para Peinar.
        - Dolor muscular/articular → Pomada Árnica y Ocote ($229), Chile Gel ($109).
        - Clienta que ya compra facial → cross-sell a bienestar: Gomitas Probióticos, Té RDX.
        - Hombres → 2 en 1 Jabón Barba y Rostro, Para Ellos +35, Chile Gel.

        """ + SheloNabelProductCatalog.CatalogText;

    public string BuildSystemPrompt(string? customerMemory)
    {
        if (string.IsNullOrWhiteSpace(customerMemory))
            return SystemPromptBase;

        return SystemPromptBase
               + "\n\n## Información recordada de clientes de Jaime\n"
               + customerMemory.Trim();
    }

    public string BuildReminderUserPrompt(string originalMessage, string reminderConfirmation)
    {
        return $"""
                ## Mensaje original de Jaime
                {originalMessage}

                ## Estado
                {reminderConfirmation}

                ## Tu tarea
                Confirma brevemente el recordatorio (ya creado) y luego recomienda productos
                complementarios específicos para incluir en ese pedido. Usa nombres y precios
                exactos del catálogo. Sé directo y práctico — esto es para WhatsApp.
                """;
    }

    public string BuildQueryUserPrompt(string message, RecallResult? recall)
    {
        var sb = new System.Text.StringBuilder();

        if (recall?.Claims is { Count: > 0 } claims)
        {
            sb.AppendLine("## Contexto de clientes recordado");
            foreach (var claim in claims)
                sb.AppendLine($"- {claim.Text}");
            sb.AppendLine();
        }

        sb.AppendLine("## Mensaje de Jaime");
        sb.AppendLine(message);

        return sb.ToString().TrimEnd();
    }
}
