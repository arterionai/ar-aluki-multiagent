using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aluki.Runtime.Host.Calendar.Security;

public static partial class TokenRedactionPolicy
{
    private static readonly string[] RedactedKeys =
        ["access_token", "refresh_token", "client_secret", "code", "id_token"];

    public static string Redact(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        // Replace the value of any sensitive field in a single pass, preserving the
        // original field name via the captured key ($1). Looping with one hardcoded
        // key per iteration renamed every matched field to that loop's key.
        return TokenValuePattern().Replace(json, @"""$1"":""[REDACTED]""");
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
