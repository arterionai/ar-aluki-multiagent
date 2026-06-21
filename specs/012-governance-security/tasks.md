# Tasks: Governance and Security (Phase 1B)

## Phase 1B-a: RLS Infrastructure (1 week)

- [ ] **GS-001**: Design RLS schema pattern (SET app.current_tenant_id)
- [ ] **GS-002**: Create RLS policies on reminders table
- [ ] **GS-003**: Create RLS policies on delegated_reminders table
- [ ] **GS-004**: Create RLS policies on feedback_suggestions table
- [ ] **GS-005**: Create RLS policies on link_captures table
- [ ] **GS-006**: Create RLS policies on billing tables (ledger, accounts, invoices)
- [ ] **GS-007**: Create RLS policies on semantic_entities, semantic_relationships
- [ ] **GS-008**: Implement ConsentManagementSkill (check, grant, revoke)
- [ ] **GS-009**: Unit tests for RLS policy evaluation
- [ ] **GS-010**: Integration test: cross-tenant query prevention

## Phase 1B-b: Policy Engine & Audit (1 week)

- [ ] **GS-011**: Create immutable audit_events table (WORM)
- [ ] **GS-012**: Implement PolicyDecisionSkill (quota, budget, feature_flag, compliance)
- [ ] **GS-013**: Implement policy rules engine with JSONB evaluation
- [ ] **GS-014**: Implement AuditLogSkill (emit events non-blocking)
- [ ] **GS-015**: Implement AuditQuerySkill (export for compliance)
- [ ] **GS-016**: Unit tests for policy evaluation logic
- [ ] **GS-017**: Integration tests for audit immutability (no UPDATE/DELETE)
- [ ] **GS-018**: Compliance export test (7-year retention query)

## Phase 1B-c: Principal Context Gate (3 days)

- [ ] **GS-019**: Implement PrincipalContext resolution from Entra ID
- [ ] **GS-020**: Middleware for Orleans to establish context
- [ ] **GS-021**: Middleware for Durable Functions to establish context
- [ ] **GS-022**: Fail-closed pattern documentation
- [ ] **GS-023**: Feature team integration checklist
- [ ] **GS-024**: Integration test template for all features
