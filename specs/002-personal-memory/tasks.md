# Tasks: Personal Memory and Grounded Recall

**Input**: Design documents from /specs/002-personal-memory/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/personal-memory-contract.yaml, quickstart.md

**Tests**: Included (spec mandates scenario-based verification and measurable success criteria).

**Organization**: Tasks are grouped by user story and ordered by dependency so each story can be implemented and tested independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare solution, package references, and configuration scaffolding required by all stories.

- [ ] T001 Add test projects and solution references in Aluki.Runtime.slnx, tests/unit/Aluki.Runtime.UnitTests/Aluki.Runtime.UnitTests.csproj, tests/integration/Aluki.Runtime.IntegrationTests/Aluki.Runtime.IntegrationTests.csproj, tests/contract/Aluki.Runtime.ContractTests/Aluki.Runtime.ContractTests.csproj
- [ ] T002 Update package dependencies for runtime and persistence in src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
- [ ] T003 [P] Add personal memory configuration section and validation defaults in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the core security, persistence, orchestration, and observability scaffolding required by all user stories.

**Critical**: No user story work starts until this phase is complete.

- [ ] T004 Create personal memory schema migration and indexes (canonical chain, idempotency, deletion markers, citations, audit records) in db/migrations/004_personal_memory.sql
- [ ] T005 [P] Add principal scope guard abstraction and denial model in src/Aluki.Runtime.Abstractions/Security/IMemoryScopeGuard.cs and src/Aluki.Runtime.Abstractions/Security/ScopeDenial.cs
- [ ] T006 [P] Define memory domain contracts (artifact, recall query, citation, topic cluster, audit event) in src/Aluki.Runtime.Abstractions/Skills/Memory/MemoryContracts.cs
- [ ] T007 Implement PostgreSQL repositories for scoped memory read/write and idempotent canonical chain updates in src/Aluki.Runtime.Host/Memory/Persistence/PostgresMemoryRepository.cs and src/Aluki.Runtime.Host/Memory/Persistence/PostgresAuditRepository.cs
- [ ] T008 Implement telemetry emitter for memory skill dimensions (request, scope, latency_ms, result, cost_estimate, retry_count) in src/Aluki.Runtime.Host/Memory/Observability/MemoryTelemetry.cs
- [ ] T009 Wire memory services, repositories, and options into DI/bootstrap in src/Aluki.Runtime.Host/Program.cs

**Checkpoint**: Foundation ready. User stories can proceed.

---

## Phase 3: User Story 1 - Store memory from conversational notes (Priority: P1) MVP

**Goal**: Classify note-to-store interactions, enforce scope, and persist canonical memory artifacts with duplicate-safe behavior.

**Independent Test**: Submit valid and duplicate note inputs and verify canonical persistence, dedupe suppression, and scope-denied auditing.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add contract tests for accepted and duplicate_suppressed note capture responses in tests/contract/PersonalMemoryInteractionContractTests.cs
- [ ] T011 [P] [US1] Add integration tests for canonical persistence and source-identity dedupe in tests/integration/PersonalMemoryCaptureIntegrationTests.cs
- [ ] T012 [P] [US1] Add unit tests for intent classification boundaries (note_to_store vs recall_query) in tests/unit/MemoryIntentClassifierSkillTests.cs

### Implementation for User Story 1

- [ ] T013 [US1] Implement intent classifier skill and deterministic ambiguity rules in src/Aluki.Runtime.Host/Memory/Skills/MemoryIntentClassifierSkill.cs
- [ ] T014 [US1] Implement note capture skill with canonical chain idempotency and provenance persistence in src/Aluki.Runtime.Host/Memory/Skills/MemoryCaptureSkill.cs
- [ ] T015 [US1] Implement scope gate enforcement before side effects with denial payload emission in src/Aluki.Runtime.Host/Memory/Security/MemoryScopeGuard.cs
- [ ] T016 [US1] Expose personal memory interaction endpoint for note_to_store path in src/Aluki.Runtime.Host/Endpoints/PersonalMemoryEndpoints.cs
- [ ] T017 [US1] Emit audit events for accepted, duplicate_suppressed, and denied capture outcomes in src/Aluki.Runtime.Host/Memory/Audit/MemoryAuditSkill.cs

**Checkpoint**: US1 independently functional and testable.

---

## Phase 4: User Story 2 - Ask recall questions and receive grounded answers (Priority: P1)

