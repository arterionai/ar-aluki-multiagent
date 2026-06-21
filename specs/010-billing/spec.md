# Feature Specification: Billing and Package Management (Starter Baseline)

Feature ID: SB-010
Status: Draft
Date: 2026-06-21

## 1. Objective

Define and implement a unified billing model that supports:

- Pay-as-you-go charging for total platform usage (consumption-based billing).
- Prepaid and postpaid packages with clear quotas, overage rules, and lifecycle handling.
- Billing ownership per tenant type (`INDIVIDUAL` and `ORGANIZATION`) with auditable charge attribution.

The feature resolves the current gap where pricing and charge mechanics are not yet formally defined.

## Clarifications

### Session 2026-06-21

- Q: What billing scopes must be supported? → A: Tenant-level billing is mandatory for both tenant types; optional user-level sub-allocation is supported only within organization tenants for internal chargeback visibility.
- Q: Which monetization models are required at launch? → A: Two models are required at launch: consumption-based pay-as-you-go and package-based plans with included quotas and overage charging.
- Q: How are overages handled when package quota is exhausted? → A: Overage is billable using configured unit prices unless tenant policy sets hard-stop mode.
- Q: How are users without organization represented for billing? → A: They are billed through `INDIVIDUAL` tenants; no billing flow exists without tenant association.

## 2. Architecture Adaptation

- Introduce a Billing Domain that integrates with existing tenancy (`tenants`, `memberships`) and audit model.
- Keep billing decisions tenant-scoped and policy-driven.
- Persist immutable usage ledger entries before invoice aggregation.
- Separate real-time entitlement checks (authorization to consume) from financial settlement (invoicing/collections).
- Support package lifecycle events (activation, renewal, upgrade, downgrade, suspension, cancellation).

## 3. In Scope

- Billing model definitions: pay-as-you-go and package-based.
- Product catalog, package definitions, and pricing versioning.
- Entitlements and quota tracking by tenant.
- Overage behavior (billable or hard-stop policy).
- Invoice generation artifacts and billing cycle closures.
- Credit balance and prepaid package consumption.
- Charge attribution metadata for tenant and optional actor user.
- Billing audit events and reconciliation support.

## 4. Out of Scope

- Payment gateway provider implementation specifics (card tokenization, PCI workflows).
- Tax engine localization details per country (kept pluggable).
- Full accounting ERP synchronization implementation.
- Human support workflows for disputes.

## 5. User Scenarios & Testing

### US1 - Pay-as-you-go for Total Usage (Priority: P0)

As a billing owner, I need total platform usage to be charged accurately so that each tenant pays for what they consume.

Why this priority: It is the minimum monetization baseline.

Independent Test: Simulate usage events across multiple billable meters and verify invoice line totals match ledger-based aggregation for a billing cycle.

Acceptance Scenarios:

1. Given an active tenant with pay-as-you-go mode, when billable usage is recorded, then immutable usage ledger entries are persisted with meter, quantity, unit price snapshot, tenant scope, and timestamp.
2. Given a closed billing cycle, when invoice is generated, then all included ledger entries are aggregated exactly once and totals are deterministic.

### US2 - Package Purchase and Included Quotas (Priority: P0)

As a tenant owner, I need to purchase a package with included quotas so that predictable usage is covered before overage is charged.

Why this priority: Package-based monetization is explicitly required.

Independent Test: Activate a package, consume included quota, exceed quota, and verify allocation order and overage application policy.

Acceptance Scenarios:

1. Given an active package with included quotas, when usage occurs, then consumption first decrements included quota by matching meter until exhausted.
2. Given exhausted quota and overage-enabled policy, when additional usage occurs, then overage ledger entries are created using package overage price rules.
3. Given exhausted quota and hard-stop policy, when additional usage occurs, then consumption is denied with explicit entitlement reason and audit event.

### US3 - Billing by Individual or Organization Tenant (Priority: P1)

As a platform operator, I need billing ownership to align with tenant type so individual users and organizations are both billed correctly.

Why this priority: Aligns monetization with current tenancy model and avoids ambiguity.

Independent Test: Run one cycle for an `INDIVIDUAL` tenant and one for an `ORGANIZATION` tenant with equivalent usage; verify ownership, invoice party, and access controls differ only by tenant metadata.

Acceptance Scenarios:

