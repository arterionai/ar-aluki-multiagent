# Aluki Runtime — Project Memory

Persistent notes for working in this repo. Keep this file current; do not remove
documented intended behaviors without explicit instruction.

## Solution layout

- `Aluki.Runtime.slnx` — projects: `Abstractions`, `Capture`, `Memory`,
  `Functions` (Azure Functions isolated worker, the deployed unit), `Host`
  (ASP.NET Core, not deployed), plus `tests/{unit,contract,integration}`.
- .NET 10 (`net10.0`), C#. Local SDK at `/tmp/dotnet`.
- Build: `dotnet build Aluki.Runtime.slnx -c Release`.
- Tests by category trait: `Unit`, `Contract`, `Integration`. Integration tests
  need `ALUKI_TEST_POSTGRES` (a pgvector-capable Postgres) or they self-skip.

## Delivery state (spec-kit `specs/00X-*`, strict order)

- **SB-001 WhatsApp Capture** — done & deployed. Meta webhook → capture pipeline.
- **SB-002 Personal Memory & grounded recall** — done & deployed.
  - US1 note capture, US2 grounded semantic recall (pgvector + corroboration ≥2 +
    citations), US3 topic grouping + cross-channel continuity.
  - **WhatsApp→memory bridge**: captured messages are promoted to recall-able
    `memory_artifact` via `IMemoryIngestionSink` (best-effort, off the ack path).
- **SB-003 Calendar Integration** — done (merged to `main`, commit `354e612`;
  not separately tracked in this section before). Google + Outlook providers,
  connect/disconnect/create skills, callback security, `008_calendar_integration.sql`.
- **SB-004 AI Extraction** — US1/US2/US3 done (not yet deployed). Project
  `Aluki.Runtime.Extraction` (mirrors `Memory`). Migration `009_ai_extraction.sql`
  (jobs/results/fields/audit + tenant RLS). US1 (audio→transcription+structured
  facts), US2 (text→summary/actions/decisions/entities), and **US3 receipt OCR
  (image)** all done. Durable/async orchestration is a follow-up: processing runs
  inline today, the status endpoint reflects the persisted lifecycle.
  - HTTP (Functions): `POST api/extraction/execute`, `GET api/extraction/jobs/{jobId}`.
  - Confidence tiers (per field): High ≥0.85, Medium 0.70–0.84 (flagged), Low
    <0.70 (persisted but withheld from the surfaced set — no fabrication).
  - Inference (Azure-only): transcription via Azure OpenAI Whisper
    (`Extraction:Transcription:Endpoint/ApiKey/Deployment`, falls back to
    `AiExtraction:*`); structured extraction via Foundry model-router
    (`IChatModelRouter`).
  - **US3 receipt OCR**: Azure vision OCR (`FoundryReceiptOcrProvider`) extracts
    vendor/total/subtotal/tax/date + Mexican RFC. Clarified fallback chain:
    structured OCR → text-only OCR (recovered fields capped at medium, warning
    `ocr_fallback_used`) → unreadable ⇒ job `failed`/`ocr_failed_all` plus a
    `manual_review_flagged` audit (no fabrication). RFC/amount/date validated in
    `ReceiptNormalization`; present-but-invalid values are persisted-for-review but
    withheld from the surfaced set. Config `Extraction:ReceiptOcr:Endpoint/ApiKey/
    Deployment`, falling back to `Foundry:Endpoint/ApiKey` +
    `Foundry:VisionDeployment`/`ChatDeployment`.
