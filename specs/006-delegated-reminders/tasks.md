# Tasks: Delegated Reminders (SB-006)

**Input**: Design documents from /specs/006-delegated-reminders/

**Prerequisites**: plan.md (required), spec.md (required), .specify/memory/constitution.md

**Tests**: Included. This feature defines measurable NFR/SC targets and operational guarantees, so test evidence is required to validate delivery windows, consent gating, retry bounds, and sender failure notifications.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing in the current .NET solution.

## Format: [ID] [P?] [Story] Description

- [P]: Can run in parallel (different files, no dependency conflicts)
- [Story]: User story label ([US1], [US2], [US3])
- Every task includes: dependencies, file touch points, Definition of Done, and requirement traceability.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare solution structure, feature config surface, and baseline projects for delegated reminder implementation.

- [ ] T001 Create delegated reminder feature folders in host, abstractions, and functions projects
  Depends on: None
  Touch points: src/Aluki.Runtime.Abstractions/Skills/.gitkeep; src/Aluki.Runtime.Abstractions/Orchestration/.gitkeep; src/Aluki.Runtime.Host/Services/.gitkeep; src/Aluki.Runtime.Functions/Functions/.gitkeep
  Definition of Done: Folder layout exists and matches plan structure for delegated reminder orchestration.
  Traceability: FR-001, FR-012

- [ ] T002 Update runtime configuration and options for delegated reminders, consent, anti-spam limits, and delivery retry policy
  Depends on: T001
  Touch points: src/Aluki.Runtime.Host/appsettings.json; src/Aluki.Runtime.Host/appsettings.Development.json; src/Aluki.Runtime.Host/Program.cs
  Definition of Done: Typed options are bound and validated at startup for baseline thresholds (anti-spam=10/day, retries=5, cancellation window=30s).
  Traceability: FR-005, FR-006, FR-008, NFR-004, SC-004, SC-006

- [ ] T003 [P] Create delegated reminder test project scaffolding for unit and integration suites
  Depends on: T001
  Touch points: tests/Aluki.Runtime.Tests/Aluki.Runtime.Tests.csproj; tests/Aluki.Runtime.Tests/DelegatedReminder/.gitkeep; tests/Aluki.Runtime.Tests/Integration/.gitkeep
  Definition of Done: Test project builds and references runtime projects needed for feature validation.
  Traceability: FR-001..FR-013, SC-001..SC-009

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core schema, contracts, policy interfaces, and wiring that must exist before any user story implementation.

**Critical**: No user story work starts before this phase is complete.

- [ ] T004 Create delegated reminder persistence migration with tenant-scoped reminder records and routing keys
  Depends on: T002
  Touch points: db/migrations/004_create_delegated_reminders.sql
  Definition of Done: Migration creates delegated_reminders table with sender_identity, recipient_identity, due_time, status, consent_acquired, correlation fields, and routing key support.
  Traceability: FR-003, FR-011, FR-012, SC-008, SC-009

- [ ] T005 [P] Create consent registry migration with persistent opt-in scope (tenant and optional sender scope)
  Depends on: T004
  Touch points: db/migrations/005_create_consent_registry.sql
  Definition of Done: Migration creates consent_registry with per-recipient opt-in state, scope mode (global/per-sender), and audit columns.
  Traceability: FR-004, FR-011, SC-002, SC-008

- [ ] T006 [P] Create delivery attempts migration for retry traceability and terminal failure classification
  Depends on: T004
  Touch points: db/migrations/006_create_delivery_attempts.sql
  Definition of Done: Migration creates delivery_attempts with attempt index, timestamps, failure_classification, transient/permanent flags, and correlation identifiers.
  Traceability: FR-006, FR-007, FR-010, FR-011, SC-004, SC-005, SC-008

- [ ] T007 Add row-level security policies for delegated reminders, consent registry, and delivery attempts
  Depends on: T005, T006
  Touch points: db/schema/delegated-reminders-rls.sql
  Definition of Done: Tenant and principal context are enforced for all delegated reminder tables with fail-closed policy behavior.
  Traceability: FR-003, FR-004, FR-011, SC-008, SC-009

