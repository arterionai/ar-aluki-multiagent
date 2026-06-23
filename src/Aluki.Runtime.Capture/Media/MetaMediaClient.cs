using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Capture.Media;

/// <summary>
/// Downloads WhatsApp media via the Meta Graph API: resolve the media id to a
/// short-lived URL, then fetch the binary. Both calls require the bearer token.
/// </summary>
public sealed class MetaMediaClient : IMetaMediaClient
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    private readonly string _graphBaseUrl;

    public MetaMediaClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _accessToken = configuration["Meta:AccessToken"] ?? string.Empty;
        _graphBaseUrl = (configuration["Meta:GraphBaseUrl"] ?? "https://graph.facebook.com/v21.0").TrimEnd('/');
    }

    public async Task<MetaMediaContent> DownloadAsync(string providerMediaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            throw new InvalidOperationException("Meta access token is not configured (Meta:AccessToken).");
        }

        // 1) Resolve the media id to a temporary download URL.
        using var metaRequest = new HttpRequestMessage(HttpMethod.Get, $"{_graphBaseUrl}/{providerMediaId}");
        metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var metaResponse = await _http.SendAsync(metaRequest, cancellationToken);
        metaResponse.EnsureSuccessStatusCode();

        await using var metaStream = await metaResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var metadata = await JsonDocument.ParseAsync(metaStream, cancellationToken: cancellationToken);
        var root = metadata.RootElement;

        var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException($"Meta media metadata did not include a url for media {providerMediaId}.");
        }

        var mimeType = root.TryGetProperty("mime_type", out var m) ? m.GetString() : null;

        // 2) Fetch the binary from the resolved URL (also bearer-authenticated).
        using var binaryRequest = new HttpRequestMessage(HttpMethod.Get, url);
        binaryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var binaryResponse = await _http.SendAsync(binaryRequest, cancellationToken);
        binaryResponse.EnsureSuccessStatusCode();

        var bytes = await binaryResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = mimeType
            ?? binaryResponse.Content.Headers.ContentType?.ToString()
            ?? "application/octet-stream";

        return new MetaMediaContent(bytes, contentType, bytes.LongLength);
    }
}
