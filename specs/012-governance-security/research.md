# Research: Governance and Security

## Row-Level Security (RLS) in PostgreSQL

### Context Variables
- `app.current_tenant_id`: Set before each query to establish tenant context
- Pattern: `SET app.current_tenant_id = @tenantId; SELECT * FROM table;`

### RLS Policy Best Practices
- One policy per table per operation (SELECT, INSERT, UPDATE, DELETE)
- Use immutable columns (tenant_id) in policy predicates
- Test cross-tenant query attempts

## Policy Decision Engine

### Policy Types
1. **Quota**: Max resources (e.g., 50 active reminders)
2. **Budget**: Max cost (e.g., $100/month)
3. **Feature Flag**: Enable/disable operations
4. **Compliance**: GDPR, CCPA, industry-specific rules
5. **Fraud Risk**: Unusual activity detection

### Evaluation Order
1. Feature-specific checks (fast)
2. Quota checks (cached)
3. Budget checks (transactional)
4. Compliance checks (external API)

## Consent Management

### Consent Types
- `delegated_reminder_send`: User A can send reminders on behalf of User B
- `memory_share`: User A can view User B's memories
- `analytics_opt_in`: User allows data for analytics

### Revocation Semantics
- Revocation does NOT delete historical operations
- Only blocks new operations from grantor
- Audit trail preserved for compliance

## Audit Logging

### Immutability Enforcement
- CREATE POLICY audit_immutable_insert: Allow INSERT only
- CREATE POLICY audit_no_update: Deny UPDATE
- CREATE POLICY audit_no_delete: Deny DELETE

### Retention Policy
- **Financial/Billing**: 7 years (regulatory)
- **Other**: 2 years (operational)
- **Deletion**: Scheduled archival to cold storage after retention expires

## External References

- PostgreSQL RLS Documentation: https://www.postgresql.org/docs/current/sql-createrole.html
- Entra ID (Azure AD) Token Claims: https://learn.microsoft.com/en-us/azure/active-directory/develop/access-token-claims-reference
- GDPR Compliance Checklist: https://gdpr-info.eu/
