# Tasks: Feedback Suggestions Capture

**Input**: Design documents from /specs/007-feedback-suggestions/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/whatsapp-inbound-contract.yaml

**Tests**: Included. This feature has explicit acceptance and reliability outcomes (idempotency, isolation, lifecycle, and visibility) that require contract/integration coverage.

**Organization**: Tasks are grouped by user story and dependency-ordered so each story remains independently implementable and testable.

## Requirement Catalog (FR/SC)

### Functional Requirements (FR)

- FR-001: Detect suggestion intent on inbound text and route suggestion flows to FeedbackAgent.
- FR-002: Persist suggestions in a dedicated domain model separated from normal memory artifacts.
- FR-003: Exclude suggestions from normal recall result sets from `captured` onward.
- FR-004: Maintain exactly one active suggestion window per tenant-user pair.
- FR-005: Link audio/photo/text follow-ups to the active suggestion within a 30-minute window.
- FR-006: Enforce payload constraints (text inline <=5 KB, audio <=50 MB, photo <=10 MB JPEG/PNG, max 10 attachments total).
- FR-007: Store attachments by reference (blob URI) with metadata hash, MIME type, and lifecycle fields.
- FR-008: Enforce duplicate detection with composite key `(message_id + payload_hash)` and 5-minute idempotency window.
- FR-009: Implement one-way lifecycle states (`captured -> enriched -> sent_user -> archived`) with no reverse transitions.
- FR-010: Trigger lifecycle transitions by rules (window close to `enriched`, confirmation to `sent_user`, +90d to `archived`).
- FR-011: Expose only `sent_user` state to end users while preserving full internal/audit visibility.
- FR-012: Emit immutable audit trail for state transitions and duplicate-ignored events.
- FR-013: Emit telemetry for suggestion lifecycle and attachment linkage with redaction-safe payload handling.
- FR-014: Enforce tenant/user scope and RLS for all suggestion persistence and querying.
- FR-015: Include suggestion usage and media guidance in first-interaction welcome flow.

### Success Criteria (SC)

- SC-001: Suggestions never appear in normal recall result sets.
- SC-002: Follow-up window expiration causes a new suggestion artifact on subsequent follow-up.
- SC-003: Duplicate webhook deliveries are idempotent.
- SC-004: Active suggestion window is 30 minutes and unique per tenant-user pair.
- SC-005: Attachment validation and limits are enforced (size, MIME, count).
- SC-006: Lifecycle transitions are fully auditable with actor/reason/timestamps.
- SC-007: Welcome message explicitly documents suggestion plus media submission path.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare host, abstractions, and tests scaffolding for suggestion capture.

- [ ] T001 Create feedback-suggestions source and test scaffolding in src/Aluki.Runtime.Host/Feedback/, src/Aluki.Runtime.Abstractions/Skills/Feedback/, tests/contract/Aluki.Runtime.ContractTests/, tests/integration/Aluki.Runtime.IntegrationTests/, tests/unit/Aluki.Runtime.UnitTests/
  Depends on: None
  Touch points: src/Aluki.Runtime.Host/Feedback/.gitkeep; src/Aluki.Runtime.Abstractions/Skills/Feedback/.gitkeep; tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj; tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj; tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj
  Definition of Done: Required folders and test projects exist and are wired in solution build.
  Traceability: FR-001..FR-015, SC-001..SC-007

- [ ] T002 Update runtime package references and host feature configuration for suggestion capture in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj, src/Aluki.Runtime.Host/appsettings.json, src/Aluki.Runtime.Host/appsettings.Development.json
  Depends on: T001
  Touch points: src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj; src/Aluki.Runtime.Host/appsettings.json; src/Aluki.Runtime.Host/appsettings.Development.json
  Definition of Done: Host includes options for suggestion window, idempotency, attachment limits, and storage references.
  Traceability: FR-004, FR-005, FR-006, FR-008, FR-010

- [ ] T003 [P] Add feature constants and shared enums for suggestion states and attachment types in src/Aluki.Runtime.Abstractions/Skills/Feedback/SuggestionStates.cs and src/Aluki.Runtime.Abstractions/Skills/Feedback/AttachmentTypes.cs
  Depends on: T001
  Touch points: src/Aluki.Runtime.Abstractions/Skills/Feedback/SuggestionStates.cs; src/Aluki.Runtime.Abstractions/Skills/Feedback/AttachmentTypes.cs
  Definition of Done: Shared enums compile and match spec lifecycle and attachment domains.
  Traceability: FR-006, FR-009

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core schema, RLS, contracts, and platform primitives required before any story.

