-- 027_multivendor_crm.sql
-- Multi-vendor CRM extensions for Sheló NABEL and future ORGANIZATION tenants.
--
-- 1. parent_tenant_id   — sub-group hierarchy (vendedoras, chapters, etc.)
-- 2. tenant_whatsapp_channels — maps WA Business phone_number_id → tenant_id,
--    enabling automatic org-tenant routing on inbound messages.
-- 3. vendedora_assignments — owner assigns a contact/client to a specific
--    member (vendedora) within an org tenant. Used for role-scoped recall.
--
-- All idempotent (IF NOT EXISTS / DO NOTHING). No pgcrypto.

-- 1. Hierarchical tenants ---------------------------------------------------
alter table tenants
    add column if not exists parent_tenant_id uuid references tenants(id);

create index if not exists ix_tenants_parent
    on tenants (parent_tenant_id)
    where parent_tenant_id is not null;

-- 2. WA Business channel → tenant routing -----------------------------------
create table if not exists tenant_whatsapp_channels (
    phone_number_id  text    primary key,
    tenant_id        uuid    not null references tenants(id),
    display_name     text,
    status           text    not null default 'active'
        check (status in ('active', 'inactive')),
    created_at       timestamptz not null default now()
);

create index if not exists ix_twc_tenant
    on tenant_whatsapp_channels (tenant_id);

-- 3. Vendedora assignments --------------------------------------------------
create table if not exists vendedora_assignments (
    id                   uuid        primary key default gen_random_uuid(),
    tenant_id            uuid        not null references tenants(id),
    owner_user_id        uuid        not null references users_profile(id),
    client_external_id   text        not null,
    assigned_to_user_id  uuid        references users_profile(id),
    assigned_to_wa_id    text,
    notes                text,
    status               text        not null default 'active'
        check (status in ('active', 'inactive')),
    created_at           timestamptz not null default now(),
    updated_at           timestamptz not null default now(),
    unique (tenant_id, client_external_id)
);

create index if not exists ix_va_tenant        on vendedora_assignments (tenant_id);
create index if not exists ix_va_owner         on vendedora_assignments (owner_user_id);
create index if not exists ix_va_assigned_to   on vendedora_assignments (assigned_to_user_id)
    where assigned_to_user_id is not null;

-- 4. RLS for vendedora_assignments ------------------------------------------
alter table vendedora_assignments enable row level security;

drop policy if exists p_va_select on vendedora_assignments;
create policy p_va_select on vendedora_assignments
    for select using (tenant_id = current_setting('app.current_tenant', true)::uuid);

drop policy if exists p_va_insert on vendedora_assignments;
create policy p_va_insert on vendedora_assignments
    for insert with check (tenant_id = current_setting('app.current_tenant', true)::uuid);

drop policy if exists p_va_update on vendedora_assignments;
create policy p_va_update on vendedora_assignments
    for update using (tenant_id = current_setting('app.current_tenant', true)::uuid);
