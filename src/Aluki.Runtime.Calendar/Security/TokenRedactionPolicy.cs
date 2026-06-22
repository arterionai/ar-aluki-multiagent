using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aluki.Runtime.Calendar.Security;

public static partial class TokenRedactionPolicy
{
    private static readonly string[] RedactedKeys =
        ["access_token", "refresh_token", "client_secret", "code", "id_token"];

    public static string Redact(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        var result = json;
        foreach (var key in RedactedKeys)
            result = TokenValuePattern().Replace(result, $@"""{key}"":""[REDACTED]""");
        return result;
    }

    public static Dictionary<string, object?> RedactDictionary(Dictionary<string, object?> payload)
    {
        var copy = new Dictionary<string, object?>(payload, StringComparer.OrdinalIgnoreCase);
        foreach (var key in RedactedKeys)
        {
            if (copy.ContainsKey(key))
                copy[key] = "[REDACTED]";
        }
        return copy;
    }

    public static string SerializeRedacted(object? payload)
    {
        if (payload is null) return "{}";
        var json = JsonSerializer.Serialize(payload);
        return Redact(json);
    }

    [GeneratedRegex(@"""(access_token|refresh_token|client_secret|code|id_token)""\s*:\s*""[^""]*""")]
    private static partial Regex TokenValuePattern();
}
