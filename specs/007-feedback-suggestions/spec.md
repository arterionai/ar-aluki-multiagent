# Feature Specification: Feedback Suggestions Capture (Starter Baseline)

Feature ID: SB-007
Status: Draft
Date: 2026-06-21

## 1. Objective

Capture product suggestions as a dedicated artifact type, separate from normal memory recall.

## 2. Architecture Adaptation

- FeedbackAgent handles suggestion-intent detection and suggestion lifecycle.
- Suggestion records persist in isolated suggestion domain tables.
- AttachmentStoreSkill links text, audio, and image follow-up artifacts to active suggestion windows.
- AuditLogSkill records transitions without exposing full content in telemetry.

## 3. In Scope

- Suggestion intent capture from text.
- Follow-up attachment capture.
- Welcome guidance that explains suggestion usage.

## 4. Out of Scope

- Public voting by users.
- Automatic publication workflows.

## 5. User Stories

### US1 - Save suggestion separately (P1)
Given user sends clear suggestion message, when processed, then suggestion is stored in separate domain and user receives confirmation.

### US2 - Add context via audio/photo (P2)
Given active suggestion window, when user sends attachment, then it is linked to same suggestion.

### US3 - Discoverability in welcome (P3)
Given first interaction, when welcome is sent, then user is told how to submit suggestions including media context.

## 6. Acceptance Criteria

- Suggestions do not appear in normal recall result sets.
- Follow-up window expiration starts a new suggestion artifact.
- Duplicate webhook deliveries remain idempotent.
