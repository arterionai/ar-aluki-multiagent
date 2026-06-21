# Implementation Order (Starter)

This order minimizes risk and keeps architecture constraints enforced from day one.

## Phase 0 - Foundation

1. Set repository skeleton and CI checks.
2. Add architecture baseline and this starter package.
3. Define coding standards and telemetry naming conventions.

## Phase 1 - Security and data core

1. Apply 001_init_tenancy.sql.
2. Apply 002_init_artifacts.sql.
3. Apply 003_enable_rls.sql.
4. Implement DB session scope middleware:
   - app.current_tenant
   - app.current_user_id

## Phase 2 - Runtime contracts

1. Implement ISkill and SkillExecutionContext contracts in real runtime project.
2. Implement PrincipalContext resolver.
3. Implement SkillDispatcher with policy hooks.

## Phase 3 - Ingress and UMO

1. Build UMO contract.
2. Implement first adapter (WhatsApp recommended).
3. Add signature validation, dedupe, and provenance fields.

## Phase 4 - Session and orchestration

1. Implement Orleans grains:
   - AgentSessionGrain
   - CoordinatorGrain
   - WorkingMemoryGrain
2. Integrate planner and skill selection logic.

## Phase 5 - Long-running workflows

1. Add Durable Functions orchestration entry.
2. Add retry and backoff policy activities.
3. Add event-driven resume support.

## Phase 6 - Core skills

1. CaptureMessageSkill
2. ExtractionSkill
3. EmbeddingIndexSkill
4. RecallSkill
5. CitationRenderSkill
6. TenantScopeSkill
7. AuditLogSkill

## Phase 7 - Model routing policy

1. Add default value-model routing.
2. Add confidence-based escalation.
3. Add budget-aware de-escalation.
4. Add per-tenant cost controls and circuit breaker.

## Phase 8 - Hardening

1. Full OpenTelemetry and App Insights instrumentation.
2. Load and latency baselines (P95 target).
3. Security review of RLS and tenant boundary assumptions.
4. Recovery and rollback procedures.

## Definition of Done (baseline)

- No skill runs without PrincipalContext.
- No query executes without RLS scope.
- No recall response ships without provenance.
- All side effects are audit logged.
- Cost policy is enforced per tenant.
