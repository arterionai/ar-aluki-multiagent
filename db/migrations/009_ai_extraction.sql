-- 009_ai_extraction.sql
-- SB-004 AI extraction: tenant-scoped extraction jobs/results/fields/audit with
-- confidence + provenance. Builds on tenancy/RLS from 001/003. Independent of
-- 008_calendar_integration (both build only on 001/003).
-- pgcrypto is NOT allow-listed on Azure; use core gen_random_uuid() (PG16).

create table if not exists extraction_job (
    extraction_job_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid references contexts(id),
    created_by uuid not null references users_profile(id),
    job_status text not null
        check (job_status in (
            'pending', 'processing', 'completed_success',
            'completed_with_warnings', 'failed')),
    input_type text not null check (input_type in ('audio', 'text', 'image')),
    input_source text not null,
    input_size_bytes int,
    detected_language text,
    language_confidence double precision,
    processing_time_ms int,
    segment_count int not null default 0,
    completion_pct double precision not null default 0
        check (completion_pct >= 0 and completion_pct <= 1),
    error_category text,
    error_message text,
    idempotency_key text not null,
    correlation_id text not null,
    created_at_utc timestamptz not null default now(),
    started_at_utc timestamptz,
    completed_at_utc timestamptz,
    -- Idempotent submission per tenant.
    constraint ux_extraction_job_idem unique (tenant_id, idempotency_key),
    -- Lifecycle invariants from the data model.
    constraint ck_extraction_completed_state check (
        completed_at_utc is null
        or job_status in ('completed_success', 'completed_with_warnings', 'failed')),
    constraint ck_extraction_failed_category check (
        job_status <> 'failed' or error_category is not null)
);

create table if not exists extraction_result (
    extraction_result_id uuid primary key default gen_random_uuid(),
    extraction_job_id uuid not null unique references extraction_job(extraction_job_id),
    tenant_id uuid not null references tenants(id),
    extraction_type text not null
        check (extraction_type in ('transcription', 'text_summary', 'receipt_ocr')),
    overall_confidence double precision,
    model_provider text,
    model_name text,
    model_version text,
    raw_content jsonb not null default '{}'::jsonb,
    extracted_at_utc timestamptz not null default now()
);

create table if not exists extraction_field (
    extraction_field_id uuid primary key default gen_random_uuid(),
    extraction_result_id uuid not null references extraction_result(extraction_result_id),
    tenant_id uuid not null references tenants(id),
    field_name text not null,
    field_type text not null
        check (field_type in ('text', 'number', 'date', 'entity', 'decision_item', 'amount')),
    extracted_value jsonb,
    confidence_score double precision not null
        check (confidence_score >= 0 and confidence_score <= 1),
    confidence_tier text not null check (confidence_tier in ('high', 'medium', 'low')),
    confidence_justification text,
    source_segment_index int,
    detected_language text,
    created_at_utc timestamptz not null default now()
);

create table if not exists extraction_audit_event (
    extraction_audit_event_id uuid primary key default gen_random_uuid(),
    extraction_job_id uuid not null references extraction_job(extraction_job_id),
    tenant_id uuid not null references tenants(id),
    event_type text not null,
    actor uuid,
    details jsonb not null default '{}'::jsonb,
    created_at_utc timestamptz not null default now()
);

create index if not exists ix_extraction_job_tenant_created
    on extraction_job (tenant_id, created_at_utc desc);
create index if not exists ix_extraction_job_status
    on extraction_job (job_status, created_at_utc);
create index if not exists ix_extraction_result_job
    on extraction_result (extraction_job_id);
create index if not exists ix_extraction_field_result_tier
    on extraction_field (extraction_result_id, confidence_tier);
create index if not exists ix_extraction_audit_job_created
    on extraction_audit_event (extraction_job_id, created_at_utc);

-- RLS: tenant-scoped, mirroring 003/007. Denial/lifecycle audits are writable
-- under tenant scope; reads require active membership.
alter table extraction_job enable row level security;
alter table extraction_result enable row level security;
alter table extraction_field enable row level security;
alter table extraction_audit_event enable row level security;

drop policy if exists p_extraction_job_tenant on extraction_job;
create policy p_extraction_job_tenant on extraction_job
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_extraction_result_tenant on extraction_result;
create policy p_extraction_result_tenant on extraction_result
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_extraction_field_tenant on extraction_field;
create policy p_extraction_field_tenant on extraction_field
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_extraction_audit_read on extraction_audit_event;
create policy p_extraction_audit_read on extraction_audit_event
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_extraction_audit_write on extraction_audit_event;
create policy p_extraction_audit_write on extraction_audit_event
    for insert
    with check (tenant_id = app.current_tenant_id());
