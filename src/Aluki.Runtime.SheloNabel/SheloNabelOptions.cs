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
    /// Mexican mobile numbers appear in two formats in Meta's API:
    ///   52XXXXXXXXXX  (current, 12 digits) or
    ///   521XXXXXXXXX  (legacy pre-2019, 13 digits — the extra '1' after country code)
    /// Both variants are included so either format is accepted.
    /// </summary>
    public string AuthorizedWaIds { get; set; } =
        "14252307522,525528571249,5215528571249";

    internal IReadOnlySet<string> ParsedWaIds()
    {
        var parts = AuthorizedWaIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }
}
