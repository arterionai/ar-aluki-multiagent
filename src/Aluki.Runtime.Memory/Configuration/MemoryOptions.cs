namespace Aluki.Runtime.Memory.Configuration;

/// <summary>Bound from the <c>Memory</c> configuration section.</summary>
public sealed class MemoryOptions
{
    public const string SectionName = "Memory";

    /// <summary>Max candidates retrieved by vector search.</summary>
    public int RecallTopK { get; set; } = 5;

    /// <summary>
    /// Maximum cosine distance (0 = identical) for a candidate to count as
    /// relevant/corroborating evidence.
    /// </summary>
    public double RelevanceMaxDistance { get; set; } = 0.6;

    /// <summary>
    /// Maximum cosine distance for a note to be soft-deleted by a WhatsApp deletion
    /// command (SB-016). Stricter than recall relevance: deleting demands higher
    /// confidence than reading.
    /// </summary>
    public double DeleteMaxDistance { get; set; } = 0.5;
}
