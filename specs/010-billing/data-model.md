# Data Model: Billing and Package Management

## Overview

This feature introduces tenant-scoped billing entities that support both monetization models (`payg` and `package`) while preserving immutable charge evidence, deterministic invoice recomputation, and auditable policy enforcement.

## Entities

### 1) BillingAccount

Purpose: Tenant-owned billing profile and enforcement status.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, unique)
- `tenant_type` (enum: `INDIVIDUAL`, `ORGANIZATION`, required)
- `billing_mode` (enum: `payg`, `package`, required)
- `billing_status` (enum: `active`, `grace`, `suspended`, required)
- `currency_code` (text, required, ISO 4217)
- `timezone` (text, required)
- `invoice_owner_display` (text, required)
- `chargeback_enabled` (bool, required, default false)
- `created_at_utc` (timestamptz, required)
- `updated_at_utc` (timestamptz, required)

Constraints:
- Exactly one account per tenant.
- `chargeback_enabled=true` valid only when `tenant_type=ORGANIZATION`.

Validation rules:
- Tenant/principal context is mandatory for reads/writes.
- `billing_mode=package` requires active package subscription or explicit fallback policy.

### 2) BillingCatalogVersion

Purpose: Versioned pricing/catalog boundary for meter and package definitions.

Fields:
- `id` (uuid, pk)
- `version_code` (text, required, unique)
- `effective_from_utc` (timestamptz, required)
- `effective_to_utc` (timestamptz, nullable)
- `status` (enum: `draft`, `published`, `retired`, required)
- `published_at_utc` (timestamptz, nullable)

Constraints:
- At most one published version active per effective window for a meter/package tuple.

### 3) MeterPrice

Purpose: Billable meter pricing by catalog version.

Fields:
- `id` (uuid, pk)
- `catalog_version_id` (uuid, fk -> BillingCatalogVersion.id, required)
- `meter_code` (text, required)
- `unit_name` (text, required)
- `unit_price` (numeric(18,6), required)
- `min_billable_quantity` (numeric(18,6), required, default 0)
- `rounding_policy` (text, required)

Constraints:
- Unique (`catalog_version_id`, `meter_code`).

### 4) PackageDefinitionVersion

Purpose: Versioned package definition with included quotas and overage policy.

Fields:
- `id` (uuid, pk)
- `catalog_version_id` (uuid, fk -> BillingCatalogVersion.id, required)
- `package_code` (text, required)
- `package_name` (text, required)
- `billing_term` (enum: `monthly`, `annual`, required)
- `base_price` (numeric(18,6), required)
- `overage_policy` (enum: `billable_overage`, `hard_stop`, required)
- `grace_days` (int, required, default 0)

Constraints:
- Unique (`catalog_version_id`, `package_code`).

### 5) PackageQuotaRule

Purpose: Included quota and overage pricing per meter in a package.

Fields:
- `id` (uuid, pk)
- `package_definition_version_id` (uuid, fk, required)
- `meter_code` (text, required)
- `included_quantity` (numeric(18,6), required)
- `overage_unit_price` (numeric(18,6), nullable)
- `hard_stop_reason_code` (text, nullable)

Constraints:
- Unique (`package_definition_version_id`, `meter_code`).
- `overage_unit_price` required when package overage policy is `billable_overage`.

### 6) PackageSubscription

Purpose: Tenant package enrollment and lifecycle state.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `billing_account_id` (uuid, fk -> BillingAccount.id, required)
- `package_definition_version_id` (uuid, fk, required)
- `state` (enum: `pending_activation`, `active`, `suspended`, `cancellation_grace`, `canceled`, `expired`, required)
- `term_start_utc` (timestamptz, required)
- `term_end_utc` (timestamptz, required)
- `renewal_policy` (enum: `auto_renew`, `manual`, required)
- `scheduled_change_package_definition_version_id` (uuid, fk nullable)
- `canceled_at_utc` (timestamptz, nullable)
- `created_at_utc` (timestamptz, required)

Constraints:
- One active subscription per tenant billing account.
- Scheduled downgrade/upgrade must reference valid future catalog package.

State transitions:
- `pending_activation` -> `active`
- `active` -> `suspended` | `cancellation_grace` | `expired`
- `cancellation_grace` -> `canceled`
- `suspended` -> `active` | `canceled`

### 7) EntitlementSnapshot

Purpose: Runtime entitlement evaluation projection used by policy checks.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `billing_account_id` (uuid, required)
- `as_of_utc` (timestamptz, required)
- `billing_mode` (enum: `payg`, `package`, required)
- `package_subscription_id` (uuid, nullable)
- `meter_code` (text, required)
- `included_remaining` (numeric(18,6), required)
- `credit_remaining` (numeric(18,6), required)
- `overage_policy` (enum: `billable_overage`, `hard_stop`, nullable)
- `decision` (enum: `allow_included`, `allow_credit`, `allow_overage`, `deny_hard_stop`, `deny_status`, required)
- `decision_reason_code` (text, required)

Constraints:
- Latest snapshot per tenant/meter queried by `as_of_utc`.

### 8) BillingLedgerEntry

