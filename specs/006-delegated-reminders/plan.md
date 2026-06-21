# Implementation Plan: Delegated Reminders (SB-006)

**Branch**: `specs-003-009-pipeline` | **Date**: 2026-06-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-delegated-reminders/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Enable users to request reminders for third parties with correct routing, recipient handling, consent, and delivery visibility. The system must classify delegated-reminder intents separately from personal reminders, resolve recipient identities using a three-tier model (known contact → phone-only → unknown), acquire explicit recipient opt-in before delivery, implement retry logic with bounded exponential backoff (max 5 attempts, 31s window), and provide sender-side failure notifications. Architecture adaptation includes SchedulingAgent delegated-intent routing, isolated SchedulingStateStore, Durable Functions orchestration for consent/retry/delivery, and PolicyDecisionSkill for consent enforcement.

## Technical Context

**Language/Version**: C# (.NET 10.0 LTS)

**Primary Dependencies**: 
- Orleans (live session state)
- Azure Durable Functions (long-running workflows, retry orchestration)
- Azure Storage (durable state)
- Azure Key Vault (secrets)
- Azure Cognitive Services (Speech, AI Extraction)

**Storage**: PostgreSQL + pgvector (messages, memory, audit logs, consent registry)

**Testing**: xUnit, Playwright (integration), TestContainers

**Target Platform**: Azure Functions (Durable Functions), Orleans runtime on App Service/Container Apps

**Project Type**: Cloud-native multi-agent orchestration platform

**Performance Goals**: 
- Recipient consent check: <1s
- Delivery initiation from due-time: <5s
- Failure notification to sender: <60s
- Retry backoff window: 31s total (1,2,4,8,16s)

**Constraints**:
- Tenant-scoped security (mandatory RLS on all data)
- Principal context required for all operations
- No fabricated recall (grounded in persisted evidence)
- Durable Functions for long workflows, Orleans for live sessions

**Scale/Scope**: Multi-tenant SaaS platform with WhatsApp integration; delegated reminders represent a secondary use case for reminder orchestration

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

✓ **Skill-First Execution**: Delegated reminders will be implemented as explicit skills (DelegatedReminderSkill, RecipientResolutionSkill, PolicyDecisionSkill) with stable input/output contracts and declared side effects (delivery, consent acquisition, audit events).

✓ **Tenant-Scoped Security**: All delegated reminder operations require tenant scope and principal context. Consent registry, delivery attempts, and audit logs enforce tenant RLS. No operations without context.

✓ **Grounded Memory**: Consent state persisted in consent registry with audit trail. Delivery attempts logged with correlation IDs. All recalls/failures traceable to persisted evidence.

✓ **Durable Session/Workflow Separation**: Live chat-based delegated reminder requests handled by Orleans. Long-running retry and delivery workflows delegated to Durable Functions orchestration. Clear boundary preservation.

✓ **Cost-Aware Intelligence**: Recipient resolution uses tiered approach (fast local lookup, then contact-capture flow). PolicyDecisionSkill tracks anti-spam metrics. Delivery routing chooses WhatsApp (primary) with fallback policy. Telemetry emitted for all significant operations.

**Status**: PASS — no constitution violations. All non-negotiable principles satisfied by design.

## Project Structure

### Documentation (this feature)

