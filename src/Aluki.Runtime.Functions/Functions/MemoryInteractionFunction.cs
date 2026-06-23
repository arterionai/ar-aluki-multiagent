using System.Net;
using System.Text.Json;
using Aluki.Runtime.Memory;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for personal memory interactions (SB-002): note capture and
/// grounded recall. Delegates to <see cref="MemoryInteractionCoordinator"/> and
/// returns contract-compliant responses.
/// </summary>
public sealed class MemoryInteractionFunction
{
    private readonly MemoryInteractionCoordinator _coordinator;

    public MemoryInteractionFunction(MemoryInteractionCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [Function("MemoryPersonalInteraction")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "memory/personal/interactions")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        MemoryInteractionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<MemoryInteractionRequest>(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            var error = new MemoryError(Guid.NewGuid().ToString("N"), MemoryErrorCode.InvalidPayload, "Invalid JSON body.");
            var bad = request.CreateResponse();
            await bad.WriteAsJsonAsync(error, cancellationToken);
            bad.StatusCode = HttpStatusCode.BadRequest;
            return bad;
        }

        var result = await _coordinator.ProcessAsync(payload, cancellationToken);

        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(result.Body, cancellationToken);
        response.StatusCode = (HttpStatusCode)result.StatusCode;
        return response;
    }
}
