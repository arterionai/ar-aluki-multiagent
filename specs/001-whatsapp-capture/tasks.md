# Tasks: WhatsApp Capture Foundation

**Input**: Design documents from `/specs/001-whatsapp-capture/`

**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/whatsapp-inbound-contract.yaml`, `.specify/memory/constitution.md`

**Tests**: Included. The specification explicitly requires scenario-based and measurable validation for reliability, isolation, retries, and SLA outcomes.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing in the current .NET solution.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency conflicts)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`)
- Every task includes: dependencies, file touch points, Definition of Done, and requirement traceability.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the current .NET runtime solution for webhook ingress, persistence, and test harnesses.

- [ ] T001 Create feature scaffolding folders for capture pipeline, persistence, and telemetry in src/Aluki.Runtime.Host/Channels/WhatsApp/, src/Aluki.Runtime.Host/Capture/, src/Aluki.Runtime.Host/Persistence/, and src/Aluki.Runtime.Host/Observability/
  Depends on: None
  Touch points: src/Aluki.Runtime.Host/Channels/WhatsApp/.gitkeep; src/Aluki.Runtime.Host/Capture/.gitkeep; src/Aluki.Runtime.Host/Persistence/.gitkeep; src/Aluki.Runtime.Host/Observability/.gitkeep
  Definition of Done: All folders exist in source control and match plan structure.
  Traceability: FR-001, FR-012

- [ ] T002 Update host dependencies and configuration bindings for HTTP ingress, PostgreSQL, and telemetry in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj and src/Aluki.Runtime.Host/appsettings*.json
  Depends on: T001
  Touch points: src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj; src/Aluki.Runtime.Host/appsettings.json; src/Aluki.Runtime.Host/appsettings.Development.json
  Definition of Done: Project restores successfully and exposes typed config sections for capture, retry, DB, and telemetry.
  Traceability: FR-009, FR-012, FR-017, SC-001

- [ ] T003 [P] Create test solution structure for contract, integration, and unit validation in tests/contract/, tests/integration/, and tests/unit/
  Depends on: T001
  Touch points: tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj; tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj; tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj
  Definition of Done: Three test projects build and reference runtime projects as needed.
  Traceability: FR-001..FR-017, SC-001..SC-009

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core schema, contracts, and execution guards that must exist before user story delivery.

**Critical**: No user story implementation starts before this phase is complete.

- [ ] T004 Create migration for capture foundation tables, constraints, and indexes including canonical idempotency uniqueness in db/migrations/004_whatsapp_capture_foundation.sql
  Depends on: T002
  Touch points: db/migrations/004_whatsapp_capture_foundation.sql
  Definition of Done: Migration defines inbound_message_event, unified_message_artifact, media_artifact, idempotency_record, capture_audit_event, and unique key `(tenant_id, source_channel, provider_message_id)`.
  Traceability: FR-002, FR-003, FR-004, FR-008, FR-013, FR-015, FR-016, SC-002, SC-003, SC-008

- [ ] T005 [P] Add RLS policies and scoped access enforcement for new capture tables in db/migrations/005_whatsapp_capture_rls.sql
  Depends on: T004
  Touch points: db/migrations/005_whatsapp_capture_rls.sql
  Definition of Done: All capture tables require tenant/context scope and reject unscoped access.
  Traceability: FR-005, FR-006, FR-014, SC-004

- [ ] T006 Define strongly typed capture contracts and enums in src/Aluki.Runtime.Abstractions/Channels/WhatsApp/
  Depends on: T002
  Touch points: src/Aluki.Runtime.Abstractions/Channels/WhatsApp/WhatsAppInboundEnvelope.cs; src/Aluki.Runtime.Abstractions/Channels/WhatsApp/CaptureAck.cs; src/Aluki.Runtime.Abstractions/Channels/WhatsApp/CaptureError.cs; src/Aluki.Runtime.Abstractions/Channels/WhatsApp/CaptureStatus.cs
  Definition of Done: Abstractions compile with contract types aligned to OpenAPI request/response schemas.
  Traceability: FR-001, FR-007, FR-010, FR-013, FR-015

