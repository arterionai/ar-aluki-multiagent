# Implementation Plan: Link Capture and Secure Enrichment

**Branch**: `004-ai-extraction` | **Date**: 2026-06-21 | **Spec**: `specs/009-link-capture/spec.md`

**Input**: Feature specification from `specs/009-link-capture/spec.md`

## Summary

Implement a tenant-scoped link-capture capability that detects and normalizes inbound URLs, persists canonical artifacts with context/provenance, resolves yes/no confirmation exactly once, and enriches link metadata safely with a strict 4-second timeout fallback. Duplicate canonical URLs are upserted (not duplicated), and recall always returns full URL identity with provenance and explicit enrichment status/reason.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- `Microsoft.DurableTask.Client`, `Microsoft.DurableTask.Worker.Grpc`
- `Npgsql` for PostgreSQL persistence
- URL normalization/parsing from .NET runtime (`Uri`) and shared ingestion utilities

**Storage**: PostgreSQL (`link_artifacts`, `link_provenance_refs`, `link_pending_confirmations`, `link_enrichment_attempts`, `link_enrichment_policy_decisions`, `link_audit_events`) with existing tenant RLS model

**Testing**:
- Unit tests for canonical normalization, duplicate upsert merge rules, and one-time confirmation state transitions
- Integration tests for confirmation timeout/late response handling and enrichment timeout fallback (4-second cap)
- Contract tests for capture/confirm/recall payloads

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) on Azure-hosted environment

**Project Type**: Backend skill-driven orchestration service with durable wait-state workflows

**Performance Goals**:
- Non-blocking conversational paths remain within existing platform P95 target (
<= 2s)
- Enrichment waits at most 4 seconds per link attempt
- Recall rendering remains deterministic even when enrichment is blocked/timed out/failed

**Constraints**:
- One active pending confirmation per session-conversation scope
- First explicit yes/no consumes pending confirmation atomically; later yes/no are side-effect free
- Late confirmations after expiry must return explicit expired outcome with no blind save
- Canonical-equivalent duplicates must upsert one active artifact and merge context/provenance
- No outbound enrichment before policy allow decision

**Scale/Scope**:
- Single and multi-URL inbound messages
- Duplicate deliveries from webhook retries
- Enrichment outcomes: `enriched`, `policy_blocked`, `timeout`, `failed`
- Confirmation outcomes: `pending`, `resolved_yes`, `resolved_no`, `expired`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Link capture, confirmation resolution, enrichment, and recall are modeled as explicit skills/contracts.
- Agent orchestration routes flow; business side effects occur through skills only.

### Principle II: Tenant-Scoped Security by Default - PASS
- Principal context and tenant/context constraints are required on all read/write paths.
- Data entities are tenant-scoped and compatible with RLS enforcement.

### Principle III: Grounded Memory and Provenance - PASS
- Recall contract mandates canonical full URL and provenance reference.
- Limited metadata paths remain explicit through enrichment reason/status.

### Principle IV: Durable Session and Workflow Separation - PASS
- Wait-based confirmation windows and expiry are handled in durable workflow state.
- Session handlers only submit/resolve events and return deterministic outcomes.

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Telemetry events defined for capture, confirmation, enrichment policy, enrichment execution, and recall render outcomes.
- Timeout fallback avoids long blocking waits and preserves user-facing utility.

**Initial Gate Result**: PASS

## Project Structure

### Documentation (this feature)

```text
specs/009-link-capture/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── link-capture-contract.yaml
└── tasks.md    # created later by /speckit.tasks
```

### Source Code (repository root)

```text
src/
├── Aluki.Runtime.Abstractions/
│   ├── Orchestration/
│   ├── Skills/
│   └── Security/
├── Aluki.Runtime.Host/
│   ├── Program.cs
│   └── Worker.cs
└── Aluki.Runtime.Functions/
    ├── Program.cs
    └── Functions/

db/
└── migrations/
```

**Structure Decision**: Use existing runtime boundaries: contracts/DTOs in abstractions, orchestrated workflows in functions, and tenant-scoped persistence via PostgreSQL migrations.

## Phase 0: Research

Research artifacts are documented in `specs/009-link-capture/research.md` with all technical unknowns resolved:
- One-time confirmation atomic consume semantics
- 4-second enrichment timeout and fallback metadata strategy
- Duplicate canonical-link upsert and context/provenance merge strategy
- Deterministic recall rendering when metadata is limited

## Phase 1: Design and Contracts

Design artifacts for this feature:
- `specs/009-link-capture/data-model.md`: entity model, validation rules, transitions, and idempotency keys
- `specs/009-link-capture/contracts/link-capture-contract.yaml`: capture/confirm/recall and enrichment outcome contracts
- `specs/009-link-capture/quickstart.md`: end-to-end validation scenarios for one-time confirmation, timeout fallback, duplicate upsert, and recall semantics

## Post-Design Constitution Re-Check

### Principle I: Skill-First Execution - PASS
Contracts keep side effects explicit and idempotent.

### Principle II: Tenant-Scoped Security by Default - PASS
Entity constraints and contract fields include tenant/principal scoping.

### Principle III: Grounded Memory and Provenance - PASS
Recall projection requires provenance and status reason for limited metadata.

### Principle IV: Durable Session and Workflow Separation - PASS
Confirmation timeout/expiry and retries remain in durable workflow state machine.

### Principle V: Cost-Aware and Observable Intelligence - PASS
Timeout ceiling and outcome telemetry are explicit in design artifacts.

**Post-Design Gate Result**: PASS

## Complexity Tracking

No constitution violations identified. No complexity waiver required.
