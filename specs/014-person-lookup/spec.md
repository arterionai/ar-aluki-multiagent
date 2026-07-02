# Feature Specification: Person Lookup — Contact-Card Recall

Feature ID: SB-014
Status: Draft
Date: 2026-07-02
Depends on: SB-013 (person notes), SB-002 (personal memory), SB-011 (semantic graph, optional enrichment)

## 1. Objective

Give "¿Quién es Fer?" a dedicated, deterministic answer path that returns ALL saved
notes about a person in a contact-card style reply — instead of routing through the
generic conversational recall pipeline.

### Why the generic path is not enough

Today "¿Quién es Fer?" reaches `ConversationalResponseAgent` (priority 100), which
calls `MemoryRecallService.RecallAsync`. That pipeline enforces a corroboration
policy (`CorroborationPolicy`: ≥2 relevant artifacts → grounded, exactly 1 →
`LowConfidence`). A user who saved exactly one note about Fer (the normal SB-013
case) gets the clarification message "Solo encontré una nota relacionada; ¿puedes
dar más detalle para confirmar?" instead of the note they explicitly saved.

The corroboration rule exists to prevent hallucinated synthesis from weak evidence.
But a person lookup is not a synthesis question — the user is asking "show me what I
told you about X". A single self-authored note IS the ground truth; withholding it
is wrong. SB-014 bypasses corroboration for this intent while keeping citations and
scope enforcement intact.

## 2. Architecture Adaptation

### Dispatch priority chain (updated)

| Priority | Agent | Project |
|----------|-------|---------|
| 40 | `SheloNabelDomainAgent` | SheloNabel |
| 50 | `CalendarDomainAgent` | Calendar |
| 55 | `PersonMemoryDomainAgent` (SB-013, save) | Memory |
| **58** | **`PersonLookupDomainAgent` (SB-014, read)** | **Memory** |
| 60 | `ReminderDomainAgent` | Reminders |
| 100 | `ConversationalResponseAgent` | Conversation |
| MaxValue | `MemoryDomainAgent` (catch-all) | Memory |

Priority 58: after the save path (55) so "guarda que…" is never mistaken for a
lookup, before reminders (60) and long before the conversational fallback (100).

### New components (all in `Aluki.Runtime.Memory`, next to SB-013)

- **`PersonLookupDetector`** (`Dispatch/PersonLookupDetector.cs`) — pure static,
  accent-insensitive (same `Normalize()` as `PersonNoteDetector`). Returns the
  extracted person name, not just a bool:
  - `TryExtractLookup(string? text, out string personName)`
  - Triggers (normalized prefix/contains): `"quien es "`, `"quienes son "`,
    `"que sabes de "`, `"que sabes sobre "`, `"who is "`,
    `"what do you know about "`.
  - Name = trailing text after the trigger, stripped of `?`/`¿`/punctuation.
    Empty name after stripping ⇒ no claim.
  - Guard: does NOT claim if the text also matches `PersonNoteDetector` triggers
    or `ReminderSchedulingDetector` (defense in depth — priorities already order this).

