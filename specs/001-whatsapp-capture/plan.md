# Implementation Plan: WhatsApp Capture Foundation

**Branch**: `001-whatsapp-capture` | **Date**: 2026-06-21 | **Spec**: `specs/001-whatsapp-capture/spec.md`

**Input**: Feature specification from `specs/001-whatsapp-capture/spec.md`

## Summary

Implement the first production inbound channel foundation for WhatsApp capture with strict tenant/context isolation, canonical idempotency, retry-safe persistence, mandatory auditing, and measurable reliability/SLA outcomes. The approach aligns with the current .NET runtime bootstrap (Abstractions + Host), introducing ingress contracts, capture pipeline skills, persistence/audit/idempotency stores, and observable control points without expanding scope beyond capture.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: `Microsoft.Extensions.Hosting`, `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`; planned additions in implementation phase: ASP.NET Core minimal APIs for webhook ingress, PostgreSQL data access (`Npgsql`), OpenTelemetry SDK, and test stack (`xUnit` + integration harness)

**Storage**: PostgreSQL (tenancy/artifacts/RLS migrations under `db/migrations`), optional blob/object store for media pointers, immutable audit log persistence

**Testing**: Unit tests for normalization/idempotency/scope guards, integration tests for DB+RLS behavior, contract tests for webhook schema handling, resilience tests for retry exhaustion and duplicate suppression

**Target Platform**: Linux-hosted runtime service (container-ready), compatible with local Windows dev

**Project Type**: Backend runtime service with webhook ingress and skill-orchestrated execution

**Performance Goals**:
- Ack path latency: P95 <= 2s, P99 <= 3s for valid non-blocking inbound events
- Capture success: >= 99.5% valid inbound messages persisted as single canonical record within 60s
- Duplicate artifacts in retry/redelivery scenarios: 0%

**Constraints**:
- No side effects without resolved `PrincipalContext`
- Canonical idempotency key: `(tenant_id, source_channel, provider_message_id)`
- Retry limit: maximum 5 attempts with bounded exponential backoff
- Mandatory capture lifecycle audit events: `capture.accepted`, `capture.duplicate_suppressed`, `capture.scope_denied`, `capture.unsupported_payload`, `capture.retry_scheduled`, `capture.failed_terminal`
- Scope remains limited to WhatsApp inbound capture foundation only

**Scale/Scope**:
- Single channel (WhatsApp) and four supported inbound classes (text, image, audio, forwarded)
- Feature slice limited to ingress normalization, scoped persistence, idempotency, auditing, and telemetry

## Architecture Decisions

1. **Skill-first capture pipeline**
  - Decision: Model capture behavior as composable skills (scope, idempotency, normalize, persist, audit, retry policy handoff) orchestrated by coordinator/dispatcher contracts.
  - Why: Enforces constitution principles I and II while keeping side effects explicit and testable.

2. **Principal resolution gate before side effects**
  - Decision: Resolve and validate `PrincipalContext` at ingress boundary (tenant from membership, context from explicit metadata or default personal context).
  - Why: Prevents unauthorized writes and satisfies FR-005/006/014 and SC-004.

3. **Canonical deduplication guard at persistence boundary**
  - Decision: Persist and check idempotency record using unique key `(tenant_id, source_channel, provider_message_id)` before canonical message/media writes.
  - Why: Guarantees FR-004/013 and SC-002/SC-008.

4. **Dual-path processing model (fast ack + durable completion)**
  - Decision: Keep synchronous acknowledgment lightweight while delegating retriable persistence workflows to durable/retry-safe execution policy.
  - Why: Meets ack SLA (SC-006/007) and reliability objectives (FR-009/017, SC-001/009).

5. **Unsupported payload continuity policy**
  - Decision: Return accepted-but-unsupported outcome and persist minimal artifact with scope/provenance/raw envelope reference.
  - Why: Prevents continuity breaks and fulfills FR-010/015.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Skill-First Execution**: PASS. Plan uses explicit skills/contracts and side-effect declarations.
- **Tenant-Scoped Security by Default**: PASS. Principal and scope checks are hard gates pre-side effect.
- **Grounded Memory and Provenance**: PASS. Capture artifacts include immutable provenance identifiers.
- **Durable Session and Workflow Separation**: PASS. Fast ingress path separated from long-running retry path.
- **Cost-Aware and Observable Intelligence**: PASS. Telemetry and audit events are mandatory for critical stages.

No constitution violations require exceptions.

## Implementation Phases

### Phase A - Foundation Alignment and Contracts
- Confirm branch/runtime baseline and enforce migration order dependencies from `docs/IMPLEMENTATION_ORDER.md`.
- Define webhook ingress contract and normalized UMO mapping for supported WhatsApp event types.
- Establish telemetry naming, correlation propagation, and audit event schema.
- Output: contract artifacts, normalized envelope schema, telemetry dictionary.

