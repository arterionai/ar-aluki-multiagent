# Feature Specification: Delegated Reminders (Starter Baseline)

Feature ID: SB-006
Status: Draft
Date: 2026-06-21

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
