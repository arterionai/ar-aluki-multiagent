# Spec: Governance and Security - Tenant Isolation, RLS, Policy, and Compliance

**SB-ID**: SB-012  
**Feature**: F14 - Seguridad, gobernanza, tenancy y compliance  
**Status**: Ready for Phase 1B  
**Date**: 2026-06-21  
**Owner**: Security & Governance Team

## Executive Summary

Implement tenant-scoped security infrastructure that enforces row-level security (RLS), consent management, policy decision-making, and immutable audit logging across all features. This is **transversal to all features** and must be established in **Phase 1B (parallelizable with Phase 1)** to unblock subsequent phases.

**Launch Requirement**: F14 enforcement must be in place before any feature persists data in Phase 1+. This includes RLS policies, consent checks, policy evaluation, and audit logging.

---

## 1. Problem Statement

### Current Risk (Without Governance Infrastructure)

- **Data leakage**: Without RLS, queries can accidentally return facts from other tenants
- **Unaudited operations**: No immutable record of who did what, when, why
- **Unenforceable policies**: No mechanism to deny operations that violate budget/quota/compliance rules
- **Consent gaps**: Delegated reminders can send without recipient consent
- **Inconsistent scope checks**: Each feature re-implements tenant validation logic

### Desired State (With Governance)

```
Operation (e.g., create reminder):
  1. Principal context resolved (user + tenant + session)
  2. Policy decision engine evaluates: quota? budget? consent? compliance?
  3. If denied: audit event logged, error returned to user
  4. If allowed: operation proceeds, audit event logged with outcome
  5. RLS at persistence layer ensures no cross-tenant leakage
```

---

## 2. Requirements

### F-14.1: Row-Level Security (RLS) for All Data

**Description**:
- PostgreSQL RLS policies on all tenant-scoped tables
- Every query returns only data for current tenant
- Cross-tenant access attempts are audited and denied

**Acceptance Criteria**:
- 100% of tenant-scoped tables have RLS policies enabled
- RLS policies use `current_setting('app.current_tenant_id')::uuid` for context
- No query results without tenant filter can escape RLS
- Audit event emitted for any RLS violation attempt

### F-14.2: Consent Management

**Description**:
- Store consent decisions (user A permits user B to send delegated reminders)
- Verify consent before operation execution
- Support consent revocation with audit trail

**Acceptance Criteria**:
- Consent records include: grantor, grantee, consent_type, granted_at, revoked_at
- Consent check mandatory for delegated operations (feature 006)
- Revocation prevents new operations but doesn't delete old ones (audit trail)
- User can view and manage all consent grants they've given/received

### F-14.3: Policy Decision Engine

**Description**:
- Evaluate operations against quota policies, budget limits, feature flags, compliance rules
- Return structured decision (allow/deny/warn) with reason code
- Support policy override for exceptions (e.g., admin override)

**Acceptance Criteria**:
- Policy evaluation completes in < 100ms
- At least 5 policy types supported (quota, budget, feature_flag, compliance, fraud_risk)
- Decision includes machine-readable reason code for logging
- Audit trail for all policy decisions

### F-14.4: Immutable Audit Logging

**Description**:
- Append-only audit log with RLS (each tenant sees only their events)
- Audit events immutable after insertion (no update/delete)
- Support compliance queries (who accessed what, when, why)

**Acceptance Criteria**:
- All operations log audit events (success, denial, error)
- Audit events include: timestamp (UTC), principal, action, outcome, reason, tenant
- Audit log retention: 7 years for billing/financial, 2 years for others
- Export capability for compliance reviews (audit trail export)

### F-14.5: Principal Context Gate

**Description**:
- Every operation must establish principal context (user + tenant + session)
- Fail closed if context is incomplete or unresolved
- Principal context is mandatory precondition for any side effect

**Acceptance Criteria**:
- All skills require PrincipalContext parameter before side effects
- Missing context triggers audit event + error, no silent bypass
- Session context includes request_id, channel, source for tracing

---

## 3. Architecture

### 3.1 Core Concepts