**Goal**: Return grounded recall with corroboration, citation traceability, low-confidence clarification, and no-result guarantees.

**Independent Test**: Query known evidence sets and verify confirmed claims require >=2 citations, single-artifact outputs are low-confidence with clarification, and no-evidence returns explicit no_result.

### Tests for User Story 2

- [ ] T018 [P] [US2] Add contract tests for grounded_result, low_confidence, no_result, and denied recall responses in tests/contract/PersonalMemoryRecallContractTests.cs
- [ ] T019 [P] [US2] Add integration tests for corroboration threshold and citation completeness in tests/integration/PersonalMemoryRecallIntegrationTests.cs
- [ ] T020 [P] [US2] Add integration tests for deleted evidence exclusion and deleted_evidence_gap signaling in tests/integration/PersonalMemoryDeletionBehaviorIntegrationTests.cs
- [ ] T021 [P] [US2] Add unit tests for corroboration policy and claim confidence transitions in tests/unit/CorroborationPolicySkillTests.cs

### Implementation for User Story 2

- [ ] T022 [US2] Implement scoped recall retrieval over non-deleted artifacts in src/Aluki.Runtime.Host/Memory/Skills/MemoryRecallSkill.cs
- [ ] T023 [US2] Implement corroboration policy enforcing >=2 artifacts for confirmed claims in src/Aluki.Runtime.Host/Memory/Skills/CorroborationPolicySkill.cs
- [ ] T024 [US2] Implement claim-level citation renderer with provenance mapping in src/Aluki.Runtime.Host/Memory/Skills/CitationRenderSkill.cs
- [ ] T025 [US2] Implement low-confidence clarification behavior for single-artifact evidence in src/Aluki.Runtime.Host/Memory/Skills/MemoryRecallDecisionEngine.cs
- [ ] T026 [US2] Implement explicit no_result and deleted_evidence_gap decision outputs in src/Aluki.Runtime.Host/Memory/Skills/MemoryRecallDecisionEngine.cs
- [ ] T027 [US2] Extend interaction endpoint for recall_query path and response shape parity with contract in src/Aluki.Runtime.Host/Endpoints/PersonalMemoryEndpoints.cs

**Checkpoint**: US2 independently functional and testable.

---

## Phase 5: User Story 3 - Organize recall by topic across connected channels (Priority: P2)

**Goal**: Group grounded recall by topic and preserve memory continuity across already connected channels within the same tenant/context.

**Independent Test**: Capture related notes from multiple channels and verify grouped, citation-backed continuity in recall output.

### Tests for User Story 3

- [ ] T028 [P] [US3] Add integration tests for topic-group coherence in recall responses in tests/integration/PersonalMemoryTopicGroupingIntegrationTests.cs
- [ ] T029 [P] [US3] Add integration tests for cross-channel continuity within scope in tests/integration/PersonalMemoryCrossChannelContinuityIntegrationTests.cs
- [ ] T030 [P] [US3] Add unit tests for topic clustering heuristics and deterministic labeling in tests/unit/TopicGroupingSkillTests.cs

### Implementation for User Story 3

- [ ] T031 [US3] Implement topic grouping skill for scoped artifacts in src/Aluki.Runtime.Host/Memory/Skills/TopicGroupingSkill.cs
- [ ] T032 [US3] Implement multi-channel retrieval continuity policy in src/Aluki.Runtime.Host/Memory/Skills/MemoryContinuityPolicy.cs
- [ ] T033 [US3] Extend recall response assembler with topic_groups mapping in src/Aluki.Runtime.Host/Memory/Presentation/MemoryRecallResponseAssembler.cs

**Checkpoint**: US3 independently functional and testable.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Validate NFRs, observability completeness, and operational readiness across all stories.

- [ ] T034 [P] Add performance benchmark/integration test for synchronous recall P95 <= 2s on non-blocking paths in tests/integration/PersonalMemoryLatencyIntegrationTests.cs
- [ ] T035 [P] Add telemetry and audit completeness assertions covering request/scope/latency/result/cost/retry and denial audit fields in tests/integration/PersonalMemoryObservabilityIntegrationTests.cs
- [ ] T036 Update quickstart execution steps and evidence checkpoints for implemented flows in specs/002-personal-memory/quickstart.md
- [ ] T037 Run end-to-end validation checklist and record implementation evidence matrix in specs/002-personal-memory/checklists/requirements.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 -> no dependencies.
- Phase 2 -> depends on T001-T003.
- Phase 3 (US1) -> depends on T004-T009.
- Phase 4 (US2) -> depends on T004-T009 and reuses endpoint scaffolding from T016.
- Phase 5 (US3) -> depends on T022-T027.
- Phase 6 -> depends on T013-T033.

