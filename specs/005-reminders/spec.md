# Feature Specification: Scheduled Reminders (Starter Baseline)

Feature ID: SB-005
Status: Draft
Date: 2026-06-21

## 1. Objective

Let users schedule one-shot and recurring reminders from conversational commands with reliable delivery semantics.

## 2. Architecture Adaptation

- SchedulingAgent handles reminder intent and slot extraction.
- ReminderCreateSkill and ReminderConfirmSkill execute reminder lifecycle operations.
- Durable Functions own timer scheduling, retries, and overdue follow-up workflow.
- BudgetPolicySkill and TenantScopeSkill run before side effects.

## 3. In Scope

- One-shot reminders.
- Recurring reminders (daily/weekly/monthly).
- Done and snooze actions.
- Free-tier quota and entitlement-aware checks.

## 4. Out of Scope

- Complex recurrence editors.
- Cross-calendar conflict optimization.

## 5. User Stories

### US1 - One-shot reminder (P1)
Given user requests reminder at specific time, when schedule is accepted, then user gets confirmation and reminder notification at due time.

### US2 - Recurring reminder (P2)
Given recurring cadence request, when parsed, then recurring schedule is persisted and triggered on each cadence.

### US3 - Quota enforcement (P2)
Given free-tier limits reached, when new reminder is requested, then system blocks creation and returns clear upsell/limit message.

## 6. Acceptance Criteria

- Reminder firing is idempotent and audit logged.
- Snooze reschedules correctly without creating duplicates.
- Quota evaluation is derived from active records and entitlements.
