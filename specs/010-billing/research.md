# Research: Billing and Package Management

## Decision 1: Billing ownership and scope model

Decision: Use a mandatory tenant-scoped billing account (`BillingAccount`) for every charge path, supporting both `INDIVIDUAL` and `ORGANIZATION` tenant types. User-level attribution is metadata-only and enabled only for organization chargeback visibility.

Rationale: The spec requires all billing to be tenant-scoped and explicitly forbids billing without tenant association. This preserves consistency with tenancy/RLS and avoids ownership ambiguity.

Alternatives considered:
- User-first billing accounts: rejected because tenancy is the authoritative security and ownership scope.
- Organization-only billing: rejected because `INDIVIDUAL` tenants must be first-class billable entities.

## Decision 2: Monetization model coexistence and fallback order

Decision: Support two launch modes per tenant account: `payg` and `package`. In `package` mode, consumption order is: included quota -> prepaid credits (if configured) -> overage by meter policy (`billable_overage` or `hard_stop`). In `payg` mode, usage is billed directly by meter unit price snapshot.

Rationale: The feature requires pay-as-you-go and package-based quotas/overages simultaneously, plus prepaid credit consumption when configured.

Alternatives considered:
- Separate independent engines per model: rejected due to duplicated ledger semantics and reconciliation complexity.
- Credits after external settlement: rejected because FR-014 requires credits consumed before external settlement when configured.

## Decision 3: Immutable ledger-first accounting

Decision: Persist immutable usage and adjustment ledger entries (`BillingLedgerEntry`) before invoice aggregation; invoices are deterministic projections over closed-cycle ledger windows.

Rationale: The spec requires deterministic recomputation, auditable attribution, and non-retroactive pricing behavior.

Alternatives considered:
- Mutable invoice-line-first model: rejected because post-hoc edits break determinism and traceability.
- Aggregation-only without raw entries: rejected because reconciliation and denial evidence become opaque.

## Decision 4: Idempotency strategy for ingestion and charging

Decision: Require idempotency keys at ingestion and settlement boundaries. Primary usage key: `tenant_id + meter_code + source_event_id + usage_window_start`. Duplicate submissions return explicit no-op outcomes with audit evidence.

Rationale: The feature requires 0 duplicate billable entries under replay/retry conditions and deterministic outcomes under redelivery.

Alternatives considered:
- Time-window dedupe only: rejected because delayed retries can bypass windows.
- Meter-only uniqueness: rejected because legitimate repeated usage within a meter would be suppressed incorrectly.

## Decision 5: Pricing and catalog versioning

Decision: Use versioned catalog entities (`BillingCatalogVersion`, `MeterPrice`, `PackageDefinitionVersion`) and snapshot price fields onto each billable ledger entry at write time.

Rationale: FR-011 requires historical amounts to remain immutable when pricing changes.

Alternatives considered:
- Resolve live price at invoice generation: rejected because historical recomputation would drift.
- Single global price table without effective windows: rejected because package lifecycle and controlled rollout need version boundaries.

## Decision 6: Overage and hard-stop enforcement

Decision: Evaluate entitlement snapshot at runtime per meter. If included quota exhausted:
- `billable_overage`: create overage ledger entry using package overage unit price snapshot.
- `hard_stop`: deny consumption and emit machine-readable denial reason + audit event.

Rationale: Aligns directly with US2 and FR-003/FR-004 plus denial audit requirements.

Alternatives considered:
- Always allow and post-adjust later: rejected because hard-stop enforcement must be immediate.
- Always deny after exhaustion: rejected because billable overage is a required mode.

## Decision 7: Package lifecycle and proration handling

Decision: Model package lifecycle with explicit transitions (`pending_activation`, `active`, `suspended`, `cancellation_grace`, `canceled`, `expired`) and policy-bound effective dates for upgrades/downgrades. Mid-cycle upgrades generate proration adjustment ledger entries; scheduled downgrades apply at renewal boundary.

Rationale: The spec requires predictable lifecycle handling and auditable proration.

Alternatives considered:
- In-place package mutation: rejected because it obscures historical entitlements and proration evidence.
- Immediate downgrade mid-cycle: rejected because the spec requires boundary-based downgrade behavior.

## Decision 8: Invoice closure, late arrivals, and reconciliation exports

Decision: Close invoice cycles with a watermark (`cycle_closed_at`) and policy-defined adjustment window for late usage. Late entries inside the window generate cycle adjustments; otherwise they roll into next cycle with linkage metadata. Reconciliation export bundles invoice lines and contributing ledger IDs.

Rationale: Supports deterministic close, transparent late-arrival policy, and FR-016 traceability.

Alternatives considered:
- Hard reject all late usage: rejected because operational ingestion delays are expected.
- Reopen closed invoices for every late event: rejected because it destabilizes financial reporting.
