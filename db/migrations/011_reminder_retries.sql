-- 011_reminder_retries.sql
-- SB-005 delivery retry backoff: track delivery attempts on the reminder and a
-- retry due time, and extend the fire-sweep claim to also pick up reminders whose
-- retry is due. Builds on 010_reminders.sql.

alter table reminders add column if not exists delivery_attempt_count int not null default 0;
alter table reminders add column if not exists next_retry_utc timestamptz;

create index if not exists ix_reminders_retry_ready
    on reminders (status, next_retry_utc);

-- Re-create the claim to also harvest due retries. A reminder is claimable when:
--   * it is freshly due (status='scheduled' and scheduled_time_utc <= now), or
--   * it is mid-retry (status='firing' with next_retry_utc <= now).
-- The claim clears next_retry_utc so a row is not re-claimed until the caller
-- arms the next retry. delivery_attempt_count is returned so the caller knows the
-- next attempt number. (RETURNS TABLE shape changed, so drop + create.)
drop function if exists app.claim_due_reminders(int, timestamptz);
create function app.claim_due_reminders(p_limit int, p_now timestamptz)
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
    recurrence_rule_id uuid,
    delivery_attempt_count int)
language sql
security definer
set search_path = public, app
as $$
    update reminders r
    set status = 'firing', next_retry_utc = null, updated_at_utc = now()
    where r.reminder_id in (
        select r2.reminder_id
        from reminders r2
        where r2.deleted_at_utc is null
          and (
              (r2.status = 'scheduled' and r2.scheduled_time_utc <= p_now)
              or (r2.status = 'firing' and r2.next_retry_utc is not null and r2.next_retry_utc <= p_now)
          )
        order by coalesce(r2.next_retry_utc, r2.scheduled_time_utc)
        for update skip locked
        limit greatest(p_limit, 0)
    )
    returning r.reminder_id, r.tenant_id, r.context_id, r.user_id,
              r.reminder_text, r.scheduled_time_utc, r.timezone,
              r.delivery_channel, r.correlation_id, r.reminder_type,
              r.recurrence_rule_id, r.delivery_attempt_count;
$$;
