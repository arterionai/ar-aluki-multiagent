-- 012_delegated_reminders.sql
-- SB-006 Delegated Reminders: tenant-scoped sender→recipient reminder workflows
-- with three-tier recipient resolution, explicit consent gating, bounded retry
-- (1/2/4/8/16s, max 5 attempts), 30-second cancellation window, and immutable
-- audit. Builds on tenancy/RLS from 001/003. pgcrypto is NOT allow-listed on
-- Azure; use core gen_random_uuid() (PG16).

-- ── Core reminder record ────────────────────────────────────────────────────

create table if not exists delegated_reminders (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    sender_user_id uuid not null references users_profile(id),
    sender_identity text not null check (char_length(sender_identity) between 1 and 200),
    recipient_identity text not null check (char_length(recipient_identity) between 1 and 200),
    recipient_display_name text,
    routing_key text not null,
    content text not null check (char_length(content) between 1 and 1000),
    due_time_utc timestamptz not null,
    cancel_deadline_utc timestamptz not null generated always as (due_time_utc - interval '30 seconds') stored,
    delivery_phase_started_at_utc timestamptz,
    status text not null default 'draft'
        check (status in (
            'draft',
            'awaiting_recipient_resolution',
            'awaiting_consent',
            'scheduled',
            'delivery_in_progress',
            'delivered',
            'delivery_failed_terminal',
            'cancelled')),
    consent_acquired bool not null default false,
    delivery_attempt_count int not null default 0 check (delivery_attempt_count >= 0),
    next_retry_utc timestamptz,
    idempotency_key text not null,
    correlation_id text,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ux_delegated_reminders_idem unique (tenant_id, idempotency_key)
);

-- ── Recipient contact resolution ────────────────────────────────────────────

create table if not exists delegated_recipient_contact (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    sender_user_id uuid not null references users_profile(id),
    recipient_identity text not null,
    recipient_name text,
    phone_e164 text,
    whatsapp_handle text,
    resolution_tier text not null
        check (resolution_tier in (
            'tier1_known_contact_confirmed',
            'tier2_phone_only_needs_capture',
            'tier3_unknown_needs_clarification')),
    is_confirmed bool not null default false,
    last_confirmed_at_utc timestamptz,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ux_recipient_contact unique (tenant_id, sender_user_id, recipient_identity)
);

-- ── Consent registry ────────────────────────────────────────────────────────

create table if not exists delegated_consent_registry (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    recipient_identity text not null,
    consent_scope text not null check (consent_scope in ('global', 'sender_scoped')),
    sender_user_id uuid references users_profile(id),
    consent_status text not null check (consent_status in ('opted_in', 'opted_out', 'revoked')),
    granted_at_utc timestamptz,
    revoked_at_utc timestamptz,
    policy_version text not null default 'v1',
    source_event_id text not null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ux_consent_registry unique (
        tenant_id, recipient_identity, consent_scope,
        coalesce(sender_user_id::text, '*'))
);

-- ── Delivery attempts ───────────────────────────────────────────────────────

create table if not exists delegated_delivery_attempt (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    delegated_reminder_id uuid not null references delegated_reminders(id),
    attempt_index int not null check (attempt_index between 1 and 5),
    scheduled_attempt_time_utc timestamptz not null,
    started_at_utc timestamptz not null,
    completed_at_utc timestamptz,
    result text not null
        check (result in (
            'delivered',
            'transient_failure',
            'permanent_invalid_recipient',
            'permanent_permission',
            'system_error',
            'pending')),
    retry_delay_seconds int,
    provider_reference text,
    failure_detail text,
    correlation_id text,
    created_at_utc timestamptz not null default now(),
    constraint ux_delivery_attempt_idem unique (tenant_id, delegated_reminder_id, attempt_index)
);

-- ── Audit events (immutable append-only) ────────────────────────────────────

create table if not exists delegated_audit_event (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    delegated_reminder_id uuid not null references delegated_reminders(id),
    event_type text not null
        check (event_type in (
            'delegated_reminder.created',
            'delegated_reminder.recipient_resolved',
            'delegated_reminder.consent_acquired',
            'delegated_reminder.delivery_started',
            'delegated_reminder.delivery_succeeded',
            'delegated_reminder.delivery_failed_terminal',
            'delegated_reminder.cancelled')),
    actor_type text not null check (actor_type in ('sender', 'recipient', 'system')),
    actor_id text,
    payload_json jsonb not null default '{}'::jsonb,
    correlation_id text,
    occurred_at_utc timestamptz not null default now()
);

