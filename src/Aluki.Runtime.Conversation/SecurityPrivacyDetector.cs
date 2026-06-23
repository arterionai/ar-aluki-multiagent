using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Conversation;

/// <summary>
/// Deterministic, accent-insensitive detection of security/privacy questions.
/// When matched, ConversationalResponseAgent returns the fixed SecurityPrivacyResponse
/// directly without calling the LLM — ensuring a consistent, legally-safe answer.
/// </summary>
internal static class SecurityPrivacyDetector
{
    private static readonly string[] Triggers =
    [
        // Spanish
        "seguridad", "privacidad", "confidencial",
        "mis datos", "tus datos", "datos seguros", "datos privados",
        "quien ve", "quién ve", "quien puede ver", "quién puede ver",
        "informacion segura", "información segura",
        "guardan mis", "guardan tu", "guardan la informacion",
        "compartes mis", "compartes mi informacion",
        "vendes mis", "venden mis",
        "borrar mis datos", "eliminar mis datos", "borrar mi informacion",
        "donde guardas", "dónde guardas", "donde se guardan", "dónde se guardan",
        "es seguro", "puedo confiar", "confiable",
        "politica de privacidad", "política de privacidad", "aviso de privacidad",
        "terminos", "términos", "condiciones de uso",
        "hackear", "hackeado", "vulnerabilidad",
        // English
        "privacy", "security", "confidential",
        "my data", "my information", "data safe", "data secure",
        "who can see", "who sees", "who has access",
        "delete my data", "erase my data", "remove my data",
        "where is my data", "where do you store",
        "privacy policy", "terms of service",
        "trust you", "is it safe", "safe to use",
    ];

    public static bool LooksLikeSecurityQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);
        return Triggers.Any(normalized.Contains);
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
