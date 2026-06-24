using System.Globalization;
using System.Text;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Deterministic, accent-insensitive detection of "I just made a sale" intent.
/// Pure (no I/O) — cheap enough to run inside the dispatch hot path.
/// When a sale is detected the agent auto-creates a 30-day follow-up reminder.
/// </summary>
public static class SheloNabelSaleDetector
{
    private static readonly string[] Triggers =
    [
        // Spanish — past tense / completed sale
        "le vendi", "le vendí", "vendi a", "vendí a",
        "me compro", "me compró", "me pidio", "me pidió",
        "hice el pedido", "hice su pedido", "hize el pedido",
        "entregue el pedido", "entregué el pedido",
        "le entregue", "le entregué",
        "cerre la venta", "cerré la venta", "cerre venta", "cerré venta",
        "confirmo el pedido", "confirmó el pedido",
        "hizo su pedido", "hizo el pedido",
        "le surte", "le surté", "ya le surti",
        "ya le di", "ya le mande", "ya le mandé",
        "ya le lleve", "ya le llevé",
        "anoto el pedido", "anotó el pedido",
        "registra la venta", "registra que le vendi",
        // English
        "i sold", "sold to", "she bought", "he bought", "they bought",
        "placed an order", "closed the sale", "made the sale",
    ];

    public static bool LooksLikeSaleRecord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Triggers.Any(Normalize(text).Contains);
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lowered.Length);
        foreach (var c in lowered)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }
}
