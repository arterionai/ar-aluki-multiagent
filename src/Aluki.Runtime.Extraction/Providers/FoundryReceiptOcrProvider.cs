using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;

namespace Aluki.Runtime.Extraction.Providers;

/// <summary>
/// Receipt OCR via an Azure vision-capable chat deployment (Azure AI Foundry
/// model-router / Azure OpenAI; all inference stays on Azure per the runtime
/// directive). The primary call requests strict JSON fiscal fields with
/// per-field confidence; the secondary call requests a plain text transcription
/// for the text-only fallback path. The prompt forbids inventing values: missing
/// fields are returned as null rather than guessed.
/// Configuration: <c>Extraction:ReceiptOcr:Endpoint</c> / <c>:ApiKey</c> /
/// <c>:Deployment</c>, falling back to <c>Foundry:Endpoint</c> / <c>:ApiKey</c>
/// and <c>Foundry:VisionDeployment</c> ⇒ <c>Foundry:ChatDeployment</c>.
/// </summary>
public sealed class FoundryReceiptOcrProvider : IReceiptOcrProvider
{
    private const string StructuredSystemPrompt =
        "You are an OCR system for Mexican receipts (tickets and facturas). " +
        "Read the receipt in the image and return ONLY a JSON object, no prose, with this shape: " +
        "{\"readable\": boolean, \"raw_text\": string, \"fields\": {" +
        "\"vendor\": {\"value\": string, \"confidence\": number}|null, " +
        "\"total\": {\"value\": number, \"currency\": string|null, \"confidence\": number}|null, " +
        "\"subtotal\": {\"value\": number, \"currency\": string|null, \"confidence\": number}|null, " +
        "\"tax\": {\"value\": number, \"currency\": string|null, \"confidence\": number}|null, " +
        "\"date\": {\"value\": string, \"confidence\": number}|null, " +
        "\"rfc\": {\"value\": string, \"confidence\": number}|null}}. " +
        "Set readable=false only if the image is not a legible receipt. " +
        "confidence is a number in [0,1]. Never invent values: if a field is not " +
        "visible, use null. raw_text must contain the receipt text you read. Output valid JSON only.";

    private const string RawTextSystemPrompt =
        "You are an OCR system. Transcribe ALL legible text visible in the image, " +
        "preserving line breaks. Output only the raw transcribed text with no commentary. " +
        "If nothing is legible, output nothing.";

    private static readonly ModelInfo OcrModelInfo = new("Azure.AI.Foundry", "model-router", "vision-v1");

    private readonly string _deployment;
    private readonly AzureOpenAIClient _client;
    private readonly ILogger _logger;

    public FoundryReceiptOcrProvider(
        IConfiguration configuration,
        ILogger<FoundryReceiptOcrProvider>? logger = null)
    {
        var endpoint = configuration["Extraction:ReceiptOcr:Endpoint"]
            ?? configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Extraction:ReceiptOcr:Endpoint (or Foundry:Endpoint) is not configured.");
        var apiKey = configuration["Extraction:ReceiptOcr:ApiKey"]
            ?? configuration["Foundry:ApiKey"]
            ?? throw new InvalidOperationException("Extraction:ReceiptOcr:ApiKey (or Foundry:ApiKey) is not configured.");
        _deployment = configuration["Extraction:ReceiptOcr:Deployment"]
            ?? configuration["Foundry:VisionDeployment"]
            ?? configuration["Foundry:ChatDeployment"]
            ?? "model-router";
        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _logger = logger ?? NullLogger<FoundryReceiptOcrProvider>.Instance;
    }

    public async Task<ReceiptOcrResult> ExtractStructuredAsync(
        byte[] image, string mediaType, string? languageHint, CancellationToken cancellationToken)
    {
        var userPrompt = string.IsNullOrWhiteSpace(languageHint)
            ? "Extract the fiscal fields from this receipt."
            : $"Extract the fiscal fields from this receipt. Primary language hint: {languageHint}.";
        var raw = await CompleteVisionAsync(StructuredSystemPrompt, userPrompt, image, mediaType, cancellationToken);
        return ParseStructured(raw, _logger);
    }

    public async Task<string?> ExtractRawTextAsync(byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        var raw = await CompleteVisionAsync(RawTextSystemPrompt, "Transcribe this image.", image, mediaType, cancellationToken);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private async Task<string> CompleteVisionAsync(
        string systemPrompt, string userText, byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        var chat = _client.GetChatClient(_deployment);
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(userText),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(image), mediaType))
        };

        var response = await chat.CompleteChatAsync(messages, new ChatCompletionOptions(), cancellationToken);
        var parts = response.Value.Content;
        return parts.Count > 0 ? parts[0].Text : string.Empty;
    }

    /// <summary>
    /// Parses the structured OCR JSON envelope into a <see cref="ReceiptOcrResult"/>.
    /// Tolerant of surrounding prose/code fences; unparseable output is reported as
    /// unreadable rather than fabricating fields. Public for unit testing.
    /// </summary>
    public static ReceiptOcrResult ParseStructured(string? rawJson, ILogger? logger = null)
    {
        var json = ExtractJsonObject(rawJson);
        if (json is null)
        {
            return new ReceiptOcrResult(false, null, Array.Empty<ReceiptFieldCandidate>(), OcrModelInfo);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var readable = !root.TryGetProperty("readable", out var r) || r.ValueKind != JsonValueKind.False;
            var rawText = root.TryGetProperty("raw_text", out var rt) && rt.ValueKind == JsonValueKind.String
                ? rt.GetString()
                : null;

            var candidates = new List<ReceiptFieldCandidate>();
            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                AddText(fields, "vendor", candidates);
                AddAmount(fields, "total", candidates);
                AddAmount(fields, "subtotal", candidates);
                AddAmount(fields, "tax", candidates);
                AddText(fields, "date", candidates);
                AddText(fields, "rfc", candidates);
            }

            return new ReceiptOcrResult(readable, rawText, candidates, OcrModelInfo);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Receipt OCR JSON parse failed; treating as unreadable.");
            return new ReceiptOcrResult(false, null, Array.Empty<ReceiptFieldCandidate>(), OcrModelInfo);
        }
    }

    private static void AddText(JsonElement fields, string name, List<ReceiptFieldCandidate> candidates)
    {
        if (!fields.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var value = field.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        candidates.Add(new ReceiptFieldCandidate(name, "text", value, Confidence(field)));
    }

    private static void AddAmount(JsonElement fields, string name, List<ReceiptFieldCandidate> candidates)
    {
        if (!fields.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!field.TryGetProperty("value", out var v) ||
            v.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
        {
            return;
        }

        object? value = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : v.GetString();
        var currency = field.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;
        candidates.Add(new ReceiptFieldCandidate(name, "amount", value, Confidence(field), currency));
    }

    private static double Confidence(JsonElement field)
    {
        if (field.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number)
        {
            return Math.Clamp(c.GetDouble(), 0.0, 1.0);
        }

        return 0.75;
    }

    private static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start < 0 || end <= start ? null : raw.Substring(start, end - start + 1);
    }
}
