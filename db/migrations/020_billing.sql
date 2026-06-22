-- 020_billing.sql
-- SB-010 Billing & Package Management: catalog, accounts, subscriptions,
-- immutable ledger, credits, invoices, and audit trail.
-- Global catalog tables (no tenant_id) carry no RLS.
-- All tenant-scoped tables use app.current_tenant_id() RLS.

-- ── Global catalog ────────────────────────────────────────────────────────────

create table if not exists billing_catalog_versions (
    id                  uuid        primary key default gen_random_uuid(),
    version_code        text        not null unique,
    effective_from_utc  timestamptz not null,
    effective_to_utc    timestamptz,
    status              text        not null default 'draft'
        check (status in ('draft', 'published', 'retired')),
    published_at_utc    timestamptz,
    created_at_utc      timestamptz not null default now()
);

-- At most one published catalog at a time.
create unique index if not exists ux_catalog_published
    on billing_catalog_versions (status)
    where status = 'published';

create table if not exists meter_prices (
    id                      uuid        primary key default gen_random_uuid(),
    catalog_version_id      uuid        not null references billing_catalog_versions(id),
    meter_code              text        not null,
    unit_name               text        not null,
    unit_price              numeric(18,6) not null check (unit_price >= 0),
    min_billable_quantity   numeric(18,6) not null default 0,
    rounding_policy         text        not null default 'round_half_up',
    created_at_utc          timestamptz not null default now(),
    unique (catalog_version_id, meter_code)
);

create table if not exists package_definition_versions (
    id                      uuid        primary key default gen_random_uuid(),
    catalog_version_id      uuid        not null references billing_catalog_versions(id),
    package_code            text        not null,
    package_name            text        not null,
    billing_term            text        not null default 'monthly'
        check (billing_term in ('monthly', 'annual')),
    base_price              numeric(18,6) not null check (base_price >= 0),
    overage_policy          text        not null
        check (overage_policy in ('billable_overage', 'hard_stop')),
    grace_days              int         not null default 0,
    created_at_utc          timestamptz not null default now(),
    unique (catalog_version_id, package_code)
);

create table if not exists package_quota_rules (
    id                              uuid        primary key default gen_random_uuid(),
    package_definition_version_id   uuid        not null references package_definition_versions(id),
    meter_code                      text        not null,
    included_quantity               numeric(18,6) not null check (included_quantity >= 0),
    overage_unit_price              numeric(18,6) check (overage_unit_price >= 0),
    hard_stop_reason_code           text,
    created_at_utc                  timestamptz not null default now(),
    unique (package_definition_version_id, meter_code)
);

-- ── Billing accounts (one per tenant) ────────────────────────────────────────

create table if not exists billing_accounts (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null unique,
    tenant_type             text        not null
        check (tenant_type in ('INDIVIDUAL', 'ORGANIZATION')),
    billing_mode            text        not null default 'payg'
        check (billing_mode in ('payg', 'package')),
    billing_status          text        not null default 'active'
        check (billing_status in ('active', 'grace', 'suspended')),
    currency_code           text        not null default 'USD',
    timezone                text        not null default 'UTC',
    invoice_owner_display   text,
    chargeback_enabled      boolean     not null default false,
    created_at_utc          timestamptz not null default now(),
    updated_at_utc          timestamptz not null default now()
);

create index if not exists ix_billing_accounts_tenant
    on billing_accounts (tenant_id);

-- ── Package subscriptions ─────────────────────────────────────────────────────

create table if not exists package_subscriptions (
    id                                      uuid        primary key default gen_random_uuid(),
    tenant_id                               uuid        not null,
    billing_account_id                      uuid        not null references billing_accounts(id),
    package_definition_version_id           uuid        not null references package_definition_versions(id),
    state                                   text        not null default 'pending_activation'
        check (state in ('pending_activation', 'active', 'suspended', 'cancellation_grace', 'canceled', 'expired')),
    term_start_utc                          timestamptz,
    term_end_utc                            timestamptz,
    renewal_policy                          text        not null default 'auto_renew'
        check (renewal_policy in ('auto_renew', 'manual')),
    scheduled_change_package_definition_version_id uuid references package_definition_versions(id),
    canceled_at_utc                         timestamptz,
    created_at_utc                          timestamptz not null default now()
);