- [ ] T008 Define abstractions for delegated reminder and recipient resolution skill contracts
  Depends on: T002
  Touch points: src/Aluki.Runtime.Abstractions/Skills/DelegatedReminderSkill.cs; src/Aluki.Runtime.Abstractions/Skills/RecipientResolutionSkill.cs; src/Aluki.Runtime.Abstractions/Skills/PolicyDecisionContracts.cs
  Definition of Done: Contracts model three-tier recipient resolution input/output and explicit policy decision outcomes.
  Traceability: FR-001, FR-002, FR-004, FR-005, FR-007

- [ ] T009 [P] Define orchestration contract for long-running delegated reminder workflow handoff
  Depends on: T008
  Touch points: src/Aluki.Runtime.Abstractions/Orchestration/DelegatedReminderOrchestrator.cs; src/Aluki.Runtime.Abstractions/Orchestration/IAgentCoordinator.cs
  Definition of Done: Orchestration contract supports create, schedule, cancel, delivery start, and terminal failure transitions with correlation id.
  Traceability: FR-001, FR-008, FR-009, FR-012

- [ ] T010 Implement host service interfaces for delegated reminder state and consent registry access
  Depends on: T007, T008
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; src/Aluki.Runtime.Host/Services/ConsentRegistryService.cs
  Definition of Done: Services perform scoped CRUD and policy checks without bypassing tenant/principal constraints.
  Traceability: FR-003, FR-004, FR-005, FR-012

- [ ] T011 Wire delegated reminder services, skills, and durable function clients into DI bootstrapping
  Depends on: T009, T010
  Touch points: src/Aluki.Runtime.Host/Program.cs; src/Aluki.Runtime.Functions/Program.cs
  Definition of Done: Runtime starts with delegated reminder components registered and callable from orchestration pipeline.
  Traceability: FR-001, FR-012

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 - Correct delegated intent routing (Priority: P1) MVP

**Goal**: Route delegated reminder requests through delegated flow only, with correct recipient identity capture and routing key construction.

**Independent Test**: A delegated-intent request never enters personal reminder flow, and all three recipient tiers resolve to deterministic next-step outcomes.

### Tests for User Story 1

- [ ] T012 [P] [US1] Add unit tests for delegated intent classification and orchestration path isolation
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/DelegatedReminder/DelegatedIntentRoutingTests.cs
  Definition of Done: Tests prove delegated syntax always selects delegated pipeline and never personal reminder pipeline.
  Traceability: FR-001, FR-012, SC-009

- [ ] T013 [P] [US1] Add unit tests for three-tier recipient resolution outputs and routing key construction
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/DelegatedReminder/RecipientResolutionTests.cs
  Definition of Done: Tests cover Tier 1 direct route, Tier 2 contact-capture branch, Tier 3 clarification branch with routing key (tenant, recipient, sender).
  Traceability: FR-002, FR-003, SC-001, SC-002

### Implementation for User Story 1

- [ ] T014 [US1] Implement delegated intent branch in scheduling orchestration entrypoint
  Depends on: T011, T012
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; src/Aluki.Runtime.Abstractions/Orchestration/IAgentCoordinator.cs
  Definition of Done: Delegated requests are classified and dispatched to delegated orchestrator without cross-path contamination.
  Traceability: FR-001, FR-012, SC-009

- [ ] T015 [US1] Implement recipient resolution skill with known-contact, phone-only, and unknown-recipient branches
  Depends on: T011, T013
  Touch points: src/Aluki.Runtime.Abstractions/Skills/RecipientResolutionSkill.cs; src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs
  Definition of Done: Resolution returns explicit branch decision and captured identity attributes needed for downstream policy checks.
  Traceability: FR-002, FR-003, SC-001, SC-002

- [ ] T016 [US1] Persist delegated reminder creation and recipient-resolved audit events with correlation scope
  Depends on: T014, T015
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; db/migrations/004_create_delegated_reminders.sql
  Definition of Done: delegated_reminder.created and delegated_reminder.recipient_resolved are emitted and persisted with tenant, sender, recipient, and correlation identifiers.
  Traceability: FR-003, FR-011, SC-008