- [ ] T007 [P] Define repository and unit-of-work interfaces for capture persistence in src/Aluki.Runtime.Abstractions/Persistence/
  Depends on: T006
  Touch points: src/Aluki.Runtime.Abstractions/Persistence/IInboundEventRepository.cs; src/Aluki.Runtime.Abstractions/Persistence/IMessageArtifactRepository.cs; src/Aluki.Runtime.Abstractions/Persistence/IMediaArtifactRepository.cs; src/Aluki.Runtime.Abstractions/Persistence/IIdempotencyRepository.cs; src/Aluki.Runtime.Abstractions/Persistence/IAuditEventRepository.cs; src/Aluki.Runtime.Abstractions/Persistence/ICaptureUnitOfWork.cs
  Definition of Done: Interfaces fully cover create/read operations required by FR and expose transactional boundaries.
  Traceability: FR-002, FR-003, FR-004, FR-008, FR-013, FR-016

- [ ] T008 Implement scoped PostgreSQL connection and session context setter for tenant/context/user in src/Aluki.Runtime.Host/Persistence/
  Depends on: T005, T007
  Touch points: src/Aluki.Runtime.Host/Persistence/NpgsqlConnectionFactory.cs; src/Aluki.Runtime.Host/Persistence/ScopedSessionContextSetter.cs; src/Aluki.Runtime.Host/Persistence/CaptureUnitOfWork.cs
  Definition of Done: Persistence layer applies session scope before any query and supports transactional execution.
  Traceability: FR-005, FR-006, FR-014, SC-004

- [ ] T009 [P] Define audit event catalog and telemetry stage constants for capture lifecycle in src/Aluki.Runtime.Host/Observability/CaptureObservability.cs
  Depends on: T006
  Touch points: src/Aluki.Runtime.Host/Observability/CaptureObservability.cs
  Definition of Done: Mandatory audit events and telemetry stage names are centralized and reused by skills/handlers.
  Traceability: FR-012, FR-016, SC-005

- [ ] T010 Wire dependency injection and HTTP pipeline bootstrap for capture components in src/Aluki.Runtime.Host/Program.cs
  Depends on: T008, T009
  Touch points: src/Aluki.Runtime.Host/Program.cs
  Definition of Done: Host starts with configured services, persistence, and capture endpoint registration enabled.
  Traceability: FR-001, FR-007, FR-012

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 - Capture inbound WhatsApp messages reliably (Priority: P1) 🎯 MVP

**Goal**: Persist valid inbound WhatsApp text/image/audio/forwarded events exactly once with canonical dedupe and proper acknowledgments.

**Independent Test**: Send valid payloads and duplicate redeliveries; verify one canonical artifact per unique message and duplicate-safe acknowledgment behavior.

### Tests for User Story 1

- [ ] T011 [P] [US1] Implement OpenAPI contract tests for webhook request/response behavior in tests/contract/Aluki.Runtime.ContractTests/WhatsAppInboundContractTests.cs
  Depends on: T003, T010
  Touch points: tests/contract/Aluki.Runtime.ContractTests/WhatsAppInboundContractTests.cs; specs/001-whatsapp-capture/contracts/whatsapp-inbound-contract.yaml
  Definition of Done: Tests validate 202/400 response shapes, status values, and required fields per contract.
  Traceability: FR-001, FR-007, FR-010, FR-013, FR-015

- [ ] T012 [P] [US1] Implement integration tests for supported payload persistence and duplicate suppression in tests/integration/Aluki.Runtime.IntegrationTests/WhatsAppCapturePersistenceTests.cs
  Depends on: T003, T010
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/WhatsAppCapturePersistenceTests.cs; db/migrations/004_whatsapp_capture_foundation.sql
  Definition of Done: Tests assert single canonical persistence and no extra artifacts on duplicate delivery.
  Traceability: FR-001, FR-002, FR-003, FR-004, FR-013, SC-002, SC-003, SC-008

### Implementation for User Story 1

- [ ] T013 [P] [US1] Implement payload validator and normalizer skill for text/image/audio/forwarded/unsupported in src/Aluki.Runtime.Host/Capture/Skills/NormalizeWhatsAppInboundSkill.cs
  Depends on: T006, T010
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/NormalizeWhatsAppInboundSkill.cs; src/Aluki.Runtime.Abstractions/Skills/SkillExecutionContext.cs
  Definition of Done: Skill maps inbound payloads to unified internal model and flags unsupported content deterministically.
  Traceability: FR-001, FR-010, FR-015

