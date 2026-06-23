# Implementation Plan: Mexican Invoice Request (Facturación CFDI)

**Branch**: `claude/mexico-invoice-request-spec-yzpaut` | **Date**: 2026-06-23
| **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-mexico-invoice-request/spec.md`

## Summary

Let a WhatsApp user obtain a CFDI 4.0 *factura* for their own purchase by forwarding a
receipt or saying *"factúrame …"*. The runtime identifies the merchant, resolves an
invoicing **strategy** (portal / QR deep-link / email / aggregator / SAT / manual),
selects or **learns** a declarative **recipe**, executes it via a recipe-interpreting
**on-the-fly agent** (HTTP-form driver first, headless browser when needed), pauses for
human-verification gates, then captures, validates, stores, and confirms the CFDI. The
user's receptor **fiscal profile** is stored encrypted and consent-gated. The approach
composes existing Aluki blocks (OCR, dispatch, consent, reminders, memory) and follows
the established "timer-sweep now, Durable Functions later" trajectory.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), isolated-worker Azure Functions.

**Primary Dependencies**: `IChatModelRouter` (Azure AI Foundry model-router) for
merchant ID / field-mapping / recipe synthesis; SB-004 `FoundryReceiptOcrProvider`
(Azure vision) for receipt + CSF OCR; Npgsql + PostgreSQL (pgvector substrate);
`HttpClient` (named `invoice-portal`) for the HTTP-form driver; Playwright (Azure
Container Apps job) for the headless driver; Azure Blob Storage for CFDI binaries;
Key Vault for encryption keys.

**Storage**: PostgreSQL (migration `024_mexico_invoice_request.sql`) + Azure Blob
Storage for XML/PDF. Shared catalog tables (no RLS) for merchants/recipes; tenant+user
RLS for profiles/requests/steps/documents/learning/discovery.

**Testing**: xUnit by category trait (`Unit`, `Contract`, `Integration`). Integration
tests gated on `ALUKI_TEST_POSTGRES`. Pure components (intent detector, RFC/CFDI
validators, ticket fingerprint, recipe interpreter step-resolution, deadline math) are
unit-tested without I/O.

**Target Platform**: Azure Functions isolated worker (deployed unit) + a companion
Azure Container Apps job for headless browsing. Host (ASP.NET Core) for local dev.

**Project Type**: Backend service / multi-agent runtime (new project
`Aluki.Runtime.Invoicing`, mirroring `Memory`/`Extraction`/`Reminders`).

**Performance Goals**: conversational ack non-blocking (≤ 2s P95, constitution IV/V);
known-recipe HTTP-form request completes within seconds of receiving all fields;
headless/email/aggregator flows are async with explicit state transitions.

**Constraints**: Azure-only inference; fiscal PII encrypted + never logged; no
captcha/anti-bot bypass; per-merchant rate limits; SSRF guard on the HTTP-form driver;
idempotent per ticket.

**Scale/Scope**: long-tail merchant universe (thousands) handled by learned recipes,
not per-merchant code; per-user multiple fiscal profiles; monthly deadline pressure.

## Constitution Check

*GATE: must pass before Phase 0; re-checked after Phase 1 design.*

- **I. Skill-First**: PASS — explicit skills (`FiscalProfileSkill`,
  `InvoiceRequestSkill`, `RecipeResolutionSkill`, `InvoiceExecutionSkill`,
  `CfdiCaptureSkill`) with schemas, side effects, idempotency keys, telemetry, auth
  scope. Agents route only.
- **II. Tenant-Scoped Security**: PASS — RLS on all PII/state tables; recipes are
  PII-free shared catalog; principal context required pre-side-effect; WORM audit.
- **III. Grounded / No Fabrication**: PASS — OCR confidence tiers; clarify on missing
  fields; CFDI must validate before being surfaced; quarantine on mismatch.
- **IV. Durable vs Session**: PASS — target Durable Functions for the wait-heavy flow;
  interim `InvoiceRequestSweepFunction` matches SB-005/006; conversation stays
  non-blocking.
- **V. Cost-Aware & Observable**: PASS — HTTP-form before headless; recipe caching
  avoids repeat discovery; per-execution telemetry.
- **VI. Azure Baseline**: PASS — Foundry/Azure OpenAI inference; isolated worker; Key
  Vault; blob storage; OIDC deploy.

**Gate Result**: PASS — no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/013-mexico-invoice-request/
├── plan.md            # This file
├── spec.md            # Feature specification
├── research.md        # Domain investigation + decisions (C1–C8)
├── data-model.md      # Tables / RLS / migration 024
├── quickstart.md      # End-to-end validation scenarios
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
src/Aluki.Runtime.Abstractions/Invoicing/
├── FiscalProfile.cs, FiscalProfileRequest.cs
├── InvoiceRequest.cs, InvoiceRequestStatus.cs        # state machine enum
├── MerchantRecipe.cs, RecipeStep.cs, RecipeFieldMap.cs
├── InvoiceStrategy.cs                                # enum of channels
├── IFiscalProfileService.cs, IInvoiceRequestService.cs
├── IRecipeResolver.cs, IRecipeRepository.cs
├── IPortalAutomationDriver.cs                        # navigate/fill/click/submit/download
├── IMerchantDiscoveryProvider.cs                     # web/RFC/QR discovery (stub)
├── IInvoiceAggregatorProvider.cs                     # aggregator_api strategy
├── IFiscalDataProtector.cs                           # AES-256-GCM (calendar pattern)
└── ICfdiValidator.cs, CfdiDocument.cs

src/Aluki.Runtime.Invoicing/                          # new project (mirrors Reminders)
├── InvoiceRepository.cs, RecipeRepository.cs
├── FiscalProfileService.cs, InvoiceRequestService.cs
├── RecipeResolver.cs                                 # confidence/promote/demote
├── Execution/InvoiceExecutionAgent.cs                # recipe interpreter
├── Execution/HttpFormAutomationDriver.cs
├── Execution/LoggingPortalAutomationDriver.cs        # stub/fallback
├── Discovery/StubMerchantDiscoveryProvider.cs
├── Learning/RecipeLearningService.cs                 # apply_invoice_learning
├── Validation/RfcValidator.cs, CfdiValidator.cs, SatCatalogs.cs
├── TicketFingerprint.cs
├── Dispatch/InvoiceRequestDomainAgent.cs             # IDomainAgent (priority 40)
├── Dispatch/InvoiceRequestIntentDetector.cs          # accent-insensitive es/en
├── FiscalDataProtector.cs
└── AddInvoicing.cs                                    # DI registration

src/Aluki.Runtime.Functions/Invoicing/
├── InvoiceFunctions.cs                                # HTTP triggers
└── InvoiceRequestSweepFunction.cs                     # timer, every minute

src/Aluki.Runtime.Host/Skills/Invoicing/               # minimal-API parity for local dev
└── InvoiceEndpoints.cs

db/migrations/024_mexico_invoice_request.sql

tests/unit/Invoicing/        # detector, RFC/CFDI validators, fingerprint, interpreter, deadline
tests/contract/Invoicing/    # HTTP contract shapes
tests/integration/Invoicing/ # RLS, idempotency, sweep claim, learning promote/demote
```

