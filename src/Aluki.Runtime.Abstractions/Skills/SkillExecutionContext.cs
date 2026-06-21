using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Skills;

public sealed record SkillExecutionContext(
    PrincipalContext Principal,
    string SkillName,
    IReadOnlyDictionary<string, object?> Input,
    DateTimeOffset RequestedAtUtc
);
