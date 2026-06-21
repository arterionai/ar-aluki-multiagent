# Feature Specification: Domain Agents Runtime (Starter Baseline)

Feature ID: SB-009B
Status: Draft
Date: 2026-06-21

## 1. Objective

Adopt a domain-agent runtime architecture that keeps the core message processor thin and enables modular feature growth.

## 2. Architecture Adaptation

- Message ingress delegates to MessageDispatcher.
- Domain agents own deterministic guards, intent handling, and domain-specific state stores.
- SkillDispatcher executes CoordinatorPlan against Skill Registry.
- MemoryAgent remains fallback when no domain agent claims intent.

## 3. In Scope

- Agent registration and priority order.
- Domain-local state store boundaries.
- Thin core processor responsibilities only.

## 4. Out of Scope

- Reverting to monolithic processor behavior.
- Cross-domain state coupling.

## 5. User Stories

### US1 - Add new domain without core edits (P1)
Given new domain agent is registered, when runtime starts, then dispatcher can route without modifying core processor logic.

### US2 - Isolated domain state (P2)
Given multiple pending states across domains, when message arrives, then each agent reads only its own state store.

### US3 - Readable core processor (P3)
Given runtime flow, when developer inspects core processor, then only deserialization, auth/consent gate, and dispatch remain.

## 6. Acceptance Criteria

- Agent priority and CanHandle checks are deterministic.
- Unknown intents route to fallback capture policy.
- Domain failures are contained with auditable diagnostics.
