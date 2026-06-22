-- 013_link_capture.sql
-- SB-009A Link Capture: tenant-scoped canonical link artifacts with enrichment
-- tracking, per-session confirmation gating, outbound policy audit, and immutable
-- event log. Builds on tenancy/RLS from 001/003. pgcrypto is NOT allow-listed on
-- Azure; use core gen_random_uuid() (PG16).

-- ── Canonical link artifact ──────────────────────────────────────────────────

create table if not exists link_artifacts (
    id                      uuid        primary key default gen_random_uuid(),
    tenant_id               uuid        not null,
    context_scope_id        uuid        not null,
    created_by_principal_id uuid        not null,
    source_channel          text        not null,
    canonical_url           text        not null,
    url_hash                text        not null,
    context_label           text,
    enrichment_status       text        not null default 'pending'
        check (enrichment_status in (
            'pending',
            'enriched',
            'policy_blocked',
            'timeout',
            'failed')),
    enrichment_reason_code  text,
    description_text        text        not null default '',
    site_name               text,
    title_text              text,
    first_captured_at_utc   timestamptz not null default now(),
    last_upserted_at_utc    timestamptz not null default now(),
    is_active               boolean     not null default true
);

-- Idempotency: one active artifact per (tenant, url_hash)
create unique index if not exists ux_link_artifacts_url_hash
    on link_artifacts (tenant_id, url_hash)
    where is_active = true;

-- ── Provenance / context refs ────────────────────────────────────────────────

create table if not exists link_provenance_refs (
    id                        uuid        primary key default gen_random_uuid(),
    tenant_id                 uuid        not null,
    link_artifact_id          uuid        not null references link_artifacts(id),
    source_message_id         text        not null,
    source_channel            text        not null,
    source_timestamp_utc      timestamptz not null,
    captured_by_principal_id  uuid        not null,
    context_label_snapshot    text,
    created_at_utc            timestamptz not null default now(),
    constraint ux_link_provenance_refs_source
        unique (tenant_id, link_artifact_id, source_message_id)
);

-- ── Per-session pending confirmations ────────────────────────────────────────

create table if not exists link_pending_confirmations (
    id                        uuid        primary key default gen_random_uuid(),
    tenant_id                 uuid        not null,
    context_scope_id          uuid        not null,
    session_id                text        not null,
    conversation_id           text        not null,
    subject_link_artifact_id  uuid        references link_artifacts(id),
    state                     text        not null default 'pending'
        check (state in (
            'pending',
            'resolved_yes',
            'resolved_no',
            'expired')),
    expires_at_utc            timestamptz not null,
    resolved_at_utc           timestamptz,
    resolved_by_principal_id  uuid,
    resolve_message_id        text,
    resolve_cause             text,
    created_at_utc            timestamptz not null default now()
);

-- At most one pending confirmation per (tenant, session, conversation)
create unique index if not exists ux_link_pending_confirmations_active
    on link_pending_confirmations (tenant_id, session_id, conversation_id)
    where state = 'pending';

-- ── Enrichment attempts ──────────────────────────────────────────────────────

create table if not exists link_enrichment_attempts (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    link_artifact_id    uuid        not null references link_artifacts(id),
    attempt_no          int         not null,
    started_at_utc      timestamptz not null,
    completed_at_utc    timestamptz,
    duration_ms         int,
    outcome             text
        check (outcome in (
            'enriched',
            'policy_blocked',
            'timeout',
            'failed')),
    reason_code         text,
    provider_trace_id   text
);

-- ── Policy decisions (auditable, append-only) ────────────────────────────────

create table if not exists link_enrichment_policy_decisions (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    link_artifact_id    uuid        not null references link_artifacts(id),
    decision            text        not null
        check (decision in ('allow', 'block')),
    reason_code         text        not null,
    destination_host    text        not null,
    evaluated_at_utc    timestamptz not null default now(),
    evaluator_version   text
);

-- ── Audit events (immutable append-only) ─────────────────────────────────────

