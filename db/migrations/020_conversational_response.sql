-- 020_conversational_response.sql
-- SB-000 Core Conversational Response: outbound messages table.
-- Stores every Aluki-generated reply for conversation history, audit, and idempotency.

create table if not exists app.outbound_messages (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null,
    user_id                 uuid        not null,
    correlation_message_id  text        not null,
    channel                 text        not null,
    recipient_wa_id         text        not null,
    body                    text        not null,
    status                  text        not null
        check (status in ('delivered', 'error_fallback', 'pending')),
    error_reason            text,
    created_at_utc          timestamptz not null default now(),
    delivered_at_utc        timestamptz,
    constraint ux_outbound_tenant_correlation
        unique (tenant_id, correlation_message_id)
);

create index if not exists ix_outbound_tenant_user_created
    on app.outbound_messages (tenant_id, user_id, created_at_utc desc);

-- ── Row-level security ────────────────────────────────────────────────────────

alter table app.outbound_messages enable row level security;

drop policy if exists p_outbound_select on app.outbound_messages;
create policy p_outbound_select on app.outbound_messages
    for select using (tenant_id = app.current_tenant_id());

drop policy if exists p_outbound_insert on app.outbound_messages;
create policy p_outbound_insert on app.outbound_messages
    for insert with check (tenant_id = app.current_tenant_id());
