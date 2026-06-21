# Tasks: Domain Agents Runtime (SB-009B)

**Input**: Design documents from `/specs/009-domain-agents/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/domain-agent-routing-contract.yaml, quickstart.md

**Tests**: Included because the feature specification and plan explicitly require unit, integration, and contract validation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`) for story-phase tasks only
- All tasks include concrete file paths

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare runtime and test scaffolding for deterministic domain dispatch work.

- [ ] T001 Create domain-agent feature folder structure in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/, src/Aluki.Runtime.Host/DomainAgents/, src/Aluki.Runtime.Functions/DomainAgents/, and tests/Aluki.Runtime.DomainAgents.Tests/
- [ ] T002 Add or update test project file for domain-agent tests in tests/Aluki.Runtime.DomainAgents.Tests/Aluki.Runtime.DomainAgents.Tests.csproj
- [ ] T003 [P] Add domain-agent test package references (xUnit, FluentAssertions, test host dependencies) in tests/Aluki.Runtime.DomainAgents.Tests/Aluki.Runtime.DomainAgents.Tests.csproj
- [ ] T004 [P] Add solution entry for domain-agent tests in Aluki.Runtime.slnx
- [ ] T005 Add dispatch-audit migration placeholder script in db/migrations/004_domain_dispatch_audit.sql

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement shared contracts and infrastructure required before any user story can be completed.

**CRITICAL**: No user story tasks should start until this phase is complete.

- [ ] T006 Define domain-agent registration and eligibility contracts in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/DomainAgentRegistration.cs and src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/AgentEligibilityResult.cs
- [ ] T007 [P] Define deterministic routing decision and tie-break contracts in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/AgentRoutingDecision.cs and src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/TieBreakDetails.cs
- [ ] T008 [P] Define immutable audit event contracts in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/DispatchAuditEvent.cs and src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/DispatchAuditEventType.cs
- [ ] T009 Define domain state boundary contracts in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/DomainStateEnvelope.cs and src/Aluki.Runtime.Abstractions/Security/DomainStateAccessPolicy.cs
- [ ] T010 Implement policy-gating precheck pipeline (tenant, principal, consent) in src/Aluki.Runtime.Host/DomainAgents/PolicyGatingEvaluator.cs
- [ ] T011 [P] Implement deterministic sorter utility (priority, canonical agent id, registration timestamp) in src/Aluki.Runtime.Host/DomainAgents/DeterministicAgentOrdering.cs
- [ ] T012 [P] Implement append-only audit repository interface and PostgreSQL adapter in src/Aluki.Runtime.Host/DomainAgents/IDispatchAuditRepository.cs and src/Aluki.Runtime.Host/DomainAgents/PostgresDispatchAuditRepository.cs
- [ ] T013 Apply immutable dispatch audit table, indexes, and RLS policy in db/migrations/004_domain_dispatch_audit.sql
- [ ] T014 Wire foundational domain-agent services into DI container in src/Aluki.Runtime.Host/Program.cs and src/Aluki.Runtime.Functions/Program.cs

**Checkpoint**: Foundational contracts, persistence, and policy-gating pipeline are available for all stories.

---

## Phase 3: User Story 1 - Add a Domain Without Core Edits (Priority: P1) 🎯 MVP

**Goal**: Register and deterministically route to a new domain agent without modifying core processor branching.

**Independent Test**: Register a new domain agent fixture, dispatch domain-specific input, and verify deterministic selection with unchanged core processor branch structure.

### Tests for User Story 1

- [ ] T015 [P] [US1] Add contract test for executeDomainDispatch request/response shape in tests/Aluki.Runtime.DomainAgents.Tests/Contracts/ExecuteDomainDispatchContractTests.cs
- [ ] T016 [P] [US1] Add deterministic selection unit tests for precedence and tie-break in tests/Aluki.Runtime.DomainAgents.Tests/Routing/DeterministicSelectionTests.cs
- [ ] T017 [P] [US1] Add fallback-only-when-zero-eligible test in tests/Aluki.Runtime.DomainAgents.Tests/Routing/FallbackPolicyTests.cs

