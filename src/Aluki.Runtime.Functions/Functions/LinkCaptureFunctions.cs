using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Aluki.Runtime.Host.Skills.LinkCapture;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for SB-009A link capture: capture a link, confirm a pending link,
/// and recall previously captured links.
/// </summary>
public sealed class LinkCaptureFunctions
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LinkCaptureService _captureService;
    private readonly LinkConfirmationService _confirmationService;
    private readonly LinkRecallService _recallService;

    public LinkCaptureFunctions(
        LinkCaptureService captureService,
        LinkConfirmationService confirmationService,
        LinkRecallService recallService)
    {
        _captureService = captureService;
        _confirmationService = confirmationService;
        _recallService = recallService;
    }

    [Function("LinkCapture")]
    public async Task<HttpResponseData> CaptureAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "skills/link-capture/capture")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CaptureLinkRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CaptureLinkRequest>(
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

        var result = await _captureService.CaptureAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function("LinkCaptureConfirm")]
    public async Task<HttpResponseData> ConfirmAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "skills/link-capture/confirm")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        ResolveConfirmationRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<ResolveConfirmationRequest>(
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

        var result = await _confirmationService.ResolveAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function("LinkCaptureRecall")]
    public async Task<HttpResponseData> RecallAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "skills/link-capture/recall")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        RecallLinksRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<RecallLinksRequest>(
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

        var result = await _recallService.RecallAsync(payload, cancellationToken);
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
