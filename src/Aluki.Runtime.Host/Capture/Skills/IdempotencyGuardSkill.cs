using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Host.Capture.Skills;

/// <summary>
/// Canonical idempotency guard keyed on
/// <c>(tenant_id, source_channel, provider_message_id)</c>. Suppresses duplicate
/// deliveries without creating or mutating canonical message/media artifacts
/// (FR-004, FR-013, SC-002, SC-008). Runs inside the active capture transaction.
/// </summary>
public sealed class IdempotencyGuardSkill : CaptureSkill
{
    public const string SkillName = "capture.idempotency_guard";

    public override string Name => SkillName;

    public override async Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var state = GetState(context);
        var uow = state.UnitOfWork
            ?? throw new InvalidOperationException("Idempotency guard requires an active unit of work.");

        var claim = await uow.Idempotency.TryClaimAsync(
            state.Principal.TenantId,
            state.SourceChannel,
            state.Envelope.ProviderMessageId,
            cancellationToken);

        state.IdempotencyId = claim.IdempotencyId;
        state.IsDuplicate = !claim.IsNew;

        if (!claim.IsNew)
        {
            state.CanonicalMessageId = claim.CanonicalMessageId;
        }

        return Ok(state);
    }
}
