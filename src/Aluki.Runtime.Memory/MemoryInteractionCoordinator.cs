using System.Security.Cryptography;
using System.Text;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Security;
using Aluki.Runtime.Memory.Skills;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory;

/// <summary>HTTP-shaped result for a memory interaction.</summary>
public sealed record MemoryHttpResult(int StatusCode, object Body);

/// <summary>
/// Orchestrates a personal memory interaction: validate, scope-guard, classify
/// intent, and either capture a note (US1) or run grounded recall (US2). Recall
/// is a no_result placeholder until US2 lands.
/// </summary>
public sealed class MemoryInteractionCoordinator
{
    private readonly MemoryIntentClassifierSkill _classifier;
    private readonly MemoryScopeGuard _scopeGuard;
    private readonly MemoryStore _store;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<MemoryInteractionCoordinator> _logger;

    public MemoryInteractionCoordinator(
        MemoryIntentClassifierSkill classifier,
        MemoryScopeGuard scopeGuard,
        MemoryStore store,
        IEmbeddingClient embeddingClient,
        ILogger<MemoryInteractionCoordinator> logger)
    {
        _classifier = classifier;
        _scopeGuard = scopeGuard;
        _store = store;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<MemoryHttpResult> ProcessAsync(
        MemoryInteractionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId!;

        if (string.IsNullOrWhiteSpace(request.SourceChannel) || string.IsNullOrWhiteSpace(request.InputText))
        {
            return BadRequest(correlationId, "source_channel and input_text are required.");
        }

        if (request.Principal is not { } principal)
        {
            await SafeDenyAuditAsync(null, correlationId, cancellationToken);
            return ScopeDenied(correlationId, "Principal scope is required.");
        }

        if (!await _scopeGuard.IsAuthorizedAsync(principal, cancellationToken))
        {
            await SafeDenyAuditAsync(principal, correlationId, cancellationToken);
            return ScopeDenied(correlationId, "Principal is not authorized for the requested scope.");
        }

        var intent = await _classifier.ClassifyAsync(request.InputText, cancellationToken);
        if (intent == MemoryIntent.RecallQuery)
        {
            // US2 grounded recall is implemented in a follow-up; fail closed with
            // an explicit no_result rather than fabricating an answer.
            var recall = new RecallResult(
                Confidence: null,
                ClarificationQuestion: null,
                NoResultReason: "no_evidence",
                TopicGroups: [],
                Claims: []);
            return Ok(new MemoryInteractionResponse(correlationId, intent, MemoryStatus.NoResult, Recall: recall));
        }

        var sourceChannel = request.SourceChannel!;
        var inputText = request.InputText!;
        var sourceIdentity = string.IsNullOrWhiteSpace(request.SourceIdentity)
            ? DeriveSourceIdentity(sourceChannel, inputText)
            : request.SourceIdentity!;
        var provenanceRef = $"{sourceChannel}:{sourceIdentity}";

        var embedding = await TryEmbedAsync(inputText, correlationId, cancellationToken);

        var result = await _store.CaptureNoteAsync(
            principal,
            sourceChannel,
            sourceIdentity,
            inputText,
            embedding,
            provenanceRef,
            correlationId,
            cancellationToken);

        var status = result.IsNew ? MemoryStatus.Accepted : MemoryStatus.DuplicateSuppressed;
        var ack = new MemoryArtifactAck(
            result.CanonicalChainId,
            result.ChainVersion,
            $"{principal.TenantId:D}|{sourceChannel}|{sourceIdentity}");

        return Ok(new MemoryInteractionResponse(correlationId, MemoryIntent.NoteToStore, status, MemoryArtifact: ack));
    }

    private async Task<float[]?> TryEmbedAsync(string text, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _embeddingClient.EmbedAsync(text, cancellationToken);
        }
        catch (Exception ex)
        {
            // Embedding is best-effort at capture; recall can backfill. Never fail capture.
            _logger.LogError(ex, "Embedding generation failed. correlation_id={CorrelationId}", correlationId);
            return null;
        }
    }

    private async Task SafeDenyAuditAsync(PrincipalScope? principal, string correlationId, CancellationToken cancellationToken)
    {
        if (principal is null)
        {
            _logger.LogWarning("memory.scope_denied (no principal). correlation_id={CorrelationId}", correlationId);
            return;
        }

        try
        {
            await _store.WriteScopeDeniedAuditAsync(principal, correlationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist memory.scope_denied audit. correlation_id={CorrelationId}", correlationId);
        }
    }

    private static string DeriveSourceIdentity(string sourceChannel, string inputText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceChannel}|{inputText}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static MemoryHttpResult Ok(MemoryInteractionResponse response) => new(200, response);

    private static MemoryHttpResult BadRequest(string correlationId, string message) =>
        new(400, new MemoryError(correlationId, MemoryErrorCode.InvalidPayload, message));

    private static MemoryHttpResult ScopeDenied(string correlationId, string message) =>
        new(403, new MemoryError(correlationId, MemoryErrorCode.ScopeDenied, message));
}
