# Tasks: YouTube Link Save and Classification

**Input**: Design documents from /specs/008-youtube-links/

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included because the implementation plan explicitly requires unit, integration, and contract coverage.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: [ID] [P?] [Story] Description

- [P]: Can run in parallel (different files, no dependencies)
- [Story]: Which user story this task belongs to (US1, US2, US3)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize feature scaffolding and test project structure for YouTube link capture.

- [ ] T001 Create feature migration placeholder and naming convention notes in db/migrations/004_youtube_link_capture.sql
- [ ] T002 Create YouTube link contracts folder structure in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/
- [ ] T003 [P] Create test folders for unit, integration, and contract scopes in tests/Aluki.Runtime.Tests/
- [ ] T004 [P] Add feature test data fixtures for valid/invalid URL variants in tests/Aluki.Runtime.Tests/Fixtures/YouTubeUrlFixtures.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data model, contracts, and shared orchestration primitives that all stories require.

**CRITICAL**: No user story implementation starts before this phase is complete.

- [ ] T005 Create PostgreSQL schema for saved_link_artifacts, link_enrichments, link_classifications, and link_capture_audit_events in db/migrations/004_youtube_link_capture.sql
- [ ] T006 [P] Add indexes and tenant-scope constraints for idempotent upsert/read patterns in db/migrations/004_youtube_link_capture.sql
- [ ] T007 [P] Define capture request/response contracts in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/CaptureYoutubeLinksContracts.cs
- [ ] T008 Implement canonical URL normalization utility and stable video-id extraction in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/YouTubeUrlCanonicalizer.cs
- [ ] T009 [P] Implement message-level canonical identity dedupe helper in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/CanonicalIdentitySet.cs
- [ ] T010 Implement tenant/context authorization guard for link capture commands in src/Aluki.Runtime.Abstractions/Security/YouTubeLinkCaptureAuthorizationGuard.cs
- [ ] T011 [P] Add shared audit event contract and event-type constants in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/LinkCaptureAuditContracts.cs
- [ ] T012 Implement repository upsert contract for (tenant_id, canonical_video_id) semantics in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/IYouTubeLinkRepository.cs
- [ ] T013 Implement host composition and dependency registration for link capture services in src/Aluki.Runtime.Host/Program.cs

**Checkpoint**: Foundation complete. User stories can now proceed.

---

## Phase 3: User Story 1 - Save Enriched YouTube Link (Priority: P1) MVP

**Goal**: Save a valid YouTube link as one canonical artifact with enriched metadata and structured classification.

**Independent Test**: Submit one valid URL and verify created persistenceAction, enriched outcome, structured classification, and visible confidence label.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add contract test for 200 response schema and enriched outcome payload in tests/Aluki.Runtime.Tests/Contract/YouTubeLinks/CaptureYoutubeLinksContractTests.cs
- [ ] T015 [P] [US1] Add unit tests for canonicalization across short and standard YouTube URLs in tests/Aluki.Runtime.Tests/Unit/YouTubeLinks/YouTubeUrlCanonicalizerTests.cs
- [ ] T016 [P] [US1] Add unit tests for per-message duplicate canonical-id suppression in tests/Aluki.Runtime.Tests/Unit/YouTubeLinks/CanonicalIdentitySetTests.cs
- [ ] T017 [US1] Add integration test for create flow with primary provider success in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCapturePrimarySuccessTests.cs

### Implementation for User Story 1

- [ ] T018 [P] [US1] Implement primary-provider enrichment client contract in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/IPrimaryYouTubeMetadataProvider.cs
- [ ] T019 [P] [US1] Implement structured classification model with confidence label and uncertainty fields in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/LinkClassificationModel.cs
- [ ] T020 [US1] Implement link capture orchestrator happy path (detect, normalize, enrich, classify, save, respond) in src/Aluki.Runtime.Host/Orchestration/YouTubeLinks/YouTubeLinkCaptureOrchestrator.cs
- [ ] T021 [US1] Implement persistence workflow for create outcome and provenance linkage in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkRepository.cs
- [ ] T022 [US1] Implement audit emission for detection, normalization, enrichment, persistence, and user outcome in src/Aluki.Runtime.Host/Skills/YouTubeLinks/LinkCaptureAuditLogger.cs
- [ ] T023 [US1] Expose capture endpoint/function for inbound message processing in src/Aluki.Runtime.Functions/Functions/CaptureYoutubeLinksFunction.cs
- [ ] T024 [US1] Implement confirmation formatter including title, source URL, and confidence label in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkOutcomeFormatter.cs

