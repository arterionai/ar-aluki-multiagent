# Tasks: Scheduled Reminders

**Input**: Design documents from /specs/005-reminders/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/reminder-delivery-contract.yaml, quickstart.md

**Tests**: Included (plan and quickstart require unit/integration/contract validation for recurrence, idempotency, retries, and quotas).

**Organization**: Tasks are grouped by user story and ordered by dependency so each story is independently implementable and testable.

## Implementation status & adaptations (PR1 — 2026-06-22)

Adapted to the real codebase (same deviations as SB-004):

- **Project**: implemented in a new `src/Aluki.Runtime.Reminders` project (mirrors
  `Memory`/`Extraction`), not in `Host`.
- **Migration**: single `db/migrations/010_reminders.sql` (the spec's `004..007`
  numbers were already taken). Added to the deploy loop + `DbCaptureFixture`.
- **Scheduling**: **timer-triggered sweep** (`ReminderSweepFunction`, every minute)
  instead of Durable Functions — durable orchestration with per-reminder timers and
  retry-backoff is a documented follow-up. Cross-tenant claim uses a SECURITY DEFINER
  `app.claim_due_reminders` function (RLS-safe, SKIP LOCKED).
- **Delivery**: pluggable `IReminderDeliveryChannel` with a logging/persisting stub
  (`LoggingReminderDeliveryChannel`); a real outbound channel (WhatsApp/in-app) plugs
  in later with no engine change.

**Done in PR1**: foundation (schema, contracts, store, scope guard, DI), **US1
one-shot** (create/list/snooze/cancel + lifecycle audit), creation-time **quota
enforcement** (US3 core), and the fire-sweep with idempotent delivery + terminal
`delivered`/`delivery_failed`/`expired_undelivered`. Tests: `ReminderPolicyTests`
(unit), `ReminderContractTests` (contract), `ReminderLifecycleIntegrationTests`
(integration: create/idempotency/snooze/cancel/quota/sweep).

**Deferred to follow-up PRs**: US2 recurring (recurrence calculator + DST; create
currently returns `422 unsupported_recurrence`), multi-attempt retry backoff
(5s/25s/125s), and the standalone telemetry emitter.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare projects, dependencies, and configuration shared by all reminder stories.

- [ ] T001 Add reminders test projects and solution references in Aluki.Runtime.slnx, tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj, tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj, tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj
- [ ] T002 Update runtime package dependencies for Durable Functions, PostgreSQL, timezone conversion, and test tooling in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj and src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
- [ ] T003 [P] Add reminder configuration sections and defaults (quota, retry backoff, snooze caps, timezone policy) in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json
- [ ] T004 [P] Add local development overrides for reminder orchestration in src/Aluki.Runtime.Functions/host.json and src/Aluki.Runtime.Functions/local.settings.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core schema, contracts, security guards, repositories, and orchestration scaffolding required before user stories.

**Critical**: No user story work starts until this phase is complete.

- [ ] T005 Create reminder schema migrations (reminders, recurrence rules, delivery attempts, audit events, quotas) in db/migrations/004_init_reminders.sql, db/migrations/005_init_reminder_rules.sql, db/migrations/006_init_reminder_audit.sql, db/migrations/007_init_reminder_quotas.sql
- [ ] T006 [P] Define reminder domain contracts and enums in src/Aluki.Runtime.Abstractions/Skills/Reminder/ReminderContracts.cs
- [ ] T007 [P] Define reminder repository abstractions for persistence, delivery attempts, quota, and audit in src/Aluki.Runtime.Abstractions/Skills/Reminder/IReminderPersistence.cs
- [ ] T008 [P] Add principal and tenant scope guard abstractions for reminder side effects in src/Aluki.Runtime.Abstractions/Security/IReminderScopeGuard.cs
- [ ] T009 Implement PostgreSQL reminder repositories and migration bootstrap checks in src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderRepository.cs, src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderQuotaRepository.cs, src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderAuditRepository.cs
- [ ] T010 [P] Implement reminder telemetry and audit emitters (latency, delivery result, retry count, quota checks) in src/Aluki.Runtime.Host/Reminder/Observability/ReminderTelemetry.cs and src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs
- [ ] T011 Implement durable orchestration skeleton (schedule, fire, retry, terminal states) in src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs, src/Aluki.Runtime.Functions/Activities/ScheduleReminderActivity.cs, src/Aluki.Runtime.Functions/Activities/DeliverReminderActivity.cs, src/Aluki.Runtime.Functions/Activities/HandleFailedDeliveryActivity.cs
- [ ] T012 Register reminder services, repositories, scope guards, and orchestrators in src/Aluki.Runtime.Host/Program.cs and src/Aluki.Runtime.Functions/Program.cs

