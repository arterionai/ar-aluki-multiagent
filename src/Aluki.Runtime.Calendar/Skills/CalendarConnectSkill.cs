using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Audit;
using Aluki.Runtime.Calendar.Observability;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Skills;

public sealed class CalendarConnectSkill
{
    private readonly ICalendarScopeGuard _scopeGuard;
    private readonly IOAuthCallbackStateRepository _callbackStates;
    private readonly CalendarAuditWriter _audit;
    private readonly CalendarTelemetry _telemetry;
    private readonly CalendarOptions _options;

    public CalendarConnectSkill(
        ICalendarScopeGuard scopeGuard,
        IOAuthCallbackStateRepository callbackStates,
        CalendarAuditWriter audit,
        CalendarTelemetry telemetry,
        IOptions<CalendarOptions> options)
    {
        _scopeGuard = scopeGuard;
        _callbackStates = callbackStates;
        _audit = audit;
        _telemetry = telemetry;
        _options = options.Value;
    }

    public async Task<CalendarConnectSkillResult> ExecuteAsync(CalendarConnectRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _telemetry.RecordConnectAttempt(request.TenantId, request.UserId, request.Provider);

        var denial = await _scopeGuard.EvaluateConnectAsync(request.TenantId, request.ContextId, request.UserId, ct);
        if (denial is not null)
        {
            _telemetry.RecordScopeDenial(request.TenantId, request.UserId, denial.DenialCode);
            await _audit.WriteAsync("calendar.connect.denied", request.TenantId, request.ContextId, request.UserId,
                request.Provider, nameof(CalendarConnectSkill), "scope_denied", null, request.CorrelationId,
                new { denial.DenialCode, denial.Reason }, ct);
            return CalendarConnectSkillResult.Denied(denial);
        }

        var nonce = Guid.NewGuid().ToString("N");
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.OAuthStateExpiryMinutes);

        var callbackState = new OAuthCallbackStateRecord(
            OAuthCallbackStateId: Guid.NewGuid(),
            TenantId: request.TenantId,
            ContextId: request.ContextId,
            UserId: request.UserId,
            Provider: request.Provider,
            StateNonce: nonce,
            IssuedAtUtc: issuedAt,
            ExpiresAtUtc: expiresAt,
            UsedAtUtc: null,
            Status: OAuthCallbackStatus.Issued,
            CorrelationId: request.CorrelationId);

        await _callbackStates.CreateAsync(callbackState, ct);

        var connectUrl = BuildAuthorizeUrl(request.Provider, nonce);

        await _audit.WriteAsync("calendar.connect.initiated", request.TenantId, request.ContextId, request.UserId,
            request.Provider, nameof(CalendarConnectSkill), "initiated", null, request.CorrelationId,
            new { provider = request.Provider.ToString(), expires_at_utc = expiresAt }, ct);

        _telemetry.RecordConnectOutcome(request.TenantId, request.UserId, request.Provider, "initiated", sw.ElapsedMilliseconds);

        return CalendarConnectSkillResult.Ok(new CalendarConnectResult(connectUrl, nonce, expiresAt));
    }

    private string BuildAuthorizeUrl(CalendarProvider provider, string nonce)
    {
        var callbackUrl = $"{_options.CallbackBaseUrl.TrimEnd('/')}/api/calendar/callback";

        return provider switch
        {
            CalendarProvider.Outlook => BuildOutlookUrl(nonce, callbackUrl),
            CalendarProvider.Google => BuildGoogleUrl(nonce, callbackUrl),
            _ => throw new InvalidOperationException($"Unknown provider: {provider}")
        };
    }

    private string BuildOutlookUrl(string nonce, string callbackUrl)
    {
        var opts = _options.Outlook;
        var scopes = Uri.EscapeDataString(string.Join(" ", opts.Scopes));
        var redirect = Uri.EscapeDataString(callbackUrl);
        return $"https://login.microsoftonline.com/{opts.TenantId}/oauth2/v2.0/authorize" +
               $"?client_id={opts.ClientId}&response_type=code&redirect_uri={redirect}" +
               $"&response_mode=query&scope={scopes}&state={nonce}";
    }

    private string BuildGoogleUrl(string nonce, string callbackUrl)
    {
        var opts = _options.Google;
        var scopes = Uri.EscapeDataString(string.Join(" ", opts.Scopes));
        var redirect = Uri.EscapeDataString(callbackUrl);
        return $"https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={opts.ClientId}&response_type=code&redirect_uri={redirect}" +
               $"&scope={scopes}&state={nonce}&access_type=offline&prompt=consent";
    }
}

public sealed class CalendarConnectSkillResult
{
    public bool IsSuccess { get; private init; }
    public CalendarConnectResult? Result { get; private init; }
    public CalendarScopeDenial? Denial { get; private init; }

    public static CalendarConnectSkillResult Ok(CalendarConnectResult result) =>
        new() { IsSuccess = true, Result = result };

    public static CalendarConnectSkillResult Denied(CalendarScopeDenial denial) =>
        new() { IsSuccess = false, Denial = denial };
}
