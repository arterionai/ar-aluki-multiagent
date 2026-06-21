# Implementation Plan: AI Extraction

**Branch**: `004-ai-extraction` | **Date**: 2026-06-21 | **Spec**: `specs/004-ai-extraction/spec.md`

**Input**: Feature specification from `specs/004-ai-extraction/spec.md`

## Summary

Deliver tenant-scoped, grounded extraction for audio, text, and receipt images through explicit skills and durable orchestration. The implementation will provide transcription, summary/action extraction, and receipt OCR with confidence-tier handling, strict no-fabrication behavior, provenance persistence, and async lifecycle tracking for long-running jobs.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- `Microsoft.DurableTask.Client`, `Microsoft.DurableTask.Worker.Grpc`
- `Npgsql` (PostgreSQL)
- Azure SDK packages for Speech/OCR/OpenAI integrations (exact package selection during tasks phase)

**Storage**: PostgreSQL (`extraction_jobs`, `extraction_results`, `extraction_fields`, `extraction_audit_events`) with existing tenant RLS model

**Testing**:
- Unit tests for confidence tiering, language fallback, and status transitions
- Integration tests for durable extraction lifecycle and idempotency
- Contract tests for extraction skill request/response schema

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) on Azure-hosted environment

**Project Type**: Backend skill-driven orchestration service with async workflows

**Performance Goals**:
- P95 <= 2s for non-blocking synchronous extraction paths
- Async workflow returns job status quickly and tracks progress to completion

**Constraints**:
- Tenant and principal context required for every operation
- No invented data: uncertain fields must be flagged, not fabricated
- Idempotent job submission by `(tenant_id, idempotency_key)`
- Durable/long-running work must stay in Functions orchestration, not session handlers

**Scale/Scope**:
- Input modes: audio, text, receipt image
- Language behavior: auto-detect with region fallback (`es-MX`, `en-US`)
- Status model: `pending`, `processing`, `completed_success`, `completed_with_warnings`, `failed`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Extraction behavior is modeled as explicit skills with contracts and side effects.
- Orchestration routes work and does not hide business-side effects.

### Principle II: Tenant-Scoped Security by Default - PASS
- Principal context and tenant scope are mandatory in request contracts and persistence.
- Data model is tenant-scoped and aligned to RLS.

### Principle III: Grounded Memory and Provenance - PASS
- Confidence-tier model and unreadable fragment handling prevent fabrication.
- Audit events and per-field provenance are included in design.

### Principle IV: Durable Session and Workflow Separation - PASS
- Long transcription/OCR jobs are async and durable.
- Session-level handlers only initiate and query workflow state.

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Telemetry events are defined for request, provider calls, confidence classification, completion/failure.
- Supports monitoring latency, error rates, and cost-sensitive routing decisions.

**Initial Gate Result**: PASS

## Project Structure

### Documentation (this feature)

```text
specs/004-ai-extraction/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── extraction-skill-contract.yaml
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

**Structure Decision**: Use existing runtime projects with skill contracts in abstractions, orchestration/durable logic in functions, and persistence through PostgreSQL migrations.

## Phase 0: Research

Research artifacts are documented in `specs/004-ai-extraction/research.md` with all clarifications resolved:
- Language detection and fallback policy
- Confidence thresholds and surfacing policy
- Mixed-language segment behavior
- OCR fallback sequence and unreadable handling
- Async status lifecycle and idempotency

## Phase 1: Design and Contracts

Design artifacts for this feature:
- `specs/004-ai-extraction/data-model.md`: entities, constraints, SQL-oriented schema details, and RLS notes
- `specs/004-ai-extraction/contracts/extraction-skill-contract.yaml`: request/response/status contract and telemetry mapping
- `specs/004-ai-extraction/quickstart.md`: end-to-end validation guide for audio/text/receipt and async lifecycle paths

## Post-Design Constitution Re-Check

### Principle I: Skill-First Execution - PASS
Design contract enforces explicit skill interfaces.

### Principle II: Tenant-Scoped Security by Default - PASS
Contract and model require tenant/principal context; aligned with RLS.

### Principle III: Grounded Memory and Provenance - PASS
Data model includes per-field confidence + provenance and immutable audit trail.

### Principle IV: Durable Session and Workflow Separation - PASS
Async lifecycle semantics and state model remain in orchestration boundary.

### Principle V: Cost-Aware and Observable Intelligence - PASS
Telemetry and processing metadata are explicit in contract/model artifacts.

**Post-Design Gate Result**: PASS

## Complexity Tracking

No constitution violations identified. No exceptional complexity waiver required.
