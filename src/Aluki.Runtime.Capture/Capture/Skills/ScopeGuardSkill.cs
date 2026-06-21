using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Final pre-side-effect guard. Re-asserts a resolved principal scope and halts
/// capture when a consent-stop (STOP/ALTO) is active (FR-005, FR-006, FR-011,
/// FR-014). On halt it returns an unsuccessful result with the scope_denied code.
/// </summary>
public sealed class ScopeGuardSkill : CaptureSkill
{
    public const string SkillName = "capture.scope_guard";

    private readonly IConsentStopPolicy _consentStopPolicy;

    public ScopeGuardSkill(IConsentStopPolicy consentStopPolicy)
    {
        _consentStopPolicy = consentStopPolicy;
    }

    public override string Name => SkillName;

    public override async Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var state = GetState(context);

        var stopActive = await _consentStopPolicy.IsStopActiveAsync(
            state.Principal,
            state.SenderExternalId,
            cancellationToken);

        if (stopActive)
        {
            return new SkillResult(
                Success: false,
                Output: state,
                ErrorCode: CaptureErrorCode.ScopeDenied,
                ErrorMessage: "Consent stop is active for the principal.");
        }

        return Ok(state);
    }
}
