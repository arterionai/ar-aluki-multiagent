using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class MetaWhatsAppWebhookFunction
{
    private const string VerifyTokenSetting = "Meta__VerifyToken";
    private const string AppSecretSetting = "Meta__AppSecret";
    private const string SignatureHeader = "x-hub-signature-256";

    [Function("MetaWhatsAppWebhookVerify")]
    public HttpResponseData Verify(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "webhooks/meta/whatsapp")]
        HttpRequestData request)
    {
        var query = ParseQuery(request.Url.Query);
        query.TryGetValue("hub.mode", out var mode);
        query.TryGetValue("hub.verify_token", out var verifyToken);
        query.TryGetValue("hub.challenge", out var challenge);

        var expectedToken = Environment.GetEnvironmentVariable(VerifyTokenSetting);

        if (!"subscribe".Equals(mode, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(expectedToken) ||
            !string.Equals(verifyToken, expectedToken, StringComparison.Ordinal))
        {
            return request.CreateResponse(HttpStatusCode.Forbidden);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.WriteString(challenge ?? string.Empty);
        return response;
    }

    [Function("MetaWhatsAppWebhookInbound")]
    public async Task<HttpResponseData> InboundAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/meta/whatsapp")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadAllBytesAsync(request.Body, cancellationToken);
        var appSecret = Environment.GetEnvironmentVariable(AppSecretSetting);

        if (!string.IsNullOrWhiteSpace(appSecret))
        {
            if (!TryReadHeader(request, SignatureHeader, out var signatureHeader) ||
                !IsValidMetaSignature(signatureHeader, body, appSecret))
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.WriteString("EVENT_RECEIVED");
        return response;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static bool TryReadHeader(HttpRequestData request, string name, out string value)
    {
        if (request.Headers.TryGetValues(name, out var values))
        {
            var first = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                value = first;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsValidMetaSignature(string signatureHeader, byte[] body, string appSecret)
    {
        const string prefix = "sha256=";

        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedHex = signatureHeader[prefix.Length..];
        if (providedHex.Length != 64)
        {
            return false;
        }

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedHash = hmac.ComputeHash(body);
        return CryptographicOperations.FixedTimeEquals(computedHash, providedHash);
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(queryString))
        {
            return result;
        }

        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