**Structure Decision**: New shared library `Aluki.Runtime.Invoicing` (namespace
`Aluki.Runtime.Invoicing.*`) mirroring `Memory`/`Extraction`/`Reminders`, registered by
`AddInvoicing()`. Both `Host` (local dev endpoints) and the deployed `Functions` worker
reference it (the deployed worker also exposes the HTTP triggers + the sweep). The
headless browser runs out-of-process in an Azure Container Apps job invoked by the
execution agent so the Functions worker stays thin.

## Dispatch integration (SB-009B)

`InvoiceRequestDomainAgent : IDomainAgent`, **priority 40** (ahead of
CalendarDomainAgent 50, ReminderDomainAgent 60, ConversationalResponseAgent 100,
MemoryDomainAgent int.MaxValue). Invoice intent ("factura/factúrame") is specific and
unambiguous, so ranking it high is safe. `ClaimsIntent` uses a deterministic,
accent-insensitive `InvoiceRequestIntentDetector` (es: "factura", "factúrame",
"facturar", "necesito factura/mi factura"; en: "invoice this", "get me an invoice")
**plus** the receipt-image path (a captured receipt artifact + an invoice verb).
`HandleAsync` (singleton agent → resolves scoped services via `IServiceScopeFactory`,
matching CalendarDomainAgent) creates/advances an `invoice_request` and replies over
WhatsApp (`IWhatsAppMessenger.SendTextMessageAsync`).

