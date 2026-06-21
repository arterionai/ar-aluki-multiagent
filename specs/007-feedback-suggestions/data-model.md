# Data Model: Feedback Suggestions Capture (007)

**Version**: 1.0 | **Date**: 2026-06-21 | **Persistence**: PostgreSQL 15+ with RLS

## Entity Relationships

```
Tenant
  └─ User (tenant_scoped via RLS)
      └─ Suggestion (tenant_scoped, user_scoped via RLS)
          └─ SuggestionAttachment (linked_at timestamps track context window)
              └─ ContentMetadata (SHA-256 hash, MIME type, size, blob URI)

AuditLog (cross-feature, immutable append)
  └─ StateTransition events (suggestion_id, actor, prior_state, new_state, reason, timestamp)
```

## Core Tables

### 1. `suggestions` (Primary Domain Entity)

```sql
CREATE TABLE suggestions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  user_id UUID NOT NULL,
  
  -- State machine
  state VARCHAR(32) NOT NULL CHECK (state IN ('captured', 'enriched', 'sent_user', 'archived')),
  state_transition_ts TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  archived_ts TIMESTAMP NULL,
  
  -- Content
  text_content TEXT NULL,  -- Inline if ≤5 KB; NULL if stored in blob
  text_blob_uri VARCHAR(1024) NULL,  -- Reference to Azure Blob if >5 KB
  
  -- Context window
  captured_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  context_window_expires_at TIMESTAMP NOT NULL,  -- 30 minutes from captured_at
  
  -- Attachment tracking
  attachment_count INT DEFAULT 0,  -- Denormalized; tracks total linked attachments (max 10)
  
  -- Idempotency
  inbound_message_id VARCHAR(255) NULL,  -- WhatsApp message ID
  inbound_payload_hash VARCHAR(64) NULL,  -- SHA-256 hash of inbound payload
  
  -- Audit
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  
  -- Row-Level Security constraint
  CONSTRAINT rls_tenant CHECK (tenant_id IS NOT NULL),
  CONSTRAINT rls_user CHECK (user_id IS NOT NULL),
  CONSTRAINT max_attachments CHECK (attachment_count <= 10),
  
  FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  FOREIGN KEY (tenant_id, user_id) REFERENCES users(tenant_id, id)
);

CREATE INDEX idx_suggestions_tenant_user_state ON suggestions(tenant_id, user_id, state);
CREATE INDEX idx_suggestions_context_window ON suggestions(tenant_id, user_id, context_window_expires_at) 
  WHERE state != 'archived';
CREATE INDEX idx_suggestions_idempotency ON suggestions(tenant_id, inbound_message_id, inbound_payload_hash) 
  WHERE state IN ('captured', 'enriched');
```

### 2. `suggestion_attachments` (Context Linking)

```sql
CREATE TABLE suggestion_attachments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  suggestion_id UUID NOT NULL,
  
  -- Attachment type & reference
  attachment_type VARCHAR(32) NOT NULL CHECK (attachment_type IN ('text', 'audio', 'photo')),
  blob_uri VARCHAR(1024) NOT NULL,  -- Azure Blob Storage URI
  
  -- Content metadata
  mime_type VARCHAR(64) NOT NULL,  -- e.g., 'audio/mp4', 'image/jpeg'
  file_size_bytes INT NOT NULL,
  content_hash VARCHAR(64) NOT NULL,  -- SHA-256 hash
  
  -- Lifecycle
  linked_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,  -- Within 30-min window
  expires_at TIMESTAMP NOT NULL,  -- 90 days from created_at if not retained
  retained_until TIMESTAMP NULL,  -- Explicit override; if set, never auto-delete
  
  -- Audit
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  
  CONSTRAINT rls_tenant CHECK (tenant_id IS NOT NULL),
  
  FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  FOREIGN KEY (suggestion_id) REFERENCES suggestions(id) ON DELETE CASCADE
);

CREATE INDEX idx_suggestion_attachments_suggestion ON suggestion_attachments(suggestion_id);
CREATE INDEX idx_suggestion_attachments_expiry ON suggestion_attachments(expires_at) 
  WHERE retained_until IS NULL;
```

### 3. `suggestion_state_transitions` (Audit Trail)