- **SB-005 Scheduled Reminders** — done (US1 deployed; US2 + retry pending merge).
  Project `Aluki.Runtime.Reminders` (mirrors `Memory`/`Extraction`). Migrations
  `010_reminders.sql` (reminders/recurrence_rules/delivery_attempts/audit/quotas +
  tenant RLS + SECURITY DEFINER `app.claim_due_reminders`) and
  `011_reminder_retries.sql` (`delivery_attempt_count` + `next_retry_utc`; claim
  also harvests due retries). One-shot + **recurring** (daily/weekly/monthly)
  create/list/snooze/cancel + creation-time quota enforcement + lifecycle audit.
  **Scheduling = timer-sweep** (`ReminderSweepFunction`, every minute) not Durable
  Functions (follow-up); cross-tenant claim via `app.claim_due_reminders`
  (SKIP LOCKED). **Delivery = pluggable `IReminderDeliveryChannel`** with a
  logging/persisting stub (`LoggingReminderDeliveryChannel`) until a real outbound
  channel exists. **Recurrence**: DST-safe `ReminderRecurrenceCalculator` (IANA tz,
  local time held across DST, day-31→last-day, `until_date` end); the sweep re-arms
  recurring reminders to the next occurrence after delivery. **Retry**: transient
  failures retry with backoff (`ReminderRetryPolicy` 5s/25s/125s) up to 3 attempts,
  then terminal `delivery_failed` (sub-minute backoff rounds to the sweep tick —
  Durable-Functions follow-up). HTTP (Functions): `POST/GET api/reminders`,
  `POST api/reminders/{id}/snooze`, `DELETE api/reminders/{id}`. Config `Reminders:*`.
- **SB-006 Delegated Reminders** — done (not yet deployed). Project
  `Aluki.Runtime.DelegatedReminders` (mirrors SB-005). Migration
  `012_delegated_reminders.sql` (delegated_reminders/delegated_recipient_contact/
  delegated_consent_registry/delegated_delivery_attempt/delegated_audit_event +
  tenant RLS + SECURITY DEFINER `app.claim_due_delegated_reminders`). Sender→
  recipient reminders with 3-tier recipient resolution (Tier1=known WhatsApp handle,
  Tier2=phone-only, Tier3=unknown→awaiting_consent). **Anti-spam**: 10/day rolling
  window per sender (429 on breach). **Cancellation window**: 30s from due time
  (generated column `cancel_deadline_utc`). **Retry**: 1/2/4/8/16s backoff (31s
  total), up to 5 attempts; permanent failures notify sender (stub, WhatsApp follow-up).
  **Consent gating**: Tier3 held as `awaiting_consent`; promoted to `scheduled` on
  `opted_in` upsert. Re-verified at delivery time. **Sweep**: `DelegatedReminderSweepFunction`
  (every minute), claims fresh-due + retry-due atomically via SECURITY DEFINER.
  **Delivery = pluggable `IDelegatedReminderDeliveryChannel`** with logging stub
  (`LoggingDelegatedReminderDeliveryChannel`). Idempotency key: external ID or
  SHA256(`userId:recipient:dueUnixSeconds:contentLower`). HTTP (Functions):
  `POST/GET api/delegated-reminders`, `DELETE api/delegated-reminders/{id}`.
  Config `DelegatedReminders:*`.
- **SB-009A Link Capture** — done (not yet deployed). Migration `013_link_capture.sql`
  (link_artifacts/link_provenance_refs/link_pending_confirmations/link_enrichment_attempts/
  link_enrichment_policy_decisions/link_audit_events + tenant RLS). URL detection +
  canonical normalization + SHA256 hash dedup (`LinkCanonicalization`). Capture
  outcomes: `created`, `upsert_merged` (same canonical URL + new source), `idempotent_noop`
  (exact replay). One-time yes/no confirmation: atomic `TryConsumeConfirmationAsync`
  (WHERE state='pending'), expiry sweep (`ExpireConfirmationsFunction`, every 5m).
  Enrichment: policy-first (block private/loopback IPs), 4s timeout, fallback description,
  fire-and-forget background scope. Recall: ILIKE substring search across url/label/description.
  Implementation split: contracts/interfaces/canonicalization in `Aluki.Runtime.Abstractions/
  Skills/LinkCapture`; repository/services/policy in `Aluki.Runtime.Host/Skills/LinkCapture/`;
  HTTP endpoints in Functions (`LinkCaptureFunctions`). HTTP: `POST api/skills/link-capture/
  capture|confirm|recall`. Config: no dedicated section (uses `Postgres:*` + `HttpClient`
  "link-enrichment").