```text
specs/006-delegated-reminders/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── delegated-reminder-contract.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Aluki.Runtime.Abstractions/
│   ├── Orchestration/
│   │   ├── IAgentCoordinator.cs
│   │   └── DelegatedReminderOrchestrator.cs      # New
│   └── Skills/
│       ├── DelegatedReminderSkill.cs             # New
│       ├── RecipientResolutionSkill.cs           # New
│       └── ISkill.cs
│
├── Aluki.Runtime.Host/
│   ├── Services/
│   │   ├── DelegatedReminderService.cs           # New
│   │   └── ConsentRegistryService.cs             # New
│   └── Program.cs                                  # Update DI
│
└── Aluki.Runtime.Functions/
    ├── Functions/
    │   ├── DelegatedReminderOrchestrator.cs       # New Durable Function
    │   ├── RecipientResolutionActivity.cs         # New Activity
    │   ├── ConsentAcquisitionActivity.cs          # New Activity
    │   └── DeliveryActivity.cs                    # New Activity
    └── Program.cs                                  # Update

db/
├── migrations/
│   ├── 004_create_delegated_reminders.sql       # New
│   ├── 005_create_consent_registry.sql          # New
│   └── 006_create_delivery_attempts.sql         # New
└── schema/
    └── delegated-reminders-rls.sql              # New RLS policies

tests/
├── Aluki.Runtime.Tests/
│   ├── DelegatedReminder/
│   │   ├── DelegatedReminderSkillTests.cs       # New
│   │   ├── RecipientResolutionTests.cs          # New
│   │   └── ConsentRegistryTests.cs              # New
│   └── Integration/
│       └── DelegatedReminderFlowTests.cs        # New (Playwright)
```

**Structure Decision**: Multi-project .NET solution leveraging existing Orleans/Durable Functions baseline. Delegated reminder orchestration extends existing SchedulingAgent through dedicated skills and a new Durable Function orchestrator. Storage layer (PostgreSQL + Azure Storage) persists delegated state and audit logs. DI container in Program.cs registers new skills and services.

## Complexity Tracking

| Aspect | Justification | Trade-offs Considered |
|--------|---------------|----------------------|
| Separate DelegatedReminderOrchestrator (Durable Functions) | Long-running retry/delivery requires durable state and callback capability; live Orleans session cannot guarantee retry semantics across restarts. | Keeping all logic in Orleans would lose retry guarantees and require reimplementation of callback persistence. |
| Three-tier recipient resolution | Balances UX (fast lookup for known contacts) with coverage (manual identity entry for unknown recipients). | All-automated resolution would miss unknown recipients; all-manual would frustrate users with known contacts. |
| Explicit PolicyDecisionSkill dependency | Centralizes consent/anti-spam logic; enables auditing and policy changes without code redeploy. | Inline consent checks would couple domain logic to policy; harder to test and evolve. |

---

## Phase 0: Research & Clarification

**Objective**: Resolve all unknowns and confirm architectural patterns for delegated reminder orchestration.

### Research Topics

1. **Durable Functions Orchestration Patterns for Delegated Workflows**
   - Query: Best practices for managing long-running reminders with retry, cancellation, and state transitions in Azure Durable Functions
   - Output: Decision on orchestrator pattern, activity separation strategy, and correlation ID management
   - Reference: spec.md clarifications on retry windows (31s total), cancellation windows (30s before due-time), and delivery phase boundaries

2. **Three-Tier Recipient Resolution Implementation**
   - Query: Efficient strategies for contact lookup (local → contact-capture → consent-acquire) with fail-fast patterns
   - Output: Decision on contact data structures, phone/WhatsApp identity binding, and lookup cache strategy
   - Reference: spec.md requirement for direct routing (Tier 1), contact-capture flow (Tier 2), and manual identity entry (Tier 3)

3. **PostgreSQL + RLS for Multi-Tenant Consent & Audit**
   - Query: Row-level security policy design for delegated_reminders, consent_registry, and delivery_attempts tables; audit trail implementation
   - Output: RLS policy templates and audit event schema
   - Reference: constitution requirement for tenant-scoped security and grounded memory

4. **Anti-Spam Enforcement with Durable State**
   - Query: Patterns for tracking delegated reminders per sender per 24-hour rolling window with enforcement at PolicyDecisionSkill
   - Output: Decision on state store (in-memory vs. durable), TTL strategy, and escalation policy
   - Reference: spec.md FR-005 baseline of 10 reminders/24hrs

5. **WhatsApp Delivery with Bounded Retry & Failure Notifications**
   - Query: Existing WhatsApp channel integration; failure classification strategy (transient vs. permanent); sender notification routing
   - Output: Decision on failure taxonomy, notification template, and retry abort criteria
   - Reference: spec.md FR-007, FR-010 and clarifications on retry logic and failure categories

### Research Artifacts

See **[research.md](research.md)** for detailed findings, decisions, and alternatives considered.

