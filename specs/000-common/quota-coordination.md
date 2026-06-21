# Quota Coordination Framework

**Status**: Baseline  
**Version**: 1.0  
**Date**: 2026-06-21

## Overview

Multiple features (`005-reminders`, `006-delegated-reminders`, `010-billing`) define quotas that must be coordinated to avoid conflicts and ensure consistent user experience. This document establishes a unified enforcement model.

## Quota Types

### Type 1: Stateful Limits (Feature-Specific)

Features maintain their own active counts:

| Feature | Quota | Enforcement | Override |
|---|---|---|---|
| **005-reminders** | Max 50 active reminders per tenant | Hard-stop (creation rejected) | Package tier (premium: 200) |
| **006-delegated-reminders** | Max 10 delegated reminders per recipient | Soft-warn (warn user, allow creation) | Organization policy |

### Type 2: Rate Limits (Time-Window)

Features enforce operations per time window:

| Feature | Quota | Enforcement | Override |
|---|---|---|---|
| **006-delegated-reminders** | Max 10 reminder sends per recipient per 24h | Hard-stop after limit | Admin override |
| **007-feedback** | Max 50 suggestions per tenant per 30-day cycle | Soft-cap (encourage, allow overflow) | Feature flag |

### Type 3: Billing-Driven Entitlements (010)

Billing controls access to features based on subscription tier:

| Meter | Unit | Included (Free) | Standard Pkg | Premium Pkg | Overage Price |
|---|---|---|---|---|---|
| **Reminder Deliveries** | per delivery | 100/month | 1000/month | Unlimited | $0.01/delivery |
| **Delegated Reminder Sends** | per send | 50/month | 500/month | 2000/month | $0.05/send |
| **Suggestion Submissions** | per suggestion | 100/month | Unlimited | Unlimited | N/A (free tier only) |

## Enforcement Architecture

### Pattern: Three-Layer Checks

Every quota-constrained operation follows this pattern:

```csharp
async Task<QuotaCheckResult> CheckQuotaBeforeOperation(
    QuotaContext context)  // tenant_id, principal_id, operation_type, etc.
{
    // Layer 1: Feature-Specific Stateful Limit
    var featureQuotaStatus = await _feature.CheckActiveCount(context);
    if (featureQuotaStatus.ExceededHardLimit)
        return Deny("active_limit_exceeded");
    if (featureQuotaStatus.ApproachingLimit)
        WarnUser("You have 5 reminders left");
    
    // Layer 2: Feature-Specific Rate Limit
    var rateStatus = await _feature.CheckTimeWindowLimit(context);
    if (rateStatus.ExceededHardLimit)
        return Deny("rate_limit_exceeded_24h");
    
    // Layer 3: Billing-Driven Entitlement
    var billingStatus = await _billing.CheckEntitlementQuota(
        context.TenantId, 
        context.Operation);  // e.g., "reminder_send"
    
    if (billingStatus.AllowedUnits <= 0)
    {
        if (billingStatus.OverageAllowed)
            return Allow(chargeOverage: true);
        else
            return Deny("quota_exhausted_no_overage");
    }
    
    return Allow(chargeUsage: true);
}
```

### Key Principles

1. **Feature checks come first** (faster, cached)
2. **Billing checks come last** (slower, authoritative)
3. **Deny is fail-closed** (safer to reject than allow over-quota)
4. **Audit every check** (for billing reconciliation)

## Coordination Rules

### Rule 1: Hierarchy of Quota Limits

If multiple limits apply, **the most restrictive wins**:

```
Hard limit from feature > Soft limit from feature > Billing entitlement
```

Example:
- Feature says: "Max 50 active reminders"
- Billing says: "1000 deliveries/month included"
- User has 49 active reminders, 999 deliveries this month
- **Result**: User CAN create 1 more reminder (feature has room)
  - If user then tries to create #51: **Denied by feature limit** (50 active max)

### Rule 2: Overage Pricing Only for Billing Quotas

- **Feature quotas** (stateful/rate limits): No overage; hard-stop or soft-warn
- **Billing quotas**: Support overage pricing per meter

Example:
```
Scenario: User with 50 active reminders (at feature limit)
         But only 100 deliveries used of 1000 included

Result: DENY — Feature limit (50 active) blocks creation
        Billing has room, but feature quota is more restrictive
```

### Rule 3: Package Transitions

When a tenant upgrades/downgrades a package:

