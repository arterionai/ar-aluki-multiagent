# Billing & Entitlement Timing SLA Resolution

**Date**: 2026-06-21  
**Context**: Resolve BLOCKER B3 from analysis - coordination between billing entitlement checks (010) and quota enforcement in memory/reminders (002, 005, 006).

## Problem Statement

**Conflict**: Billing spec requires entitlement check with P95 <= 2s (non-blocking for conversational runtime), but memory recall and reminder creation need real-time quota enforcement that may require transactional ledger lookups.

**Risk**: If entitlement checks are cached/stale, reminders could exceed quota silently. If they're transactional/blocking, conversation latency degrades.

## Solution: Entitlement Snapshot + Async Reconciliation

### Architecture Pattern

```
WRITE PATH (Reminder Creation):

User: "Remind me tomorrow at 2 PM"
  ↓
1. ResolveContext() → Get PrincipalContext
  ↓
2. CheckEntitlementSnapshot(tenantId, "reminder_create")
   ├─ Read cached entitlement (reload every 60s or on cache-miss)
   ├─ Result: { allowedUnits: 5, currentUsage: 45, quotaMode: "package" }
   └─ Latency: ~50ms (cache hit)
  ↓
3. If allowed: Create reminder + write ledger entry async
   ├─ Main path returns immediately (< 100ms total)
   └─ Background: Emit billing event for usage recording
  ↓
4. If denied: Return error with overage pricing (immediate)

RECONCILIATION PATH (Background):

Every 5 minutes (configurable):
  ├─ Read ledger entries since last reconciliation
  ├─ Aggregate by meter, tenant, cycle
  ├─ Compare against entitlement snapshot
  ├─ If discrepancy:
  │  ├─ Log warning (quota was exceeded despite snapshot allowance)
  │  ├─ Emit audit event "quota_overage_detected"
  │  └─ Trigger policy action (soft-cap warning, charge overage, or hard-stop future operations)
  └─ Update entitlement snapshot cache for next check
```

### Entitlement Snapshot

```csharp
record EntitlementSnapshot(
    Guid TenantId,
    DateTime SnapshotTime,
    Dictionary<string, (int Allowed, int Current)> MetricQuotas,  
    // e.g., "reminder_creates" → (50 allowed, 45 used)
    string BillingMode,  // "payg" | "package"
    string PackageId,    // Current active package (if applicable)
    DateTime CacheExpiry // When this snapshot becomes stale
);
```

**Freshness Policy**:
- Cache valid for: 60 seconds (configurable per tier)
- Miss on cache expiry: Reload from billing service (transactional)
- High-load fallback: If billing service is degraded, allow with warning

### Quota Enforcement Rules

| Scenario | Entitlement Check | Actual Usage | Action |
|---|---|---|---|
| Within quota | Allow (1 unit left) | Create reminder | ✅ Allow + log async |
| At limit | Deny (0 units left) | Don't create | ❌ Reject immediately |
| Over quota (stale snapshot) | Allow (cached) | Concurrent creates exceed | ⚠️ Audit + soft-cap |
| Overage allowed | Allow + warn | Create reminder | ✅ Allow + charge overage |
| Hard-stop policy | Deny (quota exhausted) | Attempt creation | ❌ Reject + explain pricing |

### Ledger Recording (Async, Non-Blocking)

After reminder creation succeeds:

```csharp
// Main path returns immediately
await _reminders.Create(reminderRequest);  // ~100ms

// Background task (fire-and-forget)
_ = Task.Run(async () =>
{
    try
    {
        await _billing.RecordUsage(new UsageEvent
        {
            MetricName = "reminder_creates",
            Quantity = 1,
            TenantId = principalContext.TenantId,
            Timestamp = DateTime.UtcNow,
            IdempotencyKey = reminderRequest.IdempotencyKey
        });
    }
    catch (Exception ex)
    {
        // Log but don't block reminder creation
        _logger.LogWarning($"Async billing recording failed: {ex.Message}");
    }
});
```

## Integration Points

### 002-Personal-Memory
- Before persisting a fact: Check entitlement snapshot for "memory_persists" meter
- If over quota: Reject or warn based on policy
- Async ledger recording after success

### 005-Reminders
- Before creating reminder: Check entitlement snapshot for "reminder_creates" meter
- Quota limit: 50 active reminders (feature limit) + billing quota (from 010)
- Enforce both: feature limit (faster, cached) THEN billing limit

### 006-Delegated-Reminders
- Before sending: Check entitlement snapshot for "delegated_reminder_sends" meter
- Also check: Consent (from 012) + rate limit (10/24h)
- Deny order: Feature limit → Consent → Billing quota

### 010-Billing (This Document)
- EntitlementCheckSkill: Return snapshot (< 100ms)
- ReconciliationJob: Background periodic check (every 5 minutes)
- PolicyDecisionSkill: Evaluate overage behavior (hard-stop vs charge)

## Failure Scenarios

### Scenario 1: Billing Service Degraded
```
Entitlement check hits degradation timeout
  ↓
Fallback: Allow creation with warning
  ↓
Async ledger: Retry with exponential backoff
  ↓
Reconciliation: Detects overage when service recovers
  ↓
Action: Soft-cap warning to user + charge overage
```

### Scenario 2: Duplicate Ledger Entries (Retry Storm)
```
User creates reminder → Ledger entry 1 recorded
User retries (timeout) → Ledger entry 2 recorded
  ↓
Reconciliation detects duplicates (same idempotency key)
  ↓
Action: Keep first, void second (idempotency enforcement)
```

### Scenario 3: Snapshot Lag Allows Quota Overage
```
Snapshot taken at 14:00: 50 reminders allowed, 45 created
Concurrent creates (10 threads) at 14:00-14:01 → 60 reminders created
Actual quota: 50
  ↓
Reconciliation at 14:05: Detects 60 > 50
  ↓
Action: 
  ├─ Audit event "quota_overage"
  ├─ Calculate overage cost (10 × $0.01 = $0.10)
  ├─ Bill tenant for overage (async)
  └─ Future checks: Tighten cache expiry or reduce allowed units
```

## Performance SLA

| Operation | Target | Method |
|---|---|---|
| Entitlement check (cache hit) | < 100ms P95 | In-memory cache |
| Entitlement check (cache miss) | < 300ms P95 | Transactional lookup + refresh |
| Ledger recording (async) | Non-blocking | Background task + retry queue |
| Reconciliation (periodic) | < 5min interval | Background job |
| Quota overage detection | < 10min | Next reconciliation cycle |

## Implementation Checklist

- [ ] Design EntitlementSnapshot cache structure
- [ ] Implement EntitlementCheckSkill with cache layer
- [ ] Implement background reconciliation job
- [ ] Implement quota overage audit events
- [ ] Implement overage policy enforcement (soft-cap, charge, hard-stop)
- [ ] Test concurrent quota exceeds with stale snapshots
- [ ] Test ledger recording idempotency
- [ ] Test billing service degradation fallback
- [ ] Integration tests: 002, 005, 006 with billing checks
- [ ] Load test: 50+ concurrent reminder creates at quota boundary

## Related Documents

- [Quota Coordination Framework](../000-common/quota-coordination.md)
- [Billing Specification](spec.md)
- [Personal Memory Specification](../002-personal-memory/spec.md)
- [Reminders Specification](../005-reminders/spec.md)
