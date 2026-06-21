# Feature Specification: Delegated Reminders (Starter Baseline)

Feature ID: SB-006
Status: Draft
Date: 2026-06-21

## Clarifications

### Session 2026-06-21

- Q: What are the recipient routing and resolution rules for delegated reminders? → A: Recipient resolution follows a three-tier model: (1) If recipient is in the sender's contact list with confirmed WhatsApp identity, route directly to delegated-delivery flow; (2) If recipient is known by phone only, initiate contact-capture flow (request sender for WhatsApp handle or confirmation); (3) If recipient is unknown, collect recipient identity from sender via clarification dialog, then check consent registry before delivery. Routing key is `(tenant_id, recipient_identity, sender_identity)`.
- Q: What is the consent/contact requirement semantics? → A: Consent is required for all delegated reminders and is acquired via PolicyDecisionSkill before first delivery. "Unconsented" means recipient has not explicitly opted in to delegated reminders. Opt-in is persistent per recipient per tenant but may be scoped per sender (allow delegated from specific contacts) or global. Consent check must pass before any delivery attempt. Lack of explicit opt-in defaults to unconsented; consent is never assumed.
- Q: What are the delivery retry boundaries? → A: Transient delivery failures retry up to 5 attempts with bounded exponential backoff (1s, 2s, 4s, 8s, 16s = 31s total window). After 5 retries, failure is terminal and a failure-notification is sent to the sender. Provider errors (invalid recipient, permission denied) are treated as permanent and terminate immediately. All retries are logged with correlation identifiers for traceability.
- Q: What are the cancellation/recall timing windows? → A: Sender may cancel a delegated reminder up to 30 seconds before the due time. Once due-time arrives within the delivery window (±5s tolerance), the reminder enters delivery phase and cannot be recalled. In-flight delivery attempts (retrying) may be interrupted by cancellation request, but once recipient receives the reminder message, recall is not possible. Ambiguous cancel requests (e.g., multiple reminders due) require sender to disambiguate.
- Q: What is the failure notification policy? → A: Delivery failures are surfaced to the sender via direct notification (WhatsApp inbound message with failure reason) if recipient delivery failed after retry exhaustion. Failure classification distinguishes: transient-exhausted (retries exceeded), permanent-invalid-recipient (contact not found), permanent-permission (recipient blocked sender or opted out), and system-error (infrastructure). All failures are audited with `delegated_reminder.delivery_failed` events including correlation and recipient identity.

## 1. Objective

Enable users to request reminders for third parties with correct routing, recipient handling, consent, and delivery visibility.

## 2. Architecture Adaptation

- SchedulingAgent resolves delegated-intent path separately from personal reminder path.
- Dedicated SchedulingStateStore keeps delegated reminder state isolated from other domains.
- Durable Functions orchestrate consent wait, retries, and delivery callbacks.
- PolicyDecisionSkill enforces consent and anti-spam constraints.

## 3. In Scope

- Delegated reminder intent detection.
- Recipient capture and consent state handling.
- Third-party delivery and sender-side status feedback.
- Separate management commands for delegated reminders.

## 4. Out of Scope

- Bulk delegated campaigns.
- Non-WhatsApp delegated channels in MVP.

## 5. User Stories

### US1 - Correct delegated intent routing (P1)
Given user asks to remind someone else, when intent is classified, then system stays in delegated reminder flow and avoids unrelated contact onboarding flows.

### US2 - Recipient and consent handling (P1)
Given recipient unknown or unconsented, when user provides recipient details, then system starts proper consent path before delivery.

### US3 - Delivery and management (P2)
Given delegated reminder is scheduled, when due time arrives, then recipient receives message and sender can query/cancel delegated reminders distinctly.

## 6. Acceptance Criteria

- Delegated and personal reminders are queryable independently.
- Ambiguous cancellation requests trigger disambiguation.
- Delivery failure to recipient is surfaced to sender.

## 7. Actors

- **Sender**: User who requests a delegated reminder.
- **Recipient**: Third party to whom the reminder is addressed.
- **PolicyDecisionSkill**: Enforces recipient consent and sender anti-spam rules.
- **Delivery Service**: Routes reminder message to recipient via WhatsApp.

## 8. Key Entities

- **Delegated Reminder Record**: Tenant-scoped record with sender identity, recipient identity (resolved per clarification tier), reminder content, due-time, status, and consent-acquired flag.
- **Recipient Contact**: Resolved recipient identity binding (name, phone, WhatsApp handle) captured during recipient-resolution flow.
- **Consent Registry Entry**: Persistent per-recipient consent state indicating opt-in to delegated reminders and any per-sender or scope constraints.
- **Delivery Attempt**: Transactional record of a delivery try with timestamp, retry count, failure reason, and correlation identifier.
- **Audit Event**: Immutable record for delegated-reminder lifecycle: creation, recipient-resolution, consent-acquired, delivery-started, delivery-succeeded, delivery-failed-terminal, cancellation, or recall.