### Implementation for User Story 1

- [ ] T018 [P] [US1] Implement domain-agent registry and registration loader in src/Aluki.Runtime.Host/DomainAgents/DomainAgentRegistry.cs
- [ ] T019 [US1] Implement deterministic eligibility evaluator in src/Aluki.Runtime.Host/DomainAgents/DomainAgentEligibilityEvaluator.cs
- [ ] T020 [US1] Implement primary router enforcing precedence and selection flow in src/Aluki.Runtime.Host/DomainAgents/DomainAgentRouter.cs
- [ ] T021 [US1] Implement fallback-to-MemoryAgent rule only for zero-eligible outcomes in src/Aluki.Runtime.Host/DomainAgents/FallbackRoutingPolicy.cs
- [ ] T022 [US1] Integrate domain-agent router into dispatch orchestration without adding domain branches in src/Aluki.Runtime.Host/Worker.cs
- [ ] T023 [US1] Expose deterministic dispatch execution endpoint handler in src/Aluki.Runtime.Functions/Functions/DomainDispatchExecuteFunction.cs
- [ ] T024 [US1] Emit routing_selected, routing_fallback, and tie_break_applied audit events in src/Aluki.Runtime.Host/DomainAgents/DomainDispatchCycleExecutor.cs

**Checkpoint**: US1 is independently functional and demonstrates MVP modular routing behavior.

---

## Phase 4: User Story 2 - Keep Domain State Isolated (Priority: P2)

**Goal**: Enforce domain-local state boundaries and audit denied cross-domain access.

**Independent Test**: Run two domain agents for the same tenant with parallel state and verify each reads/writes only its own domain state while cross-domain attempts are denied and audited.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add unit tests for domain-state access policy allow/deny matrix in tests/Aluki.Runtime.DomainAgents.Tests/Security/DomainStateAccessPolicyTests.cs
- [ ] T026 [P] [US2] Add integration test for cross-domain state access denial audit in tests/Aluki.Runtime.DomainAgents.Tests/Integration/CrossDomainStateAccessTests.cs

### Implementation for User Story 2

- [ ] T027 [P] [US2] Implement tenant-domain scoped state repository contracts in src/Aluki.Runtime.Abstractions/Orchestration/DomainAgents/IDomainStateRepository.cs
- [ ] T028 [US2] Implement PostgreSQL domain-state repository with tenant/domain filters in src/Aluki.Runtime.Host/DomainAgents/PostgresDomainStateRepository.cs
- [ ] T029 [US2] Implement guard enforcement for owner_agent_id and domain boundary checks in src/Aluki.Runtime.Host/DomainAgents/DomainStateGuard.cs
- [ ] T030 [US2] Emit state_access_denied immutable audit event on blocked access in src/Aluki.Runtime.Host/DomainAgents/DomainStateGuard.cs
- [ ] T031 [US2] Wire domain-state repository and guard into dispatch execution pipeline in src/Aluki.Runtime.Host/DomainAgents/DomainDispatchCycleExecutor.cs

**Checkpoint**: US2 is independently testable with strict state isolation and auditability.

---

## Phase 5: User Story 3 - Keep Core Processor Readable and Thin (Priority: P3)

**Goal**: Keep core processor limited to normalization, gating, and orchestration responsibilities.

**Independent Test**: Static and runtime conformance checks confirm no domain-specific intent branching in core processor paths.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add architecture conformance test to detect domain-specific branches in core processor files in tests/Aluki.Runtime.DomainAgents.Tests/Architecture/CoreProcessorConformanceTests.cs
- [ ] T033 [P] [US3] Add integration test validating policy denial occurs before eligibility evaluation in tests/Aluki.Runtime.DomainAgents.Tests/Integration/PolicyGatingPrecedenceTests.cs

### Implementation for User Story 3

