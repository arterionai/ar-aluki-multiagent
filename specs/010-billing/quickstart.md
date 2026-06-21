# Quickstart: Validate Billing and Package Management

## Purpose

Validate SB-010 end-to-end for both billing modes:
- consumption-based pay-as-you-go,
- package quotas with overage or hard-stop behavior,
- tenant ownership across `INDIVIDUAL` and `ORGANIZATION`,
- deterministic invoice recomputation and reconciliation.

## Prerequisites

- .NET SDK 10 installed
- PostgreSQL available with tenant RLS enabled
- Billing migrations applied (catalog, package, ledger, invoice, credit tables)
- Runtime configured for tenant/principal resolution

## Setup

1. Build solution:

```powershell
dotnet build Aluki.Runtime.slnx -v minimal
```

2. Start runtime host:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

3. Start functions host if billing workflows use durable orchestration:

```powershell
dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
```

## Validation Scenarios

### Scenario A: Pay-as-you-go ledger and invoice determinism

1. Create an `INDIVIDUAL` billing account in `payg` mode.
2. Submit usage events for at least two billable meters.
3. Close billing cycle and generate invoice.
4. Re-run invoice recomputation for same cycle.

Expected outcome:
- one immutable billable ledger entry per accepted usage event,
- identical totals between original invoice and recomputation,
- no duplicate ledger entries for replayed idempotency keys.

### Scenario B: Package included quota then billable overage

1. Create an `ORGANIZATION` billing account in `package` mode with overage enabled.
2. Activate package with included quota for one meter.
3. Consume usage to exhaust included quota.
4. Continue usage beyond quota.

Expected outcome:
- included usage produces `usage_included` entries until exhausted,
- additional usage produces `usage_overage` entries with overage unit price snapshot,
- entitlement decisions and overage events are audited.

### Scenario C: Package hard-stop after quota exhaustion

1. Configure package overage policy to `hard_stop`.
2. Exhaust included quota for a meter.
3. Attempt additional usage.

Expected outcome:
- consumption denied with machine-readable reason code,
- denial audit event persisted,
- no billable overage ledger entry created.

### Scenario D: Ownership by tenant type

1. Run billing cycle for one `INDIVIDUAL` tenant and one `ORGANIZATION` tenant with equivalent usage.
2. Generate invoices for both.

Expected outcome:
- each invoice maps to exactly one tenant owner,
- organization invoice may include user attribution metadata when enabled,
- no orphan billing records exist.

### Scenario E: Package lifecycle transitions and proration

1. Activate package subscription and run partial cycle.
2. Execute mid-cycle upgrade.
3. Schedule downgrade for next renewal.
4. Cancel and pass grace period.

Expected outcome:
- mid-cycle upgrade generates proration adjustment ledger entries,
- scheduled downgrade applies only at renewal boundary,
- cancellation deactivates entitlements at grace end and follows fallback policy.

### Scenario F: Prepaid credit consumption precedence

1. Configure tenant credits and package with overage enabled.
2. Exhaust included quota.
3. Produce additional usage while credits remain.
4. Continue usage after credits reach zero.

Expected outcome:
- credits are debited before external settlement,
- after credit depletion, usage follows overage policy,
- credit movements are immutable and reconcilable to usage entries.

### Scenario G: Late-arriving usage and adjustment window

1. Close invoice cycle.
2. Submit usage event with event-time in previous cycle.
3. Execute once inside adjustment window, once outside.

Expected outcome:
- inside window: adjustment linkage to closed cycle,
- outside window: entry moved to next cycle with traceable linkage metadata,
- final reconciliation export maps each invoice line to ledger entries.

## Contract References

- Billing API contract: `specs/010-billing/contracts/billing-contract.yaml`
- Data constraints and transitions: `specs/010-billing/data-model.md`

## Acceptance Checklist

- 100% of billable usage events end in one immutable ledger entry or one explicit denial event.
- Duplicate delivery tests create 0 duplicate billable ledger entries for same idempotency key.
- Invoice recomputation for same cycle yields identical totals.
- Quota/overage behavior follows package policy (`billable_overage` or `hard_stop`).
- Billing ownership is always tenant-scoped and valid for both `INDIVIDUAL` and `ORGANIZATION`.
- Reconciliation exports map invoice lines to underlying ledger entries deterministically.
