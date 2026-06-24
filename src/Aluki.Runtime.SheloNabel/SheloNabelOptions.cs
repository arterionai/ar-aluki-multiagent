namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Configuration for the Sheló NABEL sales assistant agent.
/// Bind from the "SheloNabel" app settings section.
/// </summary>
public sealed class SheloNabelOptions
{
    public const string Section = "SheloNabel";

    /// <summary>
    /// Comma-separated list of WhatsApp wa_ids (no '+') authorized to use
    /// the Sheló NABEL agent. Defaults to the two demo numbers.
    /// </summary>
    public string AuthorizedWaIds { get; set; } =
        "14252307522,525528571249";

    internal IReadOnlySet<string> ParsedWaIds()
    {
        var parts = AuthorizedWaIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }
}