## HTTP endpoints (Functions)

- `POST api/invoicing/fiscal-profiles` · `GET api/invoicing/fiscal-profiles` ·
  `PUT api/invoicing/fiscal-profiles/{id}` · `POST api/invoicing/fiscal-profiles/import-csf`
- `POST api/invoicing/requests` · `GET api/invoicing/requests/{id}` ·
  `POST api/invoicing/requests/{id}/input` (human-in-loop) ·
  `POST api/invoicing/requests/{id}/cancel` ·
  `GET api/invoicing/requests/{id}/document` (CFDI XML/PDF download)
- Catalog/admin: `GET/POST api/invoicing/merchants` ·
  `GET/POST api/invoicing/recipes` · `POST api/invoicing/recipes/{id}/promote|deprecate`

## Config section `Invoicing:*`

- `Invoicing:FiscalDataEncryptionKey` (base64 32-byte AES key; falls back to
  `Calendar:TokenEncryptionKey`)
- `Invoicing:Automation:Driver` = `http_form|headless` (default `http_form`)
- `Invoicing:Automation:HeadlessJobEndpoint` (Container Apps job)
- `Invoicing:Discovery:Provider` (+ timeouts; stub by default)
- `Invoicing:Aggregator:{Enabled,Provider,Endpoint,ApiKey}`
- `Invoicing:Recipe:MinConfidenceForAutoRun` (default 0.80),
  `Invoicing:Recipe:PromoteAfterSuccesses` (default 3),
  `Invoicing:Recipe:DemoteAfterFailures` (default 2)
- `Invoicing:Deadline:DefaultWindowDays` (default 30),
  `Invoicing:Deadline:ReminderLeadDays` (default 3)
- `Invoicing:RateLimitPerMerchantPerMinute` (default 6)
- `Invoicing:Storage:DocumentsContainer` (blob container for CFDI XML/PDF)

## Inference (Azure-only)

- Merchant identification, portal field-mapping, recipe synthesis/refinement, email
  composition → Azure AI Foundry model-router (`IChatModelRouter`).
- Receipt + CSF OCR → SB-004 `FoundryReceiptOcrProvider`.
- QR decode is deterministic (library), not inference.

## Phasing

1. **Phase A — Fiscal profile + consent (US1, P1)**: migration 024, `fiscal_profiles`,
   `IFiscalDataProtector`, RFC/CP/régimen/uso validation, SB-012 consent, CSF import.
2. **Phase B — Known-recipe portal flow (US2, P1)**: merchant ID, recipe resolution,
   `InvoiceExecutionAgent` + `HttpFormAutomationDriver`, CFDI capture + validation +
   storage, dispatch agent, WhatsApp confirmation, idempotency.
3. **Phase C — QR + discovery/learning (US3/US4, P2)**: QR decode + deep-link mapping;
   discovery provider; candidate recipe synthesis; supervised execution; learning
   events + promote/demote; headless driver (Container Apps job) for SPA portals.
4. **Phase D — Email + human-in-loop + deadlines (US5/US6, P2/P3)**: email-request
   strategy + reply capture; `awaiting_user_input` resume; sweep + Durable target;
   deadline reminders (SB-005).
5. **Phase E — Aggregator strategy + retrieval polish**: `IInvoiceAggregatorProvider`;
   invoice list/download UX; admin recipe management.

## Complexity Tracking

No constitution violations → no entries.