- [ ] T014 [P] [US1] Implement webhook endpoint handler for POST /api/channels/whatsapp/inbound in src/Aluki.Runtime.Host/Channels/WhatsApp/WhatsAppInboundEndpoint.cs
  Depends on: T010, T013
  Touch points: src/Aluki.Runtime.Host/Channels/WhatsApp/WhatsAppInboundEndpoint.cs; src/Aluki.Runtime.Host/Program.cs
  Definition of Done: Endpoint validates envelope, creates correlation id, dispatches pipeline, and returns contract-compliant ack/error responses.
  Traceability: FR-001, FR-007, FR-012

- [ ] T015 [US1] Implement canonical idempotency guard using `(tenant_id, source_channel, provider_message_id)` in src/Aluki.Runtime.Host/Capture/Skills/IdempotencyGuardSkill.cs
  Depends on: T008, T013
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/IdempotencyGuardSkill.cs; src/Aluki.Runtime.Host/Persistence/IdempotencyRepository.cs
  Definition of Done: Skill suppresses duplicates without mutating canonical message/media artifacts and returns duplicate-safe outcome.
  Traceability: FR-004, FR-013, SC-002, SC-008

- [ ] T016 [US1] Implement transactional persistence skill for inbound event, canonical message, and media artifacts in src/Aluki.Runtime.Host/Capture/Skills/PersistCaptureSkill.cs
  Depends on: T008, T013, T015
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/PersistCaptureSkill.cs; src/Aluki.Runtime.Host/Persistence/InboundEventRepository.cs; src/Aluki.Runtime.Host/Persistence/MessageArtifactRepository.cs; src/Aluki.Runtime.Host/Persistence/MediaArtifactRepository.cs
  Definition of Done: One atomic write path persists required records with scope and provenance, and skips media writes for duplicates.
  Traceability: FR-002, FR-003, FR-004, FR-013, SC-003

- [ ] T017 [US1] Implement accepted-but-unsupported fallback persistence in src/Aluki.Runtime.Host/Capture/Skills/PersistUnsupportedCaptureSkill.cs
  Depends on: T016
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/PersistUnsupportedCaptureSkill.cs; src/Aluki.Runtime.Host/Persistence/MessageArtifactRepository.cs
  Definition of Done: Unsupported payloads persist minimal artifact with unsupported classification, raw envelope reference, scope, and provenance.
  Traceability: FR-010, FR-015

- [ ] T018 [US1] Implement capture coordinator pipeline and outcome mapping in src/Aluki.Runtime.Host/Capture/WhatsAppCaptureCoordinator.cs
  Depends on: T014, T015, T016, T017
  Touch points: src/Aluki.Runtime.Host/Capture/WhatsAppCaptureCoordinator.cs; src/Aluki.Runtime.Abstractions/Orchestration/IAgentCoordinator.cs; src/Aluki.Runtime.Abstractions/Orchestration/SkillDispatcher.cs
  Definition of Done: Ordered skill sequence returns accepted, duplicate_suppressed, or accepted_unsupported without leaking internal failures.
  Traceability: FR-001, FR-007, FR-010, FR-013

- [ ] T019 [US1] Emit lifecycle audit events for accepted, duplicate_suppressed, and unsupported outcomes in src/Aluki.Runtime.Host/Capture/Skills/WriteCaptureAuditSkill.cs
  Depends on: T009, T018
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/WriteCaptureAuditSkill.cs; src/Aluki.Runtime.Host/Persistence/AuditEventRepository.cs
  Definition of Done: Successful and duplicate/unsupported paths emit mandatory event names with correlation and scope fields.
  Traceability: FR-008, FR-016, SC-005, SC-008

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Enforce tenant and context isolation at capture time (Priority: P1)

**Goal**: Deny unscoped/unauthorized operations before side effects and guarantee tenant/context isolation.

**Independent Test**: Execute same flow across two tenants and unauthorized requests; verify strict isolation and audit denial behavior.

### Tests for User Story 2

