using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.SuggestionsAdmin;

public sealed class SuggestionsAdminService
{
    private readonly ISuggestionsAdminRepository _repo;
    private readonly ILogger<SuggestionsAdminService> _logger;

    public SuggestionsAdminService(ISuggestionsAdminRepository repo, ILogger<SuggestionsAdminService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<ListSuggestionsResponse> ListQueueAsync(ListSuggestionsRequest request, CancellationToken ct)
    {
        return await _repo.ListQueueAsync(request, ct);
    }

    public async Task<TriageSuggestionResponse> TriageAsync(TriageSuggestionRequest request, CancellationToken ct)
    {
        if (request.ActorRole == AdminRole.Auditor)
        {
            var item = await _repo.GetQueueItemAsync(request.TenantId, request.SuggestionId, ct);
            if (item != null)
            {
                await _repo.AppendAuditAsync(request.TenantId, request.SuggestionId,
                    request.ActorUserId, request.ActorRole, AdminActionType.AuthorizationDenied,
                    null, null, "auditor_readonly", ct);
            }
            return new TriageSuggestionResponse(TriageOutcome.Denied, DeniedReason: "auditor_readonly");
        }

        var queueItem = await _repo.GetQueueItemAsync(request.TenantId, request.SuggestionId, ct);
        if (queueItem == null) return new TriageSuggestionResponse(TriageOutcome.NotFound);

        if (request.NewStatus != null)
        {
            if (!IsValidTransition(queueItem.AdminStatus, request.NewStatus, request.ActorRole))
            {
                return new TriageSuggestionResponse(TriageOutcome.InvalidTransition,
                    DeniedReason: $"{queueItem.AdminStatus}->{request.NewStatus} not allowed for {request.ActorRole}");
            }
        }

        var oldValue = new { status = queueItem.AdminStatus, category = queueItem.AdminCategory, priority = queueItem.AdminPriority };
        var newValue = new { status = request.NewStatus ?? queueItem.AdminStatus, category = request.NewCategory ?? queueItem.AdminCategory, priority = request.NewPriority ?? queueItem.AdminPriority };

        await _repo.UpdateQueueItemAsync(request.TenantId, request.SuggestionId,
            request.NewStatus, request.NewCategory, request.NewPriority, request.ActorUserId, ct);

        var actionType = request.NewStatus != null ? AdminActionType.StatusChange :
                         request.NewCategory != null ? AdminActionType.CategoryChange : AdminActionType.PriorityChange;

        var auditId = await _repo.AppendAuditAsync(request.TenantId, request.SuggestionId,
            request.ActorUserId, request.ActorRole, actionType,
            oldValue, newValue, request.ReasonCode ?? "", ct);

        return new TriageSuggestionResponse(TriageOutcome.Updated, AuditId: auditId);
    }

    private static bool IsValidTransition(string currentStatus, string newStatus, string actorRole) =>
        (currentStatus, newStatus, actorRole) switch
        {
            (AdminStatus.Captured, AdminStatus.UnderReview, AdminRole.Reviewer) => true,
            (AdminStatus.Captured, AdminStatus.UnderReview, AdminRole.Approver) => true,
            (AdminStatus.UnderReview, AdminStatus.Accepted, AdminRole.Approver) => true,
            (AdminStatus.UnderReview, AdminStatus.Rejected, AdminRole.Approver) => true,
            (AdminStatus.Accepted, AdminStatus.Archived, AdminRole.Approver) => true,
            (AdminStatus.Rejected, AdminStatus.Archived, AdminRole.Approver) => true,
            _ => false
        };
}
