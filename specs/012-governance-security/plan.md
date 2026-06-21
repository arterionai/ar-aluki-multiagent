# Plan: Governance and Security - Tenant Isolation, RLS, Policy, and Compliance

**Branch**: `012-governance-security` | **Date**: 2026-06-21 | **Spec**: `specs/012-governance-security/spec.md`

**Input**: Feature specification from `specs/012-governance-security/spec.md`

## Summary

Implement transversal governance and security layer covering row-level security (RLS), principal context enforcement, policy decision engine, consent management, and immutable audit logging. Design uses PostgreSQL RLS policies for tenant isolation, Entra ID for principal context, and fail-closed policy evaluation to unblock all downstream features.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- `Npgsql` for PostgreSQL with RLS
- `Azure.Identity` for Entra ID principal resolution
- `System.Security.Claims` for claim-based authorization
- Existing runtime infrastructure (Orleans, Durable Functions)

**Storage**: PostgreSQL with RLS policies on all tenant-scoped tables; immutable audit log with WORM (write-once-read-many) enforcement

**Testing**:
- Unit tests for policy evaluation engine
- Unit tests for consent status determination
- Integration tests for RLS isolation (cross-tenant query prevention)
- Security tests for RLS bypass attempts
- Integration tests for principal context gate in all features

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) on Azure baseline

**Project Type**: Cross-cutting security and governance layer

**Performance Goals**:
- Policy decision < 100ms (P95)
- Audit logging non-blocking (< 50ms P99)
- RLS enforcement zero-latency (DB-side filtering)

**Constraints**:
- RLS mandatory on all tenant-scoped tables (100% coverage)
- Principal context required precondition for all side effects (fail-closed)
- Audit log immutable (no UPDATE/DELETE; only INSERT)
- Policy evaluation must handle undefined rules (default to deny)
- Cross-tenant data leakage = critical security failure

**Scale/Scope**:
- Per-tenant policy evaluation (different rules per organization)
- Support 5+ policy types (quota, budget, feature_flag, compliance, fraud_risk)
- Consent model for delegated operations (7 feature dependencies)
- Audit retention: 7 years (financial), 2 years (other)

## Constitution Check

*GATE: Must pass before Phase 1B research. Re-check after Phase 1B design.*

### Principle I: Skill-First Execution - PASS
- Policy decision, RLS enforcement, and consent checks are explicit skill contracts.
- No implicit security checks in routing logic.

### Principle II: Tenant-Scoped Security by Default - PASS
- All operations require principal context.
- RLS policies enforce tenant isolation at DB layer.
- Cross-tenant queries impossible (fail-closed design).

### Principle III: Grounded Memory and Provenance - PASS
- Immutable audit trail provides full operation provenance.
- Policy denials recorded with reason codes.

### Principle IV: Durable Session vs Workflow Separation - PASS
- RLS checks are synchronous (pre-operation).
- Audit logging is asynchronous (non-blocking).

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Policy decisions include cost estimation.
- All operations audited.

### Principle VI: Azure Deployment Baseline and LTS Runtime - PASS
- Uses standard PostgreSQL RLS and Entra ID.

**Initial Gate Result**: PASS

---

## Phase Breakdown

### Phase 1B-a: RLS Infrastructure (1 week)
- [ ] PostgreSQL RLS schema design (policies, SET app.current_tenant_id pattern)
- [ ] Implement RLS policies on all tenant-scoped tables:
  - [ ] reminders, delegated_reminders
  - [ ] feedback_suggestions, suggestion_classifications
  - [ ] link_captures, enriched_links
  - [ ] billing_ledger, billing_accounts, invoices
  - [ ] semantic_entities, semantic_relationships
  - [ ] consent_records, audit_events
- [ ] ConsentManagementSkill (check, grant, revoke)
- [ ] Unit tests for RLS policy logic
- [ ] Integration test: cross-tenant query returns empty

### Phase 1B-b: Policy Engine & Audit (1 week)
- [ ] PolicyDecisionSkill (evaluate rules against operation)
- [ ] Policy rules engine (quota, budget, feature_flag, compliance evaluation)
- [ ] Immutable audit log table with WORM constraints
- [ ] AuditLogSkill (emit events non-blocking)
- [ ] AuditQuerySkill (export for compliance)
- [ ] Unit tests for policy evaluation
- [ ] Integration tests for audit immutability (no UPDATE/DELETE success)

### Phase 1B-c: Principal Context Gate (3 days)
- [ ] PrincipalContext resolution from Entra ID tokens
- [ ] Middleware to establish context in Orleans/Functions
- [ ] Fail-closed pattern documentation
- [ ] Integration test template for all features
- [ ] Feature team onboarding documentation

---

## Key Dependencies

**Blocking Upstream**: None (independent foundation)  
**Blocking Downstream**: All features (001-010) require RLS + principal context + policy checks

**External Dependencies**:
- PostgreSQL with RLS support (already available)
- Entra ID for principal resolution (already configured)
- Azure Key Vault for policy rule versioning (optional)

---

## Related Specs

- `specs/000-common/contracts/audit-event-schema.yaml` - Unified audit event schema
- `specs/000-common/skills-registry.md` - PolicyDecisionSkill, RLSEnforcementSkill, ConsentManagementSkill, AuditLogSkill, AuditQuerySkill definitions
- ARCHITECTURE_BASELINE.md - Security, governance, and observability principles
- All features (001-010) for integration points
