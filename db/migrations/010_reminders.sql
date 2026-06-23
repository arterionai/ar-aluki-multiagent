-- 010_reminders.sql
-- SB-005 Scheduled Reminders: tenant-scoped one-shot/recurring reminders with
-- timezone-aware persistence, idempotent delivery attempts, audit, and quotas.
-- Builds on tenancy/RLS from 001/003. pgcrypto is NOT allow-listed on Azure;
-- use core gen_random_uuid() (PG16).

create table if not exists reminders (
    reminder_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid references contexts(id),
    user_id uuid not null references users_profile(id),
    reminder_text text not null check (char_length(reminder_text) between 1 and 500),
    scheduled_time_utc timestamptz not null,
    original_time_local time,
    timezone text not null,
    reminder_type text not null check (reminder_type in ('one_shot', 'recurring')),
    recurrence_rule_id uuid,
    snooze_count int not null default 0 check (snooze_count >= 0),
    quota_tier text,
    status text not null default 'scheduled'
        check (status in (
            'scheduled', 'firing', 'delivered', 'delivery_failed',
            'expired_undelivered', 'user_cancelled')),
    delivery_channel text not null default 'in_app',
    idempotency_key text not null,
    correlation_id text,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    deleted_at_utc timestamptz,
    constraint ux_reminders_idem unique (tenant_id, idempotency_key)
);

