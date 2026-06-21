using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Abstractions.Orchestration;

public sealed class SkillDispatcher
{
    private readonly IReadOnlyDictionary<string, ISkill> _skills;

    public SkillDispatcher(IEnumerable<ISkill> skills)
    {
        _skills = skills.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<SkillResult>> ExecutePlanAsync(
        CoordinatorPlan plan,
        SkillExecutionContext baseContext,
        CancellationToken cancellationToken)
    {
        var results = new List<SkillResult>(plan.SkillSequence.Length);

        foreach (var skillName in plan.SkillSequence)
        {
            if (!_skills.TryGetValue(skillName, out var skill))
            {
                results.Add(new SkillResult(
                    Success: false,
                    Output: null,
                    ErrorCode: "SKILL_NOT_FOUND",
                    ErrorMessage: $"Skill '{skillName}' is not registered."));
                break;
            }

            var context = baseContext with { SkillName = skillName };
            var result = await skill.ExecuteAsync(context, cancellationToken);
            results.Add(result);

            if (!result.Success)
            {
                break;
            }
        }

        return results;
    }
}
