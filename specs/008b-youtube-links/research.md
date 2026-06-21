# Research Report: YouTube Link Save and Classification (008)

**Date**: 2026-06-21 | **Status**: Complete | **Spec Session**: 2026-06-21

## Context

This research resolves technical choices and edge behaviors required by the feature specification for deterministic YouTube link capture with provider fallback, unsupported URL handling, duplicate protection, and confidence visibility.

## Decision 1: Provider Fallback Order

- **Decision**: Enrichment executes in strict order: configured primary provider first, configured secondary provider second, degraded save when both fail.
- **Rationale**: Spec clarification requires deterministic behavior and transparent outcomes. Strict ordering prevents inconsistent metadata quality and simplifies auditing and incident diagnosis.
- **Alternatives considered**:
  - Parallel provider calls with first-success wins (rejected: higher cost, non-deterministic source attribution).
  - Randomized fallback provider selection (rejected: unstable behavior and weak audit traceability).
  - Hard-fail when primary fails (rejected: violates requirement that enrichment failure must not block persistence).

## Decision 2: Unsupported or Malformed URL Behavior

- **Decision**: Unsupported or malformed YouTube URLs are rejected before persistence; no artifact is created; user gets explicit invalid-link response; audit event emitted.
- **Rationale**: Clarification and edge-case requirements explicitly demand non-persistence for invalid links and transparent feedback.
- **Alternatives considered**:
  - Save raw URL as unresolved artifact (rejected: pollutes recall with unusable records).
  - Silent ignore without user feedback (rejected: poor UX and weak operational visibility).
  - Attempt fuzzy recovery from malformed URL (rejected: can create incorrect canonical identity and violate grounding).

## Decision 3: Duplicate Handling

- **Decision**:
  - Within a single message, each unique canonical video identity is processed exactly once.
  - Across submissions in same tenant, upsert by `(tenant_id, canonical_video_id)` performs idempotent refresh instead of creating a new active record.
  - Across tenants, same canonical video identity creates isolated records.
- **Rationale**: Matches FR3, edge-case rules, and tenant isolation requirements.
- **Alternatives considered**:
  - Deduplicate only on full URL string (rejected: misses same-video variants across YouTube URL forms).
  - Always insert new version row per submission (rejected: violates no-duplicate active-record requirement).
  - Global dedupe across tenants (rejected: violates tenant isolation).

## Decision 4: Confidence Visibility

- **Decision**: User confirmations include a confidence label (`high`, `medium`, `low`) and explicit uncertain-field markers for low-confidence classification fields.
- **Rationale**: Clarification and FR5 require user-visible confidence and non-fabrication signaling.
- **Alternatives considered**:
  - Hide confidence from user and keep internal only (rejected: fails transparency requirement).
  - Numeric confidence only (rejected: less understandable for confirmation UX).
  - Suppress low-confidence fields entirely (rejected: loses potentially useful context and reduces explainability).

## Implementation Patterns

### Canonicalization Strategy

- Normalize supported YouTube URL forms to canonical identity `video_id`.
- Canonical URL format persisted as `https://www.youtube.com/watch?v={video_id}`.
- If stable `video_id` cannot be extracted, flow returns invalid-link outcome and emits audit event.

### Outcome Model

- `enriched`: Primary/secondary provider returns sufficient metadata.
- `partial`: Link saved with partial metadata from fallback path.
- `degraded`: Link saved with canonical identity only (providers unavailable/failed).
- `invalid_link`: No persistence; explicit user message + audit event.

### Observability and Audit

Required auditable events for every processed canonical identity:
- detection attempted,
- normalization result,
- enrichment attempt (primary),
- fallback attempt (secondary, if used),
- persistence result (created/refreshed/none),
- final user outcome emitted.

## Dependencies and Best Practices

- Runtime baseline: .NET 10 and skill-driven orchestration.
- Persistence: PostgreSQL with tenant RLS and idempotent upsert constraints.
- Security: principal + tenant + context required before read/write.
- Reliability: duplicate webhook delivery must not create duplicate artifacts.

## Result

All clarifications from the feature specification are resolved. No remaining `NEEDS CLARIFICATION` items block design-phase artifacts.