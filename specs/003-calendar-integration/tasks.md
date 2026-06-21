# Tasks: Calendar Integration

**Input**: Design documents from /specs/003-calendar-integration/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/calendar-integration-contract.yaml, quickstart.md

**Tests**: Included (spec mandates user-scenario validation and measurable success criteria SC-001..SC-013).

**Organization**: Tasks are grouped by user story and ordered by dependency so each story remains independently implementable and testable.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare solution/test scaffolding and feature configuration used by all stories.

- [ ] T001 Add calendar feature test projects and solution references in Aluki.Runtime.slnx, tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj, tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj, tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj
- [ ] T002 Update runtime package dependencies for calendar providers, persistence, and time normalization in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
- [ ] T003 [P] Add calendar integration configuration sections and defaults in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core contracts, security gates, persistence, and observability required before any story work.

**Critical**: No user story work starts until this phase is complete.

- [ ] T004 Create calendar integration schema migration (connections, callback state, requests, decisions, dedup, outcomes, audits) in db/migrations/004_calendar_integration.sql
- [ ] T005 [P] Define calendar domain contracts and response DTOs in src/Aluki.Runtime.Abstractions/Skills/Calendar/CalendarContracts.cs
- [ ] T006 [P] Add calendar scope guard interfaces and denial models in src/Aluki.Runtime.Abstractions/Security/ICalendarScopeGuard.cs and src/Aluki.Runtime.Abstractions/Security/CalendarScopeDenial.cs
- [ ] T007 [P] Define calendar repository abstractions for connection, callback state, dedup, and outcomes in src/Aluki.Runtime.Abstractions/Skills/Calendar/ICalendarPersistence.cs
- [ ] T008 Implement PostgreSQL calendar repositories and migration bootstrap checks in src/Aluki.Runtime.Host/Calendar/Persistence/PostgresCalendarRepository.cs and src/Aluki.Runtime.Host/Calendar/Persistence/PostgresCalendarAuditRepository.cs
- [ ] T009 [P] Implement token-boundary and redaction utilities for auth-only token handling in src/Aluki.Runtime.Host/Calendar/Security/ProviderTokenBoundary.cs and src/Aluki.Runtime.Host/Calendar/Security/TokenRedactionPolicy.cs
- [ ] T010 [P] Implement calendar telemetry and audit emitters (latency, result, retries, scope, correlation) in src/Aluki.Runtime.Host/Calendar/Observability/CalendarTelemetry.cs and src/Aluki.Runtime.Host/Calendar/Audit/CalendarAuditWriter.cs
- [ ] T011 Register calendar services, repositories, and options in dependency injection bootstrap in src/Aluki.Runtime.Host/Program.cs

**Checkpoint**: Foundation ready. User stories can proceed.

---

## Phase 3: User Story 1 - Connect and disconnect calendar providers (Priority: P0) MVP

**Goal**: Securely connect/disconnect Outlook and Google with strict scope enforcement and callback replay protection.

**Independent Test**: Complete connect/disconnect lifecycle and callback replay/expiry/mismatch rejection without unauthorized state mutation.

### Tests for User Story 1

- [ ] T012 [P] [US1] Add contract tests for connect/disconnect/callback outcomes in tests/contract/CalendarAuthorizationContractTests.cs
- [ ] T013 [P] [US1] Add integration tests for callback single-use, expiry, and state mismatch rejection in tests/integration/CalendarCallbackSecurityIntegrationTests.cs
- [ ] T014 [P] [US1] Add unit tests for principal/tenant/context scope enforcement in tests/unit/CalendarScopeGuardTests.cs

### Implementation for User Story 1

- [ ] T015 [US1] Implement connect-link issuance with single-use short-lived callback state creation in src/Aluki.Runtime.Host/Calendar/Skills/CalendarConnectSkill.cs
- [ ] T016 [US1] Implement OAuth callback processor with anti-forgery validation and replay rejection in src/Aluki.Runtime.Host/Calendar/Skills/CalendarCallbackSkill.cs
- [ ] T017 [US1] Implement disconnect behavior revoking active provider connection in src/Aluki.Runtime.Host/Calendar/Skills/CalendarDisconnectSkill.cs
- [ ] T018 [US1] Expose calendar interaction and callback endpoints for connect/disconnect paths in src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs
- [ ] T019 [US1] Emit auditable connect/disconnect/callback-rejected outcomes with scope identifiers in src/Aluki.Runtime.Host/Calendar/Audit/CalendarAuthorizationAuditSkill.cs