- [ ] T017 [US1] Add query separation for delegated versus personal reminders in service query interface
  Depends on: T014
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; src/Aluki.Runtime.Abstractions/Orchestration/DelegatedReminderOrchestrator.cs
  Definition of Done: Delegated reminder queries return only delegated records and personal query paths remain unaffected.
  Traceability: FR-012, SC-009

- [ ] T018 [US1] Implement cancel disambiguation request model when multiple delegated reminders match sender criteria
  Depends on: T017
  Touch points: src/Aluki.Runtime.Abstractions/Skills/DelegatedReminderSkill.cs; src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs
  Definition of Done: Ambiguous cancel requests produce a deterministic disambiguation prompt requiring recipient or reminder content selection.
  Traceability: FR-013, SC-007

- [ ] T019 [US1] Add delegated reminder intent and resolution telemetry for routing latency and branch metrics
  Depends on: T016
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs
  Definition of Done: Telemetry captures classification path, tier result, and recipient resolution latency for validation of SC targets.
  Traceability: FR-001, FR-002, SC-001

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Recipient and consent handling (Priority: P1)

**Goal**: Enforce explicit recipient opt-in before delivery and anti-spam policy limits with auditable policy decisions.

**Independent Test**: Unconsented recipients are blocked from delivery until opt-in is acquired, and sender anti-spam limits are enforced consistently.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add consent policy tests for explicit opt-in required before first delivery
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/DelegatedReminder/ConsentPolicyTests.cs
  Definition of Done: Tests verify lack of explicit opt-in defaults to unconsented and blocks delivery start.
  Traceability: FR-004, SC-002

- [ ] T021 [P] [US2] Add anti-spam policy tests for rolling 24-hour sender cap enforcement
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/DelegatedReminder/AntiSpamPolicyTests.cs
  Definition of Done: Tests verify configurable cap (default 10/day), limit exceed behavior, and policy response payload.
  Traceability: FR-005, SC-003

### Implementation for User Story 2

- [ ] T022 [US2] Implement PolicyDecision integration for consent check and delegated anti-spam gate
  Depends on: T010, T020, T021
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; src/Aluki.Runtime.Abstractions/Skills/PolicyDecisionContracts.cs
  Definition of Done: Delivery eligibility requires successful consent and anti-spam decisions before orchestration can schedule delivery.
  Traceability: FR-004, FR-005, SC-002, SC-003

- [ ] T023 [US2] Implement consent acquisition workflow activity and consent registry upsert behavior
  Depends on: T022
  Touch points: src/Aluki.Runtime.Functions/Functions/ConsentAcquisitionActivity.cs; src/Aluki.Runtime.Host/Services/ConsentRegistryService.cs
  Definition of Done: Consent acquisition outcome is persisted with scope and timestamp, including sender-scoped opt-in support.
  Traceability: FR-004, FR-011, SC-002, SC-008

- [ ] T024 [US2] Emit delegated_reminder.consent_acquired audit event with actor and scope identifiers
  Depends on: T023
  Touch points: src/Aluki.Runtime.Host/Services/ConsentRegistryService.cs
  Definition of Done: Consent acquisition events are immutable and include tenant, recipient, sender scope, and correlation id.
  Traceability: FR-011, SC-008

- [ ] T025 [US2] Implement policy-denied and consent-pending user feedback mapping for delegated flow responses
  Depends on: T022
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs
  Definition of Done: Sender receives clear, deterministic status for consent-required and anti-spam-denied outcomes.
  Traceability: FR-004, FR-005, FR-010, SC-005

- [ ] T026 [US2] Implement recipient identity update handling when sender provides missing phone or handle during contact-capture
  Depends on: T015, T023
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; db/migrations/004_create_delegated_reminders.sql
  Definition of Done: Tier 2 and Tier 3 flows can continue from partial identity to resolved recipient with persisted linkage.
  Traceability: FR-002, FR-003, SC-002

