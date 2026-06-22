using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Audit;
using Aluki.Runtime.Host.Calendar.Observability;

namespace Aluki.Runtime.Host.Calendar.Skills;

public sealed class CalendarCreateSkill
{
    private readonly ICalendarScopeGuard _scopeGuard;
    private readonly ICalendarConnectionRepository _connections;
    private readonly IEventCreationRequestRepository _requestRepo;
    private readonly ICalendarOutcomeRepository _outcomeRepo;
    private readonly CalendarRequestClassifierSkill _classifier;
    private readonly CalendarTimezoneResolverSkill _timezoneResolver;
    private readonly CalendarClarificationSkill _clarification;
    private readonly CalendarProviderSelectionSkill _providerSelection;
    private readonly CalendarIdempotencyGuardSkill _idempotency;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly CalendarAuditWriter _audit;
    private readonly CalendarTelemetry _telemetry;

    public CalendarCreateSkill(
        ICalendarScopeGuard scopeGuard,
        ICalendarConnectionRepository connections,
        IEventCreationRequestRepository requestRepo,
        ICalendarOutcomeRepository outcomeRepo,
        CalendarRequestClassifierSkill classifier,
        CalendarTimezoneResolverSkill timezoneResolver,
        CalendarClarificationSkill clarification,
        CalendarProviderSelectionSkill providerSelection,
        CalendarIdempotencyGuardSkill idempotency,
        IEnumerable<ICalendarProvider> providers,
        CalendarAuditWriter audit,
        CalendarTelemetry telemetry)
    {
        _scopeGuard = scopeGuard;
        _connections = connections;
        _requestRepo = requestRepo;
        _outcomeRepo = outcomeRepo;
        _classifier = classifier;
        _timezoneResolver = timezoneResolver;
        _clarification = clarification;
        _providerSelection = providerSelection;
        _idempotency = idempotency;
        _providers = providers;
        _audit = audit;
        _telemetry = telemetry;
    }

    public async Task<CalendarCreateResult> ExecuteAsync(CalendarCreateRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Scope gate
        var denial = await _scopeGuard.EvaluateCreateAsync(request.TenantId, request.ContextId, request.UserId, ct);
        if (denial is not null)
        {
            _telemetry.RecordScopeDenial(request.TenantId, request.UserId, denial.DenialCode);
            await _audit.WriteAsync("calendar.create.denied", request.TenantId, request.ContextId, request.UserId,
                null, nameof(CalendarCreateSkill), "scope_denied", null, request.CorrelationId,
                new { denial.DenialCode }, ct);
            return Denied(Guid.NewGuid().ToString("N"));
        }

        // 2. Provider selection — requires at least one active connection
        var activeConnections = await _connections.GetAllActiveAsync(request.TenantId, request.ContextId, request.UserId, ct);
        var selection = _providerSelection.Select(request.ProviderHint, activeConnections);

        if (!selection.HasProvider)
        {
            var outcomeRef = Guid.NewGuid().ToString("N");
            await PersistOutcomeAsync(Guid.Empty, request, CalendarProvider.Outlook, CalendarOutcomeType.ReconnectRequired,
                outcomeRef, null, null, null, null, null, ct);
            _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, null, "reconnect_required", sw.ElapsedMilliseconds);
            return new CalendarCreateResult(CalendarOutcomeType.ReconnectRequired, outcomeRef,
                null, null, null, null, null, null, null, null, ReconnectRequired: true);
        }

        var provider = selection.SelectedProvider;
        _telemetry.RecordCreateAttempt(request.TenantId, request.UserId, provider);

        // 3. Classify natural language input
        var classified = _classifier.Classify(request.NaturalLanguageInput);

        // 4. Resolve timezone
        var timezone = _timezoneResolver.Resolve(classified.TimezoneHint, profileDefault: null);

        // Check DST ambiguity only when we have a time and timezone
        var dstAmbiguous = timezone.IsResolved && !string.IsNullOrWhiteSpace(classified.StartLocal) &&
                           _timezoneResolver.IsDstAmbiguous(timezone.IanaId!, classified.StartLocal!);
        var timezoneForCheck = timezone with { DstAmbiguous = dstAmbiguous };

