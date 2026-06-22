using Aluki.Runtime.Abstractions.Skills.Calendar;

namespace Aluki.Runtime.Calendar.Skills;

/// <summary>
/// Validates that a provider adapter result conforms to the adapter contract
/// (FR-010, SC-008). Enforces cross-provider parity rules:
/// - Success implies non-null ProviderEventRef
/// - Failure implies no ProviderEventRef
/// - Error messages must never contain raw token material
/// </summary>
public sealed class CalendarProviderParityPolicy
{
    private static readonly string[] TokenFieldNames =
        ["access_token", "refresh_token", "client_secret", "code", "id_token", "Bearer "];

    public ProviderResultDiagnostic Validate(ProviderCreateResult result)
    {
        var violations = new List<string>();

        if (result.Success && string.IsNullOrEmpty(result.ProviderEventRef))
            violations.Add("Success result must include a non-null ProviderEventRef.");

        if (!result.Success && result.ProviderEventRef is not null)
            violations.Add("Failure result must not include a ProviderEventRef.");

        if (result.ReconnectRequired && result.Success)
            violations.Add("ReconnectRequired and Success are mutually exclusive.");

        if (result.ErrorMessage is not null)
        {
            foreach (var token in TokenFieldNames)
            {
                if (result.ErrorMessage.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"ErrorMessage contains sensitive token material: '{token}'.");
                    break;
                }
            }
        }

        return new ProviderResultDiagnostic(violations.Count == 0, violations);
    }
}

public sealed record ProviderResultDiagnostic(bool IsValid, IReadOnlyList<string> Violations);
