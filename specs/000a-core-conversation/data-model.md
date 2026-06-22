# Data Model: Core Conversational Response (SB-000)

## Migration file

`db/migrations/020_conversational_response.sql`

> Note: SB-010 Billing was previously planned to use migrations 020–026.
> Because SB-000 is foundational and deployed first, SB-010 migrations shift to 027–033.

## Tables

### outbound_messages

Immutable log of every Aluki-originated reply. One row per inbound trigger.
The unique constraint on `(tenant_id, correlation_message_id)` is the idempotency
boundary — replayed webhook deliveries never produce duplicate rows.

```sql
CREATE TABLE app.outbound_messages (
    id                      UUID        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id               UUID        NOT NULL,
    user_id                 UUID        NOT NULL,
    correlation_message_id  TEXT        NOT NULL,   -- inbound WhatsApp message_id
    channel                 TEXT        NOT NULL DEFAULT 'whatsapp',
    recipient_wa_id         TEXT        NOT NULL,   -- destination wa_id (phone)
    body                    TEXT        NOT NULL,
    status                  TEXT        NOT NULL,   -- delivered | error_fallback | pending
    error_reason            TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    delivered_at            TIMESTAMPTZ,
    CONSTRAINT pk_outbound_messages PRIMARY KEY (id),
    CONSTRAINT uq_outbound_messages_idempotency
        UNIQUE (tenant_id, correlation_message_id)
);

ALTER TABLE app.outbound_messages ENABLE ROW LEVEL SECURITY;

CREATE POLICY outbound_messages_tenant_isolation
    ON app.outbound_messages
    USING (tenant_id = app.current_tenant());
```

## No new tables for conversation history

Recent conversation history is assembled by querying the existing
`app.captured_messages` table (created by SB-001). The
`IConversationHistoryStore` interface wraps this query:

```sql
SELECT body, created_at, direction   -- direction: 'inbound' | 'outbound'
FROM (
    SELECT body, created_at, 'inbound' AS direction
    FROM app.captured_messages
    WHERE tenant_id = $1 AND user_id = $2
    UNION ALL
    SELECT body, created_at, 'outbound' AS direction
    FROM app.outbound_messages
    WHERE tenant_id = $1 AND user_id = $2
) combined
ORDER BY created_at DESC
LIMIT $3;   -- HistoryWindowSize, default 10
```

## Indexes

```sql
-- Support fast history lookups
CREATE INDEX ix_outbound_messages_tenant_user_created
    ON app.outbound_messages (tenant_id, user_id, created_at DESC);
```