- [ ] T020 [P] [US2] Implement integration tests for cross-tenant/context isolation and denial behavior in tests/integration/Aluki.Runtime.IntegrationTests/CaptureScopeIsolationTests.cs
  Depends on: T003, T010, T016
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/CaptureScopeIsolationTests.cs; db/migrations/005_whatsapp_capture_rls.sql
  Definition of Done: Tests prove scoped retrieval and write isolation; unauthorized/missing scope requests are denied.
  Traceability: FR-005, FR-006, FR-014, SC-004

- [ ] T021 [P] [US2] Implement unit tests for PrincipalContext derivation and fallback rules in tests/unit/Aluki.Runtime.UnitTests/PrincipalContextResolverTests.cs
  Depends on: T003, T010
  Touch points: tests/unit/Aluki.Runtime.UnitTests/PrincipalContextResolverTests.cs
  Definition of Done: Tests cover explicit context metadata, default personal context fallback, and mismatch/unauthorized denial.
  Traceability: FR-014, SC-004

### Implementation for User Story 2

- [ ] T022 [US2] Implement PrincipalContext resolver from channel membership and metadata in src/Aluki.Runtime.Host/Security/PrincipalContextResolver.cs
  Depends on: T008, T021
  Touch points: src/Aluki.Runtime.Host/Security/PrincipalContextResolver.cs; src/Aluki.Runtime.Abstractions/Security/PrincipalContext.cs
  Definition of Done: Resolver returns validated principal scope or structured denial reason before pipeline execution.
  Traceability: FR-005, FR-006, FR-014

- [ ] T023 [US2] Implement scope guard + consent-stop policy skill (STOP/ALTO) before side effects in src/Aluki.Runtime.Host/Capture/Skills/ScopeGuardSkill.cs
  Depends on: T022
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/ScopeGuardSkill.cs; src/Aluki.Runtime.Host/Security/ConsentStopPolicyService.cs
  Definition of Done: Capture processing halts before any write when scope invalid or consent-stop is active.
  Traceability: FR-005, FR-006, FR-011, FR-014

- [ ] T024 [US2] Enforce scoped session context application on every persistence operation in src/Aluki.Runtime.Host/Persistence/CaptureUnitOfWork.cs and repositories
  Depends on: T023
  Touch points: src/Aluki.Runtime.Host/Persistence/CaptureUnitOfWork.cs; src/Aluki.Runtime.Host/Persistence/InboundEventRepository.cs; src/Aluki.Runtime.Host/Persistence/MessageArtifactRepository.cs; src/Aluki.Runtime.Host/Persistence/MediaArtifactRepository.cs
  Definition of Done: All capture reads/writes fail closed when session scope is absent and pass when scope is valid.
  Traceability: FR-005, FR-006, FR-014, SC-004

- [ ] T025 [US2] Implement controlled 403 scope_denied response mapping in webhook endpoint and coordinator in src/Aluki.Runtime.Host/Channels/WhatsApp/WhatsAppInboundEndpoint.cs
  Depends on: T023, T024
  Touch points: src/Aluki.Runtime.Host/Channels/WhatsApp/WhatsAppInboundEndpoint.cs; src/Aluki.Runtime.Host/Capture/WhatsAppCaptureCoordinator.cs
  Definition of Done: Invalid scope results in contract-compliant rejection without side effects.
  Traceability: FR-006, FR-007, FR-014

- [ ] T026 [US2] Emit capture.scope_denied audit event with scope/correlation identifiers in src/Aluki.Runtime.Host/Capture/Skills/WriteScopeDeniedAuditSkill.cs
  Depends on: T009, T025
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/WriteScopeDeniedAuditSkill.cs; src/Aluki.Runtime.Host/Persistence/AuditEventRepository.cs
  Definition of Done: Every denied capture attempt writes immutable denial audit record.
  Traceability: FR-008, FR-016, SC-004, SC-005

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Preserve operational reliability and traceability (Priority: P2)

**Goal**: Ensure bounded retries, terminal failure visibility, and measurable capture performance/telemetry.

**Independent Test**: Inject transient/permanent failures and baseline load to verify retry ceilings, terminal audit visibility, and SLA compliance.

### Tests for User Story 3

