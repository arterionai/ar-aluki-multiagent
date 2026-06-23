-- Migration 023: AI usage tracking for admin dashboard cost monitoring
-- Part of SB-013 admin dashboard

create table if not exists app.ai_usage_log (
    id                bigserial primary key,
    tenant_id         uuid not null,
    feature           text not null check (feature in ('memory','conversation','extraction','calendar','semantic_graph','other')),
    model             text not null,
    prompt_tokens     int not null default 0,
    completion_tokens int not null default 0,
    total_tokens      int not null generated always as (prompt_tokens + completion_tokens) stored,
    cost_usd          numeric(10,6) not null default 0,
    recorded_at       timestamptz not null default now()
);

create index if not exists ix_ai_usage_log_tenant_date on app.ai_usage_log (tenant_id, recorded_at);
create index if not exists ix_ai_usage_log_feature_date on app.ai_usage_log (feature, recorded_at);

alter table app.ai_usage_log enable row level security;
create policy ai_usage_log_tenant on app.ai_usage_log
    using (tenant_id = app.current_tenant_id());