**Checkpoint**: Foundation ready. User stories can proceed.

---

## Phase 3: User Story 1 - One-shot reminder (Priority: P1) MVP

**Goal**: User can schedule a one-shot reminder and receive a confirmed notification at due time with idempotent delivery and audit trace.

**Independent Test**: Execute quickstart scenario 1 and verify persisted reminder, due-time fire, delivered status, and audit evidence.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add contract tests for reminder create/confirm payload and response shape in tests/contract/ReminderCreateContractTests.cs
- [ ] T014 [P] [US1] Add integration tests for one-shot create and due-time delivery in tests/integration/ReminderOneShotIntegrationTests.cs
- [ ] T015 [P] [US1] Add unit tests for idempotency key behavior and duplicate suppression in tests/unit/ReminderIdempotencyTests.cs

### Implementation for User Story 1

- [ ] T016 [US1] Implement ReminderCreateSkill request validation and persistence for one-shot reminders in src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs
- [ ] T017 [US1] Implement ReminderConfirmSkill to arm orchestration and confirmation response in src/Aluki.Runtime.Host/Reminder/Skills/ReminderConfirmSkillImpl.cs
- [ ] T018 [US1] Implement one-shot fire and delivery pipeline with attempt tracking in src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs and src/Aluki.Runtime.Functions/Activities/DeliverReminderActivity.cs
- [ ] T019 [US1] Implement reminder inbound endpoint and DTO mapping for one-shot commands in src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs
- [ ] T020 [US1] Emit lifecycle audit events (created, scheduled, firing, delivered) with correlation and principal context in src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs

**Checkpoint**: US1 independently functional and testable.

---

## Phase 4: User Story 2 - Recurring reminder (Priority: P2)

**Goal**: User can create recurring reminders (daily, weekly, monthly) with timezone-aware cadence and correct DST behavior.

**Independent Test**: Execute quickstart scenario 2 and verify recurring cadence persistence and correct next fire times across DST transitions.

### Tests for User Story 2

- [ ] T021 [P] [US2] Add contract tests for recurring cadence input and recurrence rule responses in tests/contract/ReminderRecurrenceContractTests.cs
- [ ] T022 [P] [US2] Add integration tests for daily/weekly/monthly recurrence and next-occurrence computation in tests/integration/ReminderRecurrenceIntegrationTests.cs
- [ ] T023 [P] [US2] Add integration tests for DST transition behavior in tests/integration/ReminderTimezoneDstIntegrationTests.cs
- [ ] T024 [P] [US2] Add unit tests for recurrence rule validation and local-time-to-UTC conversion in tests/unit/ReminderRecurrenceCalculatorTests.cs

### Implementation for User Story 2

- [ ] T025 [US2] Implement recurrence rule validation and persistence in src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs and src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderRepository.cs
- [ ] T026 [US2] Implement timezone-aware next-occurrence calculator (IANA timezone + DST-safe local-time semantics) in src/Aluki.Runtime.Host/Reminder/Time/ReminderRecurrenceCalculator.cs
- [ ] T027 [US2] Extend orchestrator to schedule subsequent recurring occurrences after each delivery in src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs
- [ ] T028 [US2] Extend endpoint/DTO mapping for recurring requests and recurrence metadata in src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs
- [ ] T029 [US2] Emit recurrence-specific audit events and telemetry (next occurrence, timezone used, DST path) in src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs and src/Aluki.Runtime.Host/Reminder/Observability/ReminderTelemetry.cs

**Checkpoint**: US2 independently functional and testable.

---

