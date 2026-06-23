-- 017_dispatch_audit.sql
-- SB-009B Domain Agents Runtime: immutable audit ledger for every dispatch cycle.
-- Channel-agnostic by design — channel_type accommodates whatsapp, sms, email, etc.
-- pgcrypto is NOT allow-listed on Azure; use gen_random_uuid() (PG16).

create table if not exists dispatch_audit_events (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null,
    correlation_id          text        not null,
    unified_message_id      text        not null,
    channel_type            text        not null,
    evaluated_agents        jsonb       not null default '[]'::jsonb,
    selected_agent_id       text,
    fallback_used           boolean     not null default false,
    fallback_reason         text,
    tie_break_applied       boolean     not null default false,
    tie_break_rationale     text,
    outcome                 text        not null
        check (outcome in ('dispatched', 'fallback', 'contained_failure', 'no_agents')),
    failure_agent_id        text,
    failure_details         jsonb,
    principal_user_id       uuid,
    dispatched_at_utc       timestamptz not null default now()
);

create index if not exists ix_dispatch_audit_tenant_time
    on dispatch_audit_events (tenant_id, dispatched_at_utc desc);

create index if not exists ix_dispatch_audit_correlation
    on dispatch_audit_events (correlation_id);

create index if not exists ix_dispatch_audit_message
    on dispatch_audit_events (unified_message_id);

-- ── Row-level security ───────────────────────────────────────────────────────

alter table dispatch_audit_events enable row level security;

-- Append-only WORM: SELECT and INSERT allowed by tenant; no UPDATE/DELETE.
drop policy if exists p_dispatch_audit_read on dispatch_audit_events;
create policy p_dispatch_audit_read on dispatch_audit_events
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_dispatch_audit_insert on dispatch_audit_events;
create policy p_dispatch_audit_insert on dispatch_audit_events
    for insert
    with check (tenant_id = app.current_tenant_id());