```csharp
// Principal Context: Required for every operation
record PrincipalContext(
    Guid TenantId,          // Mandatory tenant scope
    Guid PrincipalId,       // User ID or system service principal
    string PrincipalType,   // user | system_service | admin
    Guid SessionId,         // Conversation session ID
    string Channel,         // whatsapp | teams | email | web
    string RequestId = null,  // For tracing
    IReadOnlyDict<string, string> Claims = null  // From Entra ID token
);

// Policy Decision
record PolicyDecision(
    bool Allowed,
    string Reason,          // allow | quota_exceeded | insufficient_balance | unauthorized
    double? EstimatedCost,  // For billing decisions
    string? WarningMessage  // Optional warning for user
);

// Consent Record
record ConsentRecord(
    Guid ConsentId,
    Guid TenantId,
    Guid GrantorId,         // User who grants consent
    Guid GranteeId,         // User who receives consent
    string ConsentType,     // delegated_reminder_send | share_memory | etc.
    DateTime GrantedAt,
    DateTime? RevokedAt,    // null = currently active
    bool Active => RevokedAt == null
);

// Audit Event
record AuditEvent(
    Guid EventId,
    string EventType,       // From audit-event-schema
    Guid TenantId,
    Guid PrincipalId,
    DateTime Timestamp,
    string Action,
    string Outcome,         // success | denied | failed
    string Reason = null,
    string RequestId = null
);
```

### 3.2 Skills

#### PolicyDecisionSkill

**Input**:
```json
{
  "principal_context": {
    "tenant_id": "tenant-001",
    "principal_id": "user-001",
    "principal_type": "user"
  },
  "operation": "create_reminder",
  "operation_metadata": {
    "estimated_cost": 0.01,
    "resource_type": "reminder"
  },
  "idempotency_key": "policy-check-20260621-user001-abc123"
}
```

**Output**:
```json
{
  "allowed": true,
  "reason": "within_quota_and_budget",
  "estimated_cost": 0.01,
  "policy_version": 5,
  "applied_policies": ["quota_check", "budget_check"],
  "warning_message": null
}
```

**Failure Modes**:
- Principal context missing → Fail closed, audit and deny
- Policy evaluation error → Default to deny (conservative)
- Unsupported operation → Log warning, return allow with explanation

#### RLSEnforcementSkill

**Input**:
```json
{
  "principal_context": {
    "tenant_id": "tenant-001"
  },
  "table_name": "reminders",
  "operation": "select"
}
```

**Output**:
```json
{
  "rls_policy_applied": true,
  "filter": "tenant_id = 'tenant-001'",
  "enforcement_status": "active"
}
```

#### ConsentManagementSkill

**Input**:
```json
{
  "principal_context": {
    "tenant_id": "tenant-001",
    "principal_id": "user-001"
  },
  "consent_type": "delegated_reminder_send",
  "target_principal_id": "user-002",
  "action": "check"
}
```

**Output**:
```json
{
  "consent_granted": true,
  "consent_id": "consent-123",
  "granted_at": "2026-06-15T10:00:00Z",
  "active": true
}
```

### 3.3 Database Schema

