-- 022_calendar_oauth_tokens.sql
-- SB-003 calendar integration follow-up: persist OAuth access/refresh tokens so the
-- connect flow yields a usable session and provider adapters can call the real
-- Microsoft Graph / Google Calendar APIs (FR-007, token refresh flows).
-- Token material is stored ENCRYPTED at rest (AES-256-GCM, Key Vault-backed key);
-- the plaintext never touches this table. Builds on tenancy/RLS baseline from 001/003.

create table if not exists calendar_oauth_tokens (
    calendar_oauth_token_id uuid primary key default gen_random_uuid(),
    calendar_connection_id uuid not null references calendar_connections(calendar_connection_id),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    provider text not null check (provider in ('outlook', 'google')),
    -- base64(nonce || ciphertext || tag) produced by the application-layer protector.
    access_token_cipher text not null,
    refresh_token_cipher text,
    access_token_expires_at_utc timestamptz not null,
    scope text,
    token_type text,
    correlation_id text not null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

-- One live token set per (tenant, context, user, provider). The callback upserts on it.
create unique index if not exists ux_calendar_oauth_tokens_identity
    on calendar_oauth_tokens (tenant_id, context_id, user_id, provider);

create index if not exists ix_calendar_oauth_tokens_connection
    on calendar_oauth_tokens (calendar_connection_id);

-- RLS: same tenant-isolation contract as the rest of 008.
alter table calendar_oauth_tokens enable row level security;

drop policy if exists p_calendar_oauth_tokens_tenant on calendar_oauth_tokens;
create policy p_calendar_oauth_tokens_tenant on calendar_oauth_tokens
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));
