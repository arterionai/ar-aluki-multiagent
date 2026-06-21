# Data Model: Governance and Security

## Tables

### consent_records
- id: UUID PK
- tenant_id: UUID
- grantor_id: UUID
- grantee_id: UUID
- consent_type: VARCHAR(100)
- granted_at: TIMESTAMP
- revoked_at: TIMESTAMP (nullable)

### audit_events (WORM - Write Once Read Many)
- id: UUID PK
- tenant_id: UUID
- event_type: VARCHAR(100)
- principal_id: UUID
- timestamp: TIMESTAMP
- action: VARCHAR(200)
- outcome: VARCHAR(50)
- reason: VARCHAR(100)
- request_id: VARCHAR(100)

### policy_rules
- id: UUID PK
- tenant_id: UUID
- rule_type: VARCHAR(100)
- operation_type: VARCHAR(100)
- rule_definition: JSONB
- active: BOOLEAN
- created_at, updated_at: TIMESTAMP

## Indexes
- consent_records: (tenant_id, grantor_id, grantee_id, consent_type, granted_at)
- audit_events: (tenant_id, timestamp DESC), (principal_id, timestamp DESC)
- policy_rules: (tenant_id, rule_type, operation_type, active)

## RLS Policies
- consent_records: Filter by tenant_id
- audit_events: SELECT only (no UPDATE/DELETE allowed)
- policy_rules: Filter by tenant_id

## Key Columns
- PrincipalContext: tenant_id (mandatory), principal_id (mandatory), principal_type, session_id, request_id
