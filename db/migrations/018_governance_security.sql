-- 018_governance_security.sql
-- SB-012 Governance & Security: generic consent records, tenant-configurable policy
-- rules, and an immutable policy decision log. RLS for all tenant-scoped tables
-- is already established per feature (001-017). pgcrypto NOT used; gen_random_uuid() (PG16).

-- ── Generic consent records ───────────────────────────────────────────────────
-- Broader than delegated_consent_registry (SB-006); covers any consent_type.

create table if not exists consent_records (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    grantor_id          uuid        not null,
    grantee_id          uuid        not null,
    consent_type        text        not null,
    granted_at_utc      timestamptz not null default now(),
    revoked_at_utc      timestamptz,
    revocation_reason   text,
    created_at_utc      timestamptz not null default now()
);

-- One active consent per (tenant, grantor, grantee, type) at a time.
create unique index if not exists ux_consent_active
    on consent_records (tenant_id, grantor_id, grantee_id, consent_type)
    where revoked_at_utc is null;

create index if not exists ix_consent_grantor
    on consent_records (tenant_id, grantor_id, consent_type);

create index if not exists ix_consent_grantee
    on consent_records (tenant_id, grantee_id, consent_type);

-- ── Policy rules (tenant-configurable) ───────────────────────────────────────

create table if not exists policy_rules (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    rule_type           text        not null
        check (rule_type in ('quota', 'budget', 'feature_flag', 'compliance', 'fraud_risk')),
    operation_type      text        not null,
    rule_definition     jsonb       not null default '{}'::jsonb,
    priority            int         not null default 100,
    is_active           boolean     not null default true,
    created_at_utc      timestamptz not null default now(),
    updated_at_utc      timestamptz not null default now()
);

create index if not exists ix_policy_rules_lookup
    on policy_rules (tenant_id, operation_type, is_active, priority);

-- ── Policy decision log (WORM append-only) ────────────────────────────────────

create table if not exists policy_decision_log (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    principal_user_id   uuid,
    operation_type      text        not null,
    decision            text        not null
        check (decision in ('allow', 'deny', 'warn')),
    reason_code         text        not null,
    applied_rules       jsonb       not null default '[]'::jsonb,
    estimated_cost      numeric(18,6),
    correlation_id      text,
    metadata            jsonb,
    decided_at_utc      timestamptz not null default now()
);

create index if not exists ix_policy_decision_log_tenant_time
    on policy_decision_log (tenant_id, decided_at_utc desc);

create index if not exists ix_policy_decision_log_correlation
    on policy_decision_log (correlation_id)
    where correlation_id is not null;

-- ── Row-level security ───────────────────────────────────────────────────────

alter table consent_records      enable row level security;
alter table policy_rules         enable row level security;
alter table policy_decision_log  enable row level security;

-- consent_records: tenant + user scope (users see own consents)
drop policy if exists p_consent_read on consent_records;
create policy p_consent_read on consent_records
    for select
    using (tenant_id = app.current_tenant_id()
           and (grantor_id = app.current_user_id() or grantee_id = app.current_user_id()));

drop policy if exists p_consent_insert on consent_records;
create policy p_consent_insert on consent_records
    for insert
    with check (tenant_id = app.current_tenant_id());

-- Revocation is an UPDATE (only revoked_at_utc and revocation_reason fields).
drop policy if exists p_consent_revoke on consent_records;
create policy p_consent_revoke on consent_records
    for update
    using (tenant_id = app.current_tenant_id() and grantor_id = app.current_user_id())
    with check (tenant_id = app.current_tenant_id());

-- policy_rules: tenant-scoped (staff/admin access; user guard enforced at app layer)
drop policy if exists p_policy_rules_read on policy_rules;
create policy p_policy_rules_read on policy_rules
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_policy_rules_write on policy_rules;
create policy p_policy_rules_write on policy_rules
    for insert
    with check (tenant_id = app.current_tenant_id());

drop policy if exists p_policy_rules_update on policy_rules;
create policy p_policy_rules_update on policy_rules
    for update
    using (tenant_id = app.current_tenant_id())
    with check (tenant_id = app.current_tenant_id());

-- policy_decision_log: WORM; tenant SELECT + INSERT only.
drop policy if exists p_policy_decision_log_read on policy_decision_log;
create policy p_policy_decision_log_read on policy_decision_log
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_policy_decision_log_insert on policy_decision_log;
create policy p_policy_decision_log_insert on policy_decision_log
    for insert
    with check (tenant_id = app.current_tenant_id());