### Phase B - Security and Scope Enforcement
- Implement principal-context resolver and validation flow.
- Enforce tenant/context checks before any data write/read side effect.
- Add consent-stop (`STOP/ALTO`) guard behavior in capture preflight.
- Output: scope gate implementation + denial audit behavior.

### Phase C - Idempotent Capture Persistence
- Implement canonical idempotency record store with unique constraint on `(tenant_id, source_channel, provider_message_id)`.
- Persist unified message artifact and media artifact atomically for supported payloads.
- Implement unsupported-content fallback artifact flow.
- Output: canonical capture write path with duplicate-safe acknowledgment.

### Phase D - Retry, Failure Handling, and Observability
- Add bounded retry policy (max 5 attempts, bounded exponential backoff).
- Emit mandatory audit lifecycle events and terminal failure records with correlation and scope.
- Instrument latency/result/failure-category telemetry at critical stages.
- Output: reliability controls and operational traceability.

### Phase E - Validation Against Acceptance and Outcomes
- Execute contract tests, integration tests (including RLS/scope), duplicate redelivery tests, and fault-injection scenarios.
- Verify SLA and reliability metrics against SC thresholds.
- Produce evidence bundle mapping FRs and SCs to test artifacts.
- Output: go/no-go evidence for feature merge.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Principal context derivation ambiguity from channel metadata | Unauthorized or mis-scoped writes | Strict derivation precedence + deny-and-audit on ambiguity; integration tests for multi-tenant/multi-context cases |
| Duplicate deliveries under concurrent retries | Duplicate canonical artifacts | DB-level unique key + transactional idempotency check + duplicate-safe ack semantics |
| Ack SLA regression due to synchronous persistence overhead | Miss SC-006/SC-007 | Keep ack path minimal, offload retry-heavy work, add stage-level latency telemetry and budget alarms |
| Unsupported payload drift from provider changes | Processing failures/silent drops | Schema-tolerant parser + accepted-but-unsupported fallback + audit/telemetry counters |
| Incomplete observability for terminal failures | Incident triage blind spots | Mandatory event set enforcement in pipeline tests; CI gate on audit event presence |

## Dependencies

1. Existing tenancy/artifacts schema migrations and RLS enablement (`db/migrations/001..003`).
2. Principal identity and membership sources for tenant/context derivation.
3. PostgreSQL availability and connection management in runtime host.
4. Audit and telemetry sinks (OpenTelemetry/App Insights pipeline).
5. Stable provider message identity from WhatsApp webhook payload.

## Acceptance Criteria and Measurable Outcomes Alignment

| Spec Item | Plan Coverage | Validation Evidence |
|-----------|---------------|---------------------|
| FR-001, FR-002, FR-003 | Phases A + C (normalization and canonical persistence with required fields) | Contract + integration tests over text/image/audio/forwarded paths |
| FR-004, FR-013, SC-002, SC-008 | Phase C (canonical idempotency guard and duplicate-safe ack) | Redelivery tests with identical key asserting zero new canonical artifacts |
| FR-005, FR-006, FR-014, SC-004 | Phase B (scope validation gate + deny-and-audit) | Multi-tenant authorization and missing-scope denial tests |
| FR-007 | Phases A + C (ack/failure outcome envelope) | Contract tests for accepted, duplicate, denied, unsupported outcomes |
| FR-008, FR-016, SC-005 | Phase D (mandatory audit event set and terminal failure records) | Audit stream assertions and fault-injection checks |
| FR-009, FR-017, SC-001, SC-009 | Phase D (bounded retries and terminal failure semantics) | Transient/permanent failure simulations with attempt-count assertions |
| FR-010, FR-015 | Phase C (accepted-but-unsupported fallback artifacts) | Unsupported payload tests validating minimal persisted artifact |
| FR-011 | Phase B (consent-stop policy gate) | STOP/ALTO policy tests in pre-side-effect path |
| FR-012, SC-006, SC-007 | Phases D + E (critical-stage telemetry and SLA verification) | Load baseline report with P95/P99 ack metrics |

## Project Structure

### Documentation (this feature)

```text
specs/001-whatsapp-capture/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── whatsapp-inbound-contract.yaml
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

**Structure Decision**: Keep the existing two-project runtime bootstrap and extend it with ingress/capture components plus a dedicated `tests/` tree for contract, integration, and unit validation.

## Post-Design Constitution Re-Check

- Skill contract boundaries remain explicit and side effects are declared: PASS
- Tenant/context enforcement before side effects is encoded in phase ordering: PASS
- Provenance and audit requirements are represented in artifacts and contracts: PASS
- Session vs long-running workflow separation preserved in design: PASS
- Observability and measurable SLO outcomes included as mandatory validation: PASS

## Complexity Tracking

No constitution exceptions or complexity waivers required.
