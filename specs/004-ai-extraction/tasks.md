# Tasks: AI Extraction

**Input**: Design documents from /specs/004-ai-extraction/

**Prerequisites**: spec.md, research.md, data-model.md, contracts/extraction-skill-contract.yaml

**Tests**: Included. The feature defines measurable acceptance/clarification behavior and async lifecycle guarantees that require contract, unit, and integration coverage.

**Organization**: Tasks are grouped by user story and ordered by dependency so each story remains independently implementable and testable.

## Functional Requirement and Success Criteria Index (for traceability)

- FR-001: Accept polymorphic extraction inputs (audio/text/image) with tenant and principal context.
- FR-002: Process voice notes into transcription plus structured action items.
- FR-003: Process long text into summary, decisions, entities, and action items.
- FR-004: Process receipt images into vendor/amount/date/tax and RFC when present.
- FR-005: Persist extraction jobs/results/fields with provenance and audit trail.
- FR-006: Enforce confidence tiers (high/medium/low) and uncertainty flagging without fabrication.
- FR-007: Support language auto-detection with region fallback and mixed-language segment tagging.
- FR-008: Provide async lifecycle status endpoint with pending/processing/completed_success/completed_with_warnings/failed.
- FR-009: Implement OCR fallback chain (structured OCR -> text-only OCR -> unreadable/manual review).
- FR-010: Ensure idempotent extraction requests with stable job IDs across retries.
- FR-011: Emit extraction telemetry events and processing metadata.

- SC-001: Audio under target duration returns structured response within SLA.
- SC-002: Uncertain or unreadable fragments are flagged and never invented.
- SC-003: Structured output persists with provenance and can be recalled later.
- SC-004: Language behavior is deterministic (auto-detect, fallback, mixed-language tagging).
- SC-005: Async lifecycle status and progress metadata are visible and consistent.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare solution scaffolding, configuration, and test projects used by all stories.

- [ ] T001 Add extraction feature project folders and test project references in Aluki.Runtime.slnx, tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj, tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj, tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj
- [ ] T002 Add extraction provider/config dependencies in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
- [ ] T003 [P] Add extraction configuration sections and defaults in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Contracts, persistence, security, idempotency, and observability that must exist before user-story implementation.

**Critical**: No user story work starts until this phase is complete.

- [ ] T004 Create extraction persistence migration (jobs/results/fields/content tables, idempotency, status indexes) in db/migrations/004_ai_extraction.sql
- [ ] T005 [P] Define extraction contracts and DTOs aligned to extraction-skill-v1 in src/Aluki.Runtime.Abstractions/Skills/Extraction/ExtractionContracts.cs
- [ ] T006 [P] Define extraction repository abstractions for jobs/results/fields/audit in src/Aluki.Runtime.Abstractions/Skills/Extraction/IExtractionRepository.cs
- [ ] T007 [P] Define extraction provider interfaces (transcription, text extraction, receipt OCR, language detection) in src/Aluki.Runtime.Abstractions/Skills/Extraction/IExtractionProviders.cs
- [ ] T008 Implement PostgreSQL extraction repositories with tenant/context enforcement in src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs
- [ ] T009 [P] Implement confidence policy and uncertainty classification helpers in src/Aluki.Runtime.Host/Extraction/Policies/ExtractionConfidencePolicy.cs
- [ ] T010 [P] Implement extraction telemetry/audit emitters in src/Aluki.Runtime.Host/Extraction/Observability/ExtractionTelemetry.cs and src/Aluki.Runtime.Host/Extraction/Audit/ExtractionAuditWriter.cs
- [ ] T011 [P] Implement idempotency guard for extraction requests in src/Aluki.Runtime.Host/Extraction/Policies/ExtractionIdempotencyPolicy.cs
- [ ] T012 Register extraction services/repositories/options in dependency injection bootstrap in src/Aluki.Runtime.Host/Program.cs

**Checkpoint**: Foundation ready. User stories can proceed.

---

## Phase 3: User Story 1 - Voice note to structured result (Priority: P0) MVP

**Goal**: Given an audio note, return transcription and structured action items with confidence-aware flags.

**Independent Test**: Submit short and multi-segment audio; verify transcription + action items + confidence tiers + language metadata + persisted provenance.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add extraction contract tests for audio input/response semantics in tests/contract/ExtractionAudioContractTests.cs
- [ ] T014 [P] [US1] Add integration tests for async/sync audio pipeline and status transitions in tests/integration/ExtractionAudioPipelineIntegrationTests.cs
- [ ] T015 [P] [US1] Add unit tests for confidence tier assignment and uncertain-field suppression in tests/unit/ExtractionConfidencePolicyTests.cs