**Critical**: No user story implementation starts before this phase is complete.

- [ ] T004 Create suggestion domain migration (suggestions, suggestion_attachments, suggestion_state_transitions, indexes, constraints) in db/migrations/004_feedback_suggestions.sql
  Depends on: T002, T003
  Touch points: db/migrations/004_feedback_suggestions.sql
  Definition of Done: Tables, constraints, and indexes from data-model.md are implemented with lifecycle and attachment constraints.
  Traceability: FR-002, FR-005, FR-006, FR-007, FR-009, FR-010, FR-012, SC-004, SC-005, SC-006

- [ ] T005 [P] Create RLS policy migration for suggestion tables in db/migrations/005_feedback_suggestions_rls.sql
  Depends on: T004
  Touch points: db/migrations/005_feedback_suggestions_rls.sql
  Definition of Done: All suggestion tables enforce tenant/user-scoped reads and writes.
  Traceability: FR-014

- [ ] T006 Define feedback skill contracts and repository abstractions in src/Aluki.Runtime.Abstractions/Skills/Feedback/FeedbackContracts.cs and src/Aluki.Runtime.Abstractions/Skills/Feedback/IFeedbackPersistence.cs
  Depends on: T003
  Touch points: src/Aluki.Runtime.Abstractions/Skills/Feedback/FeedbackContracts.cs; src/Aluki.Runtime.Abstractions/Skills/Feedback/IFeedbackPersistence.cs
  Definition of Done: Contracts represent capture, link, transition, dedupe, and welcome guidance operations.
  Traceability: FR-001, FR-002, FR-005, FR-008, FR-009, FR-015

- [ ] T007 [P] Implement PostgreSQL feedback repositories for suggestions, attachments, transitions, and active-window lookup in src/Aluki.Runtime.Host/Feedback/Persistence/PostgresFeedbackRepository.cs
  Depends on: T004, T005, T006
  Touch points: src/Aluki.Runtime.Host/Feedback/Persistence/PostgresFeedbackRepository.cs
  Definition of Done: Repository supports tenant-scoped CRUD, active window retrieval, dedupe lookups, and state transition persistence.
  Traceability: FR-002, FR-004, FR-005, FR-008, FR-009, FR-010, FR-014

- [ ] T008 [P] Implement attachment reference store skill with metadata hashing and validation in src/Aluki.Runtime.Host/Feedback/Skills/AttachmentStoreSkill.cs
  Depends on: T002, T006
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/AttachmentStoreSkill.cs
  Definition of Done: Skill validates media constraints, computes SHA-256 hash, and persists blob reference metadata.
  Traceability: FR-006, FR-007, SC-005

- [ ] T009 [P] Implement idempotency guard service for `(message_id + payload_hash)` 5-minute window in src/Aluki.Runtime.Host/Feedback/Skills/SuggestionIdempotencySkill.cs
  Depends on: T006, T007
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionIdempotencySkill.cs
  Definition of Done: Duplicate deliveries return existing suggestion linkage without side effects and log duplicate reason context.
  Traceability: FR-008, SC-003

- [ ] T010 [P] Implement feedback audit and telemetry emitters in src/Aluki.Runtime.Host/Feedback/Observability/FeedbackTelemetry.cs and src/Aluki.Runtime.Host/Feedback/Audit/FeedbackAuditWriter.cs
  Depends on: T006
  Touch points: src/Aluki.Runtime.Host/Feedback/Observability/FeedbackTelemetry.cs; src/Aluki.Runtime.Host/Feedback/Audit/FeedbackAuditWriter.cs
  Definition of Done: Lifecycle and duplicate events are emitted with redaction-safe payload and required dimensions.
  Traceability: FR-012, FR-013, SC-006

- [ ] T011 Wire Feedback feature dependency injection and options bootstrap in src/Aluki.Runtime.Host/Program.cs
  Depends on: T007, T008, T009, T010
  Touch points: src/Aluki.Runtime.Host/Program.cs
  Definition of Done: Host resolves feedback repositories/skills, options, and observability services on startup.
  Traceability: FR-001..FR-015

