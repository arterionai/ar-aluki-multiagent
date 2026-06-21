# Implementation Plan: Suggestions Admin and Rewards (SB-008A)

**Branch**: `004-ai-extraction` | **Date**: 2026-06-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-suggestions-admin/spec.md`

## Summary

Deliver a tenant-scoped internal admin capability for suggestion triage and reward operations with strict RBAC, immutable auditability, idempotent reward grants, and operationally safe notification retries. The design extends existing multi-agent runtime patterns using explicit skills for triage and rewards, durable orchestration for retry-safe reward and notification processing, and append-only ledgers for decision evidence.

## Technical Context

**Language/Version**: C# on .NET 10 LTS

**Primary Dependencies**:
- Orleans (admin interaction orchestration)
- Azure Durable Functions (reward/notification long-running workflows)
- Npgsql/PostgreSQL (admin queue, audit, entitlement ledgers)
- Entra ID authN + role claim mapping (AdminReviewer/AdminApprover/AdminAuditor)

**Storage**: PostgreSQL (RLS-enabled admin views, append-only audit ledger, append-only entitlement ledger)

**Testing**:
- xUnit for lifecycle and idempotency unit tests
- integration tests for concurrent reward processing and append-only behavior
- contract tests for admin endpoints and reward decision payloads

**Target Platform**: Azure-hosted .NET runtime (Functions + host) with multi-tenant workloads

**Project Type**: Backend service + admin API contracts with durable workflow integration

**Performance Goals**:
- Deterministic queue pagination and sort under high volume
- Admin mutation audit write in same request transaction
- Notification retry completion (success or dead-letter) within 24 hours

**Constraints**:
- Idempotency boundary must be exactly `(tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id)`
- Payload mismatch for same idempotency tuple must fail as conflict (no side effects)
- RBAC role split: AdminReviewer/AdminApprover/AdminAuditor
- Audit and entitlement ledgers are append-only WORM; corrections via compensating records only
- Notification retries decoupled from grants with bounded exponential backoff (1m, 5m, 15m, 60m, 360m), then dead-letter

**Scale/Scope**:
- Staff triage dashboard with filtering/search/sort/pagination
- Controlled suggestion lifecycle transitions
- Three reward rule types (base, quality, streak)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution
PASS. Side-effecting operations are represented as explicit skills/contracts:
- `SuggestionTriageSkill` (classification/lifecycle updates)
- `RewardDecisionSkill` (grant/skip/reject/conflict decisions)
- `RewardNotificationSkill` (enqueue/retry/dead-letter only)

### Principle II: Tenant-Scoped Security by Default
PASS. All admin and reward operations require tenant scope + principal context and enforce RLS for data access.

### Principle III: Grounded Memory and Provenance
PASS. Every mutation and reward decision writes immutable audit evidence with actor, reason, and linked references.

### Principle IV: Durable Session and Workflow Separation
PASS. Admin API requests are synchronous and bounded; retry-heavy reward/notification flows run in Durable Functions.

### Principle V: Cost-Aware and Observable Intelligence
PASS. Decision telemetry and retry telemetry are structured and emitted for grants, duplicates, conflicts, failures, and dead-letter outcomes.

## Project Structure

### Documentation (this feature)

```text
specs/008-suggestions-admin/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── suggestions-admin-contract.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Aluki.Runtime.Abstractions/
│   ├── Skills/
│   │   ├── SuggestionTriageSkill.cs                 # new
│   │   ├── RewardDecisionSkill.cs                   # new
│   │   └── RewardNotificationSkill.cs               # new
│   └── Orchestration/
│       └── IAgentCoordinator.cs                     # existing integration point
│
├── Aluki.Runtime.Host/
│   ├── Program.cs                                   # DI and policy registration updates
│   ├── Services/
│   │   ├── SuggestionsAdminService.cs               # new
│   │   └── RewardLedgerService.cs                   # new
│   └── Security/
│       └── AdminRoleAuthorizationPolicy.cs          # new
│
└── Aluki.Runtime.Functions/
    ├── Functions/
    │   ├── RewardGrantWorkflowOrchestrator.cs       # new
    │   ├── RewardNotificationRetryOrchestrator.cs   # new
    │   └── RewardActivities.cs                      # new
    └── Program.cs                                   # DI for workflow activities

db/
└── migrations/
    ├── 00x_suggestions_admin_views.sql              # new
    ├── 00x_suggestion_admin_audit_ledger.sql        # new (append-only)
    ├── 00x_reward_entitlement_ledger.sql            # new (append-only)
    └── 00x_reward_notification_delivery.sql         # new
```

**Structure Decision**: Keep current multi-project .NET architecture and add new admin/reward skills and workflow orchestration in-place, avoiding a new service boundary while preserving durable workflow separation.

## Phase 0: Research

Research output is in [research.md](research.md). It resolves:
- Idempotency tuple and conflict semantics
- RBAC split and action matrix
- Append-only WORM enforcement strategy for audit/reward ledgers
- Notification retry strategy with dead-letter and operator replay

## Phase 1: Design and Contracts

Design outputs:
- Data entities, lifecycle transitions, and validation rules in [data-model.md](data-model.md)
- Admin API and workflow contracts in [contracts/suggestions-admin-contract.yaml](contracts/suggestions-admin-contract.yaml)
- End-to-end validation scenarios in [quickstart.md](quickstart.md)

Post-design constitution re-check: PASS. No violations introduced.

## Complexity Tracking

No constitution exceptions required.

