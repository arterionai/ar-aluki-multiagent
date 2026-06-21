# Research: Link Capture and Secure Enrichment

## Decision 1: One-time confirmation consume model

Decision: Use atomic first-writer-wins resolution on `LinkPendingConfirmation` where only the first explicit yes/no transitions from `pending` to a terminal state (`resolved_yes` or `resolved_no`).

Rationale: The feature requires that the first explicit yes/no consumes the pending item exactly once and later yes/no replies are side-effect free. Atomic transition prevents duplicate side effects under concurrent or retried inbound deliveries.

Alternatives considered:
- Stateless session flag only: rejected because retries and distributed handlers can race.
- Allow re-open of confirmation on repeated yes/no: rejected because it violates no-loop semantics.

## Decision 2: Confirmation timeout and late-response behavior

Decision: Keep pending confirmation with explicit `expires_at_utc`; any yes/no received after expiry returns `expired` outcome and never executes save side effects.

Rationale: Spec requires explicit expired outcomes for delayed responses and forbids blind saves after expiration.

Alternatives considered:
- Auto-extend timeout on any incoming text: rejected because ambiguous messages should not alter security-sensitive decision windows.
- Accept late yes/no if close to expiry threshold: rejected because it produces non-deterministic behavior.

## Decision 3: Enrichment timeout fallback policy

Decision: Enrichment attempts are capped at 4 seconds wall-clock per link. On timeout/failure, persist fallback metadata and an explicit outcome state/reason (`timeout` or `failed`) so recall remains available.

Rationale: The spec mandates a 4-second maximum wait and usable recall with explicit limited-metadata reasons.

Alternatives considered:
- Wait indefinitely for enrichment: rejected due to UX latency and operability risk.
- Drop link on enrichment timeout: rejected because capture must complete independently of enrichment success.

## Decision 4: Policy-first secure enrichment

Decision: Evaluate destination policy before any outbound metadata fetch; if disallowed, skip fetch and persist `policy_blocked` with auditable reason code.

Rationale: Security governance requires pre-fetch policy checks and auditable skip decisions.

Alternatives considered:
- Fetch then evaluate content policy: rejected because network egress would already occur.
- Silent block without audit artifact: rejected due to observability/compliance requirements.

## Decision 5: Duplicate canonical URL handling

Decision: For canonical-equivalent links within tenant scope, upsert a single active `LinkArtifact` and merge context/provenance references using deduplicated association records.

Rationale: Requirements demand no duplicate active artifacts while preserving additional context from repeated submissions.

Alternatives considered:
- Insert new artifact for every submission: rejected because it breaks duplicate constraints.
- Ignore duplicates entirely: rejected because new context/provenance must still be retained.

## Decision 6: Idempotent duplicate delivery handling

Decision: Use deterministic idempotency key at inbound capture (`tenant_id + source_channel + source_message_id + canonical_url`) and return explicit no-op outcome if replayed.

Rationale: Webhook retries and duplicate deliveries must not create extra active artifacts and must return deterministic idempotent outcomes.

Alternatives considered:
- Time-window dedupe only: rejected because it can miss delayed retries.
- Canonical URL dedupe without source event identity: rejected because distinct submissions can be valid merges, not strict no-ops.

## Decision 7: Recall projection requirements

Decision: `LinkRecallView` always returns canonical full URL, human-readable description, enrichment status/reason, and provenance reference. If enrichment is limited, description is fallback text that explains reason.

Rationale: The feature requires safe distinguishability among similar links and explicit reason when metadata is limited.

Alternatives considered:
- Return only title and short URL: rejected because identity/provenance is insufficient.
- Hide status/reason details from users: rejected because transparency is required for trust and debugging.
