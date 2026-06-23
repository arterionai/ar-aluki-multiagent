# Research: Mexican Invoice Request (Facturación CFDI)

**Date**: 2026-06-23 | **Feature**: SB-013 Mexican Invoice Request
**Source**: Domain investigation (CFDI 4.0 / SAT, merchant *autofacturación*
ecosystem) + spec design clarifications.

This document answers the user's explicit ask — *"investiga la mejor forma de
hacerlo"* — and records the decisions that shaped `spec.md`.

---

## 1. Domain primer: what a "factura" is in Mexico

A *factura* is a **CFDI** (Comprobante Fiscal Digital por Internet), currently
**version 4.0** (mandatory since 2022). It is an XML document **issued by the seller
(emisor)** and **stamped (timbrado) by a PAC** (Proveedor Autorizado de Certificación)
that validates it and registers it with the **SAT** (Servicio de Administración
Tributaria). The buyer (**receptor**) receives the XML plus a human-readable PDF.

A consumer who wants to deduct an expense must **request** the CFDI from the merchant
and supply their receptor fiscal data. Crucially, **the consumer does not issue the
CFDI** — they trigger the merchant to issue it. So this feature is a *requesting*
agent, not an *issuing* one (issuing/being a PAC is explicitly out of scope).

### Receptor data required by CFDI 4.0 (the user's "datos fiscales")

Every portal/channel asks for essentially the same receptor fields, and CFDI 4.0 made
them strict (mismatches now hard-fail):

| Field | Notes |
|---|---|
| **RFC** | Registro Federal de Contribuyentes — 12 (moral) or 13 (física) chars + checksum. |
| **Nombre / Razón social** | Must match the **Constancia de Situación Fiscal (CSF)** *exactly* (name, no régimen suffix like "S.A. de C.V." unless on the CSF). |
| **Código Postal** | Domicilio fiscal CP; must match the CSF. |
| **Régimen Fiscal** | From SAT catalog `c_RegimenFiscal` (e.g., 601, 605, 612, 626…). |
| **Uso del CFDI** | From SAT catalog `c_UsoCFDI` (e.g., G01 adquisición de mercancías, G03 gastos en general, S01 sin efectos fiscales). Must be compatible with the régimen. |
| **Email** | Where the merchant sends the XML/PDF. |

> **Design consequence**: we model these as a reusable, validated, encrypted **fiscal
> profile** (US1), validate against the SAT catalogs, and let users import them from a
> CSF via OCR (we already do receipt OCR in SB-004). Exact-match sensitivity is why
> CSF import + confirmation beats free-text entry.

