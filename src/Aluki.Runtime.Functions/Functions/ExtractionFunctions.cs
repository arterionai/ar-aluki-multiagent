using System.Net;
using System.Text.Json;
using Aluki.Runtime.Extraction;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for AI extraction (SB-004): submit an extraction request and
/// query async job status. Delegates to <see cref="ExtractionCoordinator"/> and
/// returns contract-compliant responses (extraction-skill-v1).
/// </summary>
public sealed class ExtractionFunctions
{
    private readonly ExtractionCoordinator _coordinator;

    public ExtractionFunctions(ExtractionCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [Function("ExtractionExecute")]
    public async Task<HttpResponseData> ExecuteAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "extraction/execute")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        ExtractionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<ExtractionRequest>(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return await WriteAsync(request, new ExtractionHttpResult(
                400,
                new ExtractionErrorResponse(Guid.NewGuid().ToString("N"), ExtractionErrorCode.InvalidPayload, "Invalid JSON body.")),
                cancellationToken);
        }

        var result = await _coordinator.ProcessAsync(payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("ExtractionJobStatus")]
    public async Task<HttpResponseData> StatusAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "extraction/jobs/{jobId}")]
        HttpRequestData request,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId))
        {
            return await WriteAsync(request, new ExtractionHttpResult(
                400,
                new ExtractionErrorResponse(Guid.NewGuid().ToString("N"), ExtractionErrorCode.InvalidPayload, "jobId must be a UUID.")),
                cancellationToken);
        }

        var query = ParseQuery(request.Url.Query);
        var principalId = ParseGuid(Get(query, "principal_id"));
        var tenantId = ParseGuid(Get(query, "tenant_id"));
        var contextId = ParseGuid(Get(query, "context_id"));
        var correlationId = Get(query, "correlation_id");

        var principalContext = principalId is null
            ? null
            : new ExtractionPrincipalContext(principalId.Value, tenantId ?? Guid.Empty, Get(query, "context_type"), contextId);

        var result = await _coordinator.GetStatusAsync(principalContext, tenantId, parsedJobId, correlationId, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string? Get(IReadOnlyDictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) ? value : null;

    private static IReadOnlyDictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..index]);
            var value = Uri.UnescapeDataString(pair[(index + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static async Task<HttpResponseData> WriteAsync(
        HttpRequestData request, ExtractionHttpResult result, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(result.Body, cancellationToken);
        response.StatusCode = (HttpStatusCode)result.StatusCode;
        return response;
    }
}
