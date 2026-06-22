namespace Aluki.Runtime.Abstractions.Skills.Calendar;

public enum CalendarProvider { Outlook, Google }

public enum ConnectionStatus { Connected, Disconnected, Revoked, Failed }

public enum OAuthCallbackStatus { Issued, Consumed, Expired, Rejected }

public enum TimezoneResolutionSource { Request, Profile, Clarified }

public enum ClarificationStatus { Pending, Answered, Expired }

public enum SelectionReason { ExplicitRequest, UserDefault, DeterministicTiebreak }

public enum DeduplicationStatus { InProgress, Created, Failed }

public enum CalendarOutcomeType
{
    Created,
    PreviouslyCreated,
    ClarificationRequired,
    ReconnectRequired,
    Denied,
    Failed
}

public enum AuthFailureReason { ExpiredToken, InvalidGrant, RefreshDenied, ScopeDenied }

public sealed record CalendarConnectRequest(
    Guid TenantId, Guid ContextId, Guid UserId,
    CalendarProvider Provider, string CorrelationId);

public sealed record CalendarConnectResult(
    string ConnectUrl, string StateNonce, DateTimeOffset ExpiresAtUtc);

public sealed record CalendarDisconnectRequest(
    Guid TenantId, Guid ContextId, Guid UserId,
    CalendarProvider Provider, string CorrelationId);

public sealed record CalendarDisconnectResult(bool Disconnected, string OutcomeReference);

public sealed record OAuthCallbackRequest(
    string StateNonce, string Code, CalendarProvider Provider, string CorrelationId);

public sealed record OAuthCallbackResult(
    bool Success, ConnectionStatus Status, string OutcomeReference);

public sealed record CalendarCreateRequest(
    Guid TenantId, Guid ContextId, Guid UserId,
    string NaturalLanguageInput, string? ProviderHint, string CorrelationId);

public sealed record CalendarCreateResult(
    CalendarOutcomeType OutcomeType,
    string OutcomeReference,
    string? ProviderEventReference,
    string? FinalTitle,
    DateTimeOffset? FinalStartUtc,
    DateTimeOffset? FinalEndUtc,
    string? FinalTimezone,
    string? ClarificationQuestion,
    CalendarProvider? SelectedProvider,
    string? SelectionReason,
    bool ReconnectRequired);
