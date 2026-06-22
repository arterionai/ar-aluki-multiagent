using System.Net;
using System.Text.RegularExpressions;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

public sealed record EnrichmentFetchResult(string? Title, string? SiteName, string Description);

public sealed class LinkEnrichmentRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    private readonly IHttpClientFactory _httpClientFactory;

    public LinkEnrichmentRunner(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public async Task<EnrichmentFetchResult?> FetchAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        try
        {
            var client = _httpClientFactory.CreateClient("link-enrichment");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);

            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(cts.Token);

            var title = ExtractTitle(html);
            var description = ExtractMetaDescription(html);

            return new EnrichmentFetchResult(
                Title: title,
                SiteName: null,  // og:site_name not extracted in this pass
                Description: description ?? title ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTitle(string html)
    {
        var start = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var gt = html.IndexOf('>', start);
        if (gt < 0) return null;
        var end = html.IndexOf("</title>", gt, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        var text = html[(gt + 1)..end].Trim();
        return string.IsNullOrEmpty(text) ? null : WebUtility.HtmlDecode(text);
    }

    private static string? ExtractMetaDescription(string html)
    {
        // Look for <meta name="description" content="..."> in any attribute order
        var searchFrom = 0;
        while (true)
        {
            var metaStart = html.IndexOf("<meta", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (metaStart < 0) return null;

            var metaEnd = html.IndexOf('>', metaStart);
            if (metaEnd < 0) return null;

            var tag = html[metaStart..(metaEnd + 1)];

            if (tag.Contains("name=\"description\"", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("name='description'", StringComparison.OrdinalIgnoreCase))
            {
                var content = ExtractAttributeValue(tag, "content");
                if (content is not null)
                    return WebUtility.HtmlDecode(content);
            }

            searchFrom = metaEnd + 1;
        }
    }

    private static string? ExtractAttributeValue(string tag, string attributeName)
    {
        // Match content="..." or content='...'
        var pattern = attributeName + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')";
        var match = Regex.Match(tag, pattern, RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
