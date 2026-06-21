# Quickstart: Domain Agents Runtime Validation

This guide validates deterministic routing behavior for domain agents, including precedence, tie-break, fallback constraints, and failure containment with immutable audit evidence.

## Prerequisites

- .NET 10 SDK installed
- PostgreSQL reachable with tenant-scoped RLS enabled
- Runtime projects build successfully:
  - `src/Aluki.Runtime.Abstractions`
  - `src/Aluki.Runtime.Host`
  - `src/Aluki.Runtime.Functions`
- Test tenant/principal contexts available
- At least three test agents registered with controlled priorities and IDs

## Setup

1. Build solution:

```powershell
dotnet build Aluki.Runtime.slnx
```

2. Start host runtime:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

3. Start functions runtime (separate terminal):

```powershell
dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
```

4. Ensure agent registry test fixtures:
- `AgentA` priority 10, registration `t1`
- `AgentB` priority 10, registration `t2`
- `AgentC` priority 20, registration `t3`
- `MemoryAgent` enabled as fallback

## Scenario 1: Precedence Enforcement

Objective: verify required sequence `policy gating -> eligibility -> selection -> fallback`.

1. Submit message with valid tenant/principal/consent context.
2. Inspect emitted audit events and routing decision.

Expected result:
- Policy gate event/check exists before eligibility results.
- Selection occurs from eligible agents.
- Fallback is not used when eligible agents exist.

## Scenario 2: Deterministic Tie-Break

Objective: verify equal-priority deterministic selection.

1. Configure `AgentA` and `AgentB` both eligible at priority 10.
2. Replay identical normalized message/context 20+ times.

Expected result:
- Selection is stable across all runs.
- Winner is lexical minimum by canonical `agent_id`.
- Tie-break audit includes rationale and keys.

## Scenario 3: Secondary Tie-Break by Registration Timestamp

Objective: verify secondary ordering when canonical IDs are equivalent in configured tie tests.

1. Execute synthetic tie fixture requiring secondary key evaluation.
2. Replay same input.

Expected result:
- Agent with earliest registration timestamp is selected.
- Audit event includes timestamp-based rationale.

## Scenario 4: Fallback Only on Zero Eligible

Objective: verify fallback policy boundary.

1. Submit message that no registered domain agent can claim.
2. Confirm fallback route.

Expected result:
- `MemoryAgent` selected as fallback.
- Audit includes explicit reason `no_eligible_agent`.
- No fallback occurs in cases where any eligible agent exists.

## Scenario 5: Failure Containment (No Fallback Masking)

Objective: verify selected-agent failures are contained and not converted to fallback.

1. Force selected agent to throw deterministic failure during handling.
2. Observe dispatch cycle result and follow with a normal message.

Expected result:
- Cycle terminates with containment outcome.
- Audit contains `agent_failure_contained` with correlation metadata.
- Same-cycle fallback is not invoked.
- Next valid message dispatches normally.

## Scenario 6: Policy Denial Before Eligibility

Objective: verify missing/invalid principal or consent blocks dispatch pre-evaluation.

1. Submit message without required consent/principal context.
2. Query audit stream for cycle.

Expected result:
- Dispatch blocked at policy gate.
- No eligibility evaluation for domain agents.
- Immutable denial audit record exists.

## Scenario 7: Cross-Domain State Access Denial

Objective: verify domain isolation and security auditing.

1. Trigger agent to attempt access outside its domain state boundary.
2. Observe outcome.

Expected result:
- Access denied.
- `state_access_denied` audit event recorded with tenant/principal correlation.

## Evidence Checklist

For each dispatch cycle verify existence of immutable audit evidence containing:
- Message identity and normalization fingerprint
- Tenant/principal context
- Evaluated agents and outcomes
- Selection/fallback decision and rationale
- Tie-break rationale when applicable
- Containment outcome when failure occurs
- Correlation ID and timestamp

## References

- Spec: `specs/009-domain-agents/spec.md`
- Plan: `specs/009-domain-agents/plan.md`
- Research: `specs/009-domain-agents/research.md`
- Data model: `specs/009-domain-agents/data-model.md`
- Contract: `specs/009-domain-agents/contracts/domain-agent-routing-contract.yaml`
