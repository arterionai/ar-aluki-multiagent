# Implementation Plan: Calendar Integration

**Branch**: `003-calendar-integration` | **Date**: 2026-06-21 | **Spec**: `specs/003-calendar-integration/spec.md`

**Input**: Feature specification from `specs/003-calendar-integration/spec.md`

## Summary

Implement secure provider connect/disconnect and natural-language calendar event creation for Outlook and Google with deterministic provider selection, strict tenant/context authorization gates, canonical timezone resolution, and deduplicated create semantics. The implementation preserves the architecture baseline by keeping skills as atomic business executors, Orleans for live interaction handling, and Durable Functions for long-running recovery/retry flows while maintaining auditable outcomes and token-safety constraints.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: Existing abstractions (`ISkill`, `SkillExecutionContext`, `SkillDispatcher`, `PrincipalContext`), worker host dependencies (`Microsoft.Extensions.Hosting`, `Azure.Identity`), plus planned feature dependencies for provider adapters and time normalization (`Npgsql`, Microsoft Graph Calendar API client, Google Calendar API client, `NodaTime`).

**Storage**: PostgreSQL (tenant-scoped durable artifacts + RLS) for calendar connections, authorization callback state, deduplication records, and auditable outcomes.

**Testing**: xUnit-based unit tests for parsing/clarification/provider selection; contract tests for `contracts/calendar-integration-contract.yaml`; integration tests for scope gates, idempotency/dedup window behavior, callback replay rejection, and provider parity.

**Target Platform**: Linux-hosted runtime worker service with Windows local development support.

**Project Type**: Backend runtime capability slice (skill orchestration + provider integration contracts + persistence/audit behavior).

**Performance Goals**:
- P95 time to first user-visible response <= 2 seconds for non-blocking paths (SC-007).
- >= 98% accepted complete requests confirm creation within 30 seconds under baseline load (SC-001).
- 100% deterministic provider-selection outcomes for zero/one/multi-provider cases (SC-004).

**Constraints**:
- No authorization read/write or create side effect without resolved principal/tenant/context (FR-002).
- OAuth connect callbacks must be single-use, short-lived, and anti-forgery-state validated (FR-002a).
- Access/refresh tokens cannot appear in user output, telemetry payloads, or audit-readable fields (FR-008a).
- Duplicate retries inside 10-minute dedup window must produce a single logical create outcome (FR-009a).

**Scale/Scope**:
- Scope limited to connect/disconnect and create-event for Outlook and Google.
- Excludes update/delete/list/conflict-detection/attendee orchestration.
- Reuses existing runtime project structure without adding new deployable services.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Skill-First Execution (I)**: PASS. Capability is decomposed into explicit skills (authorization lifecycle, provider selection, create, idempotency guard, audit) with side-effect boundaries.
- **Tenant-Scoped Security by Default (II)**: PASS. Plan enforces `PrincipalContext` and tenant/context checks before callback processing, token retrieval, and provider event creation.
- **Grounded Memory and Provenance (III)**: PASS. User-visible creation outcomes require provider acknowledgment and include provider reference, preserving evidence-backed confirmations.
- **Durable Session and Workflow Separation (IV)**: PASS. Interactive create path remains synchronous/non-blocking; long-running retries/recovery delegated to Durable workflows.
- **Cost-Aware and Observable Intelligence (V)**: PASS. Every significant skill path emits latency/result/retry telemetry and audit events.

No constitution waivers are required.

## Implementation Phases

### Phase 0 - Research and Decision Consolidation

- Consolidate decisions in `research.md` for OAuth callback hardening, token boundaries, timezone normalization, deterministic provider selection, deduplication window semantics, and parity controls.
- Resolve all technical uncertainties before task decomposition.

Output:
- `specs/003-calendar-integration/research.md`

### Phase 1 - Design and Contracts

- Define entities and transitions for connection lifecycle, create requests, clarification turns, selection decisions, outcomes, and dedup records.
- Publish interaction contract(s) for connect/disconnect/create and callback handling.
- Produce quickstart validation scenarios mapped to FR/SC outcomes.
- Update agent context pointer to this plan.

Output:
- `specs/003-calendar-integration/data-model.md`
- `specs/003-calendar-integration/contracts/calendar-integration-contract.yaml`
- `specs/003-calendar-integration/quickstart.md`
- `.github/copilot-instructions.md`

### Phase 2 - Runtime Foundation Alignment

- Reuse existing abstraction + host bootstrap in `src/Aluki.Runtime.Abstractions` and `src/Aluki.Runtime.Host`.
- Introduce/extend calendar-focused skills:
  - `CalendarConnectSkill`
  - `CalendarDisconnectSkill`
  - `CalendarRequestClassifierSkill`
  - `CalendarProviderSelectionSkill`
  - `CalendarCreateSkill`
  - `CalendarIdempotencyGuardSkill`
  - `CalendarAuditSkill`

### Phase 3 - Authorization Lifecycle Hardening

- Implement secure connect link issuance and callback validation (single-use, short-lived, provider/user/state-bound).
- Persist connection state only on valid callback completion.
- Implement disconnect semantics that revoke usable authorization context for future creates.
- Emit audit events for connect success/failure/disconnect outcomes.

### Phase 4 - Event Creation Semantics

