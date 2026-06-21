# Feature Specification: Suggestions Admin and Rewards (Starter Baseline)

Feature ID: SB-008A
Status: Draft
Date: 2026-06-21

## 1. Objective

Provide internal admin operations for suggestion triage and implement reward incentives for suggestion submitters.

## 2. Architecture Adaptation

- Admin API and UI are separate from conversational runtime; protected by Entra ID.
- FeedbackAgent emits suggestion lifecycle events.
- Durable workflow grants one-time rewards on capture and accepted-status transitions.
- Entitlement ledger is the single source for reward accounting.

## 3. In Scope

- Staff-only dashboard: classify, prioritize, and change status.
- Audit trail for all status/category/priority changes.
- Base reward, quality bonus, and streak bonus logic.

## 4. Out of Scope

- Cross-org public ranking pages.
- User-voting marketplace.

## 5. User Stories

### US1 - Staff triage dashboard (P1)
Given authorized staff login, when dashboard loads, then suggestions are visible with classification controls and attachment context.

### US2 - Submitter rewards (P2)
Given suggestion capture and lifecycle progression, when reward conditions are met, then entitlement grants are applied once per rule.

### US3 - Queue filtering at scale (P3)
Given large suggestion volume, when filters/search/sort are used, then triage queue remains operable.

## 6. Acceptance Criteria

- Unauthorized users are denied all admin endpoints.
- Reward grants are idempotent and capped by policy.
- User notifications follow in-window/template rules.