### Implementation for User Story 1

- [ ] T016 [US1] Implement audio transcription provider adapter (es-MX/en-US with confidence segments) in src/Aluki.Runtime.Host/Extraction/Providers/AudioTranscriptionProvider.cs
- [ ] T017 [US1] Implement language detection and mixed-language segment merger with fallback rules in src/Aluki.Runtime.Host/Extraction/Skills/ExtractionLanguageResolverSkill.cs
- [ ] T018 [US1] Implement voice extraction orchestration (transcription -> action/decision/entity extraction -> persistence) in src/Aluki.Runtime.Host/Extraction/Skills/VoiceExtractionSkill.cs
- [ ] T019 [US1] Implement extraction submit/status endpoints for audio flows in src/Aluki.Runtime.Functions/Functions/ExtractionFunctions.cs
- [ ] T020 [US1] Persist audio extraction field provenance and segment references in src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs

**Checkpoint**: US1 independently functional and testable.

---

## Phase 4: User Story 2 - Text to summary and actions (Priority: P1)

**Goal**: Given long or forwarded text, return summary, decisions, entities, and action items clearly.

**Independent Test**: Submit monolingual and code-switched text; verify summary, decisions, entities, actions, confidence tiers, and persisted recall artifacts.

### Tests for User Story 2

- [ ] T021 [P] [US2] Add extraction contract tests for text input and structured text_summary output in tests/contract/ExtractionTextContractTests.cs
- [ ] T022 [P] [US2] Add integration tests for long-text chunking and merged extraction output in tests/integration/ExtractionTextPipelineIntegrationTests.cs
- [ ] T023 [P] [US2] Add unit tests for summary/action/decision/entity normalization and language-pair metadata in tests/unit/TextExtractionNormalizationTests.cs

### Implementation for User Story 2

- [ ] T024 [US2] Implement text extraction provider adapter for summary/actions/decisions/entities in src/Aluki.Runtime.Host/Extraction/Providers/TextExtractionProvider.cs
- [ ] T025 [US2] Implement text chunking and merge strategy for long inputs in src/Aluki.Runtime.Host/Extraction/Skills/TextChunkingSkill.cs
- [ ] T026 [US2] Implement text extraction orchestration and response shaping in src/Aluki.Runtime.Host/Extraction/Skills/TextExtractionSkill.cs
- [ ] T027 [US2] Extend extraction endpoints to support text source metadata and output options in src/Aluki.Runtime.Functions/Functions/ExtractionFunctions.cs
- [ ] T028 [US2] Persist text extraction artifacts (summary/actions/decisions/entities) with provenance spans in src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs

**Checkpoint**: US2 independently functional and testable.

---

## Phase 5: User Story 3 - Receipt OCR with fiscal fields (Priority: P0)

**Goal**: Given a receipt image, extract vendor/amount/date/tax and RFC when present, with robust fallback and unreadable signaling.

**Independent Test**: Submit high-quality and degraded receipts; verify structured OCR, fallback behavior, unreadable/manual-review outcomes, and confidence-based field surfacing.

### Tests for User Story 3

> Implementation landed in `src/Aluki.Runtime.Extraction` (mirroring US1/US2),
> not `Host`; test files added under the shared test projects.

- [x] T029 [P] [US3] Receipt OCR contract tests (receipt_ocr response shape, fallback warning, image validation) in tests/contract/Aluki.Runtime.ContractTests/ExtractionContractTests.cs
- [x] T030 [P] [US3] Integration tests for the OCR fallback chain and unreadable/manual-review behavior in tests/integration/Aluki.Runtime.IntegrationTests/ExtractionPipelineIntegrationTests.cs
- [x] T031 [P] [US3] Unit tests for RFC validation and monetary/date normalization in tests/unit/Aluki.Runtime.UnitTests/ReceiptExtractionNormalizationTests.cs

### Implementation for User Story 3

- [x] T032 [US3] Structured receipt OCR provider (Azure vision) with field-level confidence in src/Aluki.Runtime.Extraction/Providers/FoundryReceiptOcrProvider.cs
- [x] T033 [US3] Text-only OCR fallback + unreadable classification in src/Aluki.Runtime.Extraction/Policies/ReceiptExtractionPolicy.cs (chain orchestrated in ExtractionCoordinator.ProcessReceiptAsync)
- [x] T034 [US3] Receipt RFC/amount/date/tax normalization in src/Aluki.Runtime.Extraction/Policies/ReceiptNormalization.cs
- [x] T035 [US3] Image receipt routing + warning payloads via ExtractionCoordinator (Functions endpoint already modality-agnostic) in src/Aluki.Runtime.Extraction/ExtractionCoordinator.cs
- [x] T036 [US3] Persist receipt fields + manual-review audit (ExtractionStore.FailJobAsync manualReview) in src/Aluki.Runtime.Extraction/Persistence/ExtractionStore.cs