- [ ] T012 Update inbound webhook contract and feature checklist for suggestion routing and dedupe behavior in specs/007-feedback-suggestions/contracts/whatsapp-inbound-contract.yaml and specs/007-feedback-suggestions/checklists/requirements.md
  Depends on: T006, T009
  Touch points: specs/007-feedback-suggestions/contracts/whatsapp-inbound-contract.yaml; specs/007-feedback-suggestions/checklists/requirements.md
  Definition of Done: Contract and checklist capture canonical routing, idempotency, and payload validation rules.
  Traceability: FR-001, FR-006, FR-008, SC-003, SC-005

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 - Save suggestion separately (Priority: P1) 🎯 MVP

**Goal**: Capture text suggestion intent into a dedicated suggestion domain and confirm receipt.

**Independent Test**: A clear suggestion text creates one suggestion artifact in suggestion tables, remains hidden from normal recall, and confirmation is returned.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add contract tests for suggestion intent routing and accepted response payload in tests/contract/Aluki.Runtime.ContractTests/FeedbackSuggestionInboundContractTests.cs
  Depends on: T001, T011, T012
  Touch points: tests/contract/Aluki.Runtime.ContractTests/FeedbackSuggestionInboundContractTests.cs
  Definition of Done: Tests validate route-to-feedback behavior and response schema for suggestion captures.
  Traceability: FR-001, FR-002, FR-011

- [ ] T014 [P] [US1] Add integration tests proving suggestions are persisted outside recall domain in tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionIsolationTests.cs
  Depends on: T001, T007, T011
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionIsolationTests.cs
  Definition of Done: Tests prove suggestion rows are created in dedicated tables and excluded from memory recall queries.
  Traceability: FR-002, FR-003, SC-001

### Implementation for User Story 1

- [ ] T015 [US1] Implement suggestion intent detection skill for text payloads in src/Aluki.Runtime.Host/Feedback/Skills/SuggestionIntentSkill.cs
  Depends on: T006, T011
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionIntentSkill.cs
  Definition of Done: Skill applies detection signals and confidence thresholds to classify suggestion intents.
  Traceability: FR-001

- [ ] T016 [US1] Implement FeedbackAgent orchestration for `captured` state creation and confirmation dispatch in src/Aluki.Runtime.Host/Feedback/Agents/FeedbackAgent.cs
  Depends on: T007, T009, T010, T015
  Touch points: src/Aluki.Runtime.Host/Feedback/Agents/FeedbackAgent.cs
  Definition of Done: Agent stores suggestion as `captured`, enforces dedupe, and returns confirmation flow outputs.
  Traceability: FR-002, FR-008, FR-009, FR-011, SC-003

- [ ] T017 [US1] Integrate webhook inbound routing to FeedbackAgent for suggestion intents in src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs
  Depends on: T011, T015, T016
  Touch points: src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs
  Definition of Done: Inbound function routes suggestion intents to FeedbackAgent and preserves existing fallback path.
  Traceability: FR-001, FR-011

- [ ] T018 [US1] Apply recall exclusion filter for suggestion artifact type in src/Aluki.Runtime.Host/Memory/MemoryQueryService.cs
  Depends on: T007, T016
  Touch points: src/Aluki.Runtime.Host/Memory/MemoryQueryService.cs
  Definition of Done: Query layer excludes suggestion artifacts from normal recall results.
  Traceability: FR-003, SC-001

- [ ] T019 [US1] Add state transition audit for `captured` creation and user confirmation visibility in src/Aluki.Runtime.Host/Feedback/Skills/SuggestionLifecycleAuditSkill.cs
  Depends on: T010, T016
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionLifecycleAuditSkill.cs
  Definition of Done: Audit records include prior/new state, actor, reason, and timestamp for initial lifecycle transitions.
  Traceability: FR-011, FR-012, FR-013, SC-006

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Add context via audio/photo (Priority: P2)

**Goal**: Link follow-up media to active suggestion windows with strict window, validation, and dedupe rules.

