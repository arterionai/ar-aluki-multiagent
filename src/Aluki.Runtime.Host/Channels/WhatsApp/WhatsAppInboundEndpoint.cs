using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Host.Capture;
using Aluki.Runtime.Host.Capture.Failure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aluki.Runtime.Host.Channels.WhatsApp;

/// <summary>
/// Minimal-API webhook ingress for inbound WhatsApp events. Validates the
/// envelope, assigns a correlation id, dispatches the capture pipeline, and
/// returns contract-compliant ack/error responses including the controlled 403
/// scope_denied mapping (FR-001, FR-006, FR-007, FR-012, FR-014).
/// </summary>
public static class WhatsAppInboundEndpoint
{
    public const string Route = "/api/channels/whatsapp/inbound";

    private static readonly HashSet<string> KnownPayloadTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        CapturePayloadType.Text,
        CapturePayloadType.Image,
        CapturePayloadType.Audio,
        CapturePayloadType.Forwarded,
        CapturePayloadType.Unsupported
    };

    public static IEndpointRouteBuilder MapWhatsAppInbound(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, HandleAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        WhatsAppInboundEnvelope? envelope,
        WhatsAppCaptureCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return BadRequest(Guid.NewGuid().ToString("N"), "Request body is missing or invalid JSON.");
        }

        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : envelope.CorrelationId!;

        var validationError = Validate(envelope);
        if (validationError is not null)
        {
            return BadRequest(correlationId, validationError);
        }

        var outcome = await coordinator.CaptureAsync(envelope, cancellationToken);
        var mapped = CaptureFailureMapper.Map(outcome);
        return Results.Json(mapped.Body, statusCode: mapped.StatusCode);
    }

    private static string? Validate(WhatsAppInboundEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.ProviderMessageId))
        {
            return "provider_message_id is required.";
        }

        if (!"whatsapp".Equals(envelope.SourceChannel, StringComparison.OrdinalIgnoreCase))
        {
            return "source_channel must be 'whatsapp'.";
        }

        if (envelope.Sender is null || string.IsNullOrWhiteSpace(envelope.Sender.ExternalUserId))
        {
            return "sender.external_user_id is required.";
        }

        if (envelope.Payload is null || string.IsNullOrWhiteSpace(envelope.Payload.Type))
        {
            return "payload.type is required.";
        }

        if (!KnownPayloadTypes.Contains(envelope.Payload.Type))
        {
            return "payload.type is invalid.";
        }

        return null;
    }

    private static IResult BadRequest(string correlationId, string message)
    {
        var error = new CaptureError(
            Status: CaptureStatus.Rejected,
            CorrelationId: correlationId,
            Code: CaptureErrorCode.InvalidPayload,
            Message: message,
            AuditEvent: null);

        return Results.Json(error, statusCode: StatusCodes.Status400BadRequest);
    }
}
