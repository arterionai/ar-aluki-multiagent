# Feature Specification: Domain Agents Runtime (Starter Baseline)

Feature ID: SB-009B
Status: Draft
Date: 2026-06-21

## 1. Objective

Adopt a domain-agent runtime architecture that keeps the core message processor thin, enforces tenant-scoped domain boundaries, and enables modular feature growth without recurring edits to core dispatch logic.

## Clarifications

### Session 2026-06-21

- Q: What routing precedence order is required for domain dispatch? → A: Dispatch precedence is fixed as: policy gating first, then eligible-agent evaluation, then deterministic selection by priority and tie-break, with fallback only if no eligible agent exists.
- Q: How are ties resolved when multiple agents are eligible at the same priority? → A: Ties are resolved by canonical agent identifier in ascending lexical order; if still tied, by registration timestamp in ascending order.
- Q: What fallback policy applies when no agent claim is available or routing cannot select one? → A: Fallback to MemoryAgent occurs only when zero agents are eligible after gating; fallback is not used to mask a selected-agent failure.
- Q: What are the minimum failure-containment and audit obligations? → A: Selected-agent failures are confined to the current dispatch cycle, must not block subsequent messages, and must emit immutable audit evidence with routing inputs, decision rationale, outcome, and correlation metadata.

## 2. Architecture Adaptation

- Message ingress delegates to MessageDispatcher.
- Domain agents own deterministic guards, intent handling, and domain-specific state stores.
- SkillDispatcher executes CoordinatorPlan against Skill Registry.
- MemoryAgent remains fallback when no domain agent claims intent.
- Consent and tenant-scope checks execute before domain dispatch.
- Domain-agent failures are contained at dispatch boundary and converted to auditable events.

## 3. In Scope

- Agent registration and priority order.
- Domain-local state store boundaries.
- Thin core processor responsibilities only.
- Deterministic dispatch, fallback policy, and failure containment behavior.
- Runtime observability for routing and containment decisions.

## 4. Out of Scope

- Reverting to monolithic processor behavior.
- Cross-domain state coupling.
- Introducing new domain behaviors or expanding existing business capabilities.
- Replacing the skill-first execution model with direct agent side-effect logic.

## 5. User Scenarios & Testing

### US1 - Add a Domain Without Core Edits (Priority: P1)

A platform developer can register a new domain agent so runtime routing supports the new domain without changes to core processor branching.

Why this priority: This is the primary modularity outcome and the central reason for introducing domain agents.

Independent Test: Register one additional domain agent in a controlled environment, restart runtime, and verify new-domain traffic is routed correctly while core processor logic remains unchanged.

Acceptance Scenarios:

1. Given a registered domain agent with valid priority and guard rules, when runtime starts, then the dispatcher includes the agent in deterministic routing order.
2. Given inbound intent that only the new domain agent can claim, when message dispatch runs, then the agent is selected without requiring core processor edits.

### US2 - Keep Domain State Isolated (Priority: P2)

A domain agent can process its own pending state while remaining isolated from other domain state stores.

Why this priority: Domain isolation protects correctness, tenant security boundaries, and maintainability.

Independent Test: Create parallel pending states across at least two domains for the same tenant and verify each agent can access only its own domain-local state.

Acceptance Scenarios:

1. Given pending states exist in multiple domains, when a domain agent handles a message, then it reads and writes only its own domain-local state.
2. Given a domain agent attempts to access another domain's state, when guard checks execute, then access is denied and an auditable event is recorded.

### US3 - Keep Core Processor Readable and Thin (Priority: P3)

An engineer can inspect the core processor and find only cross-cutting runtime responsibilities, not domain-specific logic.

Why this priority: A thin core lowers change risk and keeps architectural intent enforceable over time.

Independent Test: Perform static inspection against defined responsibility boundaries and verify no domain-specific intent branches exist in the core processor.

Acceptance Scenarios:

1. Given a runtime message flow, when a developer inspects the core processor, then only deserialization, consent and authorization gating, and dispatch orchestration are present.
2. Given a domain rule update, when implementation is completed, then changes are confined to domain agent and related domain assets, not core processor branching.

### Edge Cases

- What happens when two domain agents both claim the same message at the same priority?
	The runtime must apply deterministic tie-breaking by canonical agent identifier ascending, then registration timestamp ascending, and emit an auditable routing decision that includes tie-break rationale.