-- ── Indexes ─────────────────────────────────────────────────────────────────

create index if not exists ix_delegated_reminders_tenant_sender
    on delegated_reminders (tenant_id, sender_user_id);
create index if not exists ix_delegated_reminders_fire_ready
    on delegated_reminders (status, due_time_utc);
create index if not exists ix_delegated_reminders_retry_ready
    on delegated_reminders (status, next_retry_utc);
create index if not exists ix_delegated_reminders_tenant_status
    on delegated_reminders (tenant_id, status);
create index if not exists ix_recipient_contact_lookup
    on delegated_recipient_contact (tenant_id, sender_user_id, recipient_identity);
create index if not exists ix_consent_registry_lookup
    on delegated_consent_registry (tenant_id, recipient_identity);
create index if not exists ix_delivery_attempt_reminder
    on delegated_delivery_attempt (tenant_id, delegated_reminder_id);
create index if not exists ix_audit_event_reminder
    on delegated_audit_event (delegated_reminder_id, occurred_at_utc);
create index if not exists ix_audit_event_tenant_type
    on delegated_audit_event (tenant_id, event_type);

-- ── Row-level security ──────────────────────────────────────────────────────

alter table delegated_reminders enable row level security;
alter table delegated_recipient_contact enable row level security;
alter table delegated_consent_registry enable row level security;
alter table delegated_delivery_attempt enable row level security;
alter table delegated_audit_event enable row level security;

-- delegated_reminders: sender's tenant scope
drop policy if exists p_delegated_reminders_tenant on delegated_reminders;
create policy p_delegated_reminders_tenant on delegated_reminders
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- delegated_recipient_contact: tenant scope
drop policy if exists p_recipient_contact_tenant on delegated_recipient_contact;
create policy p_recipient_contact_tenant on delegated_recipient_contact
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- delegated_consent_registry: tenant scope
drop policy if exists p_consent_registry_tenant on delegated_consent_registry;
create policy p_consent_registry_tenant on delegated_consent_registry
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- delegated_delivery_attempt: read + write under tenant scope
drop policy if exists p_delivery_attempt_read on delegated_delivery_attempt;
create policy p_delivery_attempt_read on delegated_delivery_attempt
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_delivery_attempt_write on delegated_delivery_attempt;
create policy p_delivery_attempt_write on delegated_delivery_attempt
    for insert
    with check (tenant_id = app.current_tenant_id());

-- delegated_audit_event: append-only under tenant scope
drop policy if exists p_audit_event_read on delegated_audit_event;
create policy p_audit_event_read on delegated_audit_event
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_audit_event_write on delegated_audit_event;
create policy p_audit_event_write on delegated_audit_event
    for insert
    with check (tenant_id = app.current_tenant_id());

-- ── Background sweep claim (SECURITY DEFINER) ───────────────────────────────
-- The timer sweep has no user/tenant scope, so it cannot satisfy per-tenant RLS.
-- This function runs as the table owner (RLS-exempt) and atomically claims a
-- bounded batch of due delegated reminders using SKIP LOCKED so concurrent
-- sweeps do not double-claim. Claimable rows:
--   (a) freshly due: status='scheduled' AND due_time_utc <= now
--   (b) retry due:   status='delivery_in_progress' AND next_retry_utc <= now

create or replace function app.claim_due_delegated_reminders(p_limit int, p_now timestamptz)
returns table (
    id uuid,
    tenant_id uuid,
    sender_user_id uuid,
    sender_identity text,
    recipient_identity text,
    content text,
    due_time_utc timestamptz,
    correlation_id text,
    delivery_attempt_count int)
language sql
security definer
set search_path = public, app
as $$
    update delegated_reminders r
    set status = 'delivery_in_progress',
        delivery_phase_started_at_utc = coalesce(r.delivery_phase_started_at_utc, now()),
        next_retry_utc = null,
        updated_at_utc = now()
    where r.id in (
        select r2.id
        from delegated_reminders r2
        where (
            (r2.status = 'scheduled' and r2.due_time_utc <= p_now)
            or (r2.status = 'delivery_in_progress' and r2.next_retry_utc is not null and r2.next_retry_utc <= p_now)
        )
        order by coalesce(r2.next_retry_utc, r2.due_time_utc)
        for update skip locked
        limit greatest(p_limit, 0)
    )
    returning r.id, r.tenant_id, r.sender_user_id, r.sender_identity,
              r.recipient_identity, r.content, r.due_time_utc,
              r.correlation_id, r.delivery_attempt_count;
$$;