1. Given a user in an `INDIVIDUAL` tenant, when usage is billed, then invoice ownership is that tenant and not any organization entity.
2. Given a user in an `ORGANIZATION` tenant, when usage is billed, then invoice ownership is the organization tenant and user metadata remains attribution-only unless explicit sub-allocation reporting is enabled.

### US4 - Package Lifecycle Changes (Priority: P2)

As a tenant owner, I need upgrades, downgrades, and cancellations to be handled predictably so charges and entitlements remain consistent.

Why this priority: Prevents revenue leakage and tenant disputes.

Independent Test: Execute upgrade mid-cycle, downgrade on next cycle, and cancellation; verify proration rules and entitlement transitions.

Acceptance Scenarios:

1. Given a mid-cycle upgrade, when change is confirmed, then new entitlements activate per configured effective policy and proration entries are recorded.
2. Given a scheduled downgrade, when current cycle ends, then new package applies at renewal boundary without retroactive mutation.
3. Given cancellation, when grace period policy ends, then package entitlements are deactivated and subsequent usage follows fallback billing mode or denial policy.

### US5 - Credit Balance Consumption Before External Settlement (Priority: P1)

As a tenant owner with prepaid credits, I need credits to be consumed automatically before any external payment is triggered so that I only pay externally when credits are insufficient.

Why this priority: Prevents double-charging and maintains trust in prepaid billing.

Independent Test: Load a tenant credit balance, generate billable usage that exceeds credits, close the cycle, and verify credits are fully consumed first with only the remainder settled externally.

Acceptance Scenarios:

1. Given a tenant with sufficient credit balance, when an invoice is finalized, then credits are applied first and no external settlement amount is generated if credits cover the full total.
2. Given a tenant with partial credit balance, when an invoice is finalized, then credits are applied up to the available balance and the remainder is forwarded to external settlement.
3. Given a debit attempt that would reduce credit balance below zero, then the debit is capped at the available balance and the shortfall is recorded as an external settlement obligation.

### US6 - Billing Audit Trail Query (Priority: P1)

As a platform operator, I need to query billing audit events for a tenant so that I can verify compliance and investigate billing disputes.

Why this priority: Auditability is a non-negotiable compliance requirement.

Independent Test: Execute entitlement checks, charge creation, invoice generation, and policy denials for a tenant, then query the audit trail and verify every decision is represented with timestamp, actor, and outcome.

Acceptance Scenarios:

1. Given any billable decision (entitlement check, charge, invoice generation, denial), when the event occurs, then a BillingAuditEvent is recorded with event type, tenant ID, timestamp, and machine-readable outcome.
2. Given a tenant operator with sufficient access, when they query the audit trail for a date range, then all events for that tenant within the range are returned in chronological order.
3. Given a platform compliance review, when all billing events for a cycle are exported, then each invoice line is traceable to at least one BillingAuditEvent entry.

### Edge Cases

- Duplicate usage events due to retries or redelivery must not create duplicate billable entries (idempotency key required).
- Late-arriving usage after cycle close must be included in configurable adjustment window or next cycle according to policy.
- Pricing changes must not retroactively alter already persisted ledger entries.
- A tenant with suspended billing status must follow enforcement policy (deny, grace, or limited mode) with audit evidence.
- Partial meter outages must degrade safely and preserve reconciliation traceability.

## 6. Requirements

### Functional Requirements

