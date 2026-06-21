# Tasks: Billing and Package Management (SB-010)

**Input**: Design documents from /specs/010-billing/

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/billing-contract.yaml, quickstart.md

**Tests**: Included. This feature has strict determinism, idempotency, and audit requirements that require contract, unit, and integration evidence.

**Organization**: Tasks are grouped by user story to enable incremental delivery and independent validation.

## Format: [ID] [P?] [Story] Description

- [P]: Can run in parallel
- [Story]: User story label (US1..US6)
- Every task includes concrete file paths

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create shared billing configuration and baseline contracts in runtime projects.

- [ ] T001 Add Billing options section and validation defaults in src/Aluki.Runtime.Host/appsettings.json and src/Aluki.Runtime.Host/appsettings.Development.json
- [ ] T002 Add Billing options binding and startup validation in src/Aluki.Runtime.Host/Program.cs
- [ ] T003 [P] Add billing contract models in src/Aluki.Runtime.Abstractions/Skills/BillingContracts.cs
- [ ] T004 [P] Add shared billing reason codes and policy enums in src/Aluki.Runtime.Abstractions/Security/BillingPolicyCodes.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build schema, RLS, and core services that all stories depend on.

**Critical**: No user story work starts before this phase completes.

- [ ] T005 Create billing accounts and catalog migrations in db/migrations/020_billing_accounts_and_catalog.sql
- [ ] T006 [P] Create package definition and quota rule migrations in db/migrations/021_billing_packages_and_quotas.sql
- [ ] T007 [P] Create immutable billing ledger migration with idempotency constraints in db/migrations/022_billing_ledger.sql
- [ ] T008 [P] Create credit balance and credit movement migrations in db/migrations/023_billing_credit_ledger.sql
- [ ] T009 [P] Create invoice and invoice line migrations in db/migrations/024_billing_invoices.sql
- [ ] T010 [P] Create billing cycle migration with open/closed/settled states in db/migrations/025_billing_cycles.sql
- [ ] T011 Enforce tenant RLS policies for all billing tables in db/migrations/026_billing_rls.sql
- [ ] T012 Implement billing persistence interfaces in src/Aluki.Runtime.Abstractions/Orchestration/IBillingStore.cs
- [ ] T013 Implement PostgreSQL billing store with immutable-write semantics in src/Aluki.Runtime.Host/Services/BillingStore.cs
- [ ] T014 Register billing services and policy evaluators in src/Aluki.Runtime.Host/Program.cs
- [ ] T015 [P] Persist pinned catalog version at package subscription activation in src/Aluki.Runtime.Host/Services/BillingSubscriptionService.cs
- [ ] T016 [P] Add regression tests to ensure catalog updates do not mutate active subscription terms in tests/Aluki.Runtime.IntegrationTests/BillingCatalogVersionPinningTests.cs

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 - Pay-as-you-go for total usage (Priority: P0)

**Goal**: Record usage deterministically and generate invoice totals from immutable ledger.

**Independent Test**: Replay usage events and verify exactly-once ledger writes and deterministic invoice recomputation.

### Tests for User Story 1

- [ ] T017 [P] [US1] Add contract tests for /runtime/billing/usage/record in tests/Aluki.Runtime.Contracts.Tests/BillingUsageContractTests.cs
- [ ] T018 [P] [US1] Add integration tests for usage idempotency behavior in tests/Aluki.Runtime.IntegrationTests/BillingUsageIdempotencyTests.cs
- [ ] T019 [P] [US1] Add integration tests for deterministic invoice recomputation in tests/Aluki.Runtime.IntegrationTests/BillingInvoiceDeterminismTests.cs

### Implementation for User Story 1

- [ ] T020 [US1] Implement usage ingestion service with idempotency key enforcement in src/Aluki.Runtime.Host/Services/BillingUsageService.cs
- [ ] T021 [US1] Implement pay-as-you-go decision path and ledger snapshot writes in src/Aluki.Runtime.Host/Services/BillingDecisionService.cs
- [ ] T022 [US1] Implement invoice aggregation service from immutable ledger entries in src/Aluki.Runtime.Host/Services/BillingInvoiceService.cs
- [ ] T023 [US1] Implement usage and invoice endpoints in src/Aluki.Runtime.Host/Endpoints/BillingEndpoints.cs
- [ ] T024 [US1] Emit billing audit events for allow/deny/noop outcomes in src/Aluki.Runtime.Host/Services/BillingAuditWriter.cs

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Package purchase and included quotas (Priority: P0)

**Goal**: Consume included quotas first, then apply overage or deny by policy.

