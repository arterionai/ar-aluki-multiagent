using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Host.Skills.Feedback;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class FeedbackFunctions
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FeedbackCaptureService _service;

    public FeedbackFunctions(FeedbackCaptureService service)
    {
        _service = service;
    }

    [Function("FeedbackCapture")]
    public async Task<HttpResponseData> CaptureAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "skills/feedback/capture")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CaptureSuggestionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CaptureSuggestionRequest>(
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

    [Function("FeedbackAttach")]
    public async Task<HttpResponseData> AttachAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "skills/feedback/attach")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LinkAttachmentRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<LinkAttachmentRequest>(
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

        var result = await _service.LinkAttachmentAsync(payload, cancellationToken);
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