create index if not exists ix_subscriptions_tenant
    on package_subscriptions (tenant_id, state);

-- ── Billing ledger (WORM append-only) ────────────────────────────────────────

create table if not exists billing_ledger_entries (
    id                              uuid        primary key default gen_random_uuid(),
    tenant_id                       uuid        not null,
    billing_account_id              uuid        not null references billing_accounts(id),
    entry_type                      text        not null
        check (entry_type in ('usage_included', 'usage_credit', 'usage_overage', 'usage_payg',
                              'proration_adjustment', 'manual_adjustment', 'denial_event')),
    meter_code                      text        not null,
    quantity                        numeric(18,6) not null,
    unit_price_snapshot             numeric(18,6) not null,
    amount_total                    numeric(18,6) not null,
    currency_code                   text        not null default 'USD',
    catalog_version_code_snapshot   text        not null,
    package_code_snapshot           text,
    idempotency_key                 text        not null,
    source_event_id                 text,
    source_event_time_utc           timestamptz,
    actor_user_id                   uuid,
    attribution_metadata_json       jsonb,
    recorded_at_utc                 timestamptz not null default now(),
    unique (tenant_id, idempotency_key)
);

create index if not exists ix_ledger_tenant_meter_time
    on billing_ledger_entries (tenant_id, meter_code, recorded_at_utc desc);

create index if not exists ix_ledger_tenant_time
    on billing_ledger_entries (tenant_id, recorded_at_utc desc);

-- ── Credit balances ───────────────────────────────────────────────────────────

create table if not exists credit_balances (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null unique,
    billing_account_id  uuid        not null references billing_accounts(id),
    available_amount    numeric(18,6) not null default 0 check (available_amount >= 0),
    reserved_amount     numeric(18,6) not null default 0 check (reserved_amount >= 0),
    currency_code       text        not null default 'USD',
    updated_at_utc      timestamptz not null default now()
);

create table if not exists credit_movements (
    id                          uuid        primary key default gen_random_uuid(),
    tenant_id                   uuid        not null,
    credit_balance_id           uuid        not null references credit_balances(id),
    movement_type               text        not null
        check (movement_type in ('topup', 'debit_usage', 'refund', 'adjustment')),
    amount                      numeric(18,6) not null check (amount > 0),
    idempotency_key             text        not null,
    reference_ledger_entry_id   uuid,
    created_at_utc              timestamptz not null default now(),
    unique (tenant_id, idempotency_key)
);

-- ── Invoices ──────────────────────────────────────────────────────────────────

create table if not exists invoices (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    billing_account_id  uuid        not null references billing_accounts(id),
    invoice_number      text        not null unique,
    cycle_start_utc     timestamptz not null,
    cycle_end_utc       timestamptz not null,
    cycle_closed_at_utc timestamptz,
    status              text        not null default 'draft'
        check (status in ('draft', 'finalized', 'void')),
    subtotal_amount     numeric(18,6) not null default 0,
    adjustments_amount  numeric(18,6) not null default 0,
    total_amount        numeric(18,6) not null default 0,
    currency_code       text        not null default 'USD',
    generated_at_utc    timestamptz not null default now()
);

create index if not exists ix_invoices_tenant_cycle
    on invoices (tenant_id, cycle_start_utc, cycle_end_utc);

create table if not exists invoice_lines (
    id                      uuid        primary key default gen_random_uuid(),
    invoice_id              uuid        not null references invoices(id),
    tenant_id               uuid        not null,
    line_type               text        not null
        check (line_type in ('usage', 'overage', 'credit_offset', 'proration', 'adjustment')),
    meter_code              text,
    description             text        not null,
    quantity                numeric(18,6) not null,
    unit_price              numeric(18,6) not null,
    line_total              numeric(18,6) not null,
    ledger_entry_refs_json  jsonb       not null default '[]'::jsonb,
    created_at_utc          timestamptz not null default now()
);

create index if not exists ix_invoice_lines_invoice
    on invoice_lines (invoice_id);

-- ── Billing audit events (WORM) ───────────────────────────────────────────────