**Checkpoint**: US3 independently functional and testable.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Validate SLA, reliability, observability, and close FR/SC evidence.

- [ ] T037 [P] Add integration SLA/performance tests for sync and async extraction targets in tests/integration/ExtractionSlaIntegrationTests.cs
- [ ] T038 [P] Add integration tests for idempotency/retry stability and consistent job_id semantics in tests/integration/ExtractionIdempotencyIntegrationTests.cs
- [ ] T039 [P] Add integration tests for tenant-scope enforcement and audit completeness in tests/integration/ExtractionSecurityAuditIntegrationTests.cs
- [ ] T040 Update quickstart scenarios and runbook evidence for extraction workflows in specs/004-ai-extraction/quickstart.md
- [ ] T041 Record FR/SC evidence and verification status in specs/004-ai-extraction/checklists/requirements.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 -> no dependencies.
- Phase 2 -> depends on T001-T003.
- Phase 3 (US1) -> depends on T004-T012.
- Phase 4 (US2) -> depends on T004-T012 and shared endpoint baseline from T019.
- Phase 5 (US3) -> depends on T004-T012 and shared endpoint baseline from T019.
- Phase 6 -> depends on T016-T036.

### Task-Level Dependencies

- T008 depends on T004, T006.
- T009 depends on T005.
- T010 depends on T005.
- T011 depends on T004, T006.
- T012 depends on T005-T011.

- T016 depends on T007, T012.
- T017 depends on T007, T009, T012.
- T018 depends on T016, T017.
- T019 depends on T005, T008, T010, T011, T018.
- T020 depends on T008, T018.

- T024 depends on T007, T012.
- T025 depends on T024.
- T026 depends on T017, T024, T025.
- T027 depends on T019, T026.
- T028 depends on T008, T026.

- T032 depends on T007, T012.
- T033 depends on T009, T032.
- T034 depends on T017, T032, T033.
- T035 depends on T019, T034.
- T036 depends on T008, T034.

- T037 depends on T027, T035.
- T038 depends on T011, T027, T035.
- T039 depends on T010, T027, T035.
- T040 depends on T037-T039.
- T041 depends on T037-T040.

### User Story Dependencies

- US1 starts immediately after Phase 2 and is the MVP critical path.
- US2 depends on foundational completion and uses shared extraction submit/status endpoints introduced in US1.
- US3 depends on foundational completion and uses shared extraction submit/status endpoints introduced in US1.
- US2 and US3 can progress in parallel after T019.

### Story Completion Order (Dependency-Ordered)

1. US1 (voice transcription and structured extraction baseline)
2. US2 (text extraction and long-text handling)
3. US3 (receipt OCR and fallback/manual-review behavior)

---

## Parallel Opportunities

### Setup and Foundation

- T003 can run in parallel with T001-T002.
- T005, T006, T007 can run in parallel after T004 design alignment.
- T009, T010, T011 can run in parallel after T005/T006.

### US1

- T013, T014, T015 can run in parallel.
- T016 and T017 can run in parallel after T012.

### US2

- T021, T022, T023 can run in parallel.
- T024 can progress while T025 test fixtures are prepared.

### US3

- T029, T030, T031 can run in parallel.
- T032 and RFC normalization test-data prep can run in parallel.

### Cross-Cutting

- T037, T038, T039 can run in parallel after T027 and T035.

---

## File Touch Points by Story

