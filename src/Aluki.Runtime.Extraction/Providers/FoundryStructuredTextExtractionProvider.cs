using System.Text.Json;
using Aluki.Runtime.Extraction.Policies;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aluki.Runtime.Extraction.Providers;

/// <summary>
/// Structured text extraction via the Azure AI Foundry model-router. Prompts the
/// model for strict JSON (summary + action items + decisions + entities +
/// amount/date facts) with per-item confidence, then projects it into
/// <see cref="ExtractedFact"/> values. Strictly grounded: the prompt forbids
/// inventing facts; uncertain items must be returned with low confidence rather
/// than omitted-with-false-certainty.
/// </summary>
public sealed class FoundryStructuredTextExtractionProvider : IStructuredTextExtractionProvider
{
    private const string SystemPrompt =
        "You extract structured information from a user's note or transcription. " +
        "Return ONLY a JSON object, no prose, with this shape: " +
        "{\"summary\": string, " +
        "\"action_items\": [{\"action\": string, \"owner\": string|null, \"due_date\": string|null, \"priority\": \"high\"|\"medium\"|\"low\"|null, \"confidence\": number}], " +
        "\"decisions\": [{\"statement\": string, \"confidence\": number}], " +
        "\"entities\": [{\"name\": string, \"entity_type\": \"person\"|\"organization\"|\"location\"|\"product\"|\"event\", \"confidence\": number}], " +
        "\"amounts\": [{\"value\": number, \"currency\": string|null, \"confidence\": number}], " +
        "\"dates\": [{\"value\": string, \"confidence\": number}]}. " +
        "confidence is a number in [0,1] reflecting how certain the extraction is. " +
        "Never invent facts: if something is unclear, include it with a low confidence (<0.70). " +
        "If a list has no items, return an empty array. Output valid JSON only.";

    private static readonly ModelInfo RouterModelInfo = new("Azure.AI.Foundry", "model-router", "v1");

    private readonly IChatModelRouter _router;
    private readonly ILogger _logger;

    public FoundryStructuredTextExtractionProvider(
        IChatModelRouter router,
        ILogger<FoundryStructuredTextExtractionProvider>? logger = null)
    {
        _router = router;
        _logger = logger ?? NullLogger<FoundryStructuredTextExtractionProvider>.Instance;
    }

    public async Task<StructuredExtractionOutput> ExtractAsync(
        string text,
        LanguageResolution language,
        CancellationToken cancellationToken)
    {
        var raw = await _router.CompleteAsync(SystemPrompt, text, cancellationToken);
        return Parse(raw, language.DetectedLanguage, _logger);
    }

    /// <summary>
    /// Parses the model's JSON envelope into structured facts. Tolerant of
    /// surrounding prose/code fences; on unparseable output returns an empty
    /// extraction with the raw text as a low-confidence summary fact rather than
    /// fabricating structure.
    /// </summary>
    public static StructuredExtractionOutput Parse(string? rawJson, string detectedLanguage, ILogger? logger = null)
    {
        var json = ExtractJsonObject(rawJson);
        if (json is null)
        {
            return new StructuredExtractionOutput(string.Empty, Array.Empty<ExtractedFact>(), RouterModelInfo);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var facts = new List<ExtractedFact>();

            var summary = GetString(root, "summary") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                facts.Add(new ExtractedFact("summary", ExtractionFieldType.Text, summary, 0.9, detectedLanguage));
            }

            if (root.TryGetProperty("action_items", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in actions.EnumerateArray())
                {
                    var action = GetString(item, "action");
                    if (string.IsNullOrWhiteSpace(action))
                    {
                        continue;
                    }

                    facts.Add(new ExtractedFact(
                        "action_item",
                        ExtractionFieldType.Text,
                        new
                        {
                            action,
                            owner = GetString(item, "owner"),
                            due_date = GetString(item, "due_date"),
                            priority = GetString(item, "priority")
                        },
                        GetConfidence(item),
                        detectedLanguage));
                }
            }

            AddSimpleArray(root, "decisions", "statement", ExtractionFieldType.DecisionItem, "decision", detectedLanguage, facts);
            AddSimpleArray(root, "entities", "name", ExtractionFieldType.Entity, "entity", detectedLanguage, facts);

            if (root.TryGetProperty("amounts", out var amounts) && amounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in amounts.EnumerateArray())
                {
                    if (!item.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    facts.Add(new ExtractedFact(
                        "amount",
                        ExtractionFieldType.Amount,
                        new { value = v.GetDouble(), currency = GetString(item, "currency") },
                        GetConfidence(item),
                        detectedLanguage));
                }
            }

            AddSimpleArray(root, "dates", "value", ExtractionFieldType.Date, "date", detectedLanguage, facts);

            return new StructuredExtractionOutput(summary, facts, RouterModelInfo);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Structured extraction JSON parse failed; returning empty extraction.");
            return new StructuredExtractionOutput(string.Empty, Array.Empty<ExtractedFact>(), RouterModelInfo);
        }
    }

    private static void AddSimpleArray(
        JsonElement root,
        string arrayName,
        string valueProp,
        string fieldType,
        string fieldName,
        string detectedLanguage,
        List<ExtractedFact> facts)
    {
        if (!root.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            var value = GetString(item, valueProp);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // Carry the whole item object for entities (type/name) and the plain
            // value otherwise.
            object? payload = fieldType == ExtractionFieldType.Entity
                ? new { name = value, entity_type = GetString(item, "entity_type") }
                : value;

            facts.Add(new ExtractedFact(fieldName, fieldType, payload, GetConfidence(item), detectedLanguage));
        }
    }

    private static double GetConfidence(JsonElement item)
    {
        if (item.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number)
        {
            return Math.Clamp(c.GetDouble(), 0.0, 1.0);
        }

        // Absent confidence is treated conservatively as medium.
        return 0.75;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        return null;
    }

    /// <summary>Extracts the first balanced JSON object from a model response.</summary>
    private static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return raw.Substring(start, end - start + 1);
    }
}
