# Tasks: Suggestions Admin and Rewards (SB-008A)

**Input**: Design documents from /specs/008-suggestions-admin/

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/suggestions-admin-contract.yaml, quickstart.md

**Tests**: Included. The feature spec and plan explicitly require contract, unit, and integration testing for RBAC, idempotency, conflict handling, and queue behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: [ID] [P?] [Story] Description

- [P]: Can run in parallel (different files, no dependency on incomplete tasks)
- [Story]: User story label for phase tasks (US1, US2, US3)
- Every task includes concrete file path(s)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared runtime configuration and common contracts for admin and reward flows.

- [ ] T001 Add SuggestionsAdmin and RewardProcessing configuration sections in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json
- [ ] T002 Add bounded retry policy settings for reward notifications in src/Aluki.Runtime.Functions/host.json
- [ ] T003 [P] Create shared enums and request/response primitives for status, role, and reward rule types in src/Aluki.Runtime.Abstractions/Skills/SuggestionsAdminContracts.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build security, persistence, and orchestration foundations required by all stories.

**Critical**: No user story task starts until this phase is complete.

- [ ] T004 Create triage read-model migration (tenant-scoped admin view) in db/migrations/010_suggestions_admin_views.sql
- [ ] T005 [P] Create append-only audit ledger migration with WORM protections in db/migrations/011_suggestion_admin_audit_ledger.sql
- [ ] T006 [P] Create append-only entitlement ledger migration with idempotency unique boundary in db/migrations/012_reward_entitlement_ledger.sql
- [ ] T007 [P] Create notification delivery state migration with retry/dead-letter fields in db/migrations/013_reward_notification_delivery.sql
- [ ] T008 Enforce tenant RLS policies and role-safe data access on new tables in db/migrations/014_suggestions_admin_rls.sql
- [ ] T009 Implement role policy and action matrix guards in src/Aluki.Runtime.Host/Security/AdminRoleAuthorizationPolicy.cs
- [ ] T010 [P] Register authorization policies, services, and options in src/Aluki.Runtime.Host/Program.cs
- [ ] T011 Define repository interfaces for triage, audit, reward ledger, and notification delivery in src/Aluki.Runtime.Abstractions/Orchestration/ISuggestionsAdminStore.cs
- [ ] T012 Implement PostgreSQL-backed stores with transactional boundary handling in src/Aluki.Runtime.Host/Services/SuggestionsAdminStore.cs
- [ ] T013 Define durable activity contracts for reward grant and notification retry pipelines in src/Aluki.Runtime.Functions/Functions/RewardActivityContracts.cs

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 - Staff triage dashboard (Priority: P1) MVP

**Goal**: Staff can list, filter, classify, and transition suggestions with strict RBAC and immutable audit evidence.

**Independent Test**: With staff principals, verify queue listing and allowed transitions by role, and verify exactly one audit record per mutation plus auditable deny records for forbidden actions.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add contract tests for /admin/suggestions, /admin/suggestions/{suggestionId}/status, and /admin/suggestions/{suggestionId}/classification in tests/Aluki.Runtime.Contracts.Tests/SuggestionsAdminContractTests.cs
- [ ] T015 [P] [US1] Add integration tests for AdminReviewer/AdminApprover/AdminAuditor action matrix in tests/Aluki.Runtime.IntegrationTests/SuggestionsAdminRbacTests.cs
- [ ] T016 [P] [US1] Add integration tests for immutable audit writes and authorization-denied audit records in tests/Aluki.Runtime.IntegrationTests/SuggestionsAdminAuditTests.cs

### Implementation for User Story 1

- [ ] T017 [P] [US1] Implement triage skill contract and DTOs in src/Aluki.Runtime.Abstractions/Skills/SuggestionTriageSkill.cs
- [ ] T018 [US1] Implement queue query, filter, deterministic sort, and pagination service logic in src/Aluki.Runtime.Host/Services/SuggestionsAdminService.cs
- [ ] T019 [US1] Implement status transition and classification endpoints in src/Aluki.Runtime.Host/Endpoints/SuggestionsAdminEndpoints.cs
- [ ] T020 [US1] Implement append-only audit writer for mutations and deny outcomes in src/Aluki.Runtime.Host/Services/SuggestionsAdminAuditWriter.cs
- [ ] T021 [US1] Wire suggestions admin endpoints and authorization policy usage in src/Aluki.Runtime.Host/Program.cs
- [ ] T022 [US1] Add queue query supporting indexes for status/category/priority/date/search in db/migrations/015_suggestions_admin_queue_indexes.sql

**Checkpoint**: US1 is independently functional and is the MVP delivery scope.

---

## Phase 4: User Story 2 - Submitter rewards (Priority: P2)

**Goal**: Reward decisions produce exactly-once accounting with strict idempotency and conflict detection on payload mismatch.

**Independent Test**: Replay identical idempotency requests and confirm no duplicate grants; replay with payload mismatch and confirm conflict with no side effects; verify cap rejections are auditable.

### Tests for User Story 2

- [ ] T023 [P] [US2] Add contract tests for /admin/rewards/decide and /admin/rewards/notifications/{entitlementId} in tests/Aluki.Runtime.Contracts.Tests/RewardDecisionContractTests.cs
- [ ] T024 [P] [US2] Add integration tests for duplicate replay and exactly-once entitlement creation in tests/Aluki.Runtime.IntegrationTests/RewardIdempotencyTests.cs
- [ ] T025 [P] [US2] Add integration tests for payload mismatch conflict behavior in tests/Aluki.Runtime.IntegrationTests/RewardConflictTests.cs
- [ ] T026 [P] [US2] Add integration tests for cap enforcement and auditable rejection decisions in tests/Aluki.Runtime.IntegrationTests/RewardPolicyCapTests.cs

