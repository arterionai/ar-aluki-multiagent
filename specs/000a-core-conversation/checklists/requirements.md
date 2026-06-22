# Requirements Checklist: Core Conversational Response (SB-000)

## Functional Requirements

| ID | Requirement | Evidence |
|----|-------------|----------|
| FR-001 | Every inbound text message produces exactly one outbound reply | |
| FR-002 | Replies are grounded in memory recall and/or conversation history | |
| FR-003 | When recall finds nothing, reply states so explicitly — no hallucination | |
| FR-004 | Audio messages receive an immediate acknowledgment reply | |
| FR-005 | LLM failure produces a friendly error message, not silence | |
| FR-006 | Duplicate inbound delivery never produces a duplicate outbound message | |
| FR-007 | `outbound_messages` records every reply with status and correlation metadata | |
| FR-008 | LLM is invoked with a grounding system prompt that prohibits fabrication | |
| FR-009 | Reply latency is under 10 seconds at p95 under normal load | |
| FR-010 | `ConversationalResponseAgent` and `MemoryDomainAgent` are independent | |

## Security Requirements

| ID | Requirement | Evidence |
|----|-------------|----------|
| SC-001 | `outbound_messages` RLS enforces tenant isolation | |
| SC-002 | Context assembly never crosses tenant or user boundaries | |
| SC-003 | No PII logged beyond structured audit events | |