**Checkpoint**: US1 independently functional and testable.

---

## Phase 4: User Story 2 - Create Outlook events from natural language (Priority: P1)

**Goal**: Create Outlook events from natural language with clarification gates, canonical timezone resolution, deterministic provider selection, and idempotent create semantics.

**Independent Test**: Submit complete/ambiguous/retried create requests with Outlook connected and verify created vs previously_created semantics.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add contract tests for create_event statuses (clarification_required, created, previously_created, reconnect_required) in tests/contract/CalendarCreateEventContractTests.cs
- [ ] T021 [P] [US2] Add integration tests for timezone normalization and DST ambiguity clarification in tests/integration/CalendarTimezoneResolutionIntegrationTests.cs
- [ ] T022 [P] [US2] Add integration tests for deduplication window and stable outcome references in tests/integration/CalendarDeduplicationIntegrationTests.cs
- [ ] T023 [P] [US2] Add unit tests for request classification and required-field detection in tests/unit/CalendarRequestClassifierSkillTests.cs

### Implementation for User Story 2

- [ ] T024 [US2] Implement natural-language create request classifier and required-field extractor in src/Aluki.Runtime.Host/Calendar/Skills/CalendarRequestClassifierSkill.cs
- [ ] T025 [US2] Implement canonical timezone resolver with profile fallback and DST ambiguity detection in src/Aluki.Runtime.Host/Calendar/Skills/CalendarTimezoneResolverSkill.cs
- [ ] T026 [US2] Implement clarification decision engine that blocks side effects until required fields resolve in src/Aluki.Runtime.Host/Calendar/Skills/CalendarClarificationSkill.cs
- [ ] T027 [US2] Implement deterministic provider selection (explicit -> default -> lexical tie-break) in src/Aluki.Runtime.Host/Calendar/Skills/CalendarProviderSelectionSkill.cs
- [ ] T028 [US2] Implement idempotency guard with 10-minute deduplication window and stable outcome references in src/Aluki.Runtime.Host/Calendar/Skills/CalendarIdempotencyGuardSkill.cs
- [ ] T029 [US2] Implement Outlook provider adapter for create-event acknowledgment and reconnect-required mapping in src/Aluki.Runtime.Host/Calendar/Providers/OutlookCalendarProvider.cs
- [ ] T030 [US2] Implement create-event orchestration enforcing provider ack before created confirmation in src/Aluki.Runtime.Host/Calendar/Skills/CalendarCreateSkill.cs
- [ ] T031 [US2] Extend calendar endpoint create_event flow and response serialization for outcome semantics in src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs

**Checkpoint**: US2 independently functional and testable.

---

## Phase 5: User Story 3 - Create Google events with equivalent behavior (Priority: P2)

**Goal**: Deliver Outlook-equivalent validation, clarification, provider selection visibility, and create semantics for Google.

**Independent Test**: Re-run US2 scenario matrix using Google and verify behavior parity and deterministic outcomes for multi-provider states.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add integration tests for Google create parity against Outlook behavior matrix in tests/integration/CalendarGoogleParityIntegrationTests.cs
- [ ] T033 [P] [US3] Add integration tests for deterministic multi-provider selection visibility in confirmation outcomes in tests/integration/CalendarProviderSelectionIntegrationTests.cs
- [ ] T034 [P] [US3] Add unit tests for provider parity policy and selected-provider reason output in tests/unit/CalendarProviderParityPolicyTests.cs

### Implementation for User Story 3

- [ ] T035 [US3] Implement Google provider adapter with equivalent create/authorization-failure semantics in src/Aluki.Runtime.Host/Calendar/Providers/GoogleCalendarProvider.cs
- [ ] T036 [US3] Implement provider parity policy checks shared by Outlook/Google adapters in src/Aluki.Runtime.Host/Calendar/Skills/CalendarProviderParityPolicy.cs
- [ ] T037 [US3] Extend create orchestration and response mapping to expose selected-provider reason consistently in src/Aluki.Runtime.Host/Calendar/Skills/CalendarCreateSkill.cs and src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs

