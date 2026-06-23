using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Base for capture pipeline skills. Provides typed access to the shared
/// <see cref="CapturePipelineState"/> carried on the execution context.
/// </summary>
public abstract class CaptureSkill : ISkill
{
    public abstract string Name { get; }

    public abstract Task<SkillResult> ExecuteAsync(
        SkillExecutionContext context,
        CancellationToken cancellationToken);

    protected static CapturePipelineState GetState(SkillExecutionContext context)
    {
        if (context.Input.TryGetValue(CaptureInputKeys.State, out var value) &&
            value is CapturePipelineState state)
        {
            return state;
        }

        throw new InvalidOperationException("Capture pipeline state is missing from the execution context.");
    }

    protected static SkillResult Ok(CapturePipelineState state) => new(true, state);
}
