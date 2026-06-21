# Research: Delegated Reminders

## Decision 1: Delegated flow must be isolated from personal reminder flow

Decision: Route delegated intent through a dedicated delegated-reminder orchestration branch and never reuse personal-reminder handlers for side effects.

Rationale: The specification requires strict query and lifecycle separation between delegated and personal reminders. Isolation prevents cross-contamination of state and cancellation behavior.

Alternatives considered:
- Shared orchestration with a delegated flag: rejected because branch leakage risk is high and makes validation of FR-001/FR-012 difficult.
- Post-classification transform into personal reminders: rejected because recipient consent and sender-notification semantics are different.

## Decision 2: Three-tier recipient resolution as explicit state machine

Decision: Implement recipient resolution as explicit outcomes:
- `tier1_known_contact_confirmed` (direct route),
- `tier2_phone_only_needs_capture`,
- `tier3_unknown_needs_clarification`.

Rationale: Deterministic outcomes are required for auditable progression and for consistent sender UX when recipient identity is incomplete.

Alternatives considered:
- Best-effort fuzzy match with auto-selection: rejected because false positives can cause unintended delivery.
- Single-step mandatory form: rejected because it degrades UX for already-known contacts.

## Decision 3: Consent registry is authoritative and fail-closed

Decision: Delivery eligibility depends on explicit consent from recipient, persisted in tenant-scoped consent registry with optional sender-scoped grants.

Rationale: Clarifications require explicit opt-in and no assumed consent. Fail-closed behavior is required when policy services are unavailable.

Alternatives considered:
- Soft-consent inferred from prior interaction: rejected because it violates explicit opt-in requirement.
- Caching consent without persistence: rejected because it weakens traceability and replay correctness.

## Decision 4: Retry policy uses bounded exponential backoff and permanent-failure bypass

Decision: Retries occur only for transient failures with exact backoff sequence 1s, 2s, 4s, 8s, 16s (five attempts total). Permanent classes terminate immediately.

Rationale: This is explicitly required by clarifications and ensures predictable maximum retry window.

Alternatives considered:
- Unlimited retries with cap by time only: rejected because it is operationally unstable.
- Uniform retry delay: rejected because it increases load during provider incidents.

## Decision 5: Cancellation and recall boundaries are enforced by due-time phase gate

Decision: Sender cancellation is accepted only up to 30 seconds before due-time. Inside delivery phase (due-time +/- tolerance), cancellation is rejected unless delivery has not started and workflow can still safely abort.

Rationale: Clarifications define explicit cancellation and recall boundaries, including no recall after recipient receives the message.

Alternatives considered:
- Cancel-until-send semantics: rejected because it creates ambiguous behavior with concurrent retries.
- Hard lock at due-time without tolerance: rejected because distributed timers require bounded timing tolerance.

## Decision 6: Delivery failure notification is decoupled from delivery attempt persistence

Decision: Persist terminal failure first, then enqueue sender notification with retry-safe semantics and classification payload.

Rationale: Sender visibility is mandatory but should not mutate grant/delivery accounting paths. Decoupling avoids double effects.

Alternatives considered:
- Inline notification inside delivery transaction: rejected because notification failures can block terminal-state persistence.
- Notification-only logs without explicit failure class: rejected because triage and support need machine-readable failure taxonomy.

## Decision 7: Anti-spam policy is evaluated before scheduling

Decision: Apply sender rolling-window caps before delegated reminder enters scheduled state; policy deny emits auditable decision event.

Rationale: Prevents abuse and avoids storing reminders that are known to be non-executable.

Alternatives considered:
- Enforce only at delivery time: rejected because it allows backlog abuse and late denials.
- Enforce per recipient only: rejected because sender-level abuse control is required.

## Decision 8: Audit evidence is append-only with correlation lineage

Decision: Emit append-only lifecycle audit events for creation, recipient resolution, consent acquisition, delivery transitions, cancellation, and terminal failure.

Rationale: The feature requires full traceability and operational forensics using correlation identifiers.

Alternatives considered:
- Mutable status history table: rejected because updates can obscure original decision path.
- Success-only audit events: rejected because denied/failure outcomes are required for compliance and debugging.

## Phase 0 Conclusion

All core ambiguities are resolved for delegated routing, recipient resolution, consent gating, retry boundaries, cancellation windows, and sender failure visibility. No unresolved clarifications remain for design artifacts.
