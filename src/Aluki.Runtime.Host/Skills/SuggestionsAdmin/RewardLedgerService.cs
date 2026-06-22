using System.Security.Cryptography;
using System.Text;
using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.SuggestionsAdmin;

public sealed class RewardLedgerService
{
    private readonly ISuggestionsAdminRepository _repo;
    private readonly ILogger<RewardLedgerService> _logger;

    public RewardLedgerService(ISuggestionsAdminRepository repo, ILogger<RewardLedgerService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<RewardDecisionResponse> ProcessRewardDecisionAsync(RewardDecisionRequest request, CancellationToken ct)
    {
        var idempotencyKey = ComputeIdempotencyKey(request.TenantId, request.SubmitterUserId, request.SuggestionId, request.RewardRuleType, request.SourceEventId);
        var boundary = new { request.TenantId, request.SubmitterUserId, request.SuggestionId, request.RewardRuleType, request.SourceEventId };

        var (entitlementId, wasNew, isConflict) = await _repo.UpsertEntitlementAsync(
            request.TenantId, request.SubmitterUserId, request.SuggestionId,
            request.RewardRuleType, request.SourceEventId,
            request.GrantAmount, GrantStatus.Granted,
            request.PolicyVersion, new { },
            idempotencyKey, ct);

        if (isConflict)
        {
            await _repo.AppendRewardDecisionAsync(request.TenantId, DecisionType.Conflict, "payload_mismatch", boundary, null, request.CorrelationId, ct);
            return new RewardDecisionResponse(DecisionType.Conflict, null, "payload_mismatch", false);
        }

        if (!wasNew)
        {
            await _repo.AppendRewardDecisionAsync(request.TenantId, DecisionType.Duplicate, "idempotent_replay", boundary, entitlementId, request.CorrelationId, ct);
            return new RewardDecisionResponse(DecisionType.Duplicate, entitlementId, "idempotent_replay", true);
        }

        await _repo.CreateNotificationDeliveryAsync(request.TenantId, entitlementId, request.SubmitterUserId, "reward_granted_v1", ct);
        await _repo.AppendRewardDecisionAsync(request.TenantId, DecisionType.Granted, "new_grant", boundary, entitlementId, request.CorrelationId, ct);
        return new RewardDecisionResponse(DecisionType.Granted, entitlementId, "new_grant", false);
    }

    private static string ComputeIdempotencyKey(Guid tenantId, Guid userId, Guid suggestionId, string ruleType, string sourceEventId)
    {
        var input = $"{tenantId}:{userId}:{suggestionId}:{ruleType}:{sourceEventId}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