- What happens when no domain agent claims intent?
	The runtime must route to fallback capture policy through MemoryAgent and record the explicit reason as no eligible domain agent.
- What happens when an inbound message is duplicated?
	The routing decision must remain idempotent for the same normalized message identity.
- What happens when principal, tenant, or consent context is missing?
	Dispatch is blocked before domain-agent evaluation and a denial event is recorded.
- What happens when the selected domain agent fails during handling?
	Failure is contained to that dispatch cycle, fallback is not used to mask the failure in the same cycle, diagnostic evidence is emitted, and runtime remains available for subsequent messages.

## 6. Requirements

### Functional Requirements

- FR-001: The system MUST support registration of multiple domain agents with explicit deterministic evaluation order.
- FR-002: The system MUST evaluate agent guard checks deterministically for the same normalized message and principal context.
- FR-003: The system MUST select at most one primary domain agent per message dispatch cycle.
- FR-004: The system MUST apply deterministic tie-breaking when multiple agents are eligible.
- FR-005: The system MUST route to fallback capture policy when no domain agent claims intent.
- FR-006: The system MUST enforce consent and tenant-scoped principal checks before domain-agent evaluation.
- FR-007: Domain agents MUST read and write only their own domain-local state boundary.
- FR-008: The system MUST block and audit attempted cross-domain state access.
- FR-009: The core processor MUST remain limited to normalization, gating, and dispatch orchestration responsibilities.
- FR-010: The system MUST contain domain-agent failures so one failed dispatch does not stop subsequent dispatch cycles.
- FR-011: The system MUST emit auditable routing, fallback, and failure-containment events for each dispatch cycle.
- FR-012: The system MUST preserve skill-first execution by preventing agents from bypassing registered skills for irreversible business side effects.
- FR-013: The system MUST apply routing precedence in this order: (1) policy gating, (2) eligibility evaluation, (3) deterministic selection, (4) fallback only if no eligible agent exists.
- FR-014: For equal-priority eligible agents, the system MUST select by canonical agent identifier in ascending lexical order; if still tied, by registration timestamp in ascending order.
- FR-015: The system MUST NOT invoke fallback as a recovery path for a selected-agent failure within the same dispatch cycle.
- FR-016: Each dispatch cycle MUST persist immutable audit evidence including message identity, tenant and principal context, evaluated agents, selected outcome or fallback reason, tie-break rationale when applicable, containment result when applicable, correlation identifier, and timestamp.

### Key Entities

- UnifiedMessage: Normalized inbound message representation used for deterministic dispatch.
- PrincipalContext: Tenant, user, and consent context required before any dispatch decision.
- DomainAgentRegistration: Runtime metadata for each domain agent including identity and evaluation priority.
- AgentRoutingDecision: Auditable record of evaluated agents, selected agent, tie-break result, or fallback route.
- DomainStateEnvelope: Domain-local state container bound to one domain boundary and tenant context.
- DispatchAuditEvent: Immutable event record for routing outcome, failures, and policy denials.

## 7. Success Criteria

### Measurable Outcomes

- SC-001: In replay tests of identical normalized messages and principal contexts, routing outcomes are identical in 100% of runs.
- SC-002: In architecture conformance review, core processor code contains no domain-specific intent branching and remains limited to defined cross-cutting responsibilities.
- SC-003: In mixed-domain integration scenarios, zero unauthorized cross-domain state reads or writes are observed.
- SC-004: In controlled failure-injection scenarios, an agent failure in one dispatch cycle does not block processing of the next valid message.
- SC-005: For 100% of dispatch cycles, audit evidence exists for route selection outcome (selected agent or fallback) and policy-denied events when applicable.
- SC-006: In deterministic tie scenarios with equal-priority claims, the same canonical agent is selected in 100% of replay runs.
- SC-007: In failure-injection scenarios, 100% of selected-agent failures produce a containment audit event and 0% are converted to fallback in the same dispatch cycle.

## 8. Assumptions

- Existing message normalization and principal-context resolution are available before dispatcher evaluation.
- Existing consent policy semantics (including opt-out flows) remain unchanged and are enforced by current policy controls.
- Existing skill registry remains the only allowed path for irreversible side effects.
- Existing telemetry and audit infrastructure can store dispatch-level events without requiring a new observability platform.
- This feature does not introduce new end-user capabilities; it restructures runtime behavior for maintainability, isolation, and governance.