- **SB-008B YouTube Link Save and Classification** — done (not yet deployed). Migration
  `014_youtube_link_capture.sql` (saved_link_artifacts/link_enrichments/link_classifications/
  link_capture_audit_events + tenant RLS). YouTube URL detection + canonical video ID extraction
  (watch/shorts/embed/youtu.be/m.youtube.com), SHA256-style dedup by `canonical_video_id`.
  Provider fallback chain: primary→secondary→degraded; enrichment states: `enriched`, `partial`,
  `degraded`. AI classification: `IYouTubeClassificationProvider` with confidence labels
  (`high`/`medium`/`low`) and uncertainty flags per field. Stubs: `LoggingYouTubeMetadataProvider`
  (both primary+secondary), `StubYouTubeClassificationProvider`. Implementation split: contracts/
  interfaces/canonicalizer in `Aluki.Runtime.Abstractions/Skills/YouTubeLinks`; repository/services/
  stubs in `Aluki.Runtime.Host/Skills/YouTubeLinks/`; HTTP in Functions (`CaptureYoutubeLinksFunction`).
  HTTP: `POST api/v1/skills/youtube-links/capture`. Config: uses `Postgres:*`.
- **SB-007 Feedback Suggestions Capture** — done (not yet deployed). Migration
  `015_feedback_suggestions.sql` (suggestions/suggestion_attachments/suggestion_state_transitions
  + tenant+user RLS). Keyword-based intent detection stub. 30-min context window per tenant-user
  (one active at a time). Attachment MIME/size validation (audio ≤50MB mp4/webm/ogg/mpeg,
  photo ≤10MB JPEG/PNG, text ≤5KB inline). Lifecycle: captured→enriched→sent_user→archived
  (one-way). Archival sweep timer (hourly, 90-day cutoff). Idempotency: `(message_id+payload_hash)`
  partial unique index on active states. Implementation split: contracts/interfaces in
  `Aluki.Runtime.Abstractions/Skills/Feedback`; repository/service in
  `Aluki.Runtime.Host/Skills/Feedback/`; HTTP in Functions (`FeedbackFunctions`).
  HTTP: `POST api/skills/feedback/capture|attach`. Config: uses `Postgres:*`.
- **SB-008A Suggestions Admin and Rewards** — done (not yet deployed). Migration
  `016_suggestions_admin.sql` (suggestion_admin_queue/suggestion_admin_audit_ledger/
  reward_entitlement_ledger/reward_notification_delivery/reward_decision_record + tenant RLS,
  no user filter — staff-wide). RBAC: AdminReviewer (captured→under_review, category/priority),
  AdminApprover (all transitions), AdminAuditor (read-only). Audit ledger: WORM append-only
  with bigserial sequence + SHA256 record_hash. Rewards: idempotency boundary
  (tenant+user+suggestion+rule+sourceEventId), Granted/Duplicate/Conflict outcomes. Notification
  sweep: 1/5/15/60/360min backoff, dead-letter after 5 attempts (stub delivery). Implementation
  split: contracts in `Aluki.Runtime.Abstractions/Skills/SuggestionsAdmin`; repository/services
  in `Aluki.Runtime.Host/Skills/SuggestionsAdmin/`; HTTP in Functions (`SuggestionsAdminFunctions`).
  HTTP: `GET api/admin/suggestions`, `POST api/admin/suggestions/{id}/triage`,
  `POST api/admin/rewards/decide`. Config: uses `Postgres:*`.
- Next per order: SB-009B, 010-012.

## AI inference — MUST use Azure OpenAI or Azure AI Foundry