## 9. Functional Requirements

- **FR-001**: System MUST detect delegated-reminder intent and classify it separately from personal-reminder flow without mixing orchestration paths.
- **FR-002**: System MUST resolve recipient identity using the three-tier model: (1) known contact with confirmed WhatsApp identity → direct routing; (2) known by phone only → initiate contact-capture; (3) unknown → collect from sender then consent-check.
- **FR-003**: System MUST capture recipient identity (name, phone, WhatsApp handle) and associate with routing key `(tenant_id, recipient_identity, sender_identity)`.
- **FR-004**: System MUST acquire explicit recipient opt-in before any delivery attempt; opt-in is persistent per recipient per tenant and defaults to unconsented.
- **FR-005**: System MUST enforce anti-spam by limiting delegated reminders per sender per rolling 24-hour window (configurable, baseline: 10 reminders).
- **FR-006**: System MUST implement delivery retry with a maximum of 5 attempts and bounded exponential backoff (1s, 2s, 4s, 8s, 16s = 31s total window) for transient failures only.
- **FR-007**: System MUST classify delivery failures as transient-exhausted, permanent-invalid-recipient, permanent-permission, or system-error and terminate immediately for permanent failures.
- **FR-008**: System MUST allow sender to cancel a delegated reminder up to 30 seconds before due-time; cancellation after 30-second window is rejected.
- **FR-009**: System MUST enforce recall impossibility once reminder enters delivery phase (±5s tolerance of due-time).
- **FR-010**: System MUST surface delivery failures to sender via WhatsApp notification with failure classification and recipient identity.
- **FR-011**: System MUST emit mandatory audit events: `delegated_reminder.created`, `delegated_reminder.recipient_resolved`, `delegated_reminder.consent_acquired`, `delegated_reminder.delivery_started`, `delegated_reminder.delivery_succeeded`, `delegated_reminder.delivery_failed_terminal`, `delegated_reminder.cancelled`, with correlation and scope identifiers.
- **FR-012**: System MUST support queryable distinction between delegated and personal reminders via separate query interfaces.
- **FR-013**: System MUST disambiguate ambiguous cancel requests (multiple reminders due within same window) by asking sender to specify reminder content or recipient.

## 10. Non-Functional Requirements

- **NFR-001**: Recipient consent check must complete within 1 second of consent-acquisition trigger.
- **NFR-002**: Delivery attempt must initiate within 5 seconds of due-time; late delivery after ±10s tolerance is logged as a service-level miss.
- **NFR-003**: Failure notification to sender must be delivered within 60 seconds of terminal failure classification.
- **NFR-004**: Retry backoff must guarantee that final retry attempt occurs within 31 seconds of first attempt (bounded window, no unbounded retries).
- **NFR-005**: All audit events must include correlation identifier, tenant scope, and affected actor identities for complete traceability.

## 11. Success Criteria

- **SC-001**: 100% of delegated reminder requests with recipient in sender's contact list complete recipient resolution within 1 second.
- **SC-002**: 100% of delegated reminders with unknown recipients collect recipient identity and successfully acquire consent before delivery.
- **SC-003**: Delivery success rate for recipients with active opt-in is at least 99% after retry exhaustion or immediate terminal failure classification.
- **SC-004**: 100% of transient-failure deliveries retry exactly up to 5 times within 31-second window; no retry occurs after window closes.
- **SC-005**: 100% of delivery failures are surface to sender with failure classification and recipient identity via WhatsApp notification within 60 seconds.
- **SC-006**: 100% of cancellation requests received ≤30 seconds before due-time are honored; requests after window are rejected with clear messaging.
- **SC-007**: Ambiguous cancel requests trigger disambiguation prompt and require sender to provide reminder identifier or recipient name.
- **SC-008**: 100% of delegated reminder lifecycle events (creation, resolution, consent, delivery, failure, cancel) are audited with correlation and scope identifiers.
- **SC-009**: Delegated and personal reminder queries return mutually exclusive result sets with no cross-contamination.

## 12. Edge Cases

- Recipient is sender (self-reminder via delegation syntax).
- Recipient identity resolves to multiple contacts with same phone but different WhatsApp handles.
- Recipient opts out after consent acquired but before delivery.
- Sender cancels during in-flight delivery attempt after recipient has already received message.
- Delivery provider returns invalid-recipient error (recipient deleted/deactivated WhatsApp).
- Sender blocks recipient or vice versa between consent-acquisition and delivery.
- Reminder due-time is in the past when orchestration starts.
- Multiple delegated reminders for same recipient within same 30-second window with ambiguous cancel request.
- Retry backoff timer expires without completion (clock skew, redeployment).
- Consensus-check failure (PolicyDecisionSkill unavailable) prevents consent validation.