## Phase 5: User Story 3 - Quota enforcement (Priority: P2)

**Goal**: Quota and entitlement rules block new reminder creation when limits are exceeded while preserving delivery of already-scheduled reminders.

**Independent Test**: Execute quickstart scenario 4 and verify create-block behavior, clear limit messaging, and runtime behavior for already-scheduled reminders.

### Tests for User Story 3

- [ ] T030 [P] [US3] Add contract tests for quota-exceeded and entitlement messaging outcomes in tests/contract/ReminderQuotaContractTests.cs
- [ ] T031 [P] [US3] Add integration tests for creation-time quota checks and active-count derivation in tests/integration/ReminderQuotaIntegrationTests.cs
- [ ] T032 [P] [US3] Add unit tests for active reminder counting and tier limit policies in tests/unit/ReminderQuotaPolicyTests.cs

### Implementation for User Story 3

- [ ] T033 [US3] Implement BudgetPolicySkill and TenantScopeSkill enforcement for reminder create path in src/Aluki.Runtime.Host/Reminder/Policies/ReminderBudgetPolicySkill.cs and src/Aluki.Runtime.Host/Reminder/Policies/ReminderTenantScopeSkill.cs
- [ ] T034 [US3] Implement quota evaluator and active-count repository query path in src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderQuotaRepository.cs and src/Aluki.Runtime.Host/Reminder/Policies/ReminderQuotaEvaluator.cs
- [ ] T035 [US3] Integrate quota checks and limit response mapping in reminder create/confirm workflow in src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs and src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs
- [ ] T036 [US3] Emit quota_checked and quota_blocked audit/telemetry events with remaining quota details in src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs and src/Aluki.Runtime.Host/Reminder/Observability/ReminderTelemetry.cs

**Checkpoint**: US3 independently functional and testable.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Complete snooze, retries, terminal states, overdue handling, performance validation, and traceability closure.

- [ ] T037 [P] Implement snooze behavior (preset durations, same-instance reschedule, snooze_count increment, 24h cap) in src/Aluki.Runtime.Host/Reminder/Skills/SnoozeReminderSkillImpl.cs and src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs
- [ ] T038 [P] Add integration tests for snooze behavior and duplicate prevention in tests/integration/ReminderSnoozeIntegrationTests.cs
- [ ] T039 [P] Implement retry backoff and terminal outcomes (delivery_failed, expired_undelivered) in src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs and src/Aluki.Runtime.Functions/Activities/HandleFailedDeliveryActivity.cs
- [ ] T040 [P] Add integration tests for retry sequence (5s, 25s, 125s), terminal failure, and overdue expiration in tests/integration/ReminderRetryAndExpiryIntegrationTests.cs
- [ ] T041 [P] Add performance and SLA validation tests for fire latency, delivery success, and quota evaluation time in tests/integration/ReminderSlaIntegrationTests.cs
- [ ] T042 Update quickstart execution evidence and scenario status in specs/005-reminders/quickstart.md
- [ ] T043 Record FR/SC verification evidence and closure status in specs/005-reminders/checklists/requirements.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 has no dependencies.
- Phase 2 depends on T001-T004.
- Phase 3 (US1) depends on T005-T012.
- Phase 4 (US2) depends on T005-T012 and one-shot pipeline scaffolding from T016-T020.
- Phase 5 (US3) depends on T005-T012 and creation pipeline from T016-T019.
- Phase 6 depends on completion of T016-T036.

### Task-Level Dependencies

- T009 depends on T005, T006, T007.
- T010 depends on T006, T007.
- T011 depends on T005, T006.
- T012 depends on T008-T011.
- T016 depends on T006-T009, T012.
- T017 depends on T006, T011, T016.
- T018 depends on T011, T017.
- T019 depends on T006, T016.
- T020 depends on T010, T016-T019.
- T025 depends on T006, T007, T009, T016.
- T026 depends on T006, T025.
- T027 depends on T018, T026.
- T028 depends on T019, T025.
- T029 depends on T010, T026-T028.
- T033 depends on T008, T012.
- T034 depends on T007, T009.
- T035 depends on T016, T019, T033, T034.
- T036 depends on T010, T034, T035.
- T037 depends on T018, T026, T029.
- T038 depends on T037.
- T039 depends on T018, T029.
- T040 depends on T039.
- T041 depends on T035, T039.
- T042 depends on T038, T040, T041.
- T043 depends on T042.