- [ ] T027 [P] [US3] Implement resilience integration tests for retry success and exhaustion in tests/integration/Aluki.Runtime.IntegrationTests/CaptureRetryReliabilityTests.cs
  Depends on: T003, T018, T026
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/CaptureRetryReliabilityTests.cs
  Definition of Done: Tests prove max 5 attempts, eventual persistence on transient faults, and terminal outcome on retry exhaustion.
  Traceability: FR-009, FR-017, SC-001, SC-009

- [ ] T028 [P] [US3] Implement performance/telemetry tests for acknowledgment latency and stage metrics in tests/integration/Aluki.Runtime.IntegrationTests/CaptureSlaTelemetryTests.cs
  Depends on: T003, T018
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/CaptureSlaTelemetryTests.cs
  Definition of Done: Tests/report verify P95 <=2s and P99 <=3s for valid non-blocking events with telemetry evidence.
  Traceability: FR-012, SC-006, SC-007

### Implementation for User Story 3

- [ ] T029 [US3] Implement bounded exponential retry policy (max 5 attempts) around transient persistence in src/Aluki.Runtime.Host/Capture/Retry/CaptureRetryPolicy.cs
  Depends on: T018
  Touch points: src/Aluki.Runtime.Host/Capture/Retry/CaptureRetryPolicy.cs; src/Aluki.Runtime.Host/Capture/WhatsAppCaptureCoordinator.cs
  Definition of Done: Retry policy classifies transient failures, caps attempts at 5, and exposes attempt metadata.
  Traceability: FR-009, FR-017, SC-009

- [ ] T030 [US3] Emit capture.retry_scheduled and capture.failed_terminal audit events with attempt and failure category in src/Aluki.Runtime.Host/Capture/Skills/WriteRetryAuditSkill.cs
  Depends on: T029
  Touch points: src/Aluki.Runtime.Host/Capture/Skills/WriteRetryAuditSkill.cs; src/Aluki.Runtime.Host/Persistence/AuditEventRepository.cs
  Definition of Done: Scheduled retries and terminal failures are always audited with correlation/scope and reason fields.
  Traceability: FR-008, FR-016, FR-017, SC-005, SC-009

- [ ] T031 [US3] Implement critical-stage telemetry instrumentation (latency, result status, failure category) in src/Aluki.Runtime.Host/Observability/CaptureTelemetry.cs
  Depends on: T009, T029
  Touch points: src/Aluki.Runtime.Host/Observability/CaptureTelemetry.cs; src/Aluki.Runtime.Host/Capture/WhatsAppCaptureCoordinator.cs
  Definition of Done: Telemetry is emitted for ingress, scope check, dedupe, persist, retry schedule, and terminal failure stages.
  Traceability: FR-012, SC-001, SC-005, SC-006, SC-007

- [ ] T032 [US3] Implement terminal failure response mapping and controlled failure outcomes in src/Aluki.Runtime.Host/Capture/Failure/CaptureFailureMapper.cs
  Depends on: T029, T030
  Touch points: src/Aluki.Runtime.Host/Capture/Failure/CaptureFailureMapper.cs; src/Aluki.Runtime.Host/Channels/WhatsApp/WhatsAppInboundEndpoint.cs
  Definition of Done: Retry-exhausted flows return controlled failure code (`retry_exhausted`) with correlation id and no false success.
  Traceability: FR-007, FR-009, FR-017, SC-005, SC-009

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate full requirements coverage and ship-ready documentation/evidence.

- [ ] T033 [P] Create FR/SC evidence checklist and mark verification artifacts in specs/001-whatsapp-capture/checklists/requirements.md
  Depends on: T019, T026, T030, T031, T032
  Touch points: specs/001-whatsapp-capture/checklists/requirements.md
  Definition of Done: Every FR-001..FR-017 and SC-001..SC-009 has test/evidence linkage and status.
  Traceability: FR-001..FR-017, SC-001..SC-009

- [ ] T034 Run quickstart validation suite and capture implementation verification notes in specs/001-whatsapp-capture/quickstart.md
  Depends on: T011, T012, T020, T027, T028, T033
  Touch points: specs/001-whatsapp-capture/quickstart.md
  Definition of Done: Quickstart scenarios are executed or updated with exact commands and expected outcomes.
  Traceability: FR-001..FR-017, SC-001..SC-009