**Independent Test**: Attachments within 30 minutes link to same suggestion; after expiration a new suggestion begins; duplicates are ignored idempotently.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add integration tests for 30-minute active window linking and expiration behavior in tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionWindowIntegrationTests.cs
  Depends on: T001, T007, T011, T016
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionWindowIntegrationTests.cs
  Definition of Done: Tests verify one active window per tenant-user and expiration-driven new suggestion creation.
  Traceability: FR-004, FR-005, FR-010, SC-002, SC-004

- [ ] T021 [P] [US2] Add integration tests for attachment validation and limits in tests/integration/Aluki.Runtime.IntegrationTests/FeedbackAttachmentValidationIntegrationTests.cs
  Depends on: T001, T008, T011
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/FeedbackAttachmentValidationIntegrationTests.cs
  Definition of Done: Tests validate MIME/size thresholds and max attachment-count enforcement.
  Traceability: FR-006, FR-007, SC-005

- [ ] T022 [P] [US2] Add integration tests for duplicate follow-up suppression in tests/integration/Aluki.Runtime.IntegrationTests/FeedbackDuplicateFollowupIntegrationTests.cs
  Depends on: T001, T009, T011, T016
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/FeedbackDuplicateFollowupIntegrationTests.cs
  Definition of Done: Tests prove duplicates by message+hash are ignored within 5 minutes with no extra writes.
  Traceability: FR-008, SC-003

### Implementation for User Story 2

- [ ] T023 [US2] Implement active suggestion window resolver and close/reopen policy in src/Aluki.Runtime.Host/Feedback/Skills/ActiveSuggestionWindowSkill.cs
  Depends on: T007, T016
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/ActiveSuggestionWindowSkill.cs
  Definition of Done: Resolver enforces one active window and new-intent close/reopen semantics.
  Traceability: FR-004, FR-005, SC-004

- [ ] T024 [US2] Implement follow-up attachment linking flow with blob references and `linked_at` timestamps in src/Aluki.Runtime.Host/Feedback/Skills/SuggestionAttachmentLinkSkill.cs
  Depends on: T008, T009, T023
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionAttachmentLinkSkill.cs
  Definition of Done: Audio/photo/text follow-ups in active window are linked to same suggestion with metadata persistence.
  Traceability: FR-005, FR-006, FR-007, FR-008, SC-005

- [ ] T025 [US2] Implement lifecycle transition logic for `captured -> enriched` when window closes in src/Aluki.Runtime.Host/Feedback/Skills/SuggestionWindowTransitionSkill.cs
  Depends on: T010, T023, T024
  Touch points: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionWindowTransitionSkill.cs
  Definition of Done: Transition occurs on window closure and records audit/telemetry with reason.
  Traceability: FR-009, FR-010, FR-012, FR-013, SC-006

- [ ] T026 [US2] Integrate media follow-up routing from inbound handler to attachment-linking flow in src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs
  Depends on: T017, T024, T025
  Touch points: src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs
  Definition of Done: Media follow-ups are linked to active suggestion or new suggestion based on window state.
  Traceability: FR-005, FR-010, SC-002

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Discoverability in welcome (Priority: P3)

**Goal**: Ensure first interaction welcome explains how to submit suggestions and media context.

**Independent Test**: First-contact welcome message includes explicit suggestion and media guidance and can be validated by contract/integration tests.

### Tests for User Story 3

- [ ] T027 [P] [US3] Add contract tests for welcome payload content requirements in tests/contract/Aluki.Runtime.ContractTests/FeedbackWelcomeContractTests.cs
  Depends on: T001, T011
  Touch points: tests/contract/Aluki.Runtime.ContractTests/FeedbackWelcomeContractTests.cs
  Definition of Done: Contract tests assert welcome text contains suggestion and media guidance clauses.
  Traceability: FR-015, SC-007

- [ ] T028 [P] [US3] Add integration test for first-interaction welcome with suggestion guidance in tests/integration/Aluki.Runtime.IntegrationTests/FeedbackWelcomeIntegrationTests.cs
  Depends on: T001, T011
  Touch points: tests/integration/Aluki.Runtime.IntegrationTests/FeedbackWelcomeIntegrationTests.cs
  Definition of Done: First interaction triggers expected welcome guidance content path.
  Traceability: FR-015, SC-007

### Implementation for User Story 3