```sql
-- RLS Policy Setup (per tenant-scoped table)
CREATE TABLE reminders (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    principal_id UUID NOT NULL,
    content TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    
    INDEX idx_tenant_reminder (tenant_id, created_at)
);

-- Enable RLS
ALTER TABLE reminders ENABLE ROW LEVEL SECURITY;

-- Policy: Users see only their tenant's reminders
CREATE POLICY tenant_isolation_policy ON reminders
    FOR ALL
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Consent Table
CREATE TABLE consent_records (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    grantor_id UUID NOT NULL,
    grantee_id UUID NOT NULL,
    consent_type VARCHAR(100) NOT NULL,
    granted_at TIMESTAMP WITH TIME ZONE NOT NULL,
    revoked_at TIMESTAMP WITH TIME ZONE,
    
    INDEX idx_tenant_consent (tenant_id, grantor_id, grantee_id, consent_type),
    UNIQUE (tenant_id, grantor_id, grantee_id, consent_type, granted_at)
);

ALTER TABLE consent_records ENABLE ROW LEVEL SECURITY;

CREATE POLICY consent_isolation ON consent_records
    FOR ALL
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Immutable Audit Log (WORM - Write Once Read Many)
CREATE TABLE audit_events (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    principal_id UUID NOT NULL,
    timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    action VARCHAR(200) NOT NULL,
    outcome VARCHAR(50) NOT NULL,  -- success | denied | failed
    reason VARCHAR(100),
    request_id VARCHAR(100),
    
    -- NO UPDATE or DELETE allowed; only INSERT
    INDEX idx_tenant_timestamp (tenant_id, timestamp DESC),
    INDEX idx_principal_timestamp (principal_id, timestamp DESC)
);

ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;

CREATE POLICY audit_isolation ON audit_events
    FOR SELECT
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Prevent updates/deletes on audit log
CREATE POLICY audit_immutable_insert ON audit_events
    FOR INSERT
    WITH CHECK (true);  -- Allow insert only

CREATE POLICY audit_no_update ON audit_events
    FOR UPDATE
    USING (false);  -- No updates

CREATE POLICY audit_no_delete ON audit_events
    FOR DELETE
    USING (false);  -- No deletes

-- Policy Rules Table (governance)
CREATE TABLE policy_rules (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    rule_type VARCHAR(100) NOT NULL,  -- quota | budget | feature_flag | compliance
    operation_type VARCHAR(100) NOT NULL,  -- create_reminder | send_delegated | etc.
    rule_definition JSONB NOT NULL,
    active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    
    INDEX idx_tenant_rule (tenant_id, rule_type, operation_type, active)
);

-- Example policy rule:
-- {
--   "rule_type": "quota",
--   "operation_type": "create_reminder",
--   "rule_definition": {
--     "max_active": 50,
--     "enforcement": "hard_stop"
--   }
-- }
```

---

## 4. Integration with All Features

### Principal Context Gate

Every feature MUST establish PrincipalContext before any side effect:

```csharp
// In any skill that modifies data
async Task<Result> CreateReminder(CreateReminderRequest req)
{
    // Step 1: Resolve principal context from request/session
    var principalContext = await ResolvePrincipalContext(req);
    if (principalContext == null)
    {
        await _auditLog.LogDenial("create_reminder", "no_principal_context");
        return Error("Unauthorized");
    }
    
    // Step 2: Check policy
    var policyDecision = await _policyEngine.Decide(
        principalContext,
        "create_reminder");
    if (!policyDecision.Allowed)
    {
        await _auditLog.LogDenial("create_reminder", policyDecision.Reason);
        return Error(policyDecision.WarningMessage);
    }
    
    // Step 3: Check consent (if applicable, e.g., delegated reminder)
    if (req.IsDelegated)
    {
        var consent = await _consentMgmt.CheckConsent(
            principalContext.PrincipalId,
            req.RecipientId,
            "delegated_reminder_send");
        if (!consent.Granted)
        {
            await _auditLog.LogDenial("create_reminder", "no_consent");
            return Error("Recipient has not granted permission");
        }
    }
    
    // Step 4: Execute operation (with RLS in DB)
    var reminderId = await _db.InsertReminder(new Reminder
    {
        TenantId = principalContext.TenantId,
        PrincipalId = principalContext.PrincipalId,
        Content = req.Content,
        CreatedAt = DateTime.UtcNow
    });
    
    // Step 5: Log success
    await _auditLog.LogSuccess("create_reminder", reminderId);
    
    return Success(reminderId);
}
```

### RLS in Queries

All queries are automatically filtered by RLS:

```csharp
// In any repository
async Task<List<Reminder>> GetRemindersForTenant(string tenantId)
{
    // Set tenant context in session
    await _db.ExecuteAsync("SET app.current_tenant_id = @tenantId", tenantId);
    
    // This query will only return reminders for this tenant (enforced by RLS)
    var reminders = await _db.Query(
        "SELECT * FROM reminders WHERE created_at > @since",
        new { since = DateTime.UtcNow.AddDays(-7) });
    
    return reminders;
}
```

---

## 5. Compliance and Audit

### Audit Trail Example

