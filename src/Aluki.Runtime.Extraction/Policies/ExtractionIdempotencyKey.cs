using System.Security.Cryptography;
using System.Text;

namespace Aluki.Runtime.Extraction.Policies;

/// <summary>
/// Derives the stable idempotency key for an extraction submission so retries of
/// the same request resolve to the same job (FR-010). When the caller supplies
/// an explicit <c>extraction_id</c> it is used verbatim; otherwise the key is a
/// content hash over the input modality + payload digest.
/// </summary>
public static class ExtractionIdempotencyKey
{
    public static string Derive(string? explicitExtractionId, string inputType, string payloadDigestSource)
    {
        if (!string.IsNullOrWhiteSpace(explicitExtractionId))
        {
            return explicitExtractionId!.Trim();
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{inputType}|{payloadDigestSource}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