**Checkpoint**: US1 delivers MVP behavior and can be validated independently.

---

## Phase 4: User Story 2 - Save With Fallback Metadata (Priority: P1)

**Goal**: Preserve save behavior and return transparent partial outcomes when primary provider fails and secondary provider succeeds.

**Independent Test**: Simulate primary failure and secondary success, then verify strict fallback order and partial/enriched output with persisted artifact.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add integration test for deterministic primary-to-secondary fallback sequence in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCaptureFallbackOrderTests.cs
- [ ] T026 [P] [US2] Add integration test asserting persisted artifact with secondary metadata and partial outcome signaling in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCaptureSecondarySuccessTests.cs
- [ ] T027 [US2] Add contract test for fallback outcome response fields and providerUsed value in tests/Aluki.Runtime.Tests/Contract/YouTubeLinks/CaptureYoutubeLinksFallbackContractTests.cs

### Implementation for User Story 2

- [ ] T028 [P] [US2] Implement secondary-provider enrichment client contract in src/Aluki.Runtime.Abstractions/Skills/YouTubeLinks/ISecondaryYouTubeMetadataProvider.cs
- [ ] T029 [US2] Extend orchestrator with deterministic fallback chain (primary then secondary) in src/Aluki.Runtime.Host/Orchestration/YouTubeLinks/YouTubeLinkCaptureOrchestrator.cs
- [ ] T030 [US2] Persist enrichment_state and provider_used for fallback outcomes in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkRepository.cs
- [ ] T031 [US2] Emit fallback-specific audit events for secondary attempt and result in src/Aluki.Runtime.Host/Skills/YouTubeLinks/LinkCaptureAuditLogger.cs
- [ ] T032 [US2] Update user confirmation messaging for partial metadata transparency in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkOutcomeFormatter.cs

**Checkpoint**: US2 is independently testable with fallback behavior and transparent partial outcomes.

---

## Phase 5: User Story 3 - Save in Degraded Mode (Priority: P2)

**Goal**: Persist canonical identity when both enrichment providers fail, with explicit degraded outcome and clear user transparency.

**Independent Test**: Simulate both provider failures and verify canonical persistence with degraded response and auditable outcome.

### Tests for User Story 3

- [ ] T033 [P] [US3] Add integration test for degraded save when both providers fail in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCaptureDegradedModeTests.cs
- [ ] T034 [P] [US3] Add integration test for same-tenant idempotent refresh (no duplicate active record) in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCaptureIdempotentRefreshTests.cs
- [ ] T035 [P] [US3] Add integration test for cross-tenant isolation with same canonical video-id in tests/Aluki.Runtime.Tests/Integration/YouTubeLinks/YouTubeLinkCaptureTenantIsolationTests.cs
- [ ] T036 [US3] Add contract test for degraded and invalid_link outcomes with persistenceAction semantics in tests/Aluki.Runtime.Tests/Contract/YouTubeLinks/CaptureYoutubeLinksDegradedContractTests.cs

### Implementation for User Story 3

- [ ] T037 [US3] Extend orchestrator with degraded save branch after secondary failure in src/Aluki.Runtime.Host/Orchestration/YouTubeLinks/YouTubeLinkCaptureOrchestrator.cs
- [ ] T038 [US3] Implement invalid-link handling path (no persistence, explicit outcome) in src/Aluki.Runtime.Host/Orchestration/YouTubeLinks/YouTubeLinkCaptureOrchestrator.cs
- [ ] T039 [US3] Add persistence refresh semantics for repeat same-tenant submissions in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkRepository.cs
- [ ] T040 [US3] Persist and expose uncertainty markers for low-confidence classification fields in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkRepository.cs
- [ ] T041 [US3] Emit normalization_failed and invalid_link auditable events in src/Aluki.Runtime.Host/Skills/YouTubeLinks/LinkCaptureAuditLogger.cs
- [ ] T042 [US3] Update confirmation formatter for degraded and invalid-link user transparency in src/Aluki.Runtime.Host/Skills/YouTubeLinks/YouTubeLinkOutcomeFormatter.cs