### User Story Dependencies

- US1 starts immediately after Phase 2 and is the MVP.
- US2 depends on foundational phase and one-shot scheduling pipeline from US1.
- US3 depends on foundational phase and create-path integration from US1, but is independent from US2 recurrence logic.

### Story Completion Order (Dependency-Ordered)

1. US1 - One-shot reminder (MVP)
2. US2 - Recurring reminder
3. US3 - Quota enforcement

---

## Parallel Opportunities

### Setup and Foundation

- T003 and T004 can run in parallel with T001-T002.
- T006, T007, and T008 can run in parallel after T005 planning is complete.
- T010 can run in parallel with T009 after T006-T007.

### US1

- T013, T014, and T015 can run in parallel.
- T018 and T019 can run in parallel after T016-T017 handoff contracts are stable.

### US2

- T021, T022, T023, and T024 can run in parallel.
- T026 and T028 can run in parallel after T025.

### US3

- T030, T031, and T032 can run in parallel.
- T034 can run in parallel with T033 after foundational repositories are in place.

### Polish

- T037, T039, and T041 can run in parallel after T016-T036.
- T038 and T040 can run in parallel once their implementation dependencies are complete.

---

## Parallel Example per User Story

### US1

- T013 + T014 + T015 in parallel, then T016 -> T017 -> (T018 + T019) -> T020.

### US2

- T021 + T022 + T023 + T024 in parallel, then T025 -> (T026 + T028) -> T027 -> T029.

### US3

- T030 + T031 + T032 in parallel, then (T033 + T034) -> T035 -> T036.

---

## File Touch Points by Story

- US1: src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs, src/Aluki.Runtime.Host/Reminder/Skills/ReminderConfirmSkillImpl.cs, src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs, src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs, src/Aluki.Runtime.Functions/Activities/DeliverReminderActivity.cs, src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs, tests/contract/ReminderCreateContractTests.cs, tests/integration/ReminderOneShotIntegrationTests.cs, tests/unit/ReminderIdempotencyTests.cs
- US2: src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs, src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderRepository.cs, src/Aluki.Runtime.Host/Reminder/Time/ReminderRecurrenceCalculator.cs, src/Aluki.Runtime.Functions/Orchestrators/ReminderLifecycleOrchestrator.cs, src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs, src/Aluki.Runtime.Host/Reminder/Observability/ReminderTelemetry.cs, tests/contract/ReminderRecurrenceContractTests.cs, tests/integration/ReminderRecurrenceIntegrationTests.cs, tests/integration/ReminderTimezoneDstIntegrationTests.cs, tests/unit/ReminderRecurrenceCalculatorTests.cs
- US3: src/Aluki.Runtime.Host/Reminder/Policies/ReminderBudgetPolicySkill.cs, src/Aluki.Runtime.Host/Reminder/Policies/ReminderTenantScopeSkill.cs, src/Aluki.Runtime.Host/Reminder/Policies/ReminderQuotaEvaluator.cs, src/Aluki.Runtime.Host/Reminder/Persistence/PostgresReminderQuotaRepository.cs, src/Aluki.Runtime.Host/Reminder/Skills/ReminderCreateSkillImpl.cs, src/Aluki.Runtime.Host/Endpoints/ReminderEndpoints.cs, src/Aluki.Runtime.Host/Reminder/Audit/ReminderAuditWriter.cs, tests/contract/ReminderQuotaContractTests.cs, tests/integration/ReminderQuotaIntegrationTests.cs, tests/unit/ReminderQuotaPolicyTests.cs

---

## Definition of Done and Traceability (FR/SC)

### Functional Requirement Index

