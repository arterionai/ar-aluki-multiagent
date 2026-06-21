# Feature Specification: WhatsApp Capture Foundation (Starter Baseline)

Feature ID: SB-001
Status: Draft
Date: 2026-06-21

## 1. Objective

Enable reliable inbound WhatsApp capture as the first production channel in the new architecture.

## 2. Architecture Adaptation

- Ingress adapter normalizes every inbound payload to Unified Message Object.
- Orleans AgentSessionGrain receives UMO and coordinates deterministic guard path.
- CaptureMessageSkill persists message artifacts in PostgreSQL with tenant scope.
- Durable Functions handle retries/backoff and poison handling for long or failing steps.
- TenantScopeSkill, IdempotencyGuardSkill, and AuditLogSkill are mandatory in pipeline.

## 3. In Scope

- Text, audio, image, forwarded conversation capture.
- Metadata/provenance persistence.
- Minimal acknowledgment response.
- Idempotent processing and retry-safe durability.

## 4. Out of Scope

- Semantic Q&A and deep extraction.
- Advanced media understanding beyond baseline capture.

## 5. User Stories

### US1 - Capture voice note reliably (P1)
Given user sends audio, when pipeline runs, then audio artifact and metadata are saved with provenance and acknowledgment is returned.

### US2 - Capture core content types (P1)
Given text/image/forwarded content, when processed, then each is persisted as normalized UMO artifact with correct content type.

### US3 - Strict org isolation (P1)
Given two tenants, when querying artifacts, then tenant A cannot access tenant B data under RLS.

## 6. Acceptance Criteria

- Every saved message includes tenant_id, context_id, created_by_user_id, source_channel.
- Duplicate webhook deliveries do not create duplicate captured records.
- Permanent failures are audited; no silent loss.
