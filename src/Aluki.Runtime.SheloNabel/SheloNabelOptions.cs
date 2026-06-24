namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Configuration for the Sheló NABEL sales assistant agent.
/// Bind from the "SheloNabel" app settings section.
/// </summary>
public sealed class SheloNabelOptions
{
    public const string Section = "SheloNabel";

    /// <summary>
    /// Fixed UUID of the Sheló NABEL ORGANIZATION tenant (seeded in migration 026).
    /// Any user with a membership in this tenant is authorized to use the agent,
    /// regardless of how they arrived (wa_id list or phone_number_id channel routing).
    /// Can be overridden via SheloNabel:OrgTenantId app setting.
    /// </summary>
    public string OrgTenantId { get; set; } = "c0c0c0c0-5e10-4000-a000-000000000001";

    /// <summary>
    /// Comma-separated list of WhatsApp wa_ids (no '+') that are authorized
    /// regardless of DB membership. Used as a fallback before phone_number_id
    /// channel routing is set up, and as a bootstrap for the seed numbers.
    /// Mexican mobile numbers appear in two formats in Meta's API:
    ///   52XXXXXXXXXX  (current, 12 digits) or
    ///   521XXXXXXXXX  (legacy pre-2019, 13 digits — the extra '1' after country code)
    /// Both variants are included so either format is accepted.
    /// </summary>
    public string AuthorizedWaIds { get; set; } =
        "14252307522,525528571249,5215528571249";

    internal Guid ParsedOrgTenantId() =>
        Guid.TryParse(OrgTenantId, out var id) ? id : Guid.Empty;

    internal IReadOnlySet<string> ParsedWaIds()
    {
        var parts = AuthorizedWaIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }
}
