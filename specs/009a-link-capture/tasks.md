# Tasks: Link Capture and Secure Enrichment

**Input**: Design documents from `/specs/009-link-capture/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/link-capture-contract.yaml, quickstart.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize feature scaffolding and migration entry points.

- [ ] T001 Create feature folders for link capture contracts and domain services in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/
- [ ] T002 Create feature folders for link capture function handlers in src/Aluki.Runtime.Functions/Functions/LinkCapture/
- [ ] T003 [P] Add migration placeholder file db/migrations/004_link_capture.sql for new link-capture tables
- [ ] T004 [P] Add feature configuration section for link capture settings in src/Aluki.Runtime.Host/appsettings.json
- [ ] T005 [P] Add development overrides for link capture timeout and policy settings in src/Aluki.Runtime.Host/appsettings.Development.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement shared data model, persistence, security gates, and telemetry foundations used by all stories.

**CRITICAL**: No user story work starts before this phase is complete.

- [ ] T006 Implement schema for LinkArtifact, LinkProvenanceRef, LinkPendingConfirmation, LinkEnrichmentAttempt, EnrichmentPolicyDecision, and LinkAuditEvent in db/migrations/004_link_capture.sql
- [ ] T007 Implement tenant-scoped RLS policies for new link-capture tables in db/migrations/004_link_capture.sql
- [ ] T008 [P] Add link capture DTO contracts (capture/confirm/recall requests and responses) in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/LinkCaptureContracts.cs
- [ ] T009 [P] Add enrichment and confirmation state enums in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/LinkCaptureStates.cs
- [ ] T010 Implement canonical URL normalization utility with hash generation in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/LinkCanonicalization.cs
- [ ] T011 [P] Implement link persistence repository interface and models in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/ILinkCaptureRepository.cs
- [ ] T012 Implement PostgreSQL repository for link-capture tables with tenant/context filters in src/Aluki.Runtime.Host/Skills/LinkCapture/PostgresLinkCaptureRepository.cs
- [ ] T013 [P] Implement enrichment policy evaluator interface and reason codes in src/Aluki.Runtime.Abstractions/Security/ILinkEnrichmentPolicyEvaluator.cs
- [ ] T014 Implement enrichment policy evaluator for destination allow/block checks in src/Aluki.Runtime.Host/Security/LinkEnrichmentPolicyEvaluator.cs
- [ ] T015 [P] Add telemetry event IDs and payload builders for capture/confirm/enrichment/recall in src/Aluki.Runtime.Abstractions/Skills/LinkCapture/LinkCaptureTelemetry.cs
- [ ] T016 Register link-capture services, repository, policy evaluator, and options in src/Aluki.Runtime.Host/Program.cs

**Checkpoint**: Foundation ready. User stories can now proceed.

---

## Phase 3: User Story 1 - Save Link with User Context (Priority: P1) 🎯 MVP

**Goal**: Capture normalized URLs with tenant scope, context, provenance, idempotency, and canonical upsert merge behavior.

**Independent Test**: Send URL messages and verify created/idempotent/upsert outcomes with correct persisted artifact/provenance state.

### Tests for User Story 1

- [ ] T017 [P] [US1] Add unit tests for canonical normalization and URL hash stability in tests/Aluki.Runtime.Tests/LinkCapture/LinkCanonicalizationTests.cs
- [ ] T018 [P] [US1] Add unit tests for duplicate idempotency key behavior in tests/Aluki.Runtime.Tests/LinkCapture/LinkCaptureIdempotencyTests.cs
- [ ] T019 [P] [US1] Add integration tests for canonical-equivalent upsert merge in tests/Aluki.Runtime.IntegrationTests/LinkCapture/LinkUpsertMergeTests.cs
- [ ] T020 [P] [US1] Add contract tests for POST /skills/link-capture/capture in tests/Aluki.Runtime.ContractTests/LinkCapture/CaptureContractTests.cs

### Implementation for User Story 1

- [ ] T021 [US1] Implement URL extraction from inbound message text in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkUrlExtractor.cs
- [ ] T022 [US1] Implement capture orchestration service returning created/upsert_merged/idempotent_noop outcomes in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkCaptureService.cs
- [ ] T023 [US1] Implement provenance merge and dedupe logic for repeated canonical links in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkProvenanceMerger.cs
- [ ] T024 [US1] Implement audit event writes for duplicate replay and upsert merge events in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkAuditWriter.cs
- [ ] T025 [US1] Implement capture function endpoint wiring for POST /skills/link-capture/capture in src/Aluki.Runtime.Functions/Functions/LinkCapture/CaptureLinkFunction.cs
- [ ] T026 [US1] Add tenant/principal/context validation guard for capture requests in src/Aluki.Runtime.Functions/Functions/LinkCapture/CaptureLinkFunction.cs
- [ ] T027 [US1] Emit capture telemetry outcomes and durations in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkCaptureService.cs

**Checkpoint**: US1 is fully functional and independently testable (MVP slice).

---

## Phase 4: User Story 2 - Resolve Confirmation Once (Priority: P1)

**Goal**: Resolve pending yes/no confirmations exactly once, enforce expiry, and make repeated replies side-effect free.

**Independent Test**: Create one pending confirmation and validate first yes/no consumes it, late replies expire, and subsequent replies are no-op.

### Tests for User Story 2

- [ ] T028 [P] [US2] Add unit tests for confirmation state transitions and terminal immutability in tests/Aluki.Runtime.Tests/LinkCapture/LinkPendingConfirmationStateTests.cs
- [ ] T029 [P] [US2] Add integration tests for first-writer-wins consume under concurrent yes/no replies in tests/Aluki.Runtime.IntegrationTests/LinkCapture/ConfirmationAtomicConsumeTests.cs
- [ ] T030 [P] [US2] Add integration tests for expired confirmation late response handling in tests/Aluki.Runtime.IntegrationTests/LinkCapture/ConfirmationExpiryTests.cs
- [ ] T031 [P] [US2] Add contract tests for POST /skills/link-capture/confirm outcomes in tests/Aluki.Runtime.ContractTests/LinkCapture/ConfirmContractTests.cs

### Implementation for User Story 2

- [ ] T032 [US2] Implement pending confirmation repository operations with atomic consume query in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkPendingConfirmationRepository.cs
- [ ] T033 [US2] Implement confirmation resolution service for resolved_yes/resolved_no/already_resolved/expired outcomes in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkConfirmationService.cs
- [ ] T034 [US2] Implement timeout transition job or workflow callback for pending-to-expired transitions in src/Aluki.Runtime.Functions/Functions/LinkCapture/ExpirePendingConfirmationFunction.cs
- [ ] T035 [US2] Implement confirm function endpoint wiring for POST /skills/link-capture/confirm in src/Aluki.Runtime.Functions/Functions/LinkCapture/ResolveLinkConfirmationFunction.cs
- [ ] T036 [US2] Emit audit events and telemetry for each confirmation terminal outcome in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkConfirmationService.cs

**Checkpoint**: US2 is independently testable with deterministic one-time resolution behavior.

---

## Phase 5: User Story 3 - Recall Full Link Identity (Priority: P2)

**Goal**: Return recall results with full canonical URL, human-readable description, enrichment status/reason, and provenance.

**Independent Test**: Query recall against enriched and limited-metadata links and verify deterministic output fields for every item.

### Tests for User Story 3

- [ ] T037 [P] [US3] Add unit tests for recall item projection with fallback description rules in tests/Aluki.Runtime.Tests/LinkCapture/LinkRecallProjectionTests.cs
- [ ] T038 [P] [US3] Add integration tests for recall results with policy_blocked and timeout statuses in tests/Aluki.Runtime.IntegrationTests/LinkCapture/LinkRecallLimitedMetadataTests.cs
- [ ] T039 [P] [US3] Add contract tests for POST /skills/link-capture/recall in tests/Aluki.Runtime.ContractTests/LinkCapture/RecallContractTests.cs

### Implementation for User Story 3

- [ ] T040 [US3] Implement enrichment attempt runner with strict 4-second timeout cap in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkEnrichmentRunner.cs
- [ ] T041 [US3] Implement policy-first enrichment flow and policy decision persistence in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkEnrichmentService.cs
- [ ] T042 [US3] Implement fallback metadata generation for timeout/failed/policy_blocked outcomes in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkFallbackDescriptionBuilder.cs
- [ ] T043 [US3] Implement recall query service returning canonical URL, description, status/reason, and provenance in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkRecallService.cs
- [ ] T044 [US3] Implement recall function endpoint wiring for POST /skills/link-capture/recall in src/Aluki.Runtime.Functions/Functions/LinkCapture/RecallLinksFunction.cs
- [ ] T045 [US3] Emit enrichment policy and enrichment attempt telemetry including timeout durations in src/Aluki.Runtime.Host/Skills/LinkCapture/LinkEnrichmentService.cs

**Checkpoint**: US3 is independently testable and returns deterministic grounded recall payloads.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, docs alignment, and end-to-end validation.

- [ ] T046 [P] Update operational runbook notes for link capture and enrichment statuses in docs/ARCHITECTURE_BASELINE.md
- [ ] T047 Add quickstart validation script commands and expected outcomes in specs/009-link-capture/quickstart.md
- [ ] T048 [P] Add failure/timeout observability dashboard query notes in docs/CUTOVER_CHECKLIST.md
- [ ] T049 Run full validation scenario set A-H and record pass/fail evidence in specs/009-link-capture/checklists/requirements.md
- [ ] T050 Perform code cleanup and dependency registration verification for link-capture feature in src/Aluki.Runtime.Host/Program.cs

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): No dependencies.
- Foundational (Phase 2): Depends on Setup completion.
- User Stories (Phases 3-5): Depend on Foundational completion.
- Polish (Phase 6): Depends on completed stories selected for release.

### User Story Dependencies

- US1 (P1): Starts after Phase 2. No dependency on other stories.
- US2 (P1): Starts after Phase 2. No dependency on US1 logic for testability (can seed pending records directly).
- US3 (P2): Starts after Phase 2, but release value is highest after US1 capture path exists.

### Within-Story Dependencies

- Test tasks should be implemented first and fail before implementation tasks.
- Extraction/DTO/service/repository sequencing must follow task order within each story.
- Endpoint wiring tasks depend on service implementation tasks in the same story.

---

## Parallel Opportunities

- Phase 1: T003, T004, and T005 can run in parallel.
- Phase 2: T008, T009, T011, T013, and T015 can run in parallel after T006 and T007 planning is clear.
- US1: T017, T018, T019, and T020 can run in parallel; T021 and T023 can proceed in parallel after foundational utilities are ready.
- US2: T028, T029, T030, and T031 can run in parallel; T034 can run in parallel with T033 once T032 exists.
- US3: T037, T038, and T039 can run in parallel; T040 and T042 can run in parallel before T041/T043 integration.
- Polish: T046 and T048 can run in parallel.

---

## Parallel Example: User Story 1

- Run T017, T018, T019, and T020 in parallel as independent test files.
- Run T021 and T023 in parallel, then merge into T022 service orchestration.

## Parallel Example: User Story 2

- Run T028, T029, T030, and T031 in parallel.
- Run T033 and T034 in parallel after T032 repository atomic consume support is in place.

## Parallel Example: User Story 3

- Run T037, T038, and T039 in parallel.
- Run T040 and T042 in parallel, then integrate via T041 and T043.

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 (T001-T005).
2. Complete Phase 2 (T006-T016).
3. Complete Phase 3 / US1 (T017-T027).
4. Validate US1 independently with quickstart Scenarios A-C.
5. Release MVP with deterministic capture outcomes and provenance grounding.

### Incremental Delivery

1. MVP release: US1 capture + upsert/idempotency.
2. Increment 2: US2 one-time confirmation consume + expiry behavior.
3. Increment 3: US3 secure enrichment timeout fallback + grounded recall.
4. Final hardening: Phase 6 polish tasks.

### Suggested MVP Scope

- Include tasks T001-T027.
- Exclude US2/US3 implementation from MVP cut unless explicitly needed for launch.

---

## Notes

- [P] tasks are file-isolated and dependency-safe for parallel execution.
- Every user story phase remains independently testable.
- Task lines follow the required checklist format: checkbox, task ID, optional [P], optional [US#], and explicit file path.
