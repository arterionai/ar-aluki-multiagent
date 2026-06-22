using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

public sealed class LinkConfirmationService
{
    private readonly ILinkCaptureRepository _repo;
    private readonly ILogger<LinkConfirmationService> _logger;

    public LinkConfirmationService(ILinkCaptureRepository repo, ILogger<LinkConfirmationService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<ResolveConfirmationResponse> ResolveAsync(ResolveConfirmationRequest request, CancellationToken ct)
    {
        // 1. Validate
        if (request.TenantId == Guid.Empty ||
            request.ContextScopeId == Guid.Empty ||
            request.PrincipalId == Guid.Empty ||
            string.IsNullOrEmpty(request.SessionId) ||
            string.IsNullOrEmpty(request.ConversationId) ||
            string.IsNullOrEmpty(request.SourceMessageId) ||
            string.IsNullOrEmpty(request.Reply))
        {
            return new ResolveConfirmationResponse(LinkConfirmationOutcome.NoActivePending, SideEffectsApplied: false);
        }

        // 2. Look up active pending
        var pending = await _repo.GetActivePendingAsync(
            request.TenantId, request.SessionId, request.ConversationId, ct);

        // 3. None found
        if (pending is null)
            return new ResolveConfirmationResponse(LinkConfirmationOutcome.NoActivePending, SideEffectsApplied: false);

        // 4. Already terminal (defensive — the query filters to 'pending', but guard anyway)
        if (pending.State != LinkConfirmationState.Pending)
            return new ResolveConfirmationResponse(
                LinkConfirmationOutcome.AlreadyResolved, SideEffectsApplied: false, ConfirmationId: pending.Id, TerminalState: pending.State);

        // 5. Expired
        if (pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return new ResolveConfirmationResponse(
                LinkConfirmationOutcome.Expired, SideEffectsApplied: false, ConfirmationId: pending.Id);

        // 6. Determine new state
        var reply = request.Reply.Trim().ToLowerInvariant();
        var newState = reply == "yes" ? LinkConfirmationState.ResolvedYes : LinkConfirmationState.ResolvedNo;
        var outcome = reply == "yes" ? LinkConfirmationOutcome.ResolvedYes : LinkConfirmationOutcome.ResolvedNo;

        // 7. Atomic consume
        var now = DateTimeOffset.UtcNow;
        var won = await _repo.TryConsumeConfirmationAsync(
            pending.Id, newState,
            request.PrincipalId, request.SourceMessageId,
            resolveCause: reply, resolvedAt: now, ct: ct);

        // 8. Won the race
        if (won)
        {
            await _repo.WriteAuditAsync(
                tenantId: request.TenantId,
                entityType: "link_pending_confirmation",
                entityId: pending.Id,
                eventType: LinkAuditEventType.ConfirmationResolved,
                actorType: LinkActorType.User,
                actorId: request.PrincipalId.ToString(),
                payload: new { new_state = newState, reply, source_message_id = request.SourceMessageId },
                ct: ct);

            return new ResolveConfirmationResponse(outcome, SideEffectsApplied: true, ConfirmationId: pending.Id, TerminalState: newState);
        }

        // 9. Lost the race — concurrent consume
        return new ResolveConfirmationResponse(
            LinkConfirmationOutcome.AlreadyResolved, SideEffectsApplied: false, ConfirmationId: pending.Id);
    }
}
