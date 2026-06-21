# Data Model: Domain Agents Runtime

**Date**: 2026-06-21 | **Feature**: SB-009B Domain Agents Runtime

## Core Entities

### UnifiedMessage

Normalized inbound message used for deterministic dispatch.

```yaml
UnifiedMessage:
  message_id: string
  tenant_id: uuid
  principal_id: uuid
  channel: enum {whatsapp, api, internal}
  normalized_payload_hash: string
  received_at: datetime
  correlation_id: string

  Constraints:
    - message_id unique within tenant scope
    - normalized_payload_hash deterministic for equivalent inputs
```

### PrincipalContext

Resolved security context required before dispatch evaluation.

```yaml
PrincipalContext:
  tenant_id: uuid
  principal_id: uuid
  context_type: enum {personal, team, delegated}
  context_id: uuid?
  consent_state: enum {granted, denied, stopped}
  policy_version: string

  Constraints:
    - tenant_id and principal_id required
    - consent_state != granted blocks dispatch before eligibility
```

### DomainAgentRegistration

Registry metadata for deterministic evaluation and selection.

```yaml
DomainAgentRegistration:
  agent_id: string              # canonical identifier
  domain: string
  priority: integer
  registration_timestamp: datetime
  enabled: boolean
  guard_policy_ref: string
  skill_contract_ref: string

  Constraints:
    - agent_id unique
    - priority is integer with lower value = higher precedence
    - disabled agents excluded from eligibility
```

### AgentEligibilityResult

Per-agent evaluation outcome after policy gating.

```yaml
AgentEligibilityResult:
  dispatch_cycle_id: uuid
  agent_id: string
  priority: integer
  eligible: boolean
  reason_code: enum {eligible, policy_denied, guard_not_matched, disabled}
  evaluated_at: datetime

  Constraints:
    - one result per (dispatch_cycle_id, agent_id)
```

### AgentRoutingDecision

Deterministic decision output for a dispatch cycle.

```yaml
AgentRoutingDecision:
  dispatch_cycle_id: uuid
  selected_agent_id: string?
  selected_priority: integer?
  fallback_used: boolean
  fallback_agent_id: string?    # expected MemoryAgent when fallback_used=true
  fallback_reason: enum {no_eligible_agent, none}
  tie_break_applied: boolean
  tie_break_primary_key: string?    # canonical agent identifier
  tie_break_secondary_key: datetime? # registration timestamp
  decision_timestamp: datetime

  Constraints:
    - selected_agent_id XOR fallback_used=true
    - fallback_used=true only when eligible agent count = 0
    - tie_break fields required when tie_break_applied=true
```

### DomainStateEnvelope

Tenant/domain-scoped state boundary used by selected agent only.

```yaml
DomainStateEnvelope:
  state_id: uuid
  tenant_id: uuid
  domain: string
  owner_agent_id: string
  payload_ref: string
  version: integer
  updated_at: datetime

  Constraints:
    - (tenant_id, domain, state_id) unique
    - cross-domain access attempts must be denied and audited
```

### DispatchAuditEvent

Immutable append-only event capturing routing and containment evidence.

```yaml
DispatchAuditEvent:
  event_id: uuid
  dispatch_cycle_id: uuid
  event_type: enum {
    policy_denied,
    routing_selected,
    routing_fallback,
    tie_break_applied,
    agent_failure_contained,
    state_access_denied
  }
  message_id: string
  normalized_payload_hash: string
  tenant_id: uuid
  principal_id: uuid
  evaluated_agents: array<object>
  selected_agent_id: string?
  fallback_reason: string?
  tie_break_rationale: string?
  containment_result: enum {not_applicable, contained, failed_to_contain}
  error_category: string?
  error_message: string?
  correlation_id: string
  occurred_at: datetime

  Constraints:
    - event is immutable after insert
    - one or more audit events exist for every dispatch cycle
```

## Relationships

- `UnifiedMessage` (1) -> (1) dispatch cycle
- Dispatch cycle (1) -> (N) `AgentEligibilityResult`
- Dispatch cycle (1) -> (1) `AgentRoutingDecision`
- Dispatch cycle (1) -> (N) `DispatchAuditEvent`
- `DomainAgentRegistration` (1) -> (N) `AgentEligibilityResult`
- `AgentRoutingDecision.selected_agent_id` -> `DomainAgentRegistration.agent_id` (nullable when fallback)

## Deterministic Selection Rules (Model-Level)

1. Filter to eligible results only.
2. Sort by `priority` ascending.
3. For equal priority, sort by `agent_id` lexical ascending.
4. If required, apply secondary key `registration_timestamp` ascending.
5. Select first item as sole primary agent.
6. If no eligible results, route to fallback (`MemoryAgent`).

## Dispatch Cycle State Transitions

```yaml
DispatchCycleState:
  states: [received, gated, evaluated, selected, executing, completed, failed_contained]
  transitions:
    - received -> gated
    - gated -> evaluated
    - evaluated -> selected
    - selected -> executing
    - executing -> completed
    - executing -> failed_contained

  Rules:
    - If policy denial occurs in gated, cycle ends with audit event policy_denied.
    - If selected agent fails, transition to failed_contained and emit agent_failure_contained.
    - failed_contained must not trigger fallback in same cycle.
```

## Validation Rules

- Policy gating must be executed before any `AgentEligibilityResult` creation.
- At most one `selected_agent_id` per dispatch cycle.
- `fallback_used=true` only when eligible count is zero.
- Selected-agent failure cannot mutate decision into fallback within same cycle.
- Audit payload must include routing inputs, rationale, outcome, and correlation metadata.