```sql
CREATE TABLE suggestion_state_transitions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  suggestion_id UUID NOT NULL,
  
  -- State change record
  prior_state VARCHAR(32) NOT NULL CHECK (prior_state IN ('captured', 'enriched', 'sent_user')),
  new_state VARCHAR(32) NOT NULL CHECK (new_state IN ('enriched', 'sent_user', 'archived')),
  
  -- Transition context
  actor VARCHAR(255) NOT NULL,  -- e.g., 'FeedbackAgent', 'System', 'AdminUser'
  reason TEXT NOT NULL,  -- e.g., 'context_window_expired', 'user_closed', 'auto_archive_90d'
  
  -- Timing
  transitioned_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  duration_seconds INT NOT NULL,  -- Time spent in prior state
  
  CONSTRAINT rls_tenant CHECK (tenant_id IS NOT NULL),
  
  FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  FOREIGN KEY (suggestion_id) REFERENCES suggestions(id) ON DELETE CASCADE
);

CREATE INDEX idx_state_transitions_suggestion ON suggestion_state_transitions(suggestion_id);
CREATE INDEX idx_state_transitions_tenant_time ON suggestion_state_transitions(tenant_id, transitioned_at DESC);
```

## Data Constraints & Validation Rules

### Suggestion Lifecycle
- **captured → enriched**: Triggered after 30-minute window closure OR user explicit close
- **enriched → sent_user**: Triggered when confirmation delivery signal received
- **sent_user → archived**: Automatic after 90 days post-transition
- **No reverse transitions**: One-way state machine only

### Context Window Rules
1. Only one active suggestion window per tenant-user pair at a time
2. Window duration: 30 minutes from `captured_at`
3. Any attachment (audio, photo, text clarification) sent within window is linked with `linked_at` timestamp
4. New suggestion intent detected while window active → closes active window, begins new one
5. Window expiry is automatic; no user action resets it
6. Context linking is tenant-scoped; no cross-tenant linking

### Attachment Rules
1. Text payloads ≤5 KB: Stored inline in `suggestions.text_content`
2. Text payloads >5 KB: Stored in blob, reference in `suggestions.text_blob_uri`
3. Audio payloads: ≤50 MB max, MIME types audio/mp4, audio/webm, audio/ogg
4. Photo payloads: ≤10 MB max, MIME types image/jpeg, image/png only
5. Multi-attachment: Single suggestion may reference up to 10 attachments (text, audio, photo combined)
6. Lifecycle: 90-day auto-expiry unless explicitly retained via `retained_until`

### Idempotency & Deduplication
- **Duplicate key**: message_id + payload_hash composite
- **Detection layer**: Inbound webhook handler
- **Idempotency window**: 5 minutes from first ingestion
- **Behavior**: Logged as ignored event in audit trail; existing suggestion unchanged
- **Cross-channel**: Audio/photo duplicates by content hash; text duplicates by message ID

### Row-Level Security (RLS)
All queries must include tenant_id + user_id predicates:
```sql
WHERE suggestions.tenant_id = current_setting('app.current_tenant_id')::UUID
  AND suggestions.user_id = current_setting('app.current_user_id')::UUID
```

RLS policies enforced at table level; no suggestion record accessible outside its tenant-user scope.

## Relationships to Other Aggregates

### Suggestions ↔ WhatsApp Messages (001-whatsapp-capture)
- **Link**: `inbound_message_id` in suggestions matches WhatsApp message ID from webhook
- **Relationship**: 1:1 (each WhatsApp suggestion message creates exactly one suggestion artifact)
- **Visibility**: Suggestions excluded from memory recall (002-personal-memory) after `captured` state

### Suggestions ↔ Recall System (002-personal-memory)
- **Link**: None by design; suggestions are separate domain
- **Visibility rule**: Suggestions never appear in recall result sets after `captured` state
- **Query filter**: Memory queries exclude `artifact_type = 'suggestion'`

### Suggestions ↔ Audit Log (cross-feature)
- **Link**: Every state transition recorded in `suggestion_state_transitions`
- **Actor field**: 'FeedbackAgent' for automated transitions; admin/system for manual overrides
- **Immutability**: Audit log is append-only, never deleted

## Storage & Performance

### Estimated Schema Size (1M active suggestions)
- suggestions table: ~200 MB (1M rows × ~200 bytes)
- suggestion_attachments: ~400 MB (2M rows avg 4 attachments/suggestion × ~200 bytes)
- suggestion_state_transitions: ~600 MB (tracking full lifecycle transitions)
- **Total**: ~1.2 GB (excludes blob storage; blobs hosted in Azure Storage)

### Index Strategy
- Clustered: tenant_id + user_id + state (query isolation)
- Context window expiry: Scans for automated 30-min transitions
- Idempotency lookups: Message ID + hash (write path deduplication)
- State transitions: Tenant + timestamp descending (audit queries)

### Lifecycle Maintenance
- **Blob expiry cleanup**: Azure Storage lifecycle policy (90-day rule configured separately)
- **State transition archival**: Quarterly compression; old records moved to cold storage after 1 year
- **Suggestion records**: Hard delete 180 days post-archive (configurable per compliance policy)