- [ ] T034 [US3] Refactor core processor orchestration to delegate routing to DomainDispatchCycleExecutor in src/Aluki.Runtime.Host/Worker.cs
- [ ] T035 [US3] Move any domain-selection conditionals to router pipeline components in src/Aluki.Runtime.Host/DomainAgents/DomainAgentRouter.cs
- [ ] T036 [US3] Add thin-core responsibility documentation comments and guard clauses in src/Aluki.Runtime.Host/Worker.cs

**Checkpoint**: US3 maintains architecture intent with verifiable thin-core boundaries.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finalize observability, replay verification, and delivery readiness across all stories.

- [ ] T037 [P] Add dispatch audit retrieval function for immutable evidence lookup in src/Aluki.Runtime.Functions/Functions/DomainDispatchAuditFunction.cs
- [ ] T038 [P] Add correlation-id logging and metrics enrichment for dispatch cycle outcomes in src/Aluki.Runtime.Host/DomainAgents/DomainDispatchTelemetry.cs
- [ ] T039 Update OpenAPI contract examples and decision-type enum consistency in specs/009-domain-agents/contracts/domain-agent-routing-contract.yaml
- [ ] T040 Run and document quickstart validation evidence in specs/009-domain-agents/quickstart.md
- [ ] T041 Add architecture and operations notes for domain-agent runtime in docs/ARCHITECTURE_BASELINE.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup) has no dependencies.
- Phase 2 (Foundational) depends on Phase 1 and blocks all user stories.
- Phase 3 (US1) depends on Phase 2 and is the MVP scope.
- Phase 4 (US2) depends on Phase 2 and should follow MVP validation of US1.
- Phase 5 (US3) depends on Phase 2 and should follow MVP validation of US1.
- Phase 6 (Polish) depends on completion of selected user stories.

### User Story Dependencies

- US1 (P1) is independent after Foundational completion and is the first delivery increment.
- US2 (P2) is independent after Foundational completion, but integrates cleanly with US1 routing/audit pipeline.
- US3 (P3) is independent after Foundational completion, but should validate against US1 implementation artifacts.

### Within-Story Ordering Rules

- Tests should be authored before implementation and fail initially.
- Contracts/models before routing/state services.
- Routing/state services before function endpoints and pipeline wiring.
- Audit emission tasks complete before quickstart evidence and polish completion.

## Parallel Opportunities

- Setup: T003 and T004 can run in parallel after T002.
- Foundational: T007, T008, T011, and T012 can run in parallel once T006 is done.
- US1: T015, T016, T017, and T018 can run in parallel before T019-T024.
- US2: T025, T026, and T027 can run in parallel before T028-T031.
- US3: T032 and T033 can run in parallel before T034-T036.
- Polish: T037 and T038 can run in parallel before T039-T041.

## Parallel Example: User Story 1

- [ ] T015 [P] [US1] Contract test for dispatch contract in tests/Aluki.Runtime.DomainAgents.Tests/Contracts/ExecuteDomainDispatchContractTests.cs
- [ ] T016 [P] [US1] Deterministic selection tests in tests/Aluki.Runtime.DomainAgents.Tests/Routing/DeterministicSelectionTests.cs
- [ ] T017 [P] [US1] Fallback policy tests in tests/Aluki.Runtime.DomainAgents.Tests/Routing/FallbackPolicyTests.cs
- [ ] T018 [P] [US1] Domain agent registry implementation in src/Aluki.Runtime.Host/DomainAgents/DomainAgentRegistry.cs

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational).
3. Complete Phase 3 (US1).
4. Validate deterministic routing, tie-break stability, and fallback boundaries.
5. Demo/deploy MVP behavior before starting US2/US3.

### Incremental Delivery

1. Deliver MVP with US1.
2. Add US2 for state isolation and security hardening.
3. Add US3 for maintainability conformance and thin-core enforcement.
4. Execute Phase 6 polish and finalize evidence.

### Suggested MVP Scope

- In scope for MVP: T001-T024 (through US1 checkpoint).
- Out of MVP (next increments): T025-T041.

## Notes

- Tasks are designed to preserve constitution principles: skill-first execution, tenant-scoped security, immutable audit evidence, and observable routing decisions.
- Avoid introducing domain-specific branching in core processor files while implementing story tasks.