- [ ] T027 [US2] Add integration test for consent-required flow from unconsented recipient to successful opt-in and schedule readiness
  Depends on: T023, T024, T026
  Touch points: tests/Aluki.Runtime.Tests/Integration/DelegatedConsentFlowTests.cs
  Definition of Done: End-to-end test proves no delivery before consent and successful transition after explicit opt-in.
  Traceability: FR-004, FR-011, SC-002, SC-008

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Delivery and management (Priority: P2)

**Goal**: Deliver delegated reminders at due time with bounded retry behavior, cancellation/recall constraints, and sender failure visibility.

**Independent Test**: Delivery starts on time, retries only for transient failures within bounded window, and sender receives classified failure notification on terminal failure.

### Tests for User Story 3

- [ ] T028 [P] [US3] Add orchestration tests for cancellation windows and recall boundary rules
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/DelegatedReminder/DelegatedCancellationWindowTests.cs
  Definition of Done: Tests verify cancellation accepted up to 30s before due time, rejected afterward, and recall blocked once delivery phase starts.
  Traceability: FR-008, FR-009, SC-006

- [ ] T029 [P] [US3] Add integration tests for retry schedule and terminal failure classification paths
  Depends on: T003, T011
  Touch points: tests/Aluki.Runtime.Tests/Integration/DelegatedDeliveryRetryTests.cs
  Definition of Done: Tests prove retries at 1,2,4,8,16 seconds for transient failures and immediate terminal behavior for permanent classifications.
  Traceability: FR-006, FR-007, SC-004

### Implementation for User Story 3

- [ ] T030 [US3] Implement durable delegated reminder orchestrator for due-time trigger, delivery start, and callback handling
  Depends on: T009, T027, T028
  Touch points: src/Aluki.Runtime.Functions/Functions/DelegatedReminderOrchestrator.cs
  Definition of Done: Orchestrator handles schedule, state transitions, cancellation checks, and hands off delivery activities with correlation id.
  Traceability: FR-006, FR-008, FR-009, FR-011

- [ ] T031 [US3] Implement recipient resolution, delivery, and failure notification activities for delegated workflow
  Depends on: T030
  Touch points: src/Aluki.Runtime.Functions/Functions/RecipientResolutionActivity.cs; src/Aluki.Runtime.Functions/Functions/DeliveryActivity.cs
  Definition of Done: Activities return structured outcomes for success, transient failure, permanent failure, and sender notification dispatch.
  Traceability: FR-006, FR-007, FR-010

- [ ] T032 [US3] Persist delivery attempts and classify failures for retry-or-terminal decisions
  Depends on: T031
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; db/migrations/006_create_delivery_attempts.sql
  Definition of Done: Every attempt is recorded with classification and correlated to delegated reminder id for operational traceability.
  Traceability: FR-006, FR-007, FR-011, SC-004, SC-008

- [ ] T033 [US3] Implement sender-side failure notification messaging with recipient identity and failure class
  Depends on: T032
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs; src/Aluki.Runtime.Functions/Functions/DeliveryActivity.cs
  Definition of Done: Terminal failures produce sender notification within service target and include recipient identity plus failure category.
  Traceability: FR-010, NFR-003, SC-005

- [ ] T034 [US3] Emit mandatory delivery lifecycle audit events for started, succeeded, failed-terminal, and cancelled transitions
  Depends on: T030, T032, T033
  Touch points: src/Aluki.Runtime.Host/Services/DelegatedReminderService.cs
  Definition of Done: All required delegated reminder lifecycle events are emitted exactly once per transition with scope and actor metadata.
  Traceability: FR-011, NFR-005, SC-008

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Consolidate evidence, validate quickstart-level behaviors, and finalize operational readiness artifacts.

- [ ] T035 [P] Build FR/SC verification checklist and map each requirement to concrete test or telemetry evidence
  Depends on: T019, T024, T027, T029, T034
  Touch points: specs/006-delegated-reminders/checklists/requirements.md
  Definition of Done: Every FR-001..FR-013 and SC-001..SC-009 has a verification status and referenced evidence location.
  Traceability: FR-001..FR-013, SC-001..SC-009

- [ ] T036 Run end-to-end delegated reminder validation scenarios and document observed results and gaps
  Depends on: T027, T028, T029, T033, T035
  Touch points: specs/006-delegated-reminders/quickstart.md
  Definition of Done: Scenario execution notes cover routing tiers, consent gating, retries, cancellation, and sender failure notifications.
  Traceability: FR-001..FR-013, SC-001..SC-009