### Implementation for User Story 2

- [ ] T027 [P] [US2] Implement reward decision skill models and contracts in src/Aluki.Runtime.Abstractions/Skills/RewardDecisionSkill.cs
- [ ] T028 [US2] Implement transactional idempotency boundary evaluation and append-only writes in src/Aluki.Runtime.Host/Services/RewardLedgerService.cs
- [ ] T029 [US2] Implement reward decision admin endpoints and telemetry correlation response fields in src/Aluki.Runtime.Host/Endpoints/RewardAdminEndpoints.cs
- [ ] T030 [US2] Implement durable reward grant orchestrator in src/Aluki.Runtime.Functions/Functions/RewardGrantWorkflowOrchestrator.cs
- [ ] T031 [US2] Implement reward activities (grant, duplicate, rejected, conflict decision handling) in src/Aluki.Runtime.Functions/Functions/RewardActivities.cs
- [ ] T032 [US2] Register reward orchestration dependencies and activity bindings in src/Aluki.Runtime.Functions/Program.cs

**Checkpoint**: US2 independently validates deterministic rewards and immutable accounting.

---

## Phase 5: User Story 3 - Queue filtering at scale (Priority: P3)

**Goal**: Queue filtering/search/sort remains deterministic and operationally responsive under high volume.

**Independent Test**: Seed high-volume suggestion data and confirm stable pagination order and consistent results across repeated identical queries.

### Tests for User Story 3

- [ ] T033 [P] [US3] Add high-volume deterministic pagination integration tests in tests/Aluki.Runtime.IntegrationTests/SuggestionsQueuePaginationTests.cs
- [ ] T034 [P] [US3] Add filter/search/sort combination integration tests in tests/Aluki.Runtime.IntegrationTests/SuggestionsQueueFilterSearchTests.cs

### Implementation for User Story 3

- [ ] T035 [US3] Add performance-focused composite indexes for queue filters and sort paths in db/migrations/016_suggestions_admin_query_performance.sql
- [ ] T036 [US3] Implement stable search/sort query builder with deterministic tie-breakers in src/Aluki.Runtime.Host/Services/SuggestionsAdminQueryBuilder.cs
- [ ] T037 [US3] Add queue performance telemetry and slow-query instrumentation in src/Aluki.Runtime.Host/Services/SuggestionsAdminService.cs
- [ ] T038 [US3] Enforce strict query parameter validation and default sort behavior in src/Aluki.Runtime.Host/Endpoints/SuggestionsAdminEndpoints.cs

**Checkpoint**: US3 independently validates operational queue behavior at scale.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Final validation, docs, and operational readiness across all stories.

- [ ] T039 [P] Update quickstart scenarios and expected outputs for implemented behavior in specs/008-suggestions-admin/quickstart.md
- [ ] T040 [P] Document admin/reward architecture decisions and observability notes in docs/ARCHITECTURE_BASELINE.md
- [ ] T041 Create validation evidence report template for this feature in specs/008-suggestions-admin/validation-report.md
- [ ] T042 Run full build and test pass for touched suites and capture execution summary in specs/008-suggestions-admin/validation-report.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Setup (Phase 1): Start immediately.
- Foundational (Phase 2): Depends on Setup and blocks all user stories.
- User Story Phases (Phase 3-5): Depend on Foundational completion.
- Polish (Phase 6): Depends on all implemented stories.

### User Story Dependencies

- US1 (P1): Starts after Phase 2 and has no dependency on US2/US3.
- US2 (P2): Starts after Phase 2; can run in parallel with US1 if team capacity allows.
- US3 (P3): Starts after Phase 2; can run in parallel with US1/US2.

### Within-Story Dependencies

- Story tests (contract/integration) are authored before or in parallel with implementation and must fail before final implementation completion.
- Contracts/models first, service logic second, endpoints/orchestration third, wiring and migration tuning last.

### Dependency Graph (High Level)

- T001-T003 -> T004-T013 -> {US1: T014-T022, US2: T023-T032, US3: T033-T038} -> T039-T042

---

## Parallel Execution Examples

### US1 Parallel Block

- T014, T015, T016 can run together.
- T017 can run in parallel with T018.
- T020 can run in parallel with T019 once T018 is stable.

### US2 Parallel Block

- T023, T024, T025, T026 can run together.
- T027 can run in parallel with T028.
- T030 and T031 can run in parallel after T028 contract is fixed.

### US3 Parallel Block

- T033 and T034 can run together.
- T035 can run in parallel with T036.

---

## Implementation Strategy

### MVP-First Scope

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) only.
3. Validate US1 independent test criteria and quickstart scenarios.
4. Demo/deploy MVP triage capability.

### Incremental Delivery

1. Deliver MVP with US1.
2. Add US2 rewards with idempotency and immutable accounting.
3. Add US3 high-volume queue optimization.
4. Execute Phase 6 hardening and final evidence capture.

### Suggested First Deployable Increment

- Phases included: Phase 1 + Phase 2 + Phase 3
- Delivered value: secure triage dashboard with deterministic queue behavior, role-safe mutations, and immutable audit trail

---

## Notes

- All tasks follow strict checklist format: checkbox, Task ID, optional [P], required [USx] label in story phases, and explicit file path.
- No cross-story hard dependency is required after foundational completion.
- Idempotency and append-only constraints are treated as non-negotiable implementation gates.