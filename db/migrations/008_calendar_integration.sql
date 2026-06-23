-- 008_calendar_integration.sql
-- SB-003 calendar integration: connections, OAuth callback state, event creation,
-- clarification turns, provider selection, dedup, outcomes, auth failures, and audit.
-- Builds on tenancy/RLS baseline from 001/003.

create table if not exists calendar_connections (
    calendar_connection_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    connection_status text not null check (connection_status in ('connected', 'disconnected', 'revoked', 'failed')),
    connected_at_utc timestamptz,
    disconnected_at_utc timestamptz,
    provider_account_ref text,
    default_for_user boolean not null default false,
    correlation_id text not null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create unique index if not exists ux_calendar_connections_active_provider
    on calendar_connections (tenant_id, context_id, user_id, provider)
    where connection_status = 'connected';

create unique index if not exists ux_calendar_connections_default
    on calendar_connections (tenant_id, context_id, user_id)
    where connection_status = 'connected' and default_for_user = true;

create table if not exists oauth_callback_states (
    oauth_callback_state_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    state_nonce text not null unique,
    issued_at_utc timestamptz not null,
    expires_at_utc timestamptz not null,
    used_at_utc timestamptz,
    status text not null check (status in ('issued', 'consumed', 'expired', 'rejected')),
    correlation_id text not null
);

create index if not exists ix_oauth_callback_states_nonce
    on oauth_callback_states (state_nonce)
    where status = 'issued';

create table if not exists event_creation_requests (
    event_creation_request_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider_hint text,
    title text not null,
    start_local text not null,
    end_local text,
    canonical_timezone text not null,
    timezone_resolution_source text not null check (timezone_resolution_source in ('request', 'profile', 'clarified')),
    normalized_payload_hash text not null,
    requested_at_utc timestamptz not null,
    correlation_id text not null
);

create index if not exists ix_event_creation_requests_scope
    on event_creation_requests (tenant_id, context_id, user_id, requested_at_utc desc);

create table if not exists clarification_turns (
    clarification_turn_id uuid primary key default gen_random_uuid(),
    event_creation_request_id uuid not null references event_creation_requests(event_creation_request_id),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    question_text text not null,
    requested_field text not null,
    answer_text text,
    status text not null check (status in ('pending', 'answered', 'expired')),
    created_at_utc timestamptz not null default now(),
    answered_at_utc timestamptz
);

create index if not exists ix_clarification_turns_request
    on clarification_turns (event_creation_request_id, status);

create table if not exists provider_selection_decisions (
    provider_selection_decision_id uuid primary key default gen_random_uuid(),
    event_creation_request_id uuid not null references event_creation_requests(event_creation_request_id),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    selected_provider text not null check (selected_provider in ('outlook', 'google')),
    selection_reason text not null check (selection_reason in ('explicit_request', 'user_default', 'deterministic_tiebreak')),
    available_providers jsonb not null,
    created_at_utc timestamptz not null default now()
);

create unique index if not exists ux_provider_selection_per_request
    on provider_selection_decisions (event_creation_request_id);

create table if not exists deduplication_records (
    deduplication_record_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    idempotency_key text not null,
    window_started_at_utc timestamptz not null,
    window_expires_at_utc timestamptz not null,
    first_outcome_ref text not null,
    first_provider_event_ref text,
    status text not null check (status in ('in_progress', 'created', 'failed'))
);

-- "Active" can't be expressed as `window_expires_at_utc > now()` here: functions
-- in an index predicate must be IMMUTABLE, and now() is STABLE. The concurrency
-- guard is the in-progress row — two racing creates can't both hold an
-- in_progress dedup for the same key. Time-window dedup of already-completed rows
-- is enforced by the GetActiveAsync query filter (`window_expires_at_utc > now()`).
create unique index if not exists ux_dedup_active_key
    on deduplication_records (tenant_id, context_id, user_id, provider, idempotency_key)
    where status = 'in_progress';

create table if not exists calendar_event_outcomes (
    calendar_event_outcome_id uuid primary key default gen_random_uuid(),
    event_creation_request_id uuid not null references event_creation_requests(event_creation_request_id),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    outcome_type text not null check (outcome_type in ('created', 'previously_created', 'clarification_required', 'reconnect_required', 'denied', 'failed')),
    outcome_reference text not null,
    provider_event_reference text,
    final_title text,
    final_start_utc timestamptz,
    final_end_utc timestamptz,
    final_timezone text,
    created_at_utc timestamptz not null default now(),
    correlation_id text not null
);

create index if not exists ix_calendar_event_outcomes_scope
    on calendar_event_outcomes (tenant_id, context_id, user_id, created_at_utc desc);

create table if not exists authorization_failure_outcomes (
    authorization_failure_outcome_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    failure_reason text not null check (failure_reason in ('expired_token', 'invalid_grant', 'refresh_denied', 'scope_denied')),
    reconnect_required boolean not null default true,
    outcome_reference text not null,
    created_at_utc timestamptz not null default now(),
    correlation_id text not null
);

create index if not exists ix_auth_failure_outcomes_scope
    on authorization_failure_outcomes (tenant_id, context_id, user_id, created_at_utc desc);

create table if not exists calendar_audit_events (
    calendar_audit_event_id uuid primary key default gen_random_uuid(),
    event_name text not null,
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid references users_profile(id),
    provider text check (provider in ('outlook', 'google')),
    skill_name text not null,
    result text not null,
    outcome_reference text,
    correlation_id text not null,
    occurred_at_utc timestamptz not null default now(),
    payload_json jsonb not null default '{}'::jsonb
);

create index if not exists ix_calendar_audit_events_tenant
    on calendar_audit_events (tenant_id, occurred_at_utc desc);

-- RLS
alter table calendar_connections enable row level security;
alter table oauth_callback_states enable row level security;
alter table event_creation_requests enable row level security;
alter table clarification_turns enable row level security;
alter table provider_selection_decisions enable row level security;
alter table deduplication_records enable row level security;
alter table calendar_event_outcomes enable row level security;
alter table authorization_failure_outcomes enable row level security;
alter table calendar_audit_events enable row level security;

drop policy if exists p_calendar_connections_tenant on calendar_connections;
create policy p_calendar_connections_tenant on calendar_connections
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_oauth_callback_states_tenant on oauth_callback_states;
create policy p_oauth_callback_states_tenant on oauth_callback_states
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_event_creation_requests_tenant on event_creation_requests;
create policy p_event_creation_requests_tenant on event_creation_requests
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_clarification_turns_tenant on clarification_turns;
create policy p_clarification_turns_tenant on clarification_turns
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_provider_selection_decisions_tenant on provider_selection_decisions;
create policy p_provider_selection_decisions_tenant on provider_selection_decisions
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_deduplication_records_tenant on deduplication_records;
create policy p_deduplication_records_tenant on deduplication_records
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_calendar_event_outcomes_tenant on calendar_event_outcomes;
create policy p_calendar_event_outcomes_tenant on calendar_event_outcomes
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_auth_failure_outcomes_tenant on authorization_failure_outcomes;
create policy p_auth_failure_outcomes_tenant on authorization_failure_outcomes
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_calendar_audit_read on calendar_audit_events;
create policy p_calendar_audit_read on calendar_audit_events
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_calendar_audit_write on calendar_audit_events;
create policy p_calendar_audit_write on calendar_audit_events
    for insert
    with check (tenant_id = app.current_tenant_id());