create table if not exists link_audit_events (
    id              uuid        primary key default gen_random_uuid(),
    tenant_id       uuid        not null,
    entity_type     text        not null,
    entity_id       uuid        not null,
    event_type      text        not null,
    event_time_utc  timestamptz not null default now(),
    actor_type      text        not null,
    actor_id        text,
    payload_json    jsonb       not null default '{}'::jsonb
);

-- ── Indexes ──────────────────────────────────────────────────────────────────

create index if not exists ix_link_artifacts_tenant
    on link_artifacts (tenant_id);
create index if not exists ix_link_artifacts_tenant_status
    on link_artifacts (tenant_id, enrichment_status);

create index if not exists ix_link_provenance_refs_tenant
    on link_provenance_refs (tenant_id);
create index if not exists ix_link_provenance_refs_artifact
    on link_provenance_refs (tenant_id, link_artifact_id);

create index if not exists ix_link_pending_confirmations_tenant
    on link_pending_confirmations (tenant_id);
create index if not exists ix_link_pending_confirmations_session
    on link_pending_confirmations (tenant_id, session_id, conversation_id);
create index if not exists ix_link_pending_confirmations_state
    on link_pending_confirmations (state);

create index if not exists ix_link_enrichment_attempts_tenant
    on link_enrichment_attempts (tenant_id);
create index if not exists ix_link_enrichment_attempts_artifact
    on link_enrichment_attempts (tenant_id, link_artifact_id);

create index if not exists ix_link_enrichment_policy_decisions_tenant
    on link_enrichment_policy_decisions (tenant_id);
create index if not exists ix_link_enrichment_policy_decisions_artifact
    on link_enrichment_policy_decisions (tenant_id, link_artifact_id);

create index if not exists ix_link_audit_events_tenant
    on link_audit_events (tenant_id);
create index if not exists ix_link_audit_events_entity
    on link_audit_events (tenant_id, entity_id);
create index if not exists ix_link_audit_events_time
    on link_audit_events (tenant_id, event_time_utc);

-- ── Row-level security ───────────────────────────────────────────────────────

alter table link_artifacts                   enable row level security;
alter table link_provenance_refs             enable row level security;
alter table link_pending_confirmations       enable row level security;
alter table link_enrichment_attempts         enable row level security;
alter table link_enrichment_policy_decisions enable row level security;
alter table link_audit_events                enable row level security;

-- link_artifacts: tenant scope
drop policy if exists p_link_artifacts_tenant on link_artifacts;
create policy p_link_artifacts_tenant on link_artifacts
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- link_provenance_refs: tenant scope
drop policy if exists p_link_provenance_refs_tenant on link_provenance_refs;
create policy p_link_provenance_refs_tenant on link_provenance_refs
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- link_pending_confirmations: tenant scope
drop policy if exists p_link_pending_confirmations_tenant on link_pending_confirmations;
create policy p_link_pending_confirmations_tenant on link_pending_confirmations
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- link_enrichment_attempts: read + write under tenant scope
drop policy if exists p_link_enrichment_attempts_read on link_enrichment_attempts;
create policy p_link_enrichment_attempts_read on link_enrichment_attempts
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_enrichment_attempts_write on link_enrichment_attempts;
create policy p_link_enrichment_attempts_write on link_enrichment_attempts
    for insert
    with check (tenant_id = app.current_tenant_id());

-- link_enrichment_policy_decisions: read + write under tenant scope
drop policy if exists p_link_enrichment_policy_decisions_read on link_enrichment_policy_decisions;
create policy p_link_enrichment_policy_decisions_read on link_enrichment_policy_decisions
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_enrichment_policy_decisions_write on link_enrichment_policy_decisions;
create policy p_link_enrichment_policy_decisions_write on link_enrichment_policy_decisions
    for insert
    with check (tenant_id = app.current_tenant_id());

-- link_audit_events: append-only under tenant scope
drop policy if exists p_link_audit_events_read on link_audit_events;
create policy p_link_audit_events_read on link_audit_events
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_audit_events_write on link_audit_events;
create policy p_link_audit_events_write on link_audit_events
    for insert
    with check (tenant_id = app.current_tenant_id());