```csharp
async Task UpdatePackageQuotas(string tenantId, string newPkg)
{
    // Get old and new quotas
    var oldQuotas = await _billing.GetPackageQuotas(CurrentPackage(tenantId));
    var newQuotas = await _billing.GetPackageQuotas(newPkg);
    
    // If downgrading (new < old), notify user
    if (newQuotas.ReminderLimit < oldQuotas.ReminderLimit)
    {
        var activeCount = await _reminders.CountActive(tenantId);
        if (activeCount > newQuotas.ReminderLimit)
        {
            // Warn: "You have 60 active reminders but your new plan allows 50"
            NotifyUserOverLimit(tenantId, activeCount, newQuotas.ReminderLimit);
            // Mark oldest reminders for soft-disable (not deletion)
        }
    }
    
    // Record entitlement snapshot for this cycle
    await _billing.RecordEntitlementSnapshot(tenantId, newPkg, newQuotas);
}
```

### Rule 4: Billing Cycle Reset

At the start of each billing cycle, **all rate-limited quotas reset**:

```
Billing Cycle Event (e.g., 2026-07-01T00:00:00Z):
  - Reset delegated reminder sends (24h sliding window → new counter)
  - Reset monthly suggestion count
  - Update entitlement snapshot (for cost reconciliation)
  - Audit log: "quota_cycle_reset" event
```

## Cross-Feature Dependencies

### 005-Reminders → 010-Billing

When `005` creates a reminder:
1. Check feature limit (50 active max)
2. Check rate limit (N per 24h)
3. **Call billing to check "reminder_creation" meter** → reserve 1 unit
4. If billing denies → return error to user with overage pricing info

### 006-Delegated-Reminders → 010-Billing + 005-Reminders

When `006` sends a delegated reminder:
1. Check feature limit (10 delegated per recipient)
2. Check rate limit (10 sends per 24h per recipient)
3. **Call billing to check "reminder_send" meter** → reserve 1 unit
4. If feature check fails → deny (no billing charge)
5. If billing check fails → offer overage pricing

### 010-Billing Enforcement

Billing is the **final authority** for cost control:

```csharp
// In BillingSkill
async Task<EntitlementDecision> CheckMetricQuota(
    string tenantId, 
    string metricName,      // e.g., "reminder_send"
    int unitsRequested)
{
    var entitlement = await GetCurrentEntitlement(tenantId);
    var currentUsage = await GetMonthlyUsage(tenantId, metricName);
    
    var allowedUnits = entitlement.GetAllowedUnits(metricName) - currentUsage;
    
    return new EntitlementDecision
    {
        Allowed = allowedUnits > 0,
        AllowedUnits = allowedUnits,
        RequiredCost = CalculateOverageCost(unitsRequested - allowedUnits),
        OverageAllowed = entitlement.OveragePolicy == OveragePolicy.Hard
                        ? false : true
    };
}
```

## User Experience

### When Limit is Reached

**Feature Quota** (e.g., 50 active reminders):
```
Error: "You've reached the limit of 50 active reminders. 
        Delete or complete some reminders to create new ones."
```

**Rate Limit** (e.g., 10 delegated sends per 24h):
```
Error: "You've sent 10 reminders in the last 24 hours. 
        Try again tomorrow, or upgrade your plan."
```

**Billing Quota** (e.g., exceeded monthly deliveries):
```
Warning: "You've used 1000 reminder deliveries this month. 
         Additional reminders will be charged at $0.01/delivery. 
         Continue?"
[Cancel] [Pay Overage]
```

## Audit & Telemetry

Every quota check emits an audit event:

```json
{
  "event_type": "quota.checked",
  "tenant_id": "...",
  "operation": "reminder_creation",
  "feature_quota_status": "PASS",
  "rate_quota_status": "PASS",
  "billing_entitlement_status": "PASS_WITH_OVERAGE_AVAILABLE",
  "decision": "ALLOW",
  "estimated_cost": 0.00,
  "audit_id": "..."
}
```

## Implementation Checklist

- [ ] **005**: Define RemindersQuotaService with CheckActiveCount + CheckRateLimit
- [ ] **006**: Define DelegatedRemindersQuotaService
- [ ] **010**: Define BillingEntitlementService with CheckMetricQuota
- [ ] **All features**: Call billing on every quota-constrained operation
- [ ] **All features**: Audit every quota decision
- [ ] **Integration tests**: Test upgrade/downgrade scenarios
- [ ] **Integration tests**: Test rate-limit reset at cycle boundary
- [ ] **Documentation**: Update user-facing docs with quota tiers

## Related Specs

- `005-reminders/spec.md`: Feature quota definitions
- `006-delegated-reminders/spec.md`: Rate limit definitions
- `010-billing/spec.md`: Entitlement model and overage pricing
- `012-governance-security/spec.md`: Policy enforcement overrides
