-- 016_suggestions_admin.sql
-- SB-008A Suggestions Admin and Rewards: admin triage queue, immutable audit
-- ledger, reward entitlement ledger, notification delivery tracking, and
-- reward decision records. Builds on tenancy/RLS from 001/003 and suggestions
-- from 015. pgcrypto is NOT allow-listed on Azure; use gen_random_uuid() (PG16).

-- ── Admin triage queue (mutable read-model, one row per suggestion) ──────────

create table if not exists suggestion_admin_queue (
    id                      uuid        primary key default gen_random_uuid(),
    suggestion_id           uuid        not null references suggestions(id),
    tenant_id               uuid        not null,
    submitter_user_id       uuid        not null,
    admin_status            text        not null default 'captured'
        check (admin_status in ('captured', 'under_review', 'accepted', 'rejected', 'archived')),
    admin_category          text,
    admin_priority          text
        check (admin_priority in ('low', 'medium', 'high', 'critical')),
    summary_excerpt         text,
    last_admin_action_at_utc timestamptz,
    created_at_utc          timestamptz not null default now(),
    updated_at_utc          timestamptz not null default now(),
    constraint ux_suggestion_admin_queue_suggestion unique (suggestion_id)
);

create index if not exists ix_suggestion_admin_queue_triage
    on suggestion_admin_queue (tenant_id, admin_status, admin_priority, created_at_utc);

create index if not exists ix_suggestion_admin_queue_submitter
    on suggestion_admin_queue (tenant_id, submitter_user_id);

-- ── Admin audit ledger (append-only WORM) ────────────────────────────────────

create table if not exists suggestion_admin_audit_ledger (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null,
    suggestion_id           uuid        not null,
    actor_user_id           text        not null,
    actor_role              text        not null
        check (actor_role in ('AdminReviewer', 'AdminApprover', 'AdminAuditor', 'System')),
    action_type             text        not null
        check (action_type in ('status_change', 'category_change', 'priority_change', 'authorization_denied', 'compensation')),
    old_value               jsonb,
    new_value               jsonb,
    reason_code             text        not null default '',
    immutable_sequence_no   bigserial   not null,
    record_hash             text        not null,
    correction_of_audit_id  uuid,
    created_at_utc          timestamptz not null default now()
);

create index if not exists ix_suggestion_admin_audit_ledger_suggestion
    on suggestion_admin_audit_ledger (tenant_id, suggestion_id);

create index if not exists ix_suggestion_admin_audit_ledger_time
    on suggestion_admin_audit_ledger (tenant_id, created_at_utc desc);

-- ── Reward entitlement ledger (append-only WORM) ─────────────────────────────

create table if not exists reward_entitlement_ledger (
    id                          uuid        primary key default gen_random_uuid(),
    tenant_id                   uuid        not null,
    submitter_user_id           uuid        not null,
    suggestion_id               uuid        not null,
    reward_rule_type            text        not null
        check (reward_rule_type in ('base', 'quality', 'streak')),
    source_event_id             text        not null,
    grant_amount                numeric(18,4) not null default 0,
    grant_status                text        not null
        check (grant_status in ('granted', 'rejected', 'duplicate', 'conflict', 'compensation')),
    policy_version              text        not null default '1.0',
    rule_metadata               jsonb       not null default '{}'::jsonb,
    idempotency_key             text        not null,
    granted_at_utc              timestamptz not null default now(),
    correction_of_entitlement_id uuid
);

-- Idempotency: one entitlement per (tenant, user, suggestion, rule, source event)
create unique index if not exists ux_reward_entitlement_ledger_idempotent
    on reward_entitlement_ledger (tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id);

create index if not exists ix_reward_entitlement_ledger_user
    on reward_entitlement_ledger (tenant_id, submitter_user_id);

create index if not exists ix_reward_entitlement_ledger_suggestion
    on reward_entitlement_ledger (tenant_id, suggestion_id);

-- ── Reward notification delivery ─────────────────────────────────────────────

create table if not exists reward_notification_delivery (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null,
    entitlement_id          uuid        not null,
    submitter_user_id       uuid        not null,
    template_id             text        not null default 'reward_granted_v1',
    delivery_state          text        not null default 'pending'
        check (delivery_state in ('pending', 'retrying', 'delivered', 'dead_letter')),
    attempt_no              int         not null default 0,
    next_attempt_at_utc     timestamptz,
    last_error_code         text,
    last_error_message      text,
    dead_letter_at_utc      timestamptz,
    operator_replay_required boolean     not null default false,
    created_at_utc          timestamptz not null default now(),
    updated_at_utc          timestamptz not null default now()
);