**Independent Test**: Exhaust included quota and verify policy-specific behavior (`billable_overage` vs `hard_stop`).

### Tests for User Story 2

- [ ] T025 [P] [US2] Add unit tests for entitlement decisions by meter in tests/Aluki.Runtime.Tests/Billing/BillingEntitlementDecisionTests.cs
- [ ] T026 [P] [US2] Add integration tests for included quota depletion and overage charging in tests/Aluki.Runtime.IntegrationTests/BillingPackageOverageTests.cs
- [ ] T027 [P] [US2] Add integration tests for hard-stop denials and reason codes in tests/Aluki.Runtime.IntegrationTests/BillingHardStopPolicyTests.cs

### Implementation for User Story 2

- [ ] T028 [US2] Implement entitlement snapshot projection service in src/Aluki.Runtime.Host/Services/BillingEntitlementService.cs
- [ ] T029 [US2] Implement package quota consumption and overage write path in src/Aluki.Runtime.Host/Services/BillingDecisionService.cs
- [ ] T030 [US2] Implement hard-stop denial handling and audit reason code mapping in src/Aluki.Runtime.Host/Services/BillingDecisionService.cs
- [ ] T031 [US2] Add billing status endpoint for runtime policy checks in src/Aluki.Runtime.Host/Endpoints/BillingEndpoints.cs

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Billing by individual or organization tenant (Priority: P1)

**Goal**: Ensure invoice ownership is tenant-scoped and aligned to tenant type.

**Independent Test**: Run cycles for `INDIVIDUAL` and `ORGANIZATION` tenants and verify ownership and attribution semantics.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add integration tests for tenant ownership attribution in tests/Aluki.Runtime.IntegrationTests/BillingTenantOwnershipTests.cs
- [ ] T033 [P] [US3] Add integration tests for optional organization chargeback attribution in tests/Aluki.Runtime.IntegrationTests/BillingChargebackAttributionTests.cs

### Implementation for User Story 3

- [ ] T034 [US3] Implement tenant-type ownership resolution in billing account service in src/Aluki.Runtime.Host/Services/BillingAccountService.cs
- [ ] T035 [US3] Implement attribution metadata propagation without ownership mutation in src/Aluki.Runtime.Host/Services/BillingUsageService.cs
- [ ] T036 [US3] Emit ownership audit events and validation diagnostics in src/Aluki.Runtime.Host/Services/BillingAuditWriter.cs

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: User Story 4 - Package lifecycle changes (Priority: P2)

**Goal**: Handle activation, renewal, upgrade, downgrade, suspension, and cancellation predictably.

**Independent Test**: Execute lifecycle transitions and verify entitlement boundaries, proration, and audit evidence.

### Tests for User Story 4

- [ ] T037 [P] [US4] Add unit tests for lifecycle transition validity in tests/Aluki.Runtime.Tests/Billing/BillingSubscriptionLifecycleTests.cs
- [ ] T038 [P] [US4] Add integration tests for mid-cycle upgrade proration entries in tests/Aluki.Runtime.IntegrationTests/BillingProrationTests.cs
- [ ] T039 [P] [US4] Add integration tests for scheduled downgrade and cancellation grace behavior in tests/Aluki.Runtime.IntegrationTests/BillingDowngradeCancelTests.cs
- [ ] T040 [P] [US4] Add integration tests for billing cycle state transitions and invalid transition rejection in tests/Aluki.Runtime.IntegrationTests/BillingCycleStateTransitionsTests.cs

### Implementation for User Story 4

- [ ] T041 [US4] Implement package subscription lifecycle service in src/Aluki.Runtime.Host/Services/BillingSubscriptionService.cs
- [ ] T042 [US4] Implement billing cycle lifecycle state machine (`open`, `closed`, `settled`) in src/Aluki.Runtime.Host/Services/BillingCycleLifecycleService.cs
- [ ] T043 [US4] Implement proration adjustment ledger writes in src/Aluki.Runtime.Host/Services/BillingInvoiceService.cs
- [ ] T044 [US4] Implement lifecycle transition endpoints in src/Aluki.Runtime.Host/Endpoints/BillingSubscriptionEndpoints.cs

**Checkpoint**: US4 is independently functional and testable.

---

## Phase 7: User Story 5 - Credit balance consumption before external settlement (Priority: P1)

**Goal**: Apply tenant credits first and prevent negative balances.

**Independent Test**: Validate full, partial, and insufficient credit scenarios during invoice finalization.

### Tests for User Story 5

- [ ] T045 [P] [US5] Add unit tests for non-negative credit debit rules in tests/Aluki.Runtime.Tests/Billing/BillingCreditRulesTests.cs
- [ ] T046 [P] [US5] Add integration tests for credit-first settlement precedence in tests/Aluki.Runtime.IntegrationTests/BillingCreditSettlementTests.cs

