using System.Globalization;
using System.Text;

namespace Aluki.Runtime.Memory.Recall;

/// <summary>
/// SB-002 US3 (T031): deterministically groups recall evidence into coherent
/// topics. Grouping is keyword-based and order-stable so the same evidence set
/// always yields the same labeled groups (no LLM, no nondeterminism). Artifacts
/// are clustered greedily by shared significant keywords; each group is labeled
/// with its most frequent keyword (alphabetical tie-break).
/// </summary>
public sealed class TopicGroupingSkill
{
    // Common Spanish/English stopwords excluded from topic keywords. Short tokens
    // (< 4 chars) are dropped too, which removes most articles/prepositions.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "para", "porque", "pero", "como", "cuando", "donde", "esto", "esta", "este",
        "estos", "estas", "eso", "esa", "unos", "unas", "sobre", "entre", "tengo",
        "tiene", "tienen", "hacer", "haces", "desde", "hasta", "muy", "mas",
        "with", "that", "this", "these", "those", "from", "have", "will", "your",
        "about", "what", "when", "where", "which", "there", "their", "they"
    };

    public IReadOnlyList<TopicGroup> Group(IReadOnlyList<RecallCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var clusters = new List<Cluster>();
        foreach (var candidate in candidates)
        {
            var keywords = ExtractKeywords(candidate.ContentText);
            var target = clusters.FirstOrDefault(c => c.Keywords.Overlaps(keywords));
            if (target is null)
            {
                target = new Cluster();
                clusters.Add(target);
            }

            target.ArtifactIds.Add(candidate.ArtifactId);
            foreach (var keyword in keywords)
            {
                target.Keywords.Add(keyword);
                target.KeywordCounts[keyword] = target.KeywordCounts.GetValueOrDefault(keyword) + 1;
            }
        }

        // Largest groups first, then by label, for a stable, coherent presentation.
        return clusters
            .Select(c => new TopicGroup(Label(c.KeywordCounts), c.ArtifactIds))
            .OrderByDescending(g => g.ArtifactIds.Count)
            .ThenBy(g => g.Topic, StringComparer.Ordinal)
            .ToList();
    }

    private static string Label(IReadOnlyDictionary<string, int> keywordCounts)
    {
        if (keywordCounts.Count == 0)
        {
            return "general";
        }

        return keywordCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    private static HashSet<string> ExtractKeywords(string? text)
    {
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return keywords;
        }

        var token = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                token.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            }
            else
            {
                AddToken(keywords, token);
            }
        }

        AddToken(keywords, token);
        return keywords;
    }

    private static void AddToken(HashSet<string> keywords, StringBuilder token)
    {
        if (token.Length >= 4)
        {
            var word = Normalize(token.ToString());
            if (!StopWords.Contains(word))
            {
                keywords.Add(word);
            }
        }

        token.Clear();
    }

    // Fold accents so "está"/"esta" and "café"/"cafe" cluster together.
    private static string Normalize(string word)
    {
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class Cluster
    {
        public List<Guid> ArtifactIds { get; } = [];
        public HashSet<string> Keywords { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> KeywordCounts { get; } = new(StringComparer.Ordinal);
    }
}