create index if not exists ix_reward_notification_delivery_sweep
    on reward_notification_delivery (tenant_id, delivery_state, next_attempt_at_utc)
    where delivery_state in ('pending', 'retrying');

-- ── Reward decision record (append-only audit) ───────────────────────────────

create table if not exists reward_decision_record (
    id                          uuid        primary key default gen_random_uuid(),
    tenant_id                   uuid        not null,
    decision_type               text        not null
        check (decision_type in ('granted', 'skipped', 'rejected', 'duplicate', 'conflict')),
    decision_reason             text        not null,
    idempotency_boundary        jsonb       not null default '{}'::jsonb,
    entitlement_id              uuid,
    telemetry_correlation_id    text,
    created_at_utc              timestamptz not null default now()
);

create index if not exists ix_reward_decision_record_time
    on reward_decision_record (tenant_id, created_at_utc desc);

create index if not exists ix_reward_decision_record_entitlement
    on reward_decision_record (tenant_id, entitlement_id)
    where entitlement_id is not null;

-- ── Row-level security ───────────────────────────────────────────────────────

alter table suggestion_admin_queue          enable row level security;
alter table suggestion_admin_audit_ledger   enable row level security;
alter table reward_entitlement_ledger       enable row level security;
alter table reward_notification_delivery    enable row level security;
alter table reward_decision_record          enable row level security;

-- suggestion_admin_queue: tenant-scoped only (admin operations are staff-wide)
drop policy if exists p_suggestion_admin_queue_read on suggestion_admin_queue;
create policy p_suggestion_admin_queue_read on suggestion_admin_queue
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_suggestion_admin_queue_write on suggestion_admin_queue;
create policy p_suggestion_admin_queue_write on suggestion_admin_queue
    for insert
    with check (tenant_id = app.current_tenant_id());

drop policy if exists p_suggestion_admin_queue_update on suggestion_admin_queue;
create policy p_suggestion_admin_queue_update on suggestion_admin_queue
    for update
    using (tenant_id = app.current_tenant_id())
    with check (tenant_id = app.current_tenant_id());

-- suggestion_admin_audit_ledger: append-only WORM; SELECT by tenant
drop policy if exists p_suggestion_admin_audit_ledger_read on suggestion_admin_audit_ledger;
create policy p_suggestion_admin_audit_ledger_read on suggestion_admin_audit_ledger
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_suggestion_admin_audit_ledger_write on suggestion_admin_audit_ledger;
create policy p_suggestion_admin_audit_ledger_write on suggestion_admin_audit_ledger
    for insert
    with check (tenant_id = app.current_tenant_id());

-- reward_entitlement_ledger: append-only WORM; SELECT by tenant
drop policy if exists p_reward_entitlement_ledger_read on reward_entitlement_ledger;
create policy p_reward_entitlement_ledger_read on reward_entitlement_ledger
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_reward_entitlement_ledger_write on reward_entitlement_ledger;
create policy p_reward_entitlement_ledger_write on reward_entitlement_ledger
    for insert
    with check (tenant_id = app.current_tenant_id());

-- reward_notification_delivery: SELECT + UPDATE + INSERT by tenant
drop policy if exists p_reward_notification_delivery_read on reward_notification_delivery;
create policy p_reward_notification_delivery_read on reward_notification_delivery
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_reward_notification_delivery_write on reward_notification_delivery;
create policy p_reward_notification_delivery_write on reward_notification_delivery
    for insert
    with check (tenant_id = app.current_tenant_id());

drop policy if exists p_reward_notification_delivery_update on reward_notification_delivery;
create policy p_reward_notification_delivery_update on reward_notification_delivery
    for update
    using (tenant_id = app.current_tenant_id())
    with check (tenant_id = app.current_tenant_id());

-- reward_decision_record: append-only audit; SELECT by tenant
drop policy if exists p_reward_decision_record_read on reward_decision_record;
create policy p_reward_decision_record_read on reward_decision_record
    for select
    using (tenant_id = app.current_tenant_id());

drop policy if exists p_reward_decision_record_write on reward_decision_record;
create policy p_reward_decision_record_write on reward_decision_record
    for insert
    with check (tenant_id = app.current_tenant_id());