- US1: src/Aluki.Runtime.Host/Extraction/Providers/AudioTranscriptionProvider.cs, src/Aluki.Runtime.Host/Extraction/Skills/ExtractionLanguageResolverSkill.cs, src/Aluki.Runtime.Host/Extraction/Skills/VoiceExtractionSkill.cs, src/Aluki.Runtime.Functions/Functions/ExtractionFunctions.cs, src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs, tests/contract/ExtractionAudioContractTests.cs, tests/integration/ExtractionAudioPipelineIntegrationTests.cs, tests/unit/ExtractionConfidencePolicyTests.cs
- US2: src/Aluki.Runtime.Host/Extraction/Providers/TextExtractionProvider.cs, src/Aluki.Runtime.Host/Extraction/Skills/TextChunkingSkill.cs, src/Aluki.Runtime.Host/Extraction/Skills/TextExtractionSkill.cs, src/Aluki.Runtime.Functions/Functions/ExtractionFunctions.cs, src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs, tests/contract/ExtractionTextContractTests.cs, tests/integration/ExtractionTextPipelineIntegrationTests.cs, tests/unit/TextExtractionNormalizationTests.cs
- US3: src/Aluki.Runtime.Host/Extraction/Providers/ReceiptOcrProvider.cs, src/Aluki.Runtime.Host/Extraction/Skills/ReceiptOcrFallbackSkill.cs, src/Aluki.Runtime.Host/Extraction/Skills/ReceiptExtractionSkill.cs, src/Aluki.Runtime.Functions/Functions/ExtractionFunctions.cs, src/Aluki.Runtime.Host/Extraction/Persistence/PostgresExtractionRepository.cs, tests/contract/ExtractionReceiptContractTests.cs, tests/integration/ExtractionReceiptOcrIntegrationTests.cs, tests/unit/ReceiptExtractionNormalizationTests.cs

---

## Definition of Done and Traceability (FR/SC)

### US1 DoD

- Audio extraction returns transcription plus structured action items/decisions/entities under tenant scope (FR-001, FR-002, FR-005).
- Language detection/fallback and mixed-language segment tagging are persisted in metadata (FR-007, SC-004).
- Confidence tiers are applied per field, low-confidence outputs are marked uncertain, and no fabricated values are surfaced (FR-006, SC-002).
- Async job lifecycle and provenance persistence are observable for audio jobs (FR-008, FR-011, SC-003, SC-005).

Task traceability:
- T013-T015 -> FR-001, FR-002, FR-006, FR-007, FR-008, SC-002, SC-004, SC-005
- T016-T020 -> FR-001, FR-002, FR-005, FR-006, FR-007, FR-008, FR-010, FR-011, SC-001, SC-002, SC-003, SC-004, SC-005

### US2 DoD

- Text extraction produces summary, actions, decisions, and entities with deterministic normalization (FR-003, FR-005).
- Long text processing preserves source spans/provenance and consistent confidence behavior (FR-005, FR-006, SC-002, SC-003).
- Mixed-language text is tagged and merged with language-pair metadata (FR-007, SC-004).

Task traceability:
- T021-T023 -> FR-003, FR-005, FR-006, FR-007, SC-002, SC-003, SC-004
- T024-T028 -> FR-003, FR-005, FR-006, FR-007, FR-008, FR-011, SC-002, SC-003, SC-004, SC-005

### US3 DoD

- Receipt OCR returns vendor/amount/date/tax and RFC when present with field-level confidence (FR-004, FR-005, FR-006).
- OCR fallback chain is enforced and unreadable/manual-review outcomes are explicit without invented data (FR-009, SC-002).
- Receipt extraction metadata, warnings, and provenance are persisted for recall (FR-005, FR-008, FR-011, SC-003, SC-005).

Task traceability:
- T029-T031 -> FR-004, FR-006, FR-009, SC-002
- T032-T036 -> FR-004, FR-005, FR-006, FR-008, FR-009, FR-011, SC-002, SC-003, SC-005

### Cross-Cutting DoD

- SLA and status consistency targets are validated for sync/async extraction paths (FR-008, SC-001, SC-005).
- Idempotency/retry semantics keep stable job identifiers and avoid duplicate side effects (FR-010, SC-005).
- Tenant-scope enforcement and audit telemetry are verifiable end-to-end (FR-001, FR-005, FR-011, SC-003).
- Quickstart and checklist documentation provide closure evidence for all FR/SC mappings.

Task traceability:
- T037 -> FR-002, FR-003, FR-004, FR-008, SC-001, SC-005
- T038 -> FR-010, SC-005
- T039 -> FR-001, FR-005, FR-011, SC-003
- T040-T041 -> FR-001 through FR-011, SC-001 through SC-005 evidence closure

---

## Implementation Strategy

### MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 3 (US1 only).
- Validate voice extraction quality, confidence policy behavior, and status/provenance persistence before adding text/receipt paths.

### Incremental Delivery

1. Deliver US1 and verify voice extraction + lifecycle + provenance.
2. Deliver US2 and verify long-text extraction + multilingual merge behavior.
3. Deliver US3 and verify OCR fallback/manual-review outcomes.
4. Close with performance/idempotency/security evidence and checklist updates.
