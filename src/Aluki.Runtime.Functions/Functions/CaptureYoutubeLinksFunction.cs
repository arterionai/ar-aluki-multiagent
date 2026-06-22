using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Aluki.Runtime.Host.Skills.YouTubeLinks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for SB-008B YouTube link save and classification.
/// Accepts a message payload, extracts YouTube URLs, enriches and classifies them,
/// persists each video as a saved link artifact, and returns per-URL outcomes.
/// </summary>
public sealed class CaptureYoutubeLinksFunction
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly YouTubeLinkCaptureService _service;

    public CaptureYoutubeLinksFunction(YouTubeLinkCaptureService service)
    {
        _service = service;
    }

    [Function("CaptureYoutubeLinks")]
    public async Task<HttpResponseData> CaptureAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "v1/skills/youtube-links/capture")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CaptureYoutubeLinksRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CaptureYoutubeLinksRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return await WriteBadRequestAsync(request, cancellationToken);
        }

        var result = await _service.CaptureAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    private static async Task<HttpResponseData> WriteBadRequestAsync(
        HttpRequestData request, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(new { error = "invalid_payload" }, cancellationToken);
        response.StatusCode = HttpStatusCode.BadRequest;
        return response;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request, HttpStatusCode statusCode, T body, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(body, cancellationToken);
        response.StatusCode = statusCode;
        return response;
    }
}