### Task-Level Dependencies

- T007 depends on T004, T006.
- T008 depends on T006.
- T009 depends on T005-T008.
- T013 depends on T006, T009.
- T014 depends on T007, T013, T015.
- T015 depends on T005, T009.
- T016 depends on T013-T015.
- T017 depends on T007, T008, T015.
- T022 depends on T007, T015.
- T023 depends on T022.
- T024 depends on T022, T023.
- T025 depends on T022, T023.
- T026 depends on T022, T025.
- T027 depends on T022-T026.
- T031 depends on T022.
- T032 depends on T022.
- T033 depends on T031, T032, T024.
- T034 depends on T027, T033.
- T035 depends on T017, T027, T033.
- T036 depends on T034-T035.
- T037 depends on T034-T036.

### User Story Dependencies

- US1 can start immediately after Phase 2.
- US2 can start after Phase 2, but final endpoint parity requires T016 from US1.
- US3 depends on US2 recall primitives for grouping/continuity output.

### Story Completion Order (Dependency-Ordered)

1. US1 (capture reliability and canonical persistence)
2. US2 (grounded recall and corroboration)
3. US3 (topic grouping and cross-channel continuity)

---

## Parallel Opportunities

### US1

- Run T010, T011, T012 in parallel.
- Implement T013 and T015 in parallel after T009.

### US2

- Run T018, T019, T020, T021 in parallel.
- Implement T025 and T026 in parallel after T022 and T023.

### US3

- Run T028, T029, T030 in parallel.
- Implement T031 and T032 in parallel.

### Cross-Cutting

- T034 and T035 can run in parallel after US3 completion.

---

## Definition of Done and Traceability (FR/SC)

### US1 DoD

- Intent classification is deterministic and covered by tests (FR-001).
- Accepted notes persist tenant/context-scoped canonical artifacts with provenance (FR-002, FR-004, SC-001, SC-008).
- Scope-denied writes are blocked and auditable with correlation + scope identifiers (FR-003, FR-014, SC-005).

Task traceability:
- T010-T012 -> FR-001, FR-002, FR-004, SC-001, SC-008
- T013-T017 -> FR-001, FR-002, FR-003, FR-004, FR-014, SC-001, SC-005, SC-008

### US2 DoD

- Confirmed claims require >=2 corroborating in-scope non-deleted artifacts (FR-005, FR-006, SC-002).
- Single-artifact evidence yields low_confidence + clarification question (FR-008, SC-009).
- No-evidence returns explicit no_result and never fabricates claims (FR-007, SC-003).
- Deleted artifacts are excluded and gaps signaled as deleted_evidence_gap when relevant (FR-009, SC-004).

Task traceability:
- T018-T021 -> FR-005, FR-006, FR-007, FR-008, FR-009, SC-002, SC-003, SC-004, SC-009
- T022-T027 -> FR-005, FR-006, FR-007, FR-008, FR-009, SC-002, SC-003, SC-004, SC-009

### US3 DoD

- Recall outputs provide coherent topic grouping for related artifacts (FR-010, SC-007).
- Scoped cross-channel continuity is preserved within same tenant/context (FR-011).

Task traceability:
- T028-T030 -> FR-010, FR-011, SC-007
- T031-T033 -> FR-010, FR-011, SC-007

### Cross-Cutting DoD

- Synchronous recall path meets P95 <= 2s baseline target for non-blocking requests (FR-012, SC-006).
- Skill-level telemetry dimensions are emitted and verifiable (FR-013).
- Quickstart and requirements checklist contain verifiable implementation evidence (workflow quality gate support).

Task traceability:
- T034 -> FR-012, SC-006
- T035 -> FR-013, FR-014
- T036-T037 -> FR-001 through FR-014 coverage evidence

---

## Implementation Strategy

### MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 3 (US1 only).
- Validate capture reliability, dedupe safety, and scope-denied auditing before starting recall expansion.

### Incremental Delivery

1. Deliver US1 (capture) and validate acceptance metrics.
2. Deliver US2 (grounded recall correctness and safety constraints).
3. Deliver US3 (topic organization and continuity).
4. Complete cross-cutting performance and observability validation.