**Sources**:
- [SAT — Servicio de facturación CFDI 4.0](https://www.sat.gob.mx/aplicacion/75169/servicio-de-facturacion-cfdi-version-4.0-(vigente-a-partir-del-1-de-enero-de-2022))
- [Guía práctica CFDI 4.0 (EdoMéx)](https://sfpya.edomexico.gob.mx/recaudacion/ReadFile.jsp?File=Guia_CFDI_V4_0.pdf)
- [Pluxee — Cómo facturar en el SAT (CFDI 4.0)](https://www.pluxee.mx/blog/como-facturar-en-el-sat-guia-actualizada/)

---

## 2. The core problem: the channel is fragmented per-merchant

The single most important finding: **there is no standard "request invoice" action.**
Each merchant runs its own *autofacturación* with its **own portal, its own field
names, its own ticket identifiers, its own validation rules, and its own time
window.** Research confirmed real, distinct flows for OXXO, OXXO Gas, Walmart, Costco,
Sam's Club, Liverpool, Starbucks, Office Depot, gas stations, restaurants (e.g., Toks),
pharmacies, hotels, and couriers — each different.

### The observed channel taxonomy ("todos los escenarios posibles")

| Strategy | How it works | Typical ticket inputs | Notes |
|---|---|---|---|
| **`self_service_portal`** | Merchant web form: enter ticket identifiers + receptor data, submit, download XML/PDF. **Dominant channel.** | folio / web ID, sucursal or serie, total, fecha, sometimes transaction/terminal ID | Field names vary wildly; some are plain HTTP form POSTs, others JS-heavy SPAs. |
| **`qr_deeplink`** | Ticket prints a QR that encodes the portal URL **with ticket params embedded**; scanning pre-fills the form. | usually all ticket params in the URL | Growing fast; removes hand-typing; still ends on a portal. |
| **`email_request`** | Send ticket photo + fiscal data to `facturacion@merchant`; merchant replies with the CFDI. | a photo of the ticket + the receptor fields in the body | Common for restaurants/SMB; **async** — reply may take hours/days. |
| **`aggregator_api`** | A third party (Facturapi, Fiscalapi, Prodigia, aggregator "ticket→factura" apps) abstracts many merchants behind one API/portal. | depends on aggregator | Lets one integration cover many merchants; coverage is partial. |
| **`sat_self_invoice`** | SAT's free CFDI generator (for cases where the buyer self-invoices). | full emisor+receptor data | Rare for ordinary retail consumers; mostly when the buyer is the emisor. |
| **`manual_in_person`** | Merchant requires visiting a physical desk / a specific site / a kiosk. | n/a | Fallback: the system **guides** the user; it cannot complete it. |

**Sources**:
- [Ticket Factura — cómo facturar OXXO](https://www.ticketfactura.com/facturacion-oxxo/) ·
  [Facturar Toks con foto del ticket](https://www.ticketfactura.com/facturar-toks/)
- [OXXO facturación electrónica (portal)](https://www4.oxxo.com:9443/facturacionElectronica-web/views/layout/inicio.do) ·
  [OXXO Gas facturación](https://facturacion.oxxogas.com/)
- [Alegra — autofacturación (QR + código de ticket)](https://ayuda.alegra.com/int/autofacturacion-mex) ·
  [Office Depot facturación paso a paso](https://www.facturacion-ticket.com.mx/office-depot-facturacion)
- Aggregators / APIs: [Facturapi](https://www.facturapi.io/blog/untangling-cfdi-3) ·
  [Fiscalapi](https://fiscalapi.com/) · [Prodigia API](https://www.prodigia.com.mx/api-facturacion) ·
  [MultiPAC.ai](https://multipac.ai/) · [Conectia PAC API](https://conectia.mx/proveedor_autorizado_certificacion_sat_api/)

### Cross-cutting realities that shaped the spec

- **Deadlines**: most merchants only allow invoicing within the **same calendar month**
  (some a few days). → FR-024/FR-027 deadline tracking + SB-005 reminders + `expired`.
- **Exact-match validation**: CFDI 4.0 rejects name/CP/régimen mismatches. → strict
  fiscal-profile validation + CSF import.
- **Ticket identifiers differ per merchant**: folio vs web ID vs serie/sucursal vs
  transaction ID. → a **field map** per recipe instead of one fixed schema.
- **QR contents vary**: sometimes a full deep-link, sometimes just an alphanumeric code
  printed under the QR. → decode QR *and* fall back to OCR of the code.

---

## 3. The central design question — how do we cover an unbounded merchant set *and* learn?

Three candidate strategies were evaluated.

### Option A — Hand-code one adapter per merchant
- **Pros**: most reliable per merchant; full control.
- **Cons**: there are **thousands** of merchants; portals change without notice;
  unbounded maintenance. Does not satisfy "vaya aprendiendo" or "agentes al vuelo".
- **Rejected** as the primary approach (kept only for a handful of highest-volume
  merchants as seed recipes, if desired).

### Option B — Rely solely on an aggregator/PAC API
- **Pros**: one integration; PACs are authorized and stable; APIs are clean.
- **Cons**: aggregators do **not** cover the long tail; many merchants are not
  reachable through any aggregator; still requires per-merchant onboarding on their
  side; cost. Coverage gap defeats "todos los escenarios posibles".
- **Decision**: **use as one strategy (`aggregator_api`) where coverage exists**, not
  as the whole solution.

### Option C — Learned, declarative **recipes** interpreted by on-the-fly execution agents *(CHOSEN)*
- A **recipe** is *data*: strategy + ordered steps + a field map (canonical → merchant
  fields) + confidence + lifecycle. It is versioned and auditable.
- An **execution agent** is spawned per request to *interpret* the recipe against an
  automation driver. For unknown merchants it runs in **discovery mode**: probe the
  portal/QR/contact, use the model-router to map fields and propose steps, attempt the
  flow under a confidence gate, and emit a **candidate recipe**.
- Outcomes feed **learning events** that raise/lower confidence; recipes get **promoted**
  to `active` (auto-run) or **demoted** on drift.

**Why C is the best way**:
- It is the only option that scales to the long tail *and* improves over time
  (matches "vaya aprendiendo cómo hacerlo").
- "Recipes as data, interpreted by an agent" is **safer and more auditable** than
  literally generating/executing arbitrary code at runtime: every action is one of a
  bounded vocabulary (navigate / read / fill / click / upload / submit / download /
  ask-user / wait-for-email), replayable, and reviewable. This is how we realize
  "generando agentes al vuelo que generen diferentes acciones" without the risks of
  free-form code generation.
- It composes cleanly with what Aluki already has (OCR, dispatch, consent, reminders,
  memory) instead of a parallel stack.

> **Rejected sub-alternative**: runtime *code* generation (LLM writes and executes a
> script per merchant). Rejected for safety/auditability — unbounded action surface,
> hard to review, hard to sandbox. The declarative-recipe interpreter gives the same
> flexibility with a bounded, inspectable action set.

---

## 4. Resolved clarifications

### C1: Recipe ownership — shared catalog vs tenant-private
**Decision**: Recipes and merchant records are **shared catalog knowledge** (no user
PII), like the SB-010 billing catalog (global, no RLS). Execution state, fiscal data,
ticket data, and captured documents are **tenant+user scoped with RLS**.
**Rationale**: a recipe for "how to invoice OXXO" is general operational knowledge;
sharing it is what makes learning compound across users. PII never lives in a recipe.
**Alternative considered**: tenant-private recipes — rejected as default (kills the
learning network effect); retained as an optional config toggle for customers that
demand strict isolation (flagged NEEDS CLARIFICATION in spec assumptions).

### C2: Automation driver — headless browser vs HTTP form
**Decision**: **Tiered.** Try an **HTTP-form driver** first (many *autofacturación*
portals are plain server-rendered form POSTs — cheap, fast, no browser). Fall back to
a **headless browser** (Playwright) only for JS-heavy SPA portals.
**Rationale**: cost-aware (constitution V); the HTTP-form path covers a large fraction
at a fraction of the cost/latency. Both sit behind one `IPortalAutomationDriver`.
**Host**: keep the Functions worker thin (enqueue + status); run the heavy headless
browser in a separate **Azure Container Apps job** invoked by the orchestrator.
**Alternative considered**: browser-only — rejected (needless cost/latency for the
common simple-form case).

### C3: Sync vs async orchestration
**Decision**: model the request as a durable, resumable workflow. **Target = Durable
Functions** (constitution IV — wait-heavy: portal latency, captcha handoff, email
replies, monthly deadlines). **Interim = a timer-sweep** (`InvoiceRequestSweepFunction`,
every minute) that advances pending requests, retries within budget, processes email
replies, and enforces deadlines — exactly the pattern SB-005/SB-006 already use.
**Rationale**: matches the repo's established "sweep now, Durable later" trajectory.

### C4: Human-in-the-loop & safety boundary
**Decision**: **never** solve/bypass captcha, OTP, or anti-bot; pause to
`awaiting_user_input` and ask the user (WhatsApp), offering a deep-link to finish
manually when possible. Per-merchant rate limiting and back-off on block responses. Act
only on the requesting user's own purchases.
**Rationale**: the system is the user's *authorized agent doing what the user could do
themselves*, with explicit consent — not a scraper, not a bulk automation tool. This
keeps us on the right side of merchant ToS and anti-abuse norms, and it is required by
the security posture.

### C5: Fiscal-data protection
**Decision**: encrypt fiscal-profile PII at rest with AES-256-GCM, reusing the calendar
token-protector pattern (`ICalendarTokenProtector` → a `IFiscalDataProtector`); never
log fiscal data; redact it from execution traces; wrap it in a value boundary
(mirroring `ProviderTokenBoundary`) so it cannot be accidentally serialized.
Consent-gated via SB-012; RLS tenant+user scoped.
**Rationale**: RFC + razón social + CP is sensitive PII; CFDI mismatches are
audit-sensitive. Reuse proven patterns (constitution II/III).

### C6: CFDI capture & validation
**Decision**: capture **both** XML and PDF; store binaries in Azure blob storage,
metadata in PostgreSQL. Validate: well-formed UUID/folio fiscal, receptor RFC == chosen
profile, emisor RFC == merchant, total == ticket (within tolerance). Failures →
`quarantined` (withheld from the completed surface), user warned, manual-review flagged
— **no fabrication** of a "success" (constitution III).
**Alternative considered**: trust the merchant output as-is — rejected (would surface
wrong-RFC or mismatched-total invoices as success).

### C7: Idempotency / dedupe
**Decision**: a **ticket fingerprint** = stable hash of (merchant RFC + folio/web ID +
total + fecha). Dedupe `invoice_request` on `(tenant, user, merchant, ticket
fingerprint)`; replays return the existing request/document. Mirrors the
`INSERT … ON CONFLICT … RETURNING (xmax=0)` idiom used across the repo.

### C8: Discovery provider
**Decision**: abstract web/RFC discovery behind `IMerchantDiscoveryProvider` (search
"{merchant} facturación", RFC→portal lookup, QR target probe). Stub initially; degrade
gracefully to user guidance (`manual_in_person`) when unavailable. Discovery is a
**data-gathering action**, not LLM inference, so it does not violate the Azure-only
inference directive; the *field-mapping/recipe-synthesis* step that consumes discovery
output uses the Azure Foundry model-router.

---

## 5. Technology stack research

### Inference (Azure-only, per constitution VI)
- **Merchant ID, field mapping, recipe synthesis/refinement, email composition**: Azure
  AI Foundry **model-router** via the existing `IChatModelRouter` (same path as SB-011
  semantic graph and SB-004 structured extraction).
- **Receipt + CSF OCR**: reuse SB-004 `FoundryReceiptOcrProvider` (Azure vision).
- **QR decode**: deterministic library decode (not inference); fall back to OCR of the
  printed alphanumeric code under the QR.

### Automation
- **HTTP-form driver**: `HttpClient`-based form discovery/POST (named client
  `invoice-portal`), with the same private-IP/SSRF guardrails proven in SB-009A link
  enrichment (block loopback/private ranges, timeouts).
- **Headless browser**: Playwright in an **Azure Container Apps job**, invoked by the
  orchestrator for SPA portals; returns redacted step results + downloaded artifacts.

### Document storage
- **Azure Blob Storage** for CFDI XML/PDF binaries (lifecycle-managed); **PostgreSQL**
  for metadata + references (consistent with SB-004 image-reference approach).

### SAT catalogs
- Bundle `c_RegimenFiscal`, `c_UsoCFDI`, `c_CodigoPostal` reference data for validation;
  refreshable. Used by FR-002.

### Aggregator/PAC APIs (the `aggregator_api` strategy)
- Where coverage exists, integrate via a `IInvoiceAggregatorProvider` (Facturapi /
  Fiscalapi / Prodigia-style). Pluggable; one provider can cover many merchants.

---

## 6. Security & compliance considerations

| Concern | Approach |
|---|---|
| Fiscal PII at rest | AES-256-GCM (`IFiscalDataProtector`), Key Vault key material. |
| PII in logs/traces | Hard redaction; value-type boundary prevents accidental serialization. |
| Consent | SB-012 `UseFiscalData` + `RequestInvoiceOnBehalf`; fail-closed; revocation honored mid-flight. |
| Tenant isolation | RLS on all request/profile/document/log tables; recipes are PII-free shared catalog. |
| Captcha / anti-bot | Never bypass; human handoff; per-merchant rate-limit + back-off. |
| SSRF / private targets | Block loopback/private IPs in the HTTP-form driver (reuse SB-009A policy). |
| Wrong-invoice prevention | CFDI validation gate (RFC/total/UUID); quarantine on mismatch. |
| Abuse prevention | Act only on the user's own purchases; per-user/merchant rate limits. |
| Audit | WORM execution log + decision log for every transition (constitution II). |

---

## 7. Known risks

| Risk | Mitigation |
|---|---|
| Portal layout drift breaks an active recipe | Auto-demote to `candidate` on repeated failure; re-discover; tell the user it may take longer. |
| Anti-bot blocks automation | Human handoff with deep-link; back-off; never evade. |
| OCR misreads ticket fields | Confidence tiers (SB-004); pause for the specific missing/ambiguous field; never guess the total. |
| Deadline missed | Proactive reminders (SB-005); `expired` state surfaced, never silent. |
| Email replies are slow/unstructured | `awaiting_merchant` + SLA follow-up; parse reply attachments with OCR/XML detection. |
| Cost of headless browser | HTTP-form first; browser only when required; recipe caching avoids repeat discovery. |
| Recipe poisoning across tenants (shared catalog) | Promotion thresholds + validation gate + provenance on learning events; optional tenant-private toggle. |
| Fiscal data leakage | Encryption + redaction + value boundary; 0-leak success criterion (SC-004). |

---

## 8. Phase 0 completion

All clarifications (C1–C8) resolved; channel taxonomy and "best way" recommendation
established (Option C — learned declarative recipes + on-the-fly execution agents).
No blocking unknowns remain for Phase 1 design; the single optional toggle
(tenant-private recipes) is flagged in spec assumptions.

---

## Sources

- [SAT — Servicio de facturación CFDI 4.0](https://www.sat.gob.mx/aplicacion/75169/servicio-de-facturacion-cfdi-version-4.0-(vigente-a-partir-del-1-de-enero-de-2022))
- [Guía práctica CFDI 4.0 (EdoMéx, PDF)](https://sfpya.edomexico.gob.mx/recaudacion/ReadFile.jsp?File=Guia_CFDI_V4_0.pdf)
- [Pluxee — Cómo facturar en el SAT 2026 (CFDI 4.0)](https://www.pluxee.mx/blog/como-facturar-en-el-sat-guia-actualizada/)
- [Ticket Factura — facturación OXXO](https://www.ticketfactura.com/facturacion-oxxo/) ·
  [Facturar Toks con foto del ticket](https://www.ticketfactura.com/facturar-toks/)
- [OXXO — portal de facturación electrónica](https://www4.oxxo.com:9443/facturacionElectronica-web/views/layout/inicio.do) ·
  [OXXO Gas — facturación](https://facturacion.oxxogas.com/)
- [Alegra — autofacturación (QR + código de ticket)](https://ayuda.alegra.com/int/autofacturacion-mex)
- [Office Depot — facturación paso a paso](https://www.facturacion-ticket.com.mx/office-depot-facturacion)
- [Facturapi — Untangling CFDI: ¿Qué es un PAC?](https://www.facturapi.io/blog/untangling-cfdi-3) ·
  [Fiscalapi](https://fiscalapi.com/) · [Prodigia — API de facturación](https://www.prodigia.com.mx/api-facturacion) ·
  [MultiPAC.ai](https://multipac.ai/) · [Conectia — PAC API](https://conectia.mx/proveedor_autorizado_certificacion_sat_api/)