- FR-001: The system MUST support pay-as-you-go billing using billable usage meters with versioned pricing.
- FR-002: The system MUST support package-based billing with included quotas per meter.
- FR-003: The system MUST support overage charging after included quota exhaustion when policy allows.
- FR-004: The system MUST support hard-stop entitlement policy that denies additional usage after quota exhaustion.
- FR-005: The system MUST record immutable usage ledger entries for every billable consumption decision.
- FR-006: The system MUST aggregate invoices deterministically from immutable ledger data.
- FR-007: The system MUST enforce idempotency for usage ingestion and billing ledger persistence.
- FR-008: The system MUST bind billing ownership to tenant scope and support both `INDIVIDUAL` and `ORGANIZATION` tenant types.
- FR-009: The system MUST allow optional user-level attribution metadata within tenant-scoped ledger entries.
- FR-010: The system MUST support package lifecycle events: activation, renewal, upgrade, downgrade, suspension, and cancellation.
- FR-011: The system MUST preserve price snapshot at charge time so future catalog changes do not mutate historical amounts.
- FR-012: The system MUST provide auditable billing events for entitlement checks, charge creation, invoice generation, and policy denials.
- FR-013: The system MUST expose billing status and remaining package quotas for runtime policy checks.
- FR-014: The system MUST support prepaid credit balances and consume credits before external settlement when configured.
- FR-015: The system MUST prevent billing operations when tenant/principal context is unresolved.
- FR-016: The system MUST support reconciliation exports that map invoices back to underlying ledger entries.
- FR-017: The system MUST define billing cycles with configurable period type (monthly default) and start-date anchor per billing account; each cycle has explicit open, closed, and settled status transitions.
- FR-018: The system MUST enforce invoice status transitions in strict order: `DRAFT` → `FINALIZED` → `SENT` → `PAID` or `VOID`; no invoice may reach settlement without passing through `FINALIZED`.
- FR-019: The system MUST prevent credit balance from being decremented below zero; any debit that would exceed the available balance is capped at the available balance and the shortfall is recorded as an external settlement obligation.
- FR-020: The system MUST create a default billing account automatically upon tenant provisioning; the initial state is `ACTIVE` with no package subscription and pay-as-you-go as the default billing mode.
- FR-021: The system MUST pin the catalog version at package subscription activation time; subsequent catalog changes must not alter pricing or quotas for existing active subscriptions.
- FR-022: The system MUST enforce a configurable late-arrival window (default: 24 hours after cycle close) for usage events; events within the window are included in the closed cycle as adjustments; events outside the window are routed to the next open cycle with a late-arrival marker.

### Key Entities

- BillingAccount: Tenant-scoped billing ownership profile and status.
- BillingCatalog: Versioned list of billable meters, package definitions, and prices.
- PackageSubscription: Tenant-bound package enrollment with lifecycle state and term boundaries.
- EntitlementSnapshot: Runtime view of included quotas, remaining balances, and policy mode.
- UsageLedgerEntry: Immutable chargeable usage fact with quantity, unit price snapshot, and attribution fields.
- Invoice: Billing-cycle financial document with summarized line items and totals.
- InvoiceLine: Aggregated or explicit line item tied to one meter and ledger references.
- CreditBalance: Tenant prepaid balance with debit/credit movements.
- BillingAuditEvent: Immutable event for billing decisions and state transitions.
- BillingCycle: Defines the time-bounded billing period for a billing account, including open/closed/settled status and boundary timestamps.
- PricingVersion: Immutable snapshot of a catalog's prices and quota definitions pinned at a specific publish timestamp; referenced by package subscriptions at activation time.

## 7. Success Criteria

### Measurable Outcomes

- SC-001: 100% of billable usage events produce either one immutable ledger entry or one explicit denial event.
- SC-002: Invoice recomputation from ledger for the same cycle yields identical totals in 100% of validation runs.
- SC-003: Duplicate delivery tests produce 0 duplicate billable ledger entries for the same idempotency key.
- SC-004: Package quota accounting error is <= 0.1% under load tests across mixed meters.
- SC-005: 100% of tenant invoices are attributable to exactly one tenant (`INDIVIDUAL` or `ORGANIZATION`) with no orphan billing records.
- SC-006: 100% of billing policy denials include machine-readable reason codes and audit evidence.
- SC-007: Credit balance debit operations never reduce balance below zero; 100% of overdrawn debit attempts produce a capped debit and a recorded external settlement obligation.
- SC-008: 100% of package lifecycle transitions (activation, renewal, upgrade, downgrade, suspension, cancellation) produce at least one corresponding BillingAuditEvent with the lifecycle event type.
- SC-009: Invoice status follows the defined state machine for 100% of invoices; zero invoices reach `PAID` or `VOID` without having passed through `FINALIZED`.
- SC-010: Billing account is automatically present for 100% of provisioned tenants before any usage can be recorded.

## 8. Assumptions

- Tenancy model remains authoritative (`INDIVIDUAL` and `ORGANIZATION`) and all billing is tenant-scoped.
- Usage metering signals are available from domain skills and can provide deterministic idempotency keys.
- Payment collection and tax computation are integrated via provider adapters outside this feature boundary.
- Existing audit and observability pipelines can store billing decision events without platform redesign.
- Currency strategy for MVP is single-currency per tenant billing account; multi-currency is deferred.
