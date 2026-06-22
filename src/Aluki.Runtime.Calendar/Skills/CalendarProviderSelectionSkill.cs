using Aluki.Runtime.Abstractions.Skills.Calendar;

namespace Aluki.Runtime.Calendar.Skills;

public sealed class CalendarProviderSelectionSkill
{
    /// <summary>
    /// Selects a provider deterministically:
    ///   1. Explicit provider hint from the request (if connected).
    ///   2. User's default connected provider.
    ///   3. Lexical tie-break among active providers (alphabetical by provider name).
    /// Returns null when no provider is connected.
    /// </summary>
    public ProviderSelectionResult Select(
        string? providerHint,
        IReadOnlyList<CalendarConnectionRecord> activeConnections)
    {
        if (activeConnections.Count == 0)
            return ProviderSelectionResult.NoProvider;

        // 1. Explicit hint
        if (!string.IsNullOrWhiteSpace(providerHint) &&
            Enum.TryParse<CalendarProvider>(providerHint, ignoreCase: true, out var hinted))
        {
            var hintedConn = activeConnections.FirstOrDefault(c => c.Provider == hinted);
            if (hintedConn is not null)
                return ProviderSelectionResult.Ok(hinted, SelectionReason.ExplicitRequest);
        }

        // 2. Default provider flag
        var defaultConn = activeConnections.FirstOrDefault(c => c.DefaultForUser);
        if (defaultConn is not null)
            return ProviderSelectionResult.Ok(defaultConn.Provider, SelectionReason.UserDefault);

        // 3. Lexical tie-break
        var lexical = activeConnections
            .OrderBy(c => c.Provider.ToString(), StringComparer.Ordinal)
            .First();
        return ProviderSelectionResult.Ok(lexical.Provider, SelectionReason.DeterministicTiebreak);
    }
}

public sealed class ProviderSelectionResult
{
    public static readonly ProviderSelectionResult NoProvider = new() { HasProvider = false };

    public bool HasProvider { get; private init; }
    public CalendarProvider SelectedProvider { get; private init; }
    public SelectionReason Reason { get; private init; }

    public static ProviderSelectionResult Ok(CalendarProvider provider, SelectionReason reason) => new()
    {
        HasProvider = true,
        SelectedProvider = provider,
        Reason = reason
    };
}