**Checkpoint**: US3 independently validates degraded behavior, invalid-link rejection, and idempotent refresh semantics.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finish cross-story quality, observability, and quickstart verification.

- [ ] T043 [P] Add telemetry dimensions for confidence_label, outcome, and provider path in src/Aluki.Runtime.Host/Skills/YouTubeLinks/LinkCaptureTelemetry.cs
- [ ] T044 [P] Update dependency registration and runtime options for provider selection in src/Aluki.Runtime.Functions/Program.cs
- [ ] T045 Add feature documentation and implementation notes in docs/ARCHITECTURE_BASELINE.md
- [ ] T046 Run end-to-end quickstart scenario validation and update any mismatches in specs/008-youtube-links/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately.
- Foundational (Phase 2): depends on Setup completion and blocks all story work.
- User Stories (Phases 3-5): all depend on Foundational completion.
- Polish (Phase 6): depends on completion of desired stories.

### User Story Dependencies

- US1 (P1): starts first after Phase 2 and defines MVP scope.
- US2 (P1): depends on US1 orchestrator baseline and extends it with fallback behavior.
- US3 (P2): depends on US1/US2 orchestration and repository patterns to add degraded and invalid-link branches.

### Within Each User Story

- Tests first, and should fail before implementation.
- Contracts/models before orchestration logic.
- Orchestration before endpoint formatter refinements.
- Persistence and audit updates before final response formatting.

## Parallel Opportunities

- Phase 1: T003 and T004 can run in parallel.
- Phase 2: T006, T007, T009, and T011 can run in parallel after T005 starts the migration file.
- US1: T014, T015, T016 can run in parallel; T018 and T019 can run in parallel.
- US2: T025 and T026 can run in parallel; T028 can run in parallel with test work.
- US3: T033, T034, T035 can run in parallel.
- Polish: T043 and T044 can run in parallel.

---

## Parallel Example: User Story 1

- Run T014, T015, and T016 together (contract + canonicalization + dedupe tests).
- Run T018 and T019 together (provider contract + classification model).
- Start T020 only after T018/T019 and test baselines are in place.

## Parallel Example: User Story 2

- Run T025 and T026 together to validate fallback order and persistence behavior.
- Run T028 in parallel with those tests to prepare secondary provider integration.
- Complete T029-T032 sequentially to preserve deterministic fallback behavior.

## Parallel Example: User Story 3

- Run T033, T034, and T035 together (degraded, idempotent refresh, tenant isolation).
- Implement T037 and T038 together if split by branch ownership inside orchestrator file.
- Complete T039-T042 in order to finalize persistence, audit, and user transparency.

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational).
3. Complete Phase 3 (US1).
4. Validate US1 independently via T014-T017 and quick manual run.
5. Stop for MVP demo/review.

### Incremental Delivery

1. Deliver MVP with US1.
2. Add US2 for deterministic fallback and partial metadata transparency.
3. Add US3 for degraded/invalid-link coverage and full resilience.
4. Finalize with Polish tasks and quickstart verification.

### Team Parallel Plan

1. Team completes Phase 1-2 together.
2. After foundation:
   - Engineer A: US1 implementation path.
   - Engineer B: US2 fallback tests and provider integration.
   - Engineer C: US3 degraded and idempotency tests.
3. Merge by story checkpoints to keep each increment independently testable.

---

## Notes

- All tasks follow the checklist format: checkbox, Task ID, optional [P], required [USx] for story tasks, and explicit file path.
- Story labels are intentionally absent from Setup, Foundational, and Polish phases.
- The MVP scope is US1 only; US2 and US3 are incremental hardening/value phases.