create table if not exists billing_audit_events (
    id              uuid        primary key default gen_random_uuid(),
    tenant_id       uuid        not null,
    event_type      text        not null,
    entity_type     text,
    entity_id       uuid,
    reason_code     text,
    actor_type      text,
    actor_id        uuid,
    correlation_id  text,
    payload_json    jsonb,
    created_at_utc  timestamptz not null default now()
);

create index if not exists ix_billing_audit_tenant_time
    on billing_audit_events (tenant_id, created_at_utc desc);

-- ── Row-level security (tenant-scoped tables only) ────────────────────────────

alter table billing_accounts        enable row level security;
alter table package_subscriptions   enable row level security;
alter table billing_ledger_entries  enable row level security;
alter table credit_balances         enable row level security;
alter table credit_movements        enable row level security;
alter table invoices                enable row level security;
alter table invoice_lines           enable row level security;
alter table billing_audit_events    enable row level security;

-- billing_accounts
drop policy if exists p_billing_accounts_read   on billing_accounts;
create policy p_billing_accounts_read on billing_accounts for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_billing_accounts_insert on billing_accounts;
create policy p_billing_accounts_insert on billing_accounts for insert with check (tenant_id = app.current_tenant_id());
drop policy if exists p_billing_accounts_update on billing_accounts;
create policy p_billing_accounts_update on billing_accounts for update using (tenant_id = app.current_tenant_id()) with check (tenant_id = app.current_tenant_id());

-- package_subscriptions
drop policy if exists p_subs_read   on package_subscriptions;
create policy p_subs_read on package_subscriptions for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_subs_insert on package_subscriptions;
create policy p_subs_insert on package_subscriptions for insert with check (tenant_id = app.current_tenant_id());
drop policy if exists p_subs_update on package_subscriptions;
create policy p_subs_update on package_subscriptions for update using (tenant_id = app.current_tenant_id()) with check (tenant_id = app.current_tenant_id());

-- billing_ledger_entries: WORM — INSERT + SELECT only
drop policy if exists p_ledger_read   on billing_ledger_entries;
create policy p_ledger_read on billing_ledger_entries for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_ledger_insert on billing_ledger_entries;
create policy p_ledger_insert on billing_ledger_entries for insert with check (tenant_id = app.current_tenant_id());

-- credit_balances
drop policy if exists p_credits_read   on credit_balances;
create policy p_credits_read on credit_balances for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_credits_insert on credit_balances;
create policy p_credits_insert on credit_balances for insert with check (tenant_id = app.current_tenant_id());
drop policy if exists p_credits_update on credit_balances;
create policy p_credits_update on credit_balances for update using (tenant_id = app.current_tenant_id()) with check (tenant_id = app.current_tenant_id());

-- credit_movements: WORM
drop policy if exists p_credit_mv_read   on credit_movements;
create policy p_credit_mv_read on credit_movements for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_credit_mv_insert on credit_movements;
create policy p_credit_mv_insert on credit_movements for insert with check (tenant_id = app.current_tenant_id());

-- invoices
drop policy if exists p_invoices_read   on invoices;
create policy p_invoices_read on invoices for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_invoices_insert on invoices;
create policy p_invoices_insert on invoices for insert with check (tenant_id = app.current_tenant_id());
drop policy if exists p_invoices_update on invoices;
create policy p_invoices_update on invoices for update using (tenant_id = app.current_tenant_id()) with check (tenant_id = app.current_tenant_id());

-- invoice_lines
drop policy if exists p_invoice_lines_read   on invoice_lines;
create policy p_invoice_lines_read on invoice_lines for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_invoice_lines_insert on invoice_lines;
create policy p_invoice_lines_insert on invoice_lines for insert with check (tenant_id = app.current_tenant_id());

-- billing_audit_events: WORM
drop policy if exists p_billing_audit_read   on billing_audit_events;
create policy p_billing_audit_read on billing_audit_events for select using (tenant_id = app.current_tenant_id());
drop policy if exists p_billing_audit_insert on billing_audit_events;
create policy p_billing_audit_insert on billing_audit_events for insert with check (tenant_id = app.current_tenant_id());