        // 5. Clarification gate
        var clarification = _clarification.Evaluate(classified, timezoneForCheck);
        if (clarification.NeedsClarification)
        {
            var outcomeRef = Guid.NewGuid().ToString("N");
            await _audit.WriteAsync("calendar.create.clarification_required", request.TenantId, request.ContextId, request.UserId,
                provider, nameof(CalendarCreateSkill), "clarification_required", outcomeRef, request.CorrelationId,
                new { field = clarification.RequestedField }, ct);
            _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, provider, "clarification_required", sw.ElapsedMilliseconds);
            return new CalendarCreateResult(CalendarOutcomeType.ClarificationRequired, outcomeRef,
                null, null, null, null, null,
                ClarificationQuestion: clarification.QuestionText,
                SelectedProvider: provider,
                SelectionReason: selection.Reason.ToString(),
                ReconnectRequired: false);
        }

        // 6. Persist creation request record
        var requestId = Guid.NewGuid();
        var creationRecord = new EventCreationRequestRecord(
            EventCreationRequestId: requestId,
            TenantId: request.TenantId,
            ContextId: request.ContextId,
            UserId: request.UserId,
            ProviderHint: request.ProviderHint,
            Title: classified.Title!,
            StartLocal: classified.StartLocal!,
            EndLocal: classified.EndLocal,
            CanonicalTimezone: timezone.IanaId!,
            TimezoneResolutionSource: timezone.Source,
            NormalizedPayloadHash: classified.NormalizedPayloadHash,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: request.CorrelationId);
        await _requestRepo.CreateAsync(creationRecord, ct);

        // 7. Idempotency check
        var idempotencyKey = _idempotency.ComputeIdempotencyKey(
            request.TenantId, request.UserId, provider,
            classified.Title, classified.StartLocal, timezone.IanaId);
        var dedupCheck = await _idempotency.CheckAsync(request.TenantId, request.ContextId, request.UserId, provider, idempotencyKey, ct);

        if (dedupCheck.IsDuplicate)
        {
            _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, provider, "previously_created", sw.ElapsedMilliseconds);
            return new CalendarCreateResult(CalendarOutcomeType.PreviouslyCreated,
                dedupCheck.Existing!.FirstOutcomeRef,
                dedupCheck.Existing.FirstProviderEventRef,
                classified.Title, null, null, timezone.IanaId, null, provider, selection.Reason.ToString(),
                ReconnectRequired: false);
        }

        // 8. Begin dedup window
        var dedupRecord = await _idempotency.BeginAsync(request.TenantId, request.ContextId, request.UserId, provider, idempotencyKey, ct);

        // 9. Convert times to UTC
        var startUtc = _timezoneResolver.ToUtc(timezone.IanaId!, classified.StartLocal!) ?? DateTimeOffset.UtcNow;
        var endUtc = !string.IsNullOrWhiteSpace(classified.EndLocal)
            ? (_timezoneResolver.ToUtc(timezone.IanaId!, classified.EndLocal!) ?? startUtc.AddHours(1))
            : startUtc.AddHours(1);

        // 10. Get provider account ref
        var connection = activeConnections.First(c => c.Provider == provider);

        // 11. Call provider adapter
        var adapter = _providers.FirstOrDefault(p => p.Provider == provider);
        if (adapter is null)
        {
            await _idempotency.FailAsync(dedupRecord.DeduplicationRecordId, ct);
            var failRef = Guid.NewGuid().ToString("N");
            await PersistOutcomeAsync(requestId, request, provider, CalendarOutcomeType.Failed,
                failRef, null, classified.Title, startUtc, endUtc, timezone.IanaId, ct);
            return new CalendarCreateResult(CalendarOutcomeType.Failed, failRef,
                null, classified.Title, startUtc, endUtc, timezone.IanaId,
                null, provider, selection.Reason.ToString(), ReconnectRequired: false);
        }

        var providerResult = await adapter.CreateEventAsync(new ProviderCreateRequest(
            Title: classified.Title!,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Timezone: timezone.IanaId!,
            ProviderAccountRef: connection.ProviderAccountRef ?? string.Empty,
            CorrelationId: request.CorrelationId), ct);

        if (providerResult.ReconnectRequired)
        {
            await _idempotency.FailAsync(dedupRecord.DeduplicationRecordId, ct);
            _telemetry.RecordAuthFailure(request.TenantId, request.UserId, provider, "token_expired");
            var reconnectRef = dedupRecord.FirstOutcomeRef;
            await PersistOutcomeAsync(requestId, request, provider, CalendarOutcomeType.ReconnectRequired,
                reconnectRef, null, classified.Title, startUtc, endUtc, timezone.IanaId, ct);
            _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, provider, "reconnect_required", sw.ElapsedMilliseconds);
            return new CalendarCreateResult(CalendarOutcomeType.ReconnectRequired, reconnectRef,
                null, classified.Title, startUtc, endUtc, timezone.IanaId,
                null, provider, selection.Reason.ToString(), ReconnectRequired: true);
        }

        if (!providerResult.Success)
        {
            await _idempotency.FailAsync(dedupRecord.DeduplicationRecordId, ct);
            var failRef = dedupRecord.FirstOutcomeRef;
            await PersistOutcomeAsync(requestId, request, provider, CalendarOutcomeType.Failed,
                failRef, null, classified.Title, startUtc, endUtc, timezone.IanaId, ct);
            _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, provider, "failed", sw.ElapsedMilliseconds);
            return new CalendarCreateResult(CalendarOutcomeType.Failed, failRef,
                null, classified.Title, startUtc, endUtc, timezone.IanaId,
                null, provider, selection.Reason.ToString(), ReconnectRequired: false);
        }

        // 12. Provider acknowledged — commit outcome
        await _idempotency.CompleteAsync(dedupRecord.DeduplicationRecordId, providerResult.ProviderEventRef!, ct);
        var createdRef = dedupRecord.FirstOutcomeRef;

        await PersistOutcomeAsync(requestId, request, provider, CalendarOutcomeType.Created,
            createdRef, providerResult.ProviderEventRef, classified.Title, startUtc, endUtc, timezone.IanaId, ct);

        await _audit.WriteAsync("calendar.create.completed", request.TenantId, request.ContextId, request.UserId,
            provider, nameof(CalendarCreateSkill), "created", createdRef, request.CorrelationId,
            new { provider_event_ref = providerResult.ProviderEventRef, title = classified.Title }, ct);

        _telemetry.RecordCreateOutcome(request.TenantId, request.UserId, provider, "created", sw.ElapsedMilliseconds);

        return new CalendarCreateResult(CalendarOutcomeType.Created, createdRef,
            providerResult.ProviderEventRef, classified.Title, startUtc, endUtc, timezone.IanaId,
            null, provider, selection.Reason.ToString(), ReconnectRequired: false);
    }

    private async Task PersistOutcomeAsync(
        Guid requestId, CalendarCreateRequest request, CalendarProvider provider,
        CalendarOutcomeType outcomeType, string outcomeRef, string? providerEventRef,
        string? title, DateTimeOffset? startUtc, DateTimeOffset? endUtc, string? timezone,
        CancellationToken ct)
    {
        if (requestId == Guid.Empty) return;

        var outcome = new CalendarEventOutcomeRecord(
            CalendarEventOutcomeId: Guid.NewGuid(),
            EventCreationRequestId: requestId,
            TenantId: request.TenantId,
            ContextId: request.ContextId,
            UserId: request.UserId,
            Provider: provider,
            OutcomeType: outcomeType,
            OutcomeReference: outcomeRef,
            ProviderEventReference: providerEventRef,
            FinalTitle: title,
            FinalStartUtc: startUtc,
            FinalEndUtc: endUtc,
            FinalTimezone: timezone,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: request.CorrelationId);

        await _outcomeRepo.CreateAsync(outcome, ct);
    }

    private static CalendarCreateResult Denied(string outcomeRef) =>
        new(CalendarOutcomeType.Denied, outcomeRef, null, null, null, null, null, null, null, null, false);
}