Purpose: Immutable atomic billing fact for usage, adjustments, denials, and proration.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `billing_account_id` (uuid, required)
- `entry_type` (enum: `usage_included`, `usage_credit`, `usage_overage`, `usage_payg`, `proration_adjustment`, `manual_adjustment`, `denial_event`, required)
- `meter_code` (text, required)
- `quantity` (numeric(18,6), required)
- `unit_price_snapshot` (numeric(18,6), required, can be 0 for denial/included)
- `amount_total` (numeric(18,6), required)
- `currency_code` (text, required)
- `catalog_version_code_snapshot` (text, required)
- `package_code_snapshot` (text, nullable)
- `idempotency_key` (text, required)
- `source_event_id` (text, required)
- `source_event_time_utc` (timestamptz, required)
- `actor_user_id` (text, nullable)
- `attribution_metadata_json` (jsonb, nullable)
- `recorded_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `idempotency_key`).
- Immutable after insert.

Validation rules:
- `actor_user_id` optional and attribution-only (never ownership).
- For organization chargeback mode, attribution metadata must include internal actor dimension.

### 9) CreditBalance

Purpose: Tenant prepaid credit state.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, unique)
- `billing_account_id` (uuid, required)
- `available_amount` (numeric(18,6), required)
- `reserved_amount` (numeric(18,6), required, default 0)
- `currency_code` (text, required)
- `updated_at_utc` (timestamptz, required)

Constraints:
- Non-negative `available_amount` and `reserved_amount`.

### 10) CreditMovement

Purpose: Immutable credit ledger for top-ups and debits.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `credit_balance_id` (uuid, fk -> CreditBalance.id, required)
- `movement_type` (enum: `topup`, `debit_usage`, `refund`, `adjustment`, required)
- `amount` (numeric(18,6), required)
- `idempotency_key` (text, required)
- `reference_ledger_entry_id` (uuid, nullable)
- `created_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `idempotency_key`).

### 11) Invoice

Purpose: Cycle financial document produced from closed ledger windows.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `billing_account_id` (uuid, required)
- `invoice_number` (text, required, unique)
- `cycle_start_utc` (timestamptz, required)
- `cycle_end_utc` (timestamptz, required)
- `cycle_closed_at_utc` (timestamptz, required)
- `status` (enum: `draft`, `finalized`, `void`, required)
- `subtotal_amount` (numeric(18,6), required)
- `adjustments_amount` (numeric(18,6), required)
- `total_amount` (numeric(18,6), required)
- `currency_code` (text, required)
- `generated_at_utc` (timestamptz, required)

Constraints:
- One finalized invoice per tenant per cycle window.

### 12) InvoiceLine

Purpose: Invoice line projection linked to contributing ledger entries.

Fields:
- `id` (uuid, pk)
- `invoice_id` (uuid, fk -> Invoice.id, required)
- `tenant_id` (text, required)
- `line_type` (enum: `usage`, `overage`, `credit_offset`, `proration`, `adjustment`, required)
- `meter_code` (text, nullable)
- `description` (text, required)
- `quantity` (numeric(18,6), required)
- `unit_price` (numeric(18,6), required)
- `line_total` (numeric(18,6), required)
- `ledger_entry_refs_json` (jsonb, required)

Constraints:
- Every invoice line must reference one or more source ledger entry IDs.

### 13) BillingAuditEvent

Purpose: Immutable audit trail for policy checks and financial lifecycle transitions.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `event_type` (text, required)
- `entity_type` (text, required)
- `entity_id` (uuid, required)
- `reason_code` (text, nullable)
- `actor_type` (text, required)
- `actor_id` (text, nullable)
- `correlation_id` (text, required)
- `payload_json` (jsonb, required)
- `created_at_utc` (timestamptz, required)

Required audit events:
- entitlement decision (`allow_*`, `deny_*`)
- ledger entry created
- idempotent no-op replay
- invoice generated/finalized
- package lifecycle transition
- billing status enforcement action

## Projection Models

### BillingStatusView

Purpose: Runtime policy view for skill authorization and UX.

Fields:
- `tenant_id`
- `tenant_type`
- `billing_mode`
- `billing_status`
- `current_package_code` (nullable)
- `remaining_quota_by_meter` (map)
- `remaining_credit_amount`
- `overage_policy_by_meter` (map)
- `effective_at_utc`

### InvoiceReconciliationView

Purpose: Deterministic mapping from invoice lines to immutable ledger entries.

Fields:
- `invoice_number`
- `tenant_id`
- `cycle_window`
- `line_id`
- `ledger_entry_ids`
- `aggregation_hash`

## Idempotency Keys

- Usage ingestion: `tenant_id + meter_code + source_event_id + usage_window_start`
- Credit movement: `tenant_id + movement_type + external_reference`
- Invoice generation: `tenant_id + cycle_start_utc + cycle_end_utc`

## RLS and Security Notes

- All billing tables include `tenant_id` and enforce tenant policy filters.
- Billing operations fail closed when tenant/principal context is unresolved (FR-015).
- Ownership is always tenant-scoped; user identifiers are attribution metadata only.
