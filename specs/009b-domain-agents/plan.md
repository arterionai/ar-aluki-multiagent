# Implementation Plan: Domain Agents Runtime

**Branch**: `004-ai-extraction` | **Date**: 2026-06-21 | **Spec**: `specs/009-domain-agents/spec.md`

**Input**: Feature specification from `specs/009-domain-agents/spec.md`

## Summary

Introduce deterministic domain-agent dispatch that keeps the core processor thin and policy-driven. Routing is enforced with fixed precedence (policy gating -> eligibility -> deterministic selection -> fallback only when zero eligible), deterministic tie-break (canonical agent identifier, then registration timestamp), explicit no-fallback-on-failure behavior, and immutable audit evidence for every dispatch cycle including failure containment outcomes.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- Runtime abstractions in `src/Aluki.Runtime.Abstractions` (`Orchestration`, `Security`, `Skills`)
- Host and functions runtimes in `src/Aluki.Runtime.Host` and `src/Aluki.Runtime.Functions`
- PostgreSQL data access stack (`Npgsql`) and existing RLS patterns

**Storage**: PostgreSQL for immutable dispatch audit events and domain-state boundaries under tenant-scoped RLS

**Testing**:
- Unit tests for deterministic precedence and tie-break logic
- Integration tests for fallback constraints and failure containment
- Contract tests for routing decision and audit payload schema

**Target Platform**: Backend runtime (`Aluki.Runtime.Host` + `Aluki.Runtime.Functions`) on Azure-hosted environment

**Project Type**: Multi-project backend runtime/orchestration service

**Performance Goals**:
- Deterministic dispatch decision per message within existing synchronous response SLO (`P95 <= 2s` for non-blocking flows)
- Failure containment with no impact on subsequent dispatch cycles

**Constraints**:
- Policy gating executes before any domain-agent evaluation
- Exactly one selected agent per cycle at most
- Fallback allowed only when zero eligible agents after gating
- Selected-agent failure cannot be masked by fallback in same cycle
- Immutable audit trail required for routing inputs, rationale, outcome, and correlation metadata

**Scale/Scope**:
- Multiple domain agents registered per tenant/runtime
- Deterministic replays must produce identical routing outcomes for identical normalized message + principal contexts
- Covers dispatch/routing/runtime governance only; no new end-user capabilities

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Agents remain routing/orchestration units; irreversible business effects stay on registered skills.
- Plan forbids bypass of SkillDispatcher for side effects.

### Principle II: Tenant-Scoped Security by Default - PASS
- Routing requires principal context and tenant gating before eligibility.
- Cross-domain state access attempts are denied and audited.

### Principle III: Grounded Memory and Provenance - PASS
- Dispatch decisions and outcomes are persisted as immutable evidence.
- Fallback and tie-break rationale are explicit and replay-verifiable.

### Principle IV: Durable Session and Workflow Separation - PASS
- Dispatch remains a runtime orchestration concern.
- No long-running workflow logic moved into session handlers.

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Every dispatch emits auditable telemetry and containment outcomes.
- Deterministic routing reduces operational ambiguity and rework.

**Initial Gate Result**: PASS

## Project Structure

### Documentation (this feature)

```text
specs/009-domain-agents/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── domain-agent-routing-contract.yaml
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

**Structure Decision**: Reuse existing runtime projects; implement routing contracts in abstractions, dispatch orchestration in runtime host/functions, and immutable audit persistence in PostgreSQL migrations.

## Phase 0: Research

Research results are captured in `specs/009-domain-agents/research.md` and resolve routing-policy unknowns and implementation choices for:
- fixed precedence enforcement;
- deterministic tie-break semantics;
- fallback constraints;
- failure containment boundaries;
- immutable audit schema and correlation.

## Phase 1: Design and Contracts

Design artifacts created for this feature:
- `specs/009-domain-agents/data-model.md`: entities, constraints, relationships, and state transitions for routing and audit evidence.
- `specs/009-domain-agents/contracts/domain-agent-routing-contract.yaml`: request/response/event contracts for dispatch decisions, tie-break rationale, fallback, and failure containment.
- `specs/009-domain-agents/quickstart.md`: end-to-end validation scenarios and replay checks for deterministic routing and containment behavior.

## Post-Design Constitution Re-Check

### Principle I: Skill-First Execution - PASS
Contract enforces skill-first side-effect path and denies bypass behavior.

### Principle II: Tenant-Scoped Security by Default - PASS
Data model includes required tenant/principal fields and policy-denied audit events.

### Principle III: Grounded Memory and Provenance - PASS
Audit event model is immutable and captures decision inputs/rationale/outcomes.

### Principle IV: Durable Session and Workflow Separation - PASS
Design keeps dispatch orchestration in runtime boundary without workflow leakage.

### Principle V: Cost-Aware and Observable Intelligence - PASS
Contract includes structured routing telemetry and containment diagnostics.

**Post-Design Gate Result**: PASS

## Complexity Tracking

No constitution violations identified; no exceptions required.
