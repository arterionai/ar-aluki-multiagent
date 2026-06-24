-- 025_dispatch_retry_queue.sql
-- General-purpose dispatch retry queue: when a domain agent fails with a system
-- exception (not a user-input error), MessageDispatcher enqueues the original
-- UnifiedMessage + PrincipalContext so the message can be replayed automatically
-- once the underlying issue is resolved (e.g. after a bug-fix deploy).

create table if not exists dispatch_retry_queue (
    retry_id         uuid        primary key default gen_random_uuid(),
    tenant_id        uuid        not null,
    context_id       uuid        not null,
    user_id          uuid        not null,
    correlation_id   text        not null,
    unified_message  jsonb       not null,   -- serialized UnifiedMessage
    principal        jsonb       not null,   -- serialized PrincipalContext (no secret material)
    failed_agent_id  text        not null,
    error_code       text        not null,
    status           text        not null default 'pending',  -- pending / processing / succeeded / abandoned
    attempt_count    int         not null default 0,
    max_attempts     int         not null default 3,
    next_retry_utc   timestamptz not null default now(),
    last_error       text,
    created_at_utc   timestamptz not null default now(),
    updated_at_utc   timestamptz not null default now()
);

alter table dispatch_retry_queue enable row level security;

create policy dispatch_retry_queue_tenant_isolation on dispatch_retry_queue
    using (app.user_in_tenant(tenant_id));

create index if not exists ix_dispatch_retry_queue_pending
    on dispatch_retry_queue (next_retry_utc)
    where status = 'pending';

-- Atomically claim pending retries that are due; increments attempt_count and
-- sets status='processing' so concurrent sweeps cannot double-claim the same row.
create function app.claim_due_dispatch_retries(p_limit int, p_now timestamptz)
returns table (
    retry_id        uuid,
    tenant_id       uuid,
    context_id      uuid,
    user_id         uuid,
    correlation_id  text,
    unified_message jsonb,
    principal       jsonb,
    failed_agent_id text,
    attempt_count   int
)
language sql
security definer
set search_path = public, app
as $$
    update dispatch_retry_queue r
    set    status        = 'processing',
           attempt_count = attempt_count + 1,
           updated_at_utc = now()
    where  r.retry_id in (
        select r2.retry_id
        from   dispatch_retry_queue r2
        where  r2.status = 'pending'
          and  r2.next_retry_utc <= p_now
          and  r2.attempt_count  <  r2.max_attempts
        order  by r2.next_retry_utc
        for update skip locked
        limit  greatest(p_limit, 0)
    )
    returning r.retry_id, r.tenant_id, r.context_id, r.user_id, r.correlation_id,
              r.unified_message, r.principal, r.failed_agent_id, r.attempt_count;
$$;