---

## Phase 1: Design & Contracts

**Objective**: Produce data models, interface contracts, and quickstart validation guide.

### 1. Data Model Design

See **[data-model.md](data-model.md)** for entity definitions:

**Key Entities**:
- **DelegatedReminder**: Tenant-scoped record with sender/recipient identity, content, due-time, status, consent-acquired flag
- **RecipientContact**: Resolved identity binding (name, phone, WhatsApp handle) from recipient-resolution flow
- **ConsentRegistryEntry**: Persistent opt-in state per recipient per tenant with per-sender scope constraints
- **DeliveryAttempt**: Transactional record of delivery try (timestamp, retry count, failure reason, correlation ID)
- **AuditEvent**: Immutable record for lifecycle events (creation, resolution, consent-acquired, delivery-started, delivery-succeeded, delivery-failed-terminal, cancellation)

**RLS Policies**:
- `delegated_reminders` table: tenant_id + principal context
- `consent_registry` table: tenant_id + recipient_identity + principal context
- `delivery_attempts` table: tenant_id + delegated_reminder_id + audit trail

### 2. Interface Contracts

See **[contracts/delegated-reminder-contract.yaml](contracts/delegated-reminder-contract.yaml)** for formal API schemas:

**Contract Scope**:
- DelegatedReminderSkill input/output
- RecipientResolutionSkill input/output (3-tier request/response)
- PolicyDecisionSkill consent-check request/response
- Durable Function orchestrator activity payloads
- Delivery service failure classification

### 3. Quickstart Validation Guide

See **[quickstart.md](quickstart.md)** for end-to-end validation scenarios:

**Scenario 1**: Recipient in known contacts
- Prerequisites: Sender with contact list, recipient WhatsApp handle confirmed
- Setup: Initialize delegated reminder request
- Test: Verify recipient resolved directly, consent acquired, message delivered
- Expected: Delivery success within 5s of due-time

**Scenario 2**: Recipient known by phone only
- Prerequisites: Sender knows phone number, recipient not yet in contact list
- Setup: Initiate delegated reminder with phone number
- Test: Verify contact-capture flow triggered, consent dialog sent
- Expected: Recipient receives opt-in request, accepts, reminder delivered

**Scenario 3**: Unknown recipient
- Prerequisites: Sender doesn't know recipient details
- Setup: Initiate delegated reminder with manual identity entry
- Test: Verify identity collection dialog, consent registry check
- Expected: Full consent flow, successful delivery upon opt-in

**Scenario 4**: Delivery retry on transient failure
- Prerequisites: WhatsApp service temporarily unavailable
- Setup: Trigger delivery during outage
- Test: Verify retry backoff (1,2,4,8,16s), final attempt logged
- Expected: Retry completes within 31s window or fails with terminal failure notification to sender

**Scenario 5**: Cancellation before due-time
- Prerequisites: Delegated reminder scheduled, sender has <30s before due-time
- Setup: Send cancel request
- Test: Verify cancellation accepted or rejected based on timing
- Expected: If within 30s window, reminder cancelled; if delivery started, recall rejected

**Scenario 6**: Delivery failure & sender notification
- Prerequisites: Recipient delivery failed (permanent error)
- Setup: Terminal failure occurs after retry exhaustion
- Test: Verify failure classification, sender notification sent
- Expected: Failure notification in sender's WhatsApp with reason and recipient identity

### 4. Agent Context Update

After Phase 1 completes, update `.github/copilot-instructions.md` to reference this plan:

```markdown
<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/006-delegated-reminders/plan.md
<!-- SPECKIT END -->
```

---

## Next Steps

1. **Phase 0**: Execute research tasks and consolidate findings in research.md
2. **Phase 1**: Generate data-model.md, contracts/delegated-reminder-contract.yaml, and quickstart.md
3. **Phase 2** (not this command): Run `/speckit.tasks` to generate tasks.md with implementation breakdown
4. **Phase 3** (implementation): Run `/speckit.implement` to execute tasks with TDD workflow

---

**Status**: PLANNING IN PROGRESS

