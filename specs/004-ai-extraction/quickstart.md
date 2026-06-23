# Quickstart: AI Extraction Validation

This guide validates the AI extraction feature end-to-end for audio, text, and receipt inputs with tenant-scoped security, confidence tiering, and async job lifecycle behavior.

## Prerequisites

- .NET 10 SDK installed
- PostgreSQL reachable and runtime configured
- Existing base migrations applied from `db/migrations`
- Runtime host and functions projects build successfully
- Test tenant/principal context available

## Setup

1. Build projects:

```powershell
dotnet build Aluki.Runtime.slnx
```

2. Start host runtime:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

3. Start functions runtime (separate shell):

```powershell
dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
```

4. Ensure test context values are set for requests:

- `tenant_id` is valid
- `principal_context.principal_id` is valid
- `principal_context.context_type` is one of `personal | team | delegated`

## Scenario 1: Audio -> Transcription + Structured Actions

Objective: confirm audio extraction returns transcription and extracted action items.

1. Submit extraction request with `extraction_input` of audio type.
2. Confirm response includes `job_id`, `job_status`, and extracted fields once completed.
3. Verify confidence tiers are assigned per field:
- `high` for `>= 0.85`
- `medium` for `0.70-0.84`
- `low` for `< 0.70`
4. Verify low-confidence fields are flagged and not surfaced as confirmed facts.

Expected result:
- Job reaches `completed_success` or `completed_with_warnings`.
- Transcription text and action items are persisted with provenance.

## Scenario 2: Text -> Summary + Decisions + Entities

Objective: confirm long text input is summarized with decision/action/entity extraction.

1. Submit text input with mixed-language content (`es-MX` and `en-US`).
2. Validate detected language metadata captures segment-level tagging or language pair notation.
3. Validate response contains summary, decisions, and action items with confidence metadata.

Expected result:
- Language metadata is present and recall-filterable.
- Medium confidence entries are visible with warnings.

## Scenario 3: Receipt Image -> OCR + Fiscal Fields

Objective: confirm receipt extraction includes vendor/amount/date/tax and RFC when present.

1. Submit a readable receipt image.
2. Verify extracted monetary and date fields include confidence values.
3. Verify `tax_id_rfc` appears when recognized.

Expected result:
- Structured receipt fields returned and persisted.
- `manual_review_required=false` for readable receipts.

## Scenario 4: OCR Fallback and Unreadable Handling

Objective: validate no-fabrication fallback when OCR confidence is insufficient.

1. Submit a low-quality/blurred receipt image.
2. Confirm structured OCR fallback path executes.
3. If fallback also fails, confirm output marks fragment unreadable.

Expected result:
- `is_unreadable=true` and `manual_review_required=true`.
- No invented vendor/amount/date/RFC values.

### US3 implementation evidence (2026-06-22)

Receipt OCR landed in `Aluki.Runtime.Extraction` (mirroring US1/US2). Contract
mapping for the scenarios above:

- Readable receipt ⇒ `extraction_type=receipt_ocr`, fields `vendor`/`total`/
  `subtotal`/`tax`/`date`/`rfc` with per-field confidence + tier. Validated RFC is
  uppercased/canonicalized; amounts and dates are normalized (`ReceiptNormalization`).
- Structured OCR empty ⇒ text-only fallback; recovered fields capped at medium
  confidence with warning `ocr_fallback_used` (job `completed_with_warnings`).
- Both attempts empty ("unreadable" + "manual review required") ⇒ job `failed`
  with `error_category=ocr_failed_all`, warning `unreadable_fragment`, and a
  `manual_review_flagged` audit event. No fabricated fields.
- "Never invent": present-but-invalid RFC/amount/date values are persisted for
  review but kept below the surfacing threshold (withheld from `extracted_fields`).
- Covered by `ReceiptExtractionNormalizationTests` (unit), the receipt cases in
  `ExtractionContractTests` (contract), and `ExtractionPipelineIntegrationTests`
  (integration: structured success, fallback warning, unreadable/manual-review).

## Scenario 5: Async Job Lifecycle

Objective: validate durable lifecycle behavior for long-running jobs.

1. Submit long audio with async option enabled.
2. Poll status endpoint using returned `job_id`.
3. Validate transitions:
- `pending -> processing -> completed_success|completed_with_warnings|failed`
4. Validate progress metadata updates (`completion_pct`, `segment_count`).

Expected result:
- Stable `job_id` across retries.
- Terminal state and processing metadata persisted.

## Scenario 6: Security and Tenant Isolation

Objective: verify tenant/context boundaries are enforced.

1. Submit extraction under tenant A.
2. Attempt to read the same `job_id` under tenant B context.

Expected result:
- Access denied or no record returned for cross-tenant query.
- Audit trail includes scope and principal metadata.

## Scenario 7: Idempotency

Objective: validate duplicate submission returns cached/consistent job behavior.

1. Submit request using same `extraction_id` and tenant context twice.
2. Compare resulting `job_id` and persisted records.

Expected result:
- No duplicate extraction job creation.
- System returns existing/cached outcome per idempotency policy.

## References

- Spec: `specs/004-ai-extraction/spec.md`
- Plan: `specs/004-ai-extraction/plan.md`
- Data model: `specs/004-ai-extraction/data-model.md`
- Contract: `specs/004-ai-extraction/contracts/extraction-skill-contract.yaml`