```json
[
  {
    "event_id": "ae-001",
    "event_type": "reminder.created",
    "tenant_id": "tenant-001",
    "principal_id": "user-001",
    "timestamp": "2026-06-21T14:32:10Z",
    "action": "create_reminder",
    "outcome": "success",
    "request_id": "req-555"
  },
  {
    "event_id": "ae-002",
    "event_type": "reminder.created",
    "tenant_id": "tenant-001",
    "principal_id": "user-002",
    "timestamp": "2026-06-21T14:35:00Z",
    "action": "create_delegated_reminder",
    "outcome": "denied",
    "reason": "no_consent",
    "request_id": "req-556"
  }
]
```

### Compliance Export

Features must support audit export for compliance:

```csharp
// AuditQuerySkill
async Task<AuditExportResult> ExportAuditTrail(
    string tenantId,
    DateTime startDate,
    DateTime endDate,
    string? filterByEventType = null)
{
    var events = await _db.Query(
        "SELECT * FROM audit_events WHERE tenant_id = @tenant AND timestamp BETWEEN @start AND @end",
        new { tenant = tenantId, start = startDate, end = endDate });
    
    if (filterByEventType != null)
        events = events.Where(e => e.EventType == filterByEventType).ToList();
    
    return new AuditExportResult
    {
        ExportId = Guid.NewGuid(),
        TenantId = tenantId,
        EventCount = events.Count,
        DateRange = (startDate, endDate),
        Events = events
    };
}
```

---

## 6. Acceptance Criteria

| Criterion | Metric | Target |
|---|---|---|
| **RLS Coverage** | Tables with RLS | 100% of tenant-scoped tables |
| **Policy Decision Latency** | P95 | < 100ms |
| **Audit Event Latency** | P99 | < 50ms (non-blocking) |
| **Consent Accuracy** | Correctness | 100% (no false grants/denials) |
| **Audit Immutability** | Breach Attempts | 0 (no UPDATE/DELETE on audit log) |
| **Cross-Tenant Leakage** | RLS Bypass Success | 0% |

---

## 7. Testing Strategy

### Unit Tests
- Policy evaluation logic (quota, budget, compliance rules)
- Consent status determination
- Audit event structure validation

### Integration Tests
- End-to-end: Operation → Policy check → Consent check → Persist → Audit log
- RLS enforcement (query without tenant filter returns error)
- Consent revocation (existing operations unaffected, new denied)
- Cross-tenant isolation (query with wrong tenant returns empty)

### Security Tests
- RLS bypass attempts (SQL injection, context hijacking)
- Audit log tampering (UPDATE/DELETE attempts)
- Consent spoofing (principal claims authorization for another user)

---

## 8. Constitution Check

✅ **Principle I: Skill-First Execution**  
Policy decision, RLS enforcement, and consent checks are explicit skill contracts.

✅ **Principle II: Tenant-Scoped by Default**  
All operations require valid principal context; RLS enforced at DB layer.

✅ **Principle III: Grounded Memory**  
Audit trail provides full provenance for every operation.

✅ **Principle IV: Durable Session vs Workflow**  
RLS checks are synchronous; audit logging is async (non-blocking).

✅ **Principle V: Cost-Aware and Observable**  
All operations audited; policy decisions include cost estimation.

✅ **Principle VI: Azure Baseline**  
Uses standard PostgreSQL RLS and Entra ID principal context.

**Gate Result**: ✅ **PASS**

---

## 9. Dependencies

### Hard Dependencies
- PostgreSQL with RLS support (already available)
- Entra ID for principal context resolution
- Feature teams must call PolicyDecisionSkill before side effects

### Blocking Downstream Features
- All other features (001-010) cannot launch without F14 infrastructure

### No Upstream Blockers

---

## 10. Timeline

| Phase | Duration | Deliverable |
|---|---|---|
| **Phase 1B-a** | 1 week | RLS policies on all tables + ConsentManagementSkill |
| **Phase 1B-b** | 1 week | PolicyDecisionSkill + immutable audit logging |
| **Phase 1B-c** | 3 days | PrincipalContext gate integration + testing |

**Total**: 2.5 weeks as part of Phase 1B (parallelizable with Phase 1)

---

## Related Documents

- `specs/000-common/contracts/audit-event-schema.yaml` - Audit event standard
- `specs/000-common/skills-registry.md` - PolicyDecisionSkill, RLSEnforcementSkill, ConsentManagementSkill
- ARCHITECTURE_BASELINE.md - Principles 1, 2, 3, 6 directly addressed