- FR-001: Create one-shot reminders with confirmation and due-time delivery.
- FR-002: Create recurring reminders (daily/weekly/monthly) and persist recurrence rules.
- FR-003: Done/cancel and lifecycle progression with clear status transitions.
- FR-004: Snooze with presets (5m/15m/30m/1h/next day) on same reminder instance.
- FR-005: Enforce quota and entitlement limits at creation time with clear denial messaging.
- FR-006: Enforce tenant scope and principal context before side effects.
- FR-007: Idempotent delivery using reminder_id + scheduled_time (+ attempt) key.
- FR-008: Retry transient failures with exponential backoff and terminal outcomes.
- FR-009: Store explicit timezone IDs and preserve local-time cadence across DST.
- FR-010: Emit immutable audit trail for reminder lifecycle and policy decisions.
- FR-011: Expire undelivered reminders after overdue window (24h) as expired_undelivered.
- FR-012: Use Durable Functions boundary for long-running reminder workflows.

### Success Criteria Index

- SC-001: One-shot reminder scenario validates persisted schedule and delivery at due time.
- SC-002: Recurring reminders fire correctly by cadence and compute next occurrence accurately.
- SC-003: Snooze reschedules correctly and avoids duplicate delivery.
- SC-004: Quota-exceeded requests are blocked with clear feedback and no side effects.
- SC-005: Retry policy executes at 5s, 25s, 125s and stops after max attempts.
- SC-006: Duplicate processing attempts are deduplicated by idempotency key.
- SC-007: Terminal statuses (delivered, delivery_failed, expired_undelivered, user_cancelled) are persisted and visible.
- SC-008: Audit records include lifecycle event type, principal context, and failure reasons.
- SC-009: Reminder firing latency meets P95 <= 500ms.
- SC-010: Delivery success rate meets >= 99.5%.
- SC-011: Quota evaluation latency stays below 100ms.
- SC-012: Overdue reminders transition to expired_undelivered after 24h window.

### US1 DoD and Traceability

- DoD-US1: One-shot reminder can be created, confirmed, delivered once, and fully audited under tenant scope.
- DoD-US1: Contract and integration tests for create and delivery pass with idempotency coverage.
- Traceability-US1 Tests (T013-T015): FR-001, FR-006, FR-007, FR-010, FR-012; SC-001, SC-006, SC-008.
- Traceability-US1 Implementation (T016-T020): FR-001, FR-003, FR-006, FR-007, FR-010, FR-012; SC-001, SC-006, SC-007, SC-008.

### US2 DoD and Traceability

- DoD-US2: Recurrence rules persist and trigger by cadence with timezone-aware DST-safe local-time behavior.
- DoD-US2: Recurring delivery and recurrence telemetry/audit evidence are available for each occurrence.
- Traceability-US2 Tests (T021-T024): FR-002, FR-009; SC-002.
- Traceability-US2 Implementation (T025-T029): FR-002, FR-009, FR-010, FR-012; SC-002, SC-008.

### US3 DoD and Traceability

- DoD-US3: Creation is denied when quota exceeds entitlement limits with clear user-facing response.
- DoD-US3: Quota checks are tenant-scoped, auditable, and measured for latency targets.
- Traceability-US3 Tests (T030-T032): FR-005, FR-006, FR-010; SC-004, SC-008, SC-011.
- Traceability-US3 Implementation (T033-T036): FR-005, FR-006, FR-010; SC-004, SC-008, SC-011.

### Cross-Cutting DoD and Traceability

- DoD-Cross: Snooze, retry backoff, terminal outcomes, and overdue expiration work end-to-end.
- DoD-Cross: SLA validation and evidence closure are captured in quickstart and requirements checklist.
- Traceability-Cross (T037-T041): FR-004, FR-008, FR-011; SC-003, SC-005, SC-007, SC-009, SC-010, SC-012.
- Traceability-Evidence (T042-T043): FR-001 through FR-012; SC-001 through SC-012.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate quickstart scenario 1 and idempotency checks.
4. Freeze MVP baseline before adding recurrence/quota behaviors.

### Incremental Delivery

1. Ship US1 (one-shot reminders).
2. Add US2 (recurrence and DST-safe scheduling).
3. Add US3 (quota and entitlement guards).
4. Finish cross-cutting reliability and SLA validation.

### Format Validation

All tasks use strict checklist format: checkbox, task ID, optional [P], required [USx] on story tasks, and explicit file path in each task description.
