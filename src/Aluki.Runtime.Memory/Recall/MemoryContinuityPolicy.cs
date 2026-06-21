namespace Aluki.Runtime.Memory.Recall;

/// <summary>
/// SB-002 US3 (T032): cross-channel memory continuity within a tenant/context.
/// Recall retrieval (<see cref="Persistence.MemoryStore.SearchAsync"/>) is scoped
/// by context only — never by source channel — so evidence captured on any
/// connected channel is eligible for the same query. This policy makes that
/// continuity observable: it reports the distinct channels represented in the
/// evidence and whether a claim is corroborated across more than one channel.
/// </summary>
public static class MemoryContinuityPolicy
{
    /// <summary>Distinct source channels in the evidence, ordered for stable output.</summary>
    public static IReadOnlyList<string> DistinctChannels(IReadOnlyList<RecallCandidate> candidates) =>
        candidates
            .Select(c => string.IsNullOrWhiteSpace(c.SourceChannel)
                ? ChannelFromProvenance(c.ProvenanceRef)
                : c.SourceChannel)
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(channel => channel, StringComparer.Ordinal)
            .ToList();

    /// <summary>True when the evidence spans two or more distinct channels.</summary>
    public static bool IsCrossChannel(IReadOnlyList<RecallCandidate> candidates) =>
        DistinctChannels(candidates).Count >= 2;

    // Provenance refs are "{channel}:{identity}"; fall back to parsing the channel
    // when an older row predates the stored source_channel column.
    private static string ChannelFromProvenance(string provenanceRef)
    {
        if (string.IsNullOrEmpty(provenanceRef))
        {
            return string.Empty;
        }

        var separator = provenanceRef.IndexOf(':');
        return separator > 0 ? provenanceRef[..separator] : string.Empty;
    }
}