- [ ] T035 Finalize operational runbook notes for capture observability and failure triage in docs/CAPTURE_OPERATIONS.md
  Depends on: T031, T032, T034
  Touch points: docs/CAPTURE_OPERATIONS.md
  Definition of Done: Runbook documents mandatory events, correlation usage, retry/terminal handling, and SLA dashboards.
  Traceability: FR-008, FR-012, FR-016, FR-017, SC-005, SC-006, SC-007, SC-009

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): Starts immediately.
- Foundational (Phase 2): Depends on setup and blocks all user stories.
- User Stories (Phase 3+): Depend on foundational completion.
- Polish (Phase 6): Depends on all selected user stories.

### User Story Dependencies

- US1 (P1): Starts after Phase 2; no dependency on US2/US3.
- US2 (P1): Starts after Phase 2; integrates with US1 persistence/endpoint paths but remains independently testable.
- US3 (P2): Starts after Phase 2 and after core capture flow exists (T018+) for reliability instrumentation.

### Critical Task Chain

T001 -> T002 -> T004 -> T005 -> T008 -> T010 -> T014 -> T018 -> T029 -> T030 -> T032 -> T033 -> T034 -> T035

---

## Parallel Opportunities

- Setup: T003 can run in parallel with T002 after T001.
- Foundational: T005 and T007/T009 can proceed in parallel after prerequisites.
- US1 tests: T011 and T012 in parallel.
- US1 implementation: T013 and T014 in parallel after T010/T013 readiness; audit task T019 follows coordinator completion.
- US2 tests: T020 and T021 in parallel.
- US3 tests: T027 and T028 in parallel.

### Parallel Example: US1

- Run together: T011 and T012.
- Run together: T013 and T014 (once T010 is complete and contract types exist).

### Parallel Example: US2

- Run together: T020 and T021.

### Parallel Example: US3

- Run together: T027 and T028.

---

## Requirement Traceability Matrix

- FR-001: T006, T011, T012, T013, T014, T018
- FR-002: T004, T012, T016
- FR-003: T004, T012, T016
- FR-004: T004, T012, T015, T016
- FR-005: T005, T008, T020, T022, T023, T024
- FR-006: T005, T008, T020, T022, T023, T025, T026
- FR-007: T006, T011, T014, T018, T025, T032
- FR-008: T004, T019, T026, T030, T035
- FR-009: T002, T027, T029, T032
- FR-010: T006, T011, T013, T017, T018
- FR-011: T023
- FR-012: T001, T009, T010, T014, T028, T031, T035
- FR-013: T004, T011, T012, T015, T016, T018
- FR-014: T005, T008, T020, T021, T022, T023, T024, T025
- FR-015: T004, T006, T011, T013, T017
- FR-016: T004, T009, T019, T026, T030, T035
- FR-017: T002, T027, T029, T030, T032

- SC-001: T002, T027, T031
- SC-002: T004, T012, T015
- SC-003: T004, T012, T016
- SC-004: T005, T020, T021, T024, T026
- SC-005: T009, T019, T026, T030, T031, T032, T035
- SC-006: T028, T031, T035
- SC-007: T028, T031, T035
- SC-008: T004, T012, T015, T019
- SC-009: T027, T029, T030, T032

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete US1 tasks through T019.
3. Validate T011/T012 and quick functional smoke.
4. Demo/deploy MVP capture foundation.

### Incremental Delivery

1. Add US2 isolation hardening (T020-T026).
2. Add US3 reliability/observability (T027-T032).
3. Finish cross-cutting evidence and runbook (T033-T035).

### Team Strategy

1. One stream owns persistence/contracts (T004-T008).
2. One stream owns endpoint/skills (T013-T019, T022-T026).
3. One stream owns tests and telemetry validation (T011-T012, T020-T021, T027-T028, T031).

---

## Notes

- All tasks follow the strict checklist format.
- `[P]` tasks are intentionally split across distinct files to reduce merge conflicts.
- File touch points target the current `.NET` solution layout under `src/Aluki.Runtime.Abstractions/` and `src/Aluki.Runtime.Host/`.
- Final acceptance should use the quickstart scenarios and FR/SC traceability evidence.