### Implementation for User Story 5

- [ ] T047 [US5] Implement credit balance service and immutable credit movements in src/Aluki.Runtime.Host/Services/BillingCreditService.cs
- [ ] T048 [US5] Integrate credit-first settlement into invoice finalization flow in src/Aluki.Runtime.Host/Services/BillingInvoiceService.cs
- [ ] T049 [US5] Emit shortfall-to-external-settlement records when credits are insufficient in src/Aluki.Runtime.Host/Services/BillingAuditWriter.cs

**Checkpoint**: US5 is independently functional and testable.

---

## Phase 8: User Story 6 - Billing audit trail query (Priority: P1)

**Goal**: Provide tenant-scoped audit queryability for billing decisions and dispute analysis.

**Independent Test**: Query cycle-range events and verify complete trace from invoice lines to billing decisions.

### Tests for User Story 6

- [ ] T050 [P] [US6] Add contract tests for billing reconciliation export and audit query endpoints in tests/Aluki.Runtime.Contracts.Tests/BillingAuditContractTests.cs
- [ ] T051 [P] [US6] Add integration tests for tenant-scoped chronological audit retrieval in tests/Aluki.Runtime.IntegrationTests/BillingAuditQueryTests.cs

### Implementation for User Story 6

- [ ] T052 [US6] Implement billing audit query and filter service in src/Aluki.Runtime.Host/Services/BillingAuditQueryService.cs
- [ ] T053 [US6] Implement tenant-scoped audit query endpoint (date-range filters) in src/Aluki.Runtime.Host/Endpoints/BillingAuditEndpoints.cs
- [ ] T054 [US6] Implement reconciliation export endpoint mapping invoice lines to ledger entries in src/Aluki.Runtime.Host/Endpoints/BillingReconciliationEndpoints.cs
- [ ] T055 [US6] Add reconciliation export materialization for deterministic output in src/Aluki.Runtime.Host/Services/BillingReconciliationService.cs

**Checkpoint**: US6 is independently functional and testable.

---

## Phase 9: Polish and Cross-Cutting Concerns

**Purpose**: Final verification, operational hardening, and evidence packaging.

- [ ] T056 [P] Update quickstart validation notes with observed outcomes in specs/010-billing/quickstart.md
- [ ] T057 [P] Add billing operations runbook for cycle close, replay, and reconciliation in docs/BILLING_OPERATIONS.md
- [ ] T058 Implement late-arrival billing classifier and cycle-routing behavior in src/Aluki.Runtime.Host/Services/LateArrivalRoutingService.cs
- [ ] T059 [P] Add integration tests for late-arrival routing (adjustment-window vs next-cycle) in tests/Aluki.Runtime.IntegrationTests/BillingLateArrivalRoutingTests.cs
- [ ] T060 [P] Add load-test scenario for mixed meter package accounting and assert <=0.1% error in tests/Aluki.Runtime.IntegrationTests/BillingQuotaAccountingLoadTests.cs
- [ ] T061 Build and execute full touched test suites; capture summary evidence in specs/010-billing/validation-report.md
- [ ] T062 Produce FR/SC traceability table with evidence links in specs/010-billing/checklists/requirements.md

---

## Dependencies and Execution Order

### Phase Dependencies

- Setup (Phase 1) starts immediately.
- Foundational (Phase 2) blocks all user stories.
- User stories (Phase 3-8) can run in parallel after foundational completion.
- Polish (Phase 9) depends on selected story completion.

### Critical Path

T001 -> T005 -> T011 -> T013 -> T014 -> T020 -> T021 -> T022 -> T029 -> T041 -> T048 -> T054 -> T061

### Parallel Opportunities

- T006, T007, T008, T009, T010 can run in parallel after T005.
- Story test tasks can run in parallel per story.
- US2, US3, US4, US5, US6 can proceed concurrently once core ledger and account services are stable.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and 2.
2. Deliver US1 and US2 (required launch monetization paths).
3. Validate determinism, idempotency, and denial behavior before launch.

### Incremental Follow-up

1. Add ownership and chargeback metadata behavior (US3).
2. Add lifecycle and proration support (US4).
3. Add credits and audit/reconciliation tooling (US5 and US6).

### First Deployable Increment

- Phases: 1 + 2 + 3 + 4
- Value: Launch-ready billing for pay-as-you-go and package quotas with deterministic ledger and invoice generation.
- Launch rule: Do not launch billing with US1-only scope; US1 and US2 are both mandatory for launch readiness.