create table if not exists reminder_recurrence_rules (
    rule_id uuid primary key default gen_random_uuid(),
    reminder_id uuid not null references reminders(reminder_id),
    tenant_id uuid not null references tenants(id),
    cadence text not null check (cadence in ('daily', 'weekly', 'monthly')),
    day_of_week text[],
    day_of_month int check (day_of_month is null or day_of_month between 1 and 31),
    end_condition text check (end_condition in ('never', 'until_date')),
    end_date_utc timestamptz,
    active boolean not null default true,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

-- reminders.recurrence_rule_id references a recurrence rule (added after the
-- referenced table exists to avoid an ordering cycle).
alter table reminders
    drop constraint if exists fk_reminders_recurrence_rule;
alter table reminders
    add constraint fk_reminders_recurrence_rule
    foreign key (recurrence_rule_id) references reminder_recurrence_rules(rule_id);

create table if not exists reminder_delivery_attempts (
    attempt_id uuid primary key default gen_random_uuid(),
    reminder_id uuid not null references reminders(reminder_id),
    tenant_id uuid not null references tenants(id),
    scheduled_time_utc timestamptz not null,
    attempt_number int not null check (attempt_number between 1 and 3),
    status text not null
        check (status in (
            'pending', 'delivered', 'transient_failure',
            'permanent_failure', 'retry_scheduled')),
    failure_category text
        check (failure_category is null or failure_category in (
            'network_timeout', 'service_unavailable', 'invalid_recipient',
            'rate_limited', 'unknown')),
    failure_message text,
    delivery_timestamp_utc timestamptz,
    next_retry_time_utc timestamptz,
    notification_id text,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    -- Natural idempotency key: one row per (reminder, fire time, attempt).
    constraint ux_delivery_attempt_idem unique (reminder_id, scheduled_time_utc, attempt_number)
);

create table if not exists reminder_audit_events (
    event_id uuid primary key default gen_random_uuid(),
    reminder_id uuid not null references reminders(reminder_id),
    tenant_id uuid not null references tenants(id),
    user_id uuid,
    event_type text not null
        check (event_type in (
            'created', 'scheduled', 'firing', 'delivered', 'delivery_failed',
            'snoozed', 'cancelled', 'quota_checked', 'quota_blocked',
            'expired_undelivered')),
    details jsonb not null default '{}'::jsonb,
    correlation_id text,
    created_at_utc timestamptz not null default now()
);

create table if not exists reminder_quotas (
    quota_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null unique references tenants(id),
    active_reminder_count int not null default 0 check (active_reminder_count >= 0),
    quota_limit int not null default 10 check (quota_limit >= 1),
    entitlement_tier text not null default 'free'
        check (entitlement_tier in ('free', 'pro', 'enterprise')),
    last_checked_at_utc timestamptz not null default now(),
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create index if not exists ix_reminders_tenant_user
    on reminders (tenant_id, user_id);
create index if not exists ix_reminders_fire_ready
    on reminders (status, scheduled_time_utc);
create index if not exists ix_reminders_tenant_status
    on reminders (tenant_id, status);
create index if not exists ix_recurrence_rules_reminder
    on reminder_recurrence_rules (reminder_id);
create index if not exists ix_recurrence_rules_sweep
    on reminder_recurrence_rules (active, end_date_utc);
create index if not exists ix_delivery_attempts_retry
    on reminder_delivery_attempts (status, next_retry_time_utc);
create index if not exists ix_delivery_attempts_tenant_status
    on reminder_delivery_attempts (tenant_id, status);
create index if not exists ix_reminder_audit_reminder
    on reminder_audit_events (reminder_id, created_at_utc);
create index if not exists ix_reminder_audit_tenant_type
    on reminder_audit_events (tenant_id, event_type);

-- RLS: tenant-scoped, mirroring 003/008/009 (app.current_tenant_id GUC +
-- app.user_in_tenant()).
alter table reminders enable row level security;
alter table reminder_recurrence_rules enable row level security;
alter table reminder_delivery_attempts enable row level security;
alter table reminder_audit_events enable row level security;
alter table reminder_quotas enable row level security;

drop policy if exists p_reminders_tenant on reminders;
create policy p_reminders_tenant on reminders
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_recurrence_rules_tenant on reminder_recurrence_rules;
create policy p_recurrence_rules_tenant on reminder_recurrence_rules
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_delivery_attempts_tenant on reminder_delivery_attempts;
create policy p_delivery_attempts_tenant on reminder_delivery_attempts
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_reminder_audit_read on reminder_audit_events;
create policy p_reminder_audit_read on reminder_audit_events
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_reminder_audit_write on reminder_audit_events;
create policy p_reminder_audit_write on reminder_audit_events
    for insert
    with check (tenant_id = app.current_tenant_id());

drop policy if exists p_reminder_quotas_tenant on reminder_quotas;
create policy p_reminder_quotas_tenant on reminder_quotas
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- Background fire-sweep claim. The timer sweep has no user/tenant scope, so it
-- cannot satisfy the per-tenant RLS policies. This SECURITY DEFINER function runs
-- as the table owner (RLS-exempt, no FORCE) and atomically claims a bounded batch
-- of due reminders, transitioning them scheduled -> firing with SKIP LOCKED so
-- concurrent sweeps don't double-claim. The caller then re-enters each reminder's
-- own (tenant, creator) scope to record delivery + audit under RLS.
create or replace function app.claim_due_reminders(p_limit int, p_now timestamptz)
returns table (
    reminder_id uuid,
    tenant_id uuid,
    context_id uuid,
    user_id uuid,
    reminder_text text,
    scheduled_time_utc timestamptz,
    timezone text,
    delivery_channel text,
    correlation_id text,
    reminder_type text,
    recurrence_rule_id uuid)
language sql
security definer
set search_path = public, app
as $$
    update reminders r
    set status = 'firing', updated_at_utc = now()
    where r.reminder_id in (
        select r2.reminder_id
        from reminders r2
        where r2.status = 'scheduled'
          and r2.scheduled_time_utc <= p_now
          and r2.deleted_at_utc is null
        order by r2.scheduled_time_utc
        for update skip locked
        limit greatest(p_limit, 0)
    )
    returning r.reminder_id, r.tenant_id, r.context_id, r.user_id,
              r.reminder_text, r.scheduled_time_utc, r.timezone,
              r.delivery_channel, r.correlation_id, r.reminder_type,
              r.recurrence_rule_id;
$$;