**Checkpoint**: US3 independently functional and testable.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Validate performance/security/observability targets and close traceability evidence.

- [ ] T038 [P] Add integration benchmark for non-blocking first-response latency and create completion SLOs in tests/integration/CalendarLatencyIntegrationTests.cs
- [ ] T039 [P] Add integration checks for token redaction, reconnect-required behavior, and audit completeness in tests/integration/CalendarSecurityAndAuditIntegrationTests.cs
- [ ] T040 Update quickstart execution evidence and scenario checklist for implemented behavior in specs/003-calendar-integration/quickstart.md
- [ ] T041 Record final FR/SC implementation evidence and verification status in specs/003-calendar-integration/checklists/requirements.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 -> no dependencies.
- Phase 2 -> depends on T001-T003.
- Phase 3 (US1) -> depends on T004-T011.
- Phase 4 (US2) -> depends on T004-T011 and endpoint scaffolding from T018.
- Phase 5 (US3) -> depends on T024-T031.
- Phase 6 -> depends on T015-T037.

### Task-Level Dependencies

- T008 depends on T004, T005, T007.
- T009 depends on T005, T007.
- T010 depends on T005.
- T011 depends on T006-T010.
- T015 depends on T006-T008, T011.
- T016 depends on T006-T009, T011, T015.
- T017 depends on T006, T008, T011.
- T018 depends on T005, T006, T015-T017.
- T019 depends on T010, T015-T018.
- T024 depends on T005, T011.
- T025 depends on T005, T011.
- T026 depends on T024, T025.
- T027 depends on T008, T024.
- T028 depends on T008, T024, T027.
- T029 depends on T009, T025, T027.
- T030 depends on T026-T029.
- T031 depends on T018, T030.
- T035 depends on T009, T025, T027.
- T036 depends on T029, T035.
- T037 depends on T030, T031, T036.
- T038 depends on T031, T037.
- T039 depends on T019, T030, T037.
- T040 depends on T038, T039.
- T041 depends on T038-T040.

### User Story Dependencies

- US1 can start immediately after Phase 2 and is the MVP-critical path.
- US2 depends on US1 endpoint/connect foundations (T018) and foundational phase completion.
- US3 depends on US2 create pipeline and extends parity behavior.

### Story Completion Order (Dependency-Ordered)

1. US1 (secure authorization lifecycle)
2. US2 (Outlook create semantics + idempotency)
3. US3 (Google parity + deterministic selection visibility)

---

## Parallel Opportunities

### Setup and Foundation

- T003 can run in parallel with T001-T002.
- T005, T006, T007 can run in parallel after T004 planning is complete.
- T009 and T010 can run in parallel after T005/T007.

### US1

- T012, T013, T014 can run in parallel.
- T016 and T017 can run in parallel after T015.

### US2

- T020, T021, T022, T023 can run in parallel.
- T024 and T025 can run in parallel.
- T027 and T028 can run in parallel after T024.

### US3

- T032, T033, T034 can run in parallel.
- T035 can run in parallel with T036 preparation once T029 is available.

### Cross-Cutting

- T038 and T039 can run in parallel after T037.

---

## File Touch Points by Story

