-- 014_youtube_link_capture.sql
-- SB-008B YouTube Link Save and Classification: tenant-scoped canonical video
-- records, best-effort enrichment metadata, structured AI classification with
-- confidence tiers, and immutable audit. Builds on tenancy/RLS from 001/003.
-- pgcrypto is NOT allow-listed on Azure; use core gen_random_uuid() (PG16).

-- ── Canonical YouTube video record ──────────────────────────────────────────

create table if not exists saved_link_artifacts (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    context_id          uuid        not null,
    principal_id        text        not null,
    canonical_video_id  text        not null,
    canonical_url       text        not null,
    original_source_url text        not null,
    status              text        not null default 'active'
                            check (status in ('active', 'inactive')),
    first_captured_at   timestamptz not null default now(),
    last_refreshed_at   timestamptz not null default now(),
    created_at          timestamptz not null default now(),
    updated_at          timestamptz not null default now(),
    constraint ux_saved_link_artifacts_video unique (tenant_id, canonical_video_id)
);

-- ── Enrichment metadata (best-effort per artifact) ──────────────────────────

create table if not exists link_enrichments (
    id                  uuid        primary key default gen_random_uuid(),
    saved_link_id       uuid        not null references saved_link_artifacts(id),
    tenant_id           uuid        not null,
    enrichment_state    text        not null
                            check (enrichment_state in ('enriched', 'partial', 'degraded')),
    provider_used       text        not null
                            check (provider_used in ('primary', 'secondary', 'none')),
    title               text,
    description_snippet text,
    channel_name        text,
    published_at        timestamptz,
    provider_error_code text,
    provider_latency_ms int,
    captured_at         timestamptz not null default now()
);

-- ── Structured classification with confidence ────────────────────────────────

create table if not exists link_classifications (
    id                  uuid        primary key default gen_random_uuid(),
    saved_link_id       uuid        not null references saved_link_artifacts(id),
    tenant_id           uuid        not null,
    category            text,
    tags                jsonb       not null default '[]'::jsonb,
    summary             text,
    confidence_label    text        not null
                            check (confidence_label in ('high', 'medium', 'low')),
    category_uncertain  bool        not null default false,
    tags_uncertain      bool        not null default false,
    summary_uncertain   bool        not null default false,
    confidence_score    numeric(4,3),
    classified_at       timestamptz not null default now()
);

-- ── Audit events (append-only) ───────────────────────────────────────────────

create table if not exists link_capture_audit_events (
    id                 uuid        primary key default gen_random_uuid(),
    tenant_id          uuid        not null,
    context_id         uuid        not null,
    principal_id       text        not null,
    message_id         text,
    canonical_video_id text,
    event_type         text        not null,
    outcome_code       text        not null,
    details            jsonb,
    created_at         timestamptz not null default now()
);

-- ── Indexes ──────────────────────────────────────────────────────────────────

create index if not exists ix_saved_link_artifacts_tenant
    on saved_link_artifacts (tenant_id);

create index if not exists ix_saved_link_artifacts_tenant_updated
    on saved_link_artifacts (tenant_id, updated_at desc);

create index if not exists ix_link_enrichments_tenant
    on link_enrichments (tenant_id);

create index if not exists ix_link_enrichments_saved_link
    on link_enrichments (saved_link_id);

create index if not exists ix_link_classifications_tenant
    on link_classifications (tenant_id);

create index if not exists ix_link_classifications_saved_link
    on link_classifications (saved_link_id);

create index if not exists ix_link_classifications_tenant_confidence
    on link_classifications (tenant_id, confidence_label);

create index if not exists ix_link_capture_audit_events_tenant
    on link_capture_audit_events (tenant_id);

create index if not exists ix_link_capture_audit_events_tenant_created
    on link_capture_audit_events (tenant_id, created_at desc);

-- ── Row-level security ───────────────────────────────────────────────────────

alter table saved_link_artifacts      enable row level security;
alter table link_enrichments          enable row level security;
alter table link_classifications      enable row level security;
alter table link_capture_audit_events enable row level security;

-- saved_link_artifacts: tenant scope
drop policy if exists p_saved_link_artifacts_tenant on saved_link_artifacts;
create policy p_saved_link_artifacts_tenant on saved_link_artifacts
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- link_enrichments: read + write under tenant scope
drop policy if exists p_link_enrichments_read on link_enrichments;
create policy p_link_enrichments_read on link_enrichments
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_enrichments_write on link_enrichments;
create policy p_link_enrichments_write on link_enrichments
    for insert
    with check (tenant_id = app.current_tenant_id());

-- link_classifications: read + write under tenant scope
drop policy if exists p_link_classifications_read on link_classifications;
create policy p_link_classifications_read on link_classifications
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_classifications_write on link_classifications;
create policy p_link_classifications_write on link_classifications
    for insert
    with check (tenant_id = app.current_tenant_id());

-- link_capture_audit_events: append-only under tenant scope
drop policy if exists p_link_capture_audit_events_read on link_capture_audit_events;
create policy p_link_capture_audit_events_read on link_capture_audit_events
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_link_capture_audit_events_write on link_capture_audit_events;
create policy p_link_capture_audit_events_write on link_capture_audit_events
    for insert
    with check (tenant_id = app.current_tenant_id());
