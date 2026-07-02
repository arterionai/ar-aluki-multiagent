# Feature Specification: Semantic Graph Ingestion Glue

Feature ID: SB-015
Status: Draft
Date: 2026-07-02
Depends on: SB-011 (semantic graph), SB-013 (person notes)

## 1. Objective

Populate the semantic graph automatically from the WhatsApp pipeline. SB-011
delivered entity resolution, traversal and merge — but nothing calls
`IEntityResolutionService.ResolveAsync` outside the manual HTTP endpoints, so
`semantic_entities`/`semantic_relationships` stay empty for real users and every
graph-dependent capability (relationship queries, SB-014 card enrichment) is inert.

## 2. Architecture Adaptation

- **Trigger point**: `PersonMemoryDomainAgent.HandleAsync` (SB-013). After the note
  is ingested into `memory_artifact`, entity resolution runs **fire-and-forget**
  (`Task.Run`) — never on the reply path, never blocking the WhatsApp confirmation.
- **Optional dependency**: the agent takes `IServiceScopeFactory` and resolves
  `IEntityResolutionService` (Abstractions interface) from a fresh scope with
  `GetService` (nullable). Hosts that don't call `AddSemanticGraph()` simply skip —
  no hard reference from `Aluki.Runtime.Memory` to `Aluki.Runtime.Host` impls.
- **CancellationToken discipline**: the background resolution uses its own
  `CancellationTokenSource` (60 s) — never the webhook ct, which is long dead by the
  time the LLM extraction finishes.
- **Request**: `ResolveEntitiesRequest(TenantId, Text: noteText, FactIds: null,
  CorrelationId)` — fact linkage is a follow-up (requires the ingestion sink to
  surface the created `memory_artifact_id`).
- Failures are logged (`LogWarning`) and swallowed — graph population is an
  enrichment, not a correctness requirement for note capture.

## 3. In Scope

- US1: Saving a person note ("guarda que Fer trabaja en TechCorp") creates/updates
  the corresponding `semantic_entities` (+aliases) and `semantic_relationships`
  rows without any extra user action.
- US2: A host without semantic graph registration (unit/contract test hosts)
  saves notes exactly as before — no exception, no behavioral change.

## 4. Out of Scope

- Linking resolved entities to the created memory artifact (`semantic_entity_facts`)
  — needs `IMemoryIngestionSink` to return the artifact ID (today it returns void).
- Entity resolution for the generic capture path (`MemoryDomainAgent` catch-all) —
  volume/cost decision to take separately (every WhatsApp message would hit the LLM).
- Graph-backed enrichment of the SB-014 contact card (marked P2 in that spec).

## 5. Acceptance Criteria

- Person note saves trigger exactly one `ResolveAsync` call with the note text and
  tenant; the WhatsApp confirmation is NOT delayed by it.
- With no `IEntityResolutionService` registered, `HandleAsync` behaves exactly as
  SB-013 shipped (contract-tested).
- Resolution errors never surface to the dispatcher (logged and swallowed).
- No new migration — writes go through the existing SB-011 repository.