- US1: src/Aluki.Runtime.Host/Calendar/Skills/CalendarConnectSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarCallbackSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarDisconnectSkill.cs, src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs, src/Aluki.Runtime.Host/Calendar/Audit/CalendarAuthorizationAuditSkill.cs, tests/contract/CalendarAuthorizationContractTests.cs, tests/integration/CalendarCallbackSecurityIntegrationTests.cs, tests/unit/CalendarScopeGuardTests.cs
- US2: src/Aluki.Runtime.Host/Calendar/Skills/CalendarRequestClassifierSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarTimezoneResolverSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarClarificationSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarProviderSelectionSkill.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarIdempotencyGuardSkill.cs, src/Aluki.Runtime.Host/Calendar/Providers/OutlookCalendarProvider.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarCreateSkill.cs, src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs, tests/contract/CalendarCreateEventContractTests.cs, tests/integration/CalendarTimezoneResolutionIntegrationTests.cs, tests/integration/CalendarDeduplicationIntegrationTests.cs, tests/unit/CalendarRequestClassifierSkillTests.cs
- US3: src/Aluki.Runtime.Host/Calendar/Providers/GoogleCalendarProvider.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarProviderParityPolicy.cs, src/Aluki.Runtime.Host/Calendar/Skills/CalendarCreateSkill.cs, src/Aluki.Runtime.Host/Endpoints/CalendarEndpoints.cs, tests/integration/CalendarGoogleParityIntegrationTests.cs, tests/integration/CalendarProviderSelectionIntegrationTests.cs, tests/unit/CalendarProviderParityPolicyTests.cs

---

## Definition of Done and Traceability (FR/SC)

### US1 DoD

- Connect/disconnect lifecycle works for Outlook and Google with scope gates before side effects (FR-001, FR-002).
- OAuth callback state is single-use, short-lived, and replay/mismatch/expiry safe (FR-002a, SC-009).
- Authorization lifecycle outcomes are auditable with scope/correlation metadata and no token leakage (FR-008a, FR-011, SC-006, SC-010).

Task traceability:
- T012-T014 -> FR-001, FR-002, FR-002a, SC-009
- T015-T019 -> FR-001, FR-002, FR-002a, FR-008a, FR-011, SC-006, SC-009, SC-010

### US2 DoD

- Natural-language create requests are classified and required fields enforced before side effects (FR-003, FR-005, SC-002).
- Time handling resolves to one canonical timezone with DST ambiguity clarification (FR-004, FR-004a, SC-011).
- Provider selection is deterministic and explicit in outcome semantics (FR-006, SC-004).
- Created responses require provider acknowledgment and include required confirmation fields (FR-007, FR-007a, SC-013).
- Authorization refresh failures return reconnect-required outcomes without partial side effects (FR-008, SC-003).
- Deduplication window prevents additional provider events and returns stable references (FR-009, FR-009a, SC-005, SC-012).

Task traceability:
- T020-T023 -> FR-003, FR-004, FR-004a, FR-005, FR-007, FR-008, FR-009, FR-009a, SC-002, SC-003, SC-005, SC-011, SC-012, SC-013
- T024-T031 -> FR-003, FR-004, FR-004a, FR-005, FR-006, FR-007, FR-007a, FR-008, FR-009, FR-009a, SC-002, SC-003, SC-004, SC-005, SC-011, SC-012, SC-013

### US3 DoD

- Google provider behavior matches Outlook for validation, clarification, reconnect, and confirmation semantics (FR-010, SC-008).
- Multi-provider outcomes remain deterministic and expose selected-provider reasoning (FR-006, FR-010, SC-004, SC-008).

Task traceability:
- T032-T034 -> FR-006, FR-010, SC-004, SC-008
- T035-T037 -> FR-006, FR-007, FR-007a, FR-008, FR-010, SC-004, SC-008, SC-013

### Cross-Cutting DoD

- Non-blocking response and create completion performance targets are validated (FR-012, SC-001, SC-007).
- Token redaction and auditable record completeness are verified end-to-end (FR-008a, FR-011, SC-006, SC-010).
- Quickstart and requirements checklist contain verifiable implementation evidence for all FR/SC coverage.

Task traceability:
- T038 -> FR-012, SC-001, SC-007
- T039 -> FR-008a, FR-011, SC-003, SC-006, SC-010
- T040-T041 -> FR-001 through FR-012, SC-001 through SC-013 evidence closure

---

## Implementation Strategy

### MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 3 (US1 only).
- Validate callback hardening and connect/disconnect audit guarantees before create-event implementation.

### Incremental Delivery

1. Deliver US1 and validate security/scope/callback behavior.
2. Deliver US2 and validate Outlook create semantics + idempotency.
3. Deliver US3 and validate Google parity + deterministic multi-provider outcomes.
4. Close with performance/security/audit evidence and requirements checklist updates.