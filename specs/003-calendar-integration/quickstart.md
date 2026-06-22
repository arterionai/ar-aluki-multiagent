# Quickstart: Validate Calendar Integration

This guide validates secure provider connection lifecycle and natural-language event creation behavior against the specification and contract artifacts.

## Prerequisites

- .NET 10 SDK installed.
- PostgreSQL reachable with baseline migrations applied:
  - `db/migrations/001_init_tenancy.sql`
  - `db/migrations/002_init_artifacts.sql`
  - `db/migrations/003_enable_rls.sql`
- Runtime configuration available (local or Key Vault-backed):
  - `PostgresConnectionString`
  - Provider client credentials for Outlook and Google test tenants.
- Solution builds:
  - `dotnet restore Aluki.Runtime.slnx`
  - `dotnet build Aluki.Runtime.slnx -v minimal`

## Validation Artifacts

- Contract: `specs/003-calendar-integration/contracts/calendar-integration-contract.yaml`
- Data model: `specs/003-calendar-integration/data-model.md`
- Plan traceability: `specs/003-calendar-integration/plan.md`

## Run the host

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

Confirm startup has no scope, migration, or configuration errors.

## Scenario A: Secure connect lifecycle and callback hardening (FR-001, FR-002a, SC-009)

1. Submit `connect` interaction for a user with no existing connection.
2. Follow returned connect URL and complete provider auth.
3. Verify callback result is `connection_established` and connection state persists.
4. Replay the same callback URL/state.
5. Verify callback is rejected (`callback_replayed` or equivalent) and connection state is unchanged.

Expected outcome:
- Callback is single-use and short-lived; invalid replay/expired/state-mismatched callbacks never mutate connection state.

## Scenario B: Disconnect semantics (FR-001)

1. With active provider connection, submit `disconnect` interaction.
2. Immediately submit `create_event` request.
3. Verify response requires reconnect (`connect_required` or `reconnect_required`) and no create side effect occurred.

Expected outcome:
- Disconnect is immediately effective for the scoped user.

## Scenario C: Complete create request with provider acknowledgment (FR-007, FR-007a, SC-013)

1. Submit `create_event` natural-language request with unambiguous title/date/time/timezone.
2. Verify response status `created` and includes:
  - `provider_event_reference`
  - final title/date/time/timezone/provider
  - stable `outcome_reference`

Expected outcome:
- `created` appears only after provider acknowledgment and includes provider reference.

## Scenario D: Clarification and timezone ambiguity handling (FR-004, FR-004a, FR-005, SC-002, SC-011)

1. Submit `create_event` request missing required fields (for example, no start time).
2. Verify `clarification_required` and missing fields are surfaced.
3. Submit request with DST-ambiguous local time.
4. Verify clarification is required before side effects.

Expected outcome:
- No event is created until required/ambiguous details are resolved to a canonical timezone.

## Scenario E: Authorization failure and reconnect guidance (FR-008, SC-003)

1. Expire/revoke provider authorization token for an otherwise connected account.
2. Submit valid `create_event` request.
3. Verify `reconnect_required` response with provider-specific reconnect guidance.
4. Confirm no partial or duplicate event is created.

Expected outcome:
- Authorization failures produce reconnect guidance and zero partial side effects.

## Scenario F: Deduplication and retry semantics (FR-009, FR-009a, SC-005, SC-012)

1. Submit a valid `create_event` request and capture `outcome_reference`.
2. Retry the same normalized request within 10 minutes.
3. Verify `previously_created` and same `outcome_reference` are returned.
4. Confirm only one provider-side event exists.

Expected outcome:
- Exactly one logical event per deduplication window for duplicate/retry submissions.

## Scenario G: Cross-provider parity (FR-010, SC-008)

1. Repeat Scenarios C-F for Outlook.
2. Repeat Scenarios C-F for Google.
3. Compare outcomes for validation/clarification behavior.

Expected outcome:
- Equivalent behavior for required-field validation, clarification gating, timezone normalization, and idempotency semantics.

## Scenario H: Scope enforcement and audit evidence (FR-002, FR-011, SC-006)

1. Submit connect/create requests with mismatched tenant/context principal.
2. Verify denial response (`scope_denied`) without fallback behavior.
3. Inspect audit records for connect, disconnect, create-success, create-denied, and auth-failure outcomes.

Expected outcome:
- Scope denials are enforced before side effects and all required outcomes are auditable.

## Scenario I: Token confidentiality (FR-008a, SC-010)

1. Run create and reconnect-failure flows while collecting user-visible outputs, telemetry events, and audit-readable payloads.
2. Verify no raw access/refresh token values are present.

Expected outcome:
- Token material remains confined to authorization boundaries and is never exposed.

---

## Execution Evidence (SB-003 Implementation)

**Branch**: `claude/tender-archimedes-8xb1vw`
**Implementation date**: 2026-06-22

### Build

```
dotnet build Aluki.Runtime.slnx -v minimal
# Result: Build succeeded (0 errors, warnings are NU1603/NU1903 package advisories only)
```

### Test suite

All tests implemented without a live database dependency (in-memory stubs):

| Category    | Test file                                         | Coverage                                              |
|-------------|---------------------------------------------------|-------------------------------------------------------|
| Unit        | CalendarRequestClassifierSkillTests               | T023 — classifier: title, time, timezone, provider hint, hash |
| Unit        | CalendarScopeGuardTests                           | FR-002 — scope guard always-permit stub               |
| Unit        | CalendarProviderParityPolicyTests                 | T034 — parity policy: contract violations + token leakage |
| Integration | CalendarCallbackSecurityIntegrationTests          | T031 — callback: nonce replay, expiry, state mismatch |
| Integration | CalendarTimezoneResolutionIntegrationTests        | T021 — DST ambiguity, IANA resolution, UTC conversion |
| Integration | CalendarDeduplicationIntegrationTests             | T022 — 10-minute dedup window, stable outcome ref     |
| Integration | CalendarGoogleParityIntegrationTests              | T032 — Google/Outlook adapter parity (enabled/disabled) |
| Integration | CalendarProviderSelectionIntegrationTests         | T033 — explicit hint, default, tiebreak               |
| Integration | CalendarLatencyIntegrationTests                   | T038 — classifier <50ms, resolver <20ms, selection <5ms |
| Integration | CalendarSecurityAndAuditIntegrationTests          | T039 — token redaction, ProviderTokenBoundary, audit isolation |
| Contract    | CalendarAuthorizationContractTests                | FR-001 — connect/disconnect HTTP contract             |
| Contract    | CalendarCreateEventContractTests                  | T020 — create_event HTTP contract, 400/402/403 shapes |

### Key implementation files

- `db/migrations/008_calendar_integration.sql` — 9 tables with RLS policies
- `src/Aluki.Runtime.Host/Calendar/CalendarServiceExtensions.cs` — single DI entry point
- `src/Aluki.Runtime.Host/Calendar/Providers/OutlookCalendarProvider.cs` — Outlook adapter
- `src/Aluki.Runtime.Host/Calendar/Providers/GoogleCalendarProvider.cs` — Google adapter
- `src/Aluki.Runtime.Host/Calendar/Skills/CalendarCreateSkill.cs` — full create pipeline
- `src/Aluki.Runtime.Host/Calendar/Security/TokenRedactionPolicy.cs` — token scrubbing
- `src/Aluki.Runtime.Host/Calendar/Security/ProviderTokenBoundary.cs` — [REDACTED] boundary
- `src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs` — 4 endpoints