- **`PersonLookupDomainAgent`** (`Dispatch/PersonLookupDomainAgent.cs`) —
  `IDomainAgent`, `AgentId = "memory.person_lookup"`, `Priority = 58`, WhatsApp only
  (same channel guard as SB-013). `HandleAsync`:
  1. Embed the person name via `IEmbeddingClient` (standalone CTS, ~15 s — NOT the
     webhook ct; embedding is on the reply path).
  2. `MemoryStore.SearchAsync(principal, embedding, topK: 8, ct)` — reuse the
     existing pgvector path; filter `Distance <= RelevanceMaxDistance` (0.6, from
     `MemoryOptions`). **No corroboration minimum** — 1 relevant artifact is enough.
  3. Rank by distance; keep up to 5 notes.
  4. Reply (contact card):
     ```
     📇 *{Nombre}*
     • {nota 1}
     • {nota 2}
     ```
     Notes truncated at 150 chars each. No LLM on this path — deterministic
     formatting only (same philosophy as SB-013's save confirmation).
  5. Zero matches ⇒ reply
     `"No tengo notas sobre *{Nombre}*. Dime \"guarda que {Nombre}…\" y lo anoto 📒"`
     and `OutcomeCode = "person_lookup_no_notes"`.
  6. Write the existing recall audit (`MemoryStore.WriteRecallAuditAsync`,
     `CancellationToken.None` — WORM discipline) with decision
     `person_lookup_direct` so lookups remain auditable like any recall.
  7. Send via `IWhatsAppMessenger.SendTextMessageAsync(CancellationToken.None)`;
     persist outbound via `IOutboundMessageStore` (both per the CancellationToken
     discipline rule).
  8. `OutcomeCode = "person_lookup_answered"`.

- **Registration**: `MemoryServiceCollectionExtensions.AddPersonalMemory()` —
  same double-registration pattern as `PersonMemoryDomainAgent`.

### Semantic graph enrichment (P2, optional — NOT in v1)

`ISemanticGraphRepository.FindByAliasAsync` (SB-011) gives exact alias resolution
and structured relationships (worksAt/owns/…) that could enrich the card
("Fer — trabaja en TechCorp"). The interfaces live in Abstractions and the impls are
registered in the Functions host (`AddSemanticGraph`), so a Memory-project agent CAN
take an optional dependency. Deferred because (a) nothing in the WhatsApp pipeline
populates the graph yet (see SB-015 candidate below), so the tables are empty for
real users; (b) v1 value is fully delivered by the notes themselves.

## 3. In Scope

- US1: Ask "¿Quién es {nombre}?" and receive every relevant saved note (≥1) as a
  contact card, without the low-confidence clarification loop.
- US2: Ask about an unknown person and receive a helpful "no notes + how to save
  one" reply instead of a generic LLM answer.
- US3: Lookup queries never fall through to the reminder agent or trigger the
  no-memory suffix logic of the conversational agent.

## 4. Out of Scope

- Editing/deleting notes ("borra lo de Fer") — separate spec candidate.
- Relationship queries ("¿quién conoce a Fer?") — needs SB-011 graph population.
- Cross-person disambiguation ("hay dos Fer, ¿cuál?") — v2 once real usage shows it.
- Non-WhatsApp channels.

## 5. User Stories

### US1 — Contact-card lookup (P1)
Given the user previously saved "guarda que Fer Amaro la conocí en galería afuera de
la casa de bluey", when they send "¿Quién es Fer?", then the reply is
"📇 *Fer*\n• Fer Amaro la conocí en galería afuera de la casa de bluey" — even
though only ONE artifact exists (no corroboration gate).

### US2 — Unknown person (P1)
Given no notes mention "Marcos", when the user sends "¿Quién es Marcos?", then the
reply is the no-notes message with the save hint, and no LLM call is made.

### US3 — No misrouting (P1)
"¿Quién es Fer?" must never produce a reminder-time question nor the
`NoMemoryMessageSuffix` of the conversational agent; `PersonLookupDomainAgent` claims
it at priority 58.

## 6. Acceptance Criteria

- `PersonLookupDetector` unit tests ≥10 cases: es/en triggers, accents, mixed case,
  name extraction (punctuation stripped), empty-name rejection, non-lookup text,
  null/empty/whitespace, no-claim when a save trigger is present.
- `PersonLookupDomainAgent` contract tests: AgentId/Priority, ClaimsIntent ±,
  single-artifact answer (the key regression vs `CorroborationPolicy`), zero-match
  reply, truncation, WhatsApp-only guard, recall audit written.
- All sends/audits use `CancellationToken.None`; embedding uses standalone CTS.
- No new migration — reads `memory_artifact` via existing `MemoryStore.SearchAsync`.
- No LLM call anywhere on this path.

## 7. Clarifications

### Why no LLM

The card lists the user's own notes verbatim. Synthesis adds latency, cost, and a
hallucination surface with zero information gain for this intent. Grounded synthesis
remains available through the generic path for open questions ("¿dónde conocí a
Fer?"), which does not match the lookup triggers.

### Interaction with the corroboration policy

`CorroborationPolicy` is untouched — it still governs `MemoryRecallService` for the
conversational agent. SB-014 reads `MemoryStore.SearchAsync` directly, so the bypass
is scoped to the explicit lookup intent only.

### Distance threshold

Reuses `MemoryOptions.RelevanceMaxDistance` (0.6). Because the query is just the
name (short text), embeddings of notes containing the name typically land well
inside this radius; the threshold guard prevents returning unrelated notes for
names never mentioned.