- [ ] T029 [US3] Implement welcome guidance template updates for suggestion and media instructions in src/Aluki.Runtime.Host/Feedback/Welcome/FeedbackWelcomeTemplate.cs
  Depends on: T011
  Touch points: src/Aluki.Runtime.Host/Feedback/Welcome/FeedbackWelcomeTemplate.cs
  Definition of Done: Template includes concise guidance for text, audio, and photo suggestion submission.
  Traceability: FR-015, SC-007

- [ ] T030 [US3] Integrate welcome guidance into first-interaction flow in src/Aluki.Runtime.Host/Channels/WhatsApp/WelcomeMessageService.cs
  Depends on: T029
  Touch points: src/Aluki.Runtime.Host/Channels/WhatsApp/WelcomeMessageService.cs
  Definition of Done: First interaction service emits updated welcome without breaking existing channel behavior.
  Traceability: FR-011, FR-015, SC-007

- [ ] T031 [US3] Emit telemetry event for welcome suggestion guidance delivery in src/Aluki.Runtime.Host/Feedback/Observability/FeedbackTelemetry.cs
  Depends on: T010, T030
  Touch points: src/Aluki.Runtime.Host/Feedback/Observability/FeedbackTelemetry.cs
  Definition of Done: Delivery metrics include tenant-scoped welcome guidance event dimensions.
  Traceability: FR-013, FR-015

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Close lifecycle automation, documentation, and full FR/SC evidence.

- [ ] T032 [P] Implement archival transition job for `sent_user -> archived` at +90 days in src/Aluki.Runtime.Host/Feedback/Jobs/SuggestionArchivalJob.cs
  Depends on: T007, T010, T016, T025
  Touch points: src/Aluki.Runtime.Host/Feedback/Jobs/SuggestionArchivalJob.cs
  Definition of Done: Scheduled job transitions eligible suggestions to archived and records immutable transition evidence.
  Traceability: FR-009, FR-010, FR-012, SC-006

- [ ] T033 [P] Update quickstart validation scenarios for suggestion capture, follow-up linking, dedupe, and welcome guidance in specs/007-feedback-suggestions/quickstart.md
  Depends on: T019, T026, T031, T032
  Touch points: specs/007-feedback-suggestions/quickstart.md
  Definition of Done: Quickstart contains executable scenario matrix for US1-US3 and lifecycle edge cases.
  Traceability: FR-001..FR-015, SC-001..SC-007

- [ ] T034 Complete FR/SC verification checklist and evidence links in specs/007-feedback-suggestions/checklists/requirements.md
  Depends on: T013, T014, T020, T021, T022, T027, T028, T033
  Touch points: specs/007-feedback-suggestions/checklists/requirements.md
  Definition of Done: Every FR and SC has status, evidence link, and verification outcome.
  Traceability: FR-001..FR-015, SC-001..SC-007

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): no dependencies.
- Phase 2 (Foundational): depends on Phase 1 and blocks all user stories.
- Phase 3 (US1): depends on Phase 2.
- Phase 4 (US2): depends on Phase 2 and US1 orchestration entry points.
- Phase 5 (US3): depends on Phase 2.
- Phase 6 (Polish): depends on all completed story phases.

### User Story Dependencies

- US1 (P1) is MVP and starts immediately after foundational completion.
- US2 (P2) depends on US1 capture pipeline and extends context-linking behavior.
- US3 (P3) is independent from US2 data paths and can start after foundational completion.

### Task-Level Critical Path

T001 -> T002 -> T004 -> T005 -> T007 -> T011 -> T015 -> T016 -> T017 -> T024 -> T025 -> T026 -> T032 -> T033 -> T034

---

## Parallel Opportunities

### Setup/Foundation

- T003 can run in parallel with T002 after T001.
- T007, T008, T009, T010 can run in parallel once prerequisites are met.

### US1

- T013 and T014 can run in parallel.

### US2

- T020, T021, T022 can run in parallel.

### US3

- T027 and T028 can run in parallel.

### Polish

- T032 and T033 can run in parallel after user stories complete.

### Parallel Example: US2

- Run together: T020, T021, T022.
- Then proceed with T023 -> T024 -> T025 -> T026.

---

## File Touch Points by Story

