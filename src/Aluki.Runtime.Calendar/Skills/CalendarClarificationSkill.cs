namespace Aluki.Runtime.Calendar.Skills;

public sealed class CalendarClarificationSkill
{
    /// <summary>
    /// Inspects a classified request and timezone resolution to determine whether
    /// a clarification question must be issued before create side effects are allowed.
    /// Returns null when all required fields are resolved and no ambiguity exists.
    /// </summary>
    public ClarificationDecision Evaluate(ClassifiedRequest classified, TimezoneResolution timezone)
    {
        if (!classified.HasTitle)
        {
            return ClarificationDecision.Required("title", "What is the title of the event?");
        }

        if (!classified.HasStartTime)
        {
            return ClarificationDecision.Required("start_time", "When should the event start? Please provide a date and time.");
        }

        if (!timezone.IsResolved)
        {
            return ClarificationDecision.Required("timezone", "Which timezone should the event be scheduled in? (e.g. Eastern Time, UTC, America/New_York)");
        }

        if (timezone.DstAmbiguous)
        {
            return ClarificationDecision.Required("dst_ambiguity",
                $"The time you specified occurs twice on that date due to daylight saving time. " +
                $"Please clarify: do you mean the first occurrence (before clocks fall back) or the second?");
        }

        return ClarificationDecision.NotRequired;
    }
}

public sealed class ClarificationDecision
{
    public static readonly ClarificationDecision NotRequired = new() { NeedsClarification = false };

    public bool NeedsClarification { get; private init; }
    public string? RequestedField { get; private init; }
    public string? QuestionText { get; private init; }

    public static ClarificationDecision Required(string field, string question) => new()
    {
        NeedsClarification = true,
        RequestedField = field,
        QuestionText = question
    };
}