- Parse natural-language create requests into required fields and clarification needs.
- Resolve canonical timezone before side effects; enforce clarification on DST ambiguity.
- Execute deterministic provider selection across zero/one/multi-connection states.
- Require provider acknowledgment before `created` confirmation; include provider event reference.

### Phase 5 - Idempotency, Retry, and Recovery

- Implement deduplication key strategy and 10-minute logical window for retries/duplicates.
- Return `previously created` with same outcome reference on in-window duplicate submissions.
- Ensure auth-refresh failures return reconnect guidance with no partial/duplicate provider-side effects.
- Offload long-running retry/recovery flows to Durable orchestration.

### Phase 6 - Validation and Evidence

- Execute contract/unit/integration suites mapped to FR/SC coverage.
- Validate callback replay rejection, token redaction, timezone ambiguity handling, parity behavior, and dedup correctness.
- Produce measurable evidence for SC-001..SC-013.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| OAuth callback replay/state tampering | Unauthorized connection persistence | Single-use callback state store, strict expiry, anti-forgery validation, and replay detection tests |
| Token leakage in logs/outcomes | Compliance/security incident | Token handling boundaries, structured log redaction, and payload schema assertions |
| DST/timezone ambiguity creating wrong times | Incorrect user scheduling outcomes | Canonical timezone resolver + explicit clarification gates for ambiguous/nonexistent local times |
| Duplicate submissions creating multiple events | User trust and data consistency failures | Provider-agnostic idempotency key + 10-minute dedup window + provider reference replay semantics |
| Provider behavior drift (Outlook vs Google) | Inconsistent user experience | Shared validation pipeline + parity contract tests and acceptance matrix |
| Retry logic blocking interactive flow | UX and latency regression | Bounded synchronous path and Durable offload for long recovery workflows |

## Dependencies

1. Security/context foundation in `src/Aluki.Runtime.Abstractions/Security/PrincipalContext.cs`.
2. Skill orchestration contracts in `src/Aluki.Runtime.Abstractions/Skills/ISkill.cs` and `src/Aluki.Runtime.Abstractions/Orchestration/SkillDispatcher.cs`.
3. Runtime host bootstrap/config in `src/Aluki.Runtime.Host/Program.cs` and `src/Aluki.Runtime.Host/Worker.cs`.
4. Durable storage and RLS baseline in `db/migrations/001_init_tenancy.sql`, `db/migrations/002_init_artifacts.sql`, `db/migrations/003_enable_rls.sql`.
5. Provider authorization infrastructure for Outlook and Google credential exchange.

## Acceptance and Success-Criteria Coverage

| Requirement/Outcome | Plan Coverage | Validation Evidence |
|---|---|---|
| FR-001, FR-002, FR-002a | Phases 2-3 (connect/disconnect + callback hardening) | Callback/state replay tests + scope-gate integration tests |
| FR-003, FR-004, FR-004a, SC-011 | Phase 4 (request parsing + timezone normalization + DST clarification) | Ambiguity scenario matrix and canonical-time assertions |
| FR-005, FR-006, SC-002, SC-004 | Phase 4 (clarification + deterministic provider selection) | Clarification gating tests + zero/one/multi-provider contract tests |
| FR-007, FR-007a, SC-013 | Phase 4 (provider-acknowledged confirmation semantics) | Response schema checks for `created` vs `previously created` |
| FR-008, FR-008a, SC-003, SC-010 | Phases 3-5 (auth failure handling + token boundaries) | Reconnect-flow tests + telemetry/audit redaction assertions |
| FR-009, FR-009a, SC-005, SC-012 | Phase 5 (idempotency + 10-minute dedup window) | Duplicate retry tests with stable outcome references |
| FR-010, SC-008 | Phase 4 + 6 (cross-provider parity controls) | Outlook/Google parity suite |
| FR-011, SC-006 | Phases 3-6 (auditable records) | Audit payload verification by outcome type |
| FR-012, SC-007, SC-001 | Phases 5-6 (non-blocking UX + bounded latency) | Latency and async recovery evidence under baseline load |
| SC-009 | Phase 3 (invalid callback rejection) | Replay/expired/state-mismatch callback test results |

## Project Structure

### Documentation (this feature)

```text
specs/003-calendar-integration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── calendar-integration-contract.yaml
└── tasks.md                # generated later by /speckit.tasks
```

### Source Code (repository root)

```text
db/
└── migrations/

src/
├── Aluki.Runtime.Abstractions/
│   ├── Orchestration/
│   ├── Security/
│   └── Skills/
└── Aluki.Runtime.Host/
    ├── Program.cs
    ├── Worker.cs
    └── appsettings*.json

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Keep the existing two-project runtime baseline and add calendar capability through skill contracts and host-facing interaction endpoints; add `tests/` suites for contract/integration/unit evidence rather than introducing new service boundaries.

## Post-Design Constitution Re-Check

- Skill contracts and side effects remain explicit and isolated: PASS
- Tenant/context scope gates precede all auth and create side effects: PASS
- Outcome confirmations remain evidence-backed (provider acknowledgment + reference): PASS
- Orleans live-session path remains separate from Durable long workflows: PASS
- Telemetry and audit obligations are covered for all critical outcomes: PASS

## Complexity Tracking

No constitution violations or waivers.
