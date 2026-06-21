namespace Aluki.Runtime.Abstractions.Skills;

public interface ISkill
{
    string Name { get; }

    Task<SkillResult> ExecuteAsync(SkillExecutionContext context, CancellationToken cancellationToken);
}

public sealed record SkillResult(
    bool Success,
    object? Output,
    string? ErrorCode = null,
    string? ErrorMessage = null
);