- US1: src/Aluki.Runtime.Host/Feedback/Skills/SuggestionIntentSkill.cs; src/Aluki.Runtime.Host/Feedback/Agents/FeedbackAgent.cs; src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs; src/Aluki.Runtime.Host/Memory/MemoryQueryService.cs; src/Aluki.Runtime.Host/Feedback/Skills/SuggestionLifecycleAuditSkill.cs; tests/contract/Aluki.Runtime.ContractTests/FeedbackSuggestionInboundContractTests.cs; tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionIsolationTests.cs
- US2: src/Aluki.Runtime.Host/Feedback/Skills/ActiveSuggestionWindowSkill.cs; src/Aluki.Runtime.Host/Feedback/Skills/SuggestionAttachmentLinkSkill.cs; src/Aluki.Runtime.Host/Feedback/Skills/SuggestionWindowTransitionSkill.cs; src/Aluki.Runtime.Functions/Functions/WhatsAppInboundFunction.cs; tests/integration/Aluki.Runtime.IntegrationTests/FeedbackSuggestionWindowIntegrationTests.cs; tests/integration/Aluki.Runtime.IntegrationTests/FeedbackAttachmentValidationIntegrationTests.cs; tests/integration/Aluki.Runtime.IntegrationTests/FeedbackDuplicateFollowupIntegrationTests.cs
- US3: src/Aluki.Runtime.Host/Feedback/Welcome/FeedbackWelcomeTemplate.cs; src/Aluki.Runtime.Host/Channels/WhatsApp/WelcomeMessageService.cs; src/Aluki.Runtime.Host/Feedback/Observability/FeedbackTelemetry.cs; tests/contract/Aluki.Runtime.ContractTests/FeedbackWelcomeContractTests.cs; tests/integration/Aluki.Runtime.IntegrationTests/FeedbackWelcomeIntegrationTests.cs

---

## Definition of Done by Story

### US1 DoD

- Suggestion intent is detected and routed to FeedbackAgent.
- Suggestion is persisted in dedicated suggestion domain and excluded from normal recall.
- User confirmation is generated for captured suggestion flow.
- Initial lifecycle events are audited and telemetry-safe.

### US2 DoD

- One active 30-minute suggestion window per tenant-user is enforced.
- Valid follow-up audio/photo/text payloads link to active suggestion.
- Attachment constraints and dedupe behavior are enforced.
- Window closure drives lifecycle transition to `enriched` with audit evidence.

### US3 DoD

- First-interaction welcome explicitly describes suggestion text/audio/photo usage.
- Welcome guidance is validated by contract and integration tests.
- Guidance delivery telemetry is emitted.

---

## FR/SC Traceability Matrix

- FR-001: T006, T012, T013, T015, T017
- FR-002: T004, T006, T014, T016
- FR-003: T014, T018
- FR-004: T002, T007, T020, T023
- FR-005: T004, T006, T020, T023, T024, T026
- FR-006: T002, T004, T008, T012, T021, T024
- FR-007: T004, T008, T021, T024
- FR-008: T006, T007, T009, T012, T016, T022, T024
- FR-009: T003, T004, T016, T025, T032
- FR-010: T002, T004, T020, T025, T026, T032
- FR-011: T013, T016, T017, T019, T030
- FR-012: T004, T010, T019, T025, T032
- FR-013: T010, T019, T025, T031
- FR-014: T005, T007, T014
- FR-015: T006, T027, T028, T029, T030, T031

- SC-001: T014, T018
- SC-002: T020, T026
- SC-003: T009, T016, T022, T024
- SC-004: T004, T020, T023
- SC-005: T008, T021, T024
- SC-006: T010, T019, T025, T032
- SC-007: T027, T028, T029, T030

---

## Implementation Strategy

### MVP First (US1)

1. Complete Setup (Phase 1).
2. Complete Foundational (Phase 2).
3. Complete US1 (Phase 3).
4. Validate US1 independently before adding media and welcome extensions.

### Incremental Delivery

1. Deliver US1 (separate suggestion capture).
2. Deliver US2 (follow-up media linking and window lifecycle).
3. Deliver US3 (welcome discoverability).
4. Deliver Polish with archival automation and full FR/SC evidence closure.

### Format Validation

All tasks in this file follow checklist format:
- `- [ ] T### ...` with optional `[P]` and required `[USx]` labels in story phases.