- [ ] T037 Finalize delegated reminder operational runbook for support triage, audit lookup, and retry diagnostics
  Depends on: T034, T036
  Touch points: docs/DELEGATED_REMINDERS_OPERATIONS.md
  Definition of Done: Runbook explains event taxonomy, correlation tracing steps, retry diagnostics, and failure notification troubleshooting.
  Traceability: FR-006, FR-007, FR-010, FR-011, SC-004, SC-005, SC-008

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): Starts immediately.
- Foundational (Phase 2): Depends on setup and blocks all user stories.
- User Stories (Phase 3+): Depend on foundational completion.
- Polish (Phase 6): Depends on all selected user stories and validation evidence.

### User Story Dependencies

- US1 (P1): Starts after Phase 2 and delivers MVP delegated routing and recipient resolution.
- US2 (P1): Starts after Phase 2 and can proceed parallel with late US1 tasks except where consent flow depends on recipient resolution outputs (T015).
- US3 (P2): Starts after consent/routing primitives are stable (T027) and foundational durable contracts exist.

### Critical Task Chain

T001 -> T002 -> T004 -> T005 -> T007 -> T010 -> T011 -> T014 -> T015 -> T022 -> T023 -> T027 -> T030 -> T031 -> T032 -> T033 -> T034 -> T035 -> T036 -> T037

---

## Parallel Opportunities

- Setup: T003 parallel with T002 after T001.
- Foundational: T005 and T006 parallel after T004; T009 parallel after T008.
- US1 tests: T012 and T013 in parallel.
- US2 policy tests: T020 and T021 in parallel.
- US3 validation tests: T028 and T029 in parallel.
- Polish: T035 can begin once core evidence-producing tasks complete.

### Parallel Example: US1

- Run together: T012 and T013.
- Run together: T017 and T019 after T014/T016 are complete.

### Parallel Example: US2

- Run together: T020 and T021.
- Run together: T025 and T026 after T022 is complete.

### Parallel Example: US3

- Run together: T028 and T029.
- Run together: T033 and non-conflicting observability checks once T032 is available.

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate independent routing behavior and delegated query separation.
4. Demo MVP with known-contact, phone-only, and unknown-recipient tiers.

### Incremental Delivery

1. Add US1 for delegated routing baseline.
2. Add US2 for consent and anti-spam enforcement.
3. Add US3 for due-time delivery, retry/failure handling, and sender notification.
4. Execute polish phase and finalize operational artifacts.

---

## Requirement Traceability Matrix

- FR-001: T001, T008, T011, T012, T014, T019
- FR-002: T008, T013, T015, T026
- FR-003: T004, T010, T013, T015, T016, T026
- FR-004: T005, T008, T020, T022, T023, T025, T027
- FR-005: T002, T008, T021, T022, T025
- FR-006: T002, T006, T029, T030, T031, T032, T037
- FR-007: T006, T008, T029, T031, T032, T037
- FR-008: T002, T009, T028, T030, T034
- FR-009: T009, T028, T030
- FR-010: T006, T025, T031, T033, T037
- FR-011: T004, T005, T006, T007, T016, T023, T024, T030, T032, T034
- FR-012: T001, T004, T009, T011, T012, T014, T017
- FR-013: T018

- SC-001: T013, T015, T019
- SC-002: T013, T020, T022, T023, T026, T027
- SC-003: T021, T022
- SC-004: T002, T006, T029, T032, T037
- SC-005: T025, T033, T037
- SC-006: T002, T028, T030
- SC-007: T018
- SC-008: T004, T005, T006, T016, T024, T027, T032, T034, T037
- SC-009: T012, T014, T017

---

## Notes

- [P] tasks are designed to avoid same-file conflicts where possible.
- Story tasks are independently verifiable through their stated independent tests.
- Keep migration scripts idempotent and aligned with existing migration naming conventions.
- Preserve constitutional boundaries: Orleans/session concerns in host, long-running retries in Durable Functions.
