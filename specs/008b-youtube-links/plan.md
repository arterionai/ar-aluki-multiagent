# Implementation Plan: YouTube Link Save and Classification

**Branch**: `004-ai-extraction` | **Date**: 2026-06-21 | **Spec**: `specs/008-youtube-links/spec.md`

**Input**: Feature specification from `specs/008-youtube-links/spec.md`

## Summary

Implement deterministic YouTube link capture in tenant scope with canonical identity deduplication, provider fallback enrichment, structured classification, and transparent user outcomes. The flow processes each unique canonical video once per message, rejects unsupported/malformed URLs with explicit user feedback, performs idempotent refresh on repeated submissions per tenant, and surfaces classification confidence labels (`high`, `medium`, `low`) with uncertainty markers.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- Runtime and hosting: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- Persistence and SQL access: `Npgsql`
- Existing runtime contracts in `src/Aluki.Runtime.Abstractions`
- Provider clients for primary/secondary metadata enrichment (selected during tasks phase)

**Storage**: PostgreSQL with tenant RLS and new link-capture tables for artifacts, enrichment, classification, and audit events

**Testing**:
- Unit tests for URL normalization, unsupported URL rejection, dedupe behavior, and confidence labeling
- Integration tests for provider fallback order and idempotent refresh semantics
- Contract tests for inbound/save and outcome payloads

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) with tenant-scoped channel ingestion

**Project Type**: Skill-driven backend orchestration and persistence feature

**Performance Goals**:
- Preserve non-blocking conversational target of P95 <= 2 seconds for standard capture flows
- At least 95% of processed links complete with confirmation in <= 2 seconds (per spec)

**Constraints**:
- Deterministic enrichment order: primary provider -> secondary provider -> degraded save
- Unsupported/malformed YouTube URLs are not persisted and must emit invalid-link outcome + audit event
- Idempotent upsert by `(tenant_id, canonical_video_id)` with same-message dedupe by canonical identity
- Confidence visibility required in user confirmation (`high`, `medium`, `low`) and uncertain-field marking when low confidence

**Scale/Scope**:
- One message may include multiple links; each unique canonical identity handled independently
- Duplicate canonical identities in same message are processed once
- Cross-tenant submissions of same canonical video remain isolated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Capture behavior maps to explicit skills (`LinkCaptureSkill`, enrichment/classification skill boundaries, `AuditLogSkill`).
- Agents coordinate; skills own side effects and persistence.

### Principle II: Tenant-Scoped Security by Default - PASS
- Plan requires principal + tenant + context scope before processing.
- Data model persists tenant ownership and enforces RLS-compatible keys.

### Principle III: Grounded Memory and Provenance - PASS
- Saved links maintain provenance to inbound evidence.
- Low-confidence classification fields are marked uncertain instead of invented.

### Principle IV: Durable Session and Workflow Separation - PASS
- Long provider delays/outages are handled through fallback/degraded behavior without mixing wait-heavy logic into live session handlers.

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Provider fallback and outcome states are auditable and telemetry-emitting.
- Degraded save avoids hard failure and unnecessary retries when providers are unavailable.

**Initial Gate Result**: PASS

## Project Structure

### Documentation (this feature)

```text
specs/008-youtube-links/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── youtube-link-capture-contract.yaml
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

**Structure Decision**: Use existing runtime projects with new YouTube link contracts in abstractions, orchestration/capture handling in host/functions, and PostgreSQL migrations under `db/migrations` for persistence and auditability.

## Phase 0: Research

Research outcomes are documented in `specs/008-youtube-links/research.md` and resolve feature clarifications:
- deterministic provider fallback order,
- unsupported/malformed URL handling,
- duplicate behavior within and across submissions,
- confidence visibility rules.

## Phase 1: Design and Contracts

Design artifacts for this feature:
- `specs/008-youtube-links/data-model.md`: entities, relationships, state/outcome fields, and constraints
- `specs/008-youtube-links/contracts/youtube-link-capture-contract.yaml`: inbound processing and outcome contract
- `specs/008-youtube-links/quickstart.md`: end-to-end validation guide for enriched/partial/degraded and invalid-link paths

## Post-Design Constitution Re-Check

### Principle I: Skill-First Execution - PASS
Contracts and model preserve explicit skill boundaries for side effects.

### Principle II: Tenant-Scoped Security by Default - PASS
Tenant/context ownership and auditable denial paths remain mandatory.

### Principle III: Grounded Memory and Provenance - PASS
Provenance mapping and uncertainty markers are represented in persisted model and user responses.

### Principle IV: Durable Session and Workflow Separation - PASS
Design keeps long-running variability in provider behavior isolated from conversational session logic.

### Principle V: Cost-Aware and Observable Intelligence - PASS
Fallback and degraded outcomes are explicit and measurable through outcome and audit fields.

**Post-Design Gate Result**: PASS

## Complexity Tracking

No constitution violations identified. No complexity waiver required.
