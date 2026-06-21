# Implementation Plan: Billing and Package Management

**Branch**: `010-billing` | **Date**: 2026-06-21 | **Spec**: `specs/010-billing/spec.md`

**Input**: Feature specification from `specs/010-billing/spec.md`

## Summary

Implement a tenant-scoped billing domain that supports both required monetization modes: consumption-based pay-as-you-go and package-based quotas with overage/hard-stop policy. The design uses immutable ledger-first accounting, deterministic invoice aggregation, package lifecycle state transitions, and reconciliation exports that map invoices to source ledger evidence while preserving tenancy ownership for both `INDIVIDUAL` and `ORGANIZATION` tenants.

Launch readiness constraint: billing launch requires both monetization modes (`payg` and `package`) enabled in production scope; a pay-as-you-go-only release is not considered launch-complete for this feature.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- `Microsoft.DurableTask.Client`, `Microsoft.DurableTask.Worker.Grpc`
- `Npgsql` for PostgreSQL billing persistence
- Existing runtime abstractions for orchestration, security context, and skill contracts

**Storage**: PostgreSQL (`billing_accounts`, `billing_catalog_versions`, `meter_prices`, `package_definition_versions`, `package_quota_rules`, `package_subscriptions`, `entitlement_snapshots`, `billing_ledger_entries`, `credit_balances`, `credit_movements`, `invoices`, `invoice_lines`, `billing_audit_events`) with tenant RLS

**Testing**:
- Unit tests for entitlement evaluation, idempotency key handling, proration computation, and deterministic aggregation
- Integration tests for package lifecycle transitions, quota exhaustion behavior, and cycle closure/late-arrival handling
- Contract tests for usage recording, billing status, invoice generation, and reconciliation export endpoints

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) on Azure baseline defined by constitution

**Project Type**: Backend skill-driven orchestration service with durable workflow support for billing-cycle and lifecycle transitions

**Performance Goals**:
- Entitlement check and usage decision path remains non-blocking for conversational runtime target (P95 <= 2s for non-waiting flows)
- Invoice generation and recomputation are deterministic for identical closed cycle input sets
- Duplicate ingestion replay yields 0 duplicate billable ledger entries for identical idempotency keys

**Constraints**:
- Billing ownership must remain tenant-scoped for `INDIVIDUAL` and `ORGANIZATION`
- Price snapshots are immutable once ledger entries are persisted
- Hard-stop policy denies over-quota usage with machine-readable reason and audit evidence
- Billing operations fail closed when tenant/principal context is unresolved

**Scale/Scope**:
- Tenant-level billing mandatory; optional user attribution only for organization chargeback visibility
- Both monetization models active at launch (`payg`, `package`)
- Lifecycle events: activation, renewal, upgrade, downgrade, suspension, cancellation
- Reconciliation exports must trace invoice lines to contributing immutable ledger entries

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Billing decisions, entitlement checks, invoice generation, and reconciliation are defined as explicit contracts.
- Side effects (ledger writes, invoice closure, lifecycle transitions) remain inside skills, not in routing logic.

### Principle II: Tenant-Scoped Security by Default - PASS
- All entities are tenant-scoped and include mandatory principal context checks.
- RLS-compatible design and fail-closed behavior for unresolved tenant/principal context satisfy security discipline.

### Principle III: Grounded Memory and Provenance - PASS
- Financial outputs (invoice totals, reconciliation exports) are derived from immutable ledger evidence.
- Denials and policy decisions are auditable with machine-readable reason codes.

### Principle IV: Durable Session and Workflow Separation - PASS
- Long-running lifecycle and cycle-close policies are modeled for durable workflows.
- Synchronous entitlement checks remain lightweight and isolated from long wait/retry loops.

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Billing decision points and policy denials emit audit/telemetry events.
- Supports measurable outcomes for deterministic recomputation and duplicate prevention.

### Principle VI: Azure Deployment Baseline and LTS Runtime - PASS
- Plan targets .NET 10 and existing Azure runtime baseline (Functions isolated worker, OIDC, Key Vault-based secret resolution).
- No parallel ad hoc infrastructure introduced.

**Initial Gate Result**: PASS

## Project Structure

### Documentation (this feature)

```text
specs/010-billing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── billing-contract.yaml
└── tasks.md    # created later by /speckit.tasks
```

### Source Code (repository root)

```text
src/
├── Aluki.Runtime.Abstractions/
│   ├── Orchestration/
│   ├── Security/
│   └── Skills/
├── Aluki.Runtime.Host/
│   ├── Program.cs
│   └── Worker.cs
└── Aluki.Runtime.Functions/
    ├── Program.cs
    └── Functions/

db/
└── migrations/
```

**Structure Decision**: Keep billing contracts and shared billing DTOs in abstractions, implement orchestration and lifecycle workflows in functions/host layers, and extend PostgreSQL schema through migrations under `db/migrations` with tenant RLS compatibility.

## Phase 0: Research

Research decisions are captured in `specs/010-billing/research.md`:
- tenant ownership and attribution model for `INDIVIDUAL`/`ORGANIZATION`
- dual monetization mode behavior and precedence
- immutable ledger-first accounting and deterministic invoice recomputation
- idempotency policy for usage and charging
- versioned pricing snapshots and package lifecycle/proration strategy
- overage vs hard-stop enforcement and late-arrival adjustment policy

All previously open clarifications in technical context are resolved.

## Phase 1: Design and Contracts

Design artifacts generated:
- `specs/010-billing/data-model.md`: entity model, lifecycle states, validation rules, projections, idempotency keys, RLS notes
- `specs/010-billing/contracts/billing-contract.yaml`: usage recording, entitlements, invoice generation, reconciliation export contracts
- `specs/010-billing/quickstart.md`: executable validation scenarios and measurable acceptance checklist

## Post-Design Constitution Re-Check

### Principle I: Skill-First Execution - PASS
Billing side effects are explicit through contracts and immutable data writes.

### Principle II: Tenant-Scoped Security by Default - PASS
Design preserves tenant ownership, principal checks, and RLS-ready table shape.

### Principle III: Grounded Memory and Provenance - PASS
Invoices and reconciliation remain evidence-based from immutable ledger entries.

### Principle IV: Durable Session and Workflow Separation - PASS
Lifecycle and cycle-close processes remain durable-workflow oriented.

### Principle V: Cost-Aware and Observable Intelligence - PASS
Decision and denial events are observable and measurable against success criteria.

### Principle VI: Azure Deployment Baseline and LTS Runtime - PASS
No baseline/runtime violations introduced in the design artifacts.

**Post-Design Gate Result**: PASS

## Complexity Tracking

No constitution violations identified. No complexity waiver required.
