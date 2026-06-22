namespace Aluki.Runtime.Abstractions.Skills.Feedback;

public static class SuggestionState
{
    public const string Captured = "captured";
    public const string Enriched = "enriched";
    public const string SentUser = "sent_user";
    public const string Archived = "archived";
}

public static class AttachmentType
{
    public const string Text = "text";
    public const string Audio = "audio";
    public const string Photo = "photo";
}

public static class SuggestionActor
{
    public const string FeedbackService = "FeedbackService";
    public const string System = "System";
}

public static class SuggestionTransitionReason
{
    public const string WindowExpired = "window_expired";
    public const string NewIntentDetected = "new_intent_detected";
    public const string ConfirmationSent = "confirmation_sent";
    public const string AutoArchive90Days = "auto_archive_90d";
}
