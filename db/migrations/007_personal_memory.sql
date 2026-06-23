-- 007_personal_memory.sql
-- SB-002 personal memory: canonical memory artifacts (with embeddings) and audit.
-- Builds on tenancy/RLS from 001/003 and pgvector from 002.

create extension if not exists vector;

create table if not exists memory_artifact (
    memory_artifact_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    source_channel text not null,
    source_identity text not null,
    canonical_chain_id uuid not null,
    chain_version int not null default 1 check (chain_version >= 1),
    content_text text,
    content_locale text,
    embedding vector(1536),
    captured_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    deleted_at_utc timestamptz,
    deletion_reason text,
    provenance_ref text not null,
    correlation_id text not null,
    -- Canonical source identity per scope (idempotency for note capture).
    constraint ux_memory_artifact_source unique (tenant_id, source_channel, source_identity)
);

create table if not exists memory_audit_event (
    audit_event_id uuid primary key default gen_random_uuid(),
    event_name text not null,
    tenant_id uuid not null references tenants(id),
    context_id uuid references contexts(id),
    user_id uuid references users_profile(id),
    skill_name text not null,
    result text not null,
    correlation_id text not null,
    occurred_at_utc timestamptz not null default now(),
    payload_json jsonb not null default '{}'::jsonb
);

create index if not exists ix_memory_artifact_scope
    on memory_artifact (tenant_id, context_id)
    where deleted_at_utc is null;
create index if not exists ix_memory_artifact_chain
    on memory_artifact (canonical_chain_id);
create index if not exists ix_memory_audit_tenant_created
    on memory_audit_event (tenant_id, occurred_at_utc desc);

-- RLS
alter table memory_artifact enable row level security;
alter table memory_audit_event enable row level security;

drop policy if exists p_memory_artifact_tenant on memory_artifact;
create policy p_memory_artifact_tenant on memory_artifact
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- Audit: reads require membership; denial audits are writable under tenant scope.
drop policy if exists p_memory_audit_read on memory_audit_event;
create policy p_memory_audit_read on memory_audit_event
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_memory_audit_write on memory_audit_event;
create policy p_memory_audit_write on memory_audit_event
    for insert
    with check (tenant_id = app.current_tenant_id());