Directive: ALL AI inference goes through Azure OpenAI or Azure AI Foundry.
- Embeddings: Azure OpenAI, config `AiExtraction:*` (deployment default
  `gotnote-embeddings` = text-embedding-3-small, 1536 dims).
- Chat/synthesis + intent classification: Azure AI Foundry **model-router**,
  config `Foundry:Endpoint/ApiKey/ChatDeployment`.

## WhatsApp channel behavior (intended — DO NOT remove without instruction)

- **Inbound webhook**: `MetaWhatsAppWebhookFunction`, route `api/whatsapp`
  (GET verify, POST inbound). HMAC-SHA256 signature check via `Meta__AppSecret`.
  Always 200s so Meta does not retry; capture idempotency makes redelivery safe.
- **Read receipt + typing indicator** (`IWhatsAppMessenger` /
  `MetaWhatsAppMessenger`): on every inbound message the webhook immediately
  sends a single Graph API call to `/{phone_number_id}/messages` with
  `status=read` + `typing_indicator:{type:text}`. This shows the sender the blue
  double-check (read) and the "…" typing bubble (auto-dismisses after ~25s or on
  next message). Best-effort; never blocks or fails capture. `phone_number_id`
  comes from the webhook payload (`value.metadata.phone_number_id`), extracted by
  `MetaWebhookMapper.ExtractReadReceiptTargets`. The Functions deployment wires
  the real messenger via `AddHttpClient<IWhatsAppMessenger, MetaWhatsAppMessenger>`;
  Host/tests use `NullWhatsAppMessenger`.
- Graph config: `Meta:AccessToken`, `Meta:GraphBaseUrl` (default
  `https://graph.facebook.com/v21.0`). Same token used by media download.

## Deployment

- GitHub Actions `.github/workflows/azure-deploy-runtime.yml` deploys on **push to
  `main` only** (feature branches do NOT auto-deploy). Jobs: build+test →
  `migrate-database` (opens PG firewall, applies migrations via a
  `schema_migrations` ledger, closes firewall) → `deploy-function` (OIDC).
- **IMPORTANT**: the migrate-database step iterates an **explicit, hardcoded list**
  of migration filenames (the `for f in 001_… 009_…` loop), NOT a glob. Every new
  `db/migrations/NNN_*.sql` MUST be appended to that loop AND to the test fixture
  list in `tests/integration/.../DbCaptureFixture.cs`, or it silently never runs.
  (This is why SB-003/SB-004's `008/009` migrations were not applied until added.)
- DB migrations run as part of deploy. pgcrypto is NOT allow-listed on Azure —
  use core `gen_random_uuid()` (PG16), do not `create extension pgcrypto`.
- App settings use Key Vault references; Postgres connection string app setting is
  `Postgres__AppConnection` (the connection factory resolves several keys).

## Azure resources (dev)

- Subscription `33bbf1e1-134d-42e3-a370-0dfb1da16cff`, tenant `7b1683c8…`.
- RG `ar-Aluki`. Function `func-araluki-dev-6155`. Key Vault `kvaralukidev6155`.
- Postgres `pg-araluki-dev-6155` (db `aluki_multiagent`, user `pgadminaluki`).
- Foundry `aifoundry-araluki-dev` (eastus2, model-router GA).
- App Insights = Log Analytics workspace customerId
  `306438f9-1e1d-4a0c-a27f-4c1afdb77d9c` (query tables `AppTraces`/`AppRequests`).
- Test/owner WhatsApp number: **+14252307522** (`wa_id` 14252307522), seeded live
  principal: tenant `44444444…` / context `66666666…` / user `55555555…`.

## Conventions

- RLS via `app.current_tenant` / `app.current_user_id` GUCs + `app.user_in_tenant()`.
- Idempotent upsert: `INSERT … ON CONFLICT … RETURNING (xmax=0) AS is_new`.
- Develop on the designated feature branch; merge to `main` only with explicit
  user permission (merging triggers the deploy).
