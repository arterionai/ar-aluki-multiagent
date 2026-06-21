# Quickstart: YouTube Link Save and Classification Validation

This guide validates end-to-end behavior for YouTube link capture, including provider fallback, unsupported URL handling, duplicate protection, and confidence visibility.

## Prerequisites

- .NET 10 SDK installed
- PostgreSQL configured with tenant RLS baseline
- Runtime projects build successfully
- Test tenant/principal/context values available

## Setup

1. Build solution:

```powershell
dotnet build Aluki.Runtime.slnx -v minimal
```

2. Start host runtime:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

3. Start functions runtime (if orchestration path is used):

```powershell
dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
```

4. Prepare capture request payload using contract schema from:
- `specs/008-youtube-links/contracts/youtube-link-capture-contract.yaml`

## Scenario 1: Enriched Save (Primary Provider Success)

Objective: Verify successful enrichment on primary provider and canonical persistence.

1. Submit one valid YouTube URL in message text.
2. Confirm canonical video identity extraction succeeds.
3. Confirm persistence action is `created` (first submission).
4. Confirm outcome is `enriched` and provider is `primary`.
5. Confirm response includes classification confidence label.

Expected:
- One tenant-scoped saved link artifact exists.
- Confidence label is present (`high`/`medium`/`low`).
- Audit contains detection, normalization, primary enrichment, persistence, and user outcome events.

## Scenario 2: Partial Save (Primary Fails, Secondary Succeeds)

Objective: Verify deterministic fallback behavior and partial metadata handling.

1. Configure or simulate primary provider failure.
2. Submit valid YouTube URL.
3. Verify system attempts secondary provider after primary failure.
4. Verify persistence still occurs and outcome is `partial` or `enriched` depending on returned completeness.

Expected:
- Enrichment order is strictly primary then secondary.
- Persistence is not blocked.
- User confirmation explicitly states metadata is partial when applicable.

## Scenario 3: Degraded Save (Both Providers Fail)

Objective: Verify degraded mode still saves canonical identity.

1. Simulate failure/unavailability for both providers.
2. Submit valid YouTube URL.
3. Verify canonical identity is persisted with degraded enrichment state.

Expected:
- Outcome is `degraded`.
- Persistence action is `created` or `refreshed`.
- User confirmation clearly indicates metadata unavailable.

## Scenario 4: Unsupported or Malformed URL

Objective: Ensure invalid links are rejected transparently with no persistence.

1. Submit malformed or unsupported YouTube-like URL.
2. Verify normalization cannot extract stable video identity.

Expected:
- Outcome is `invalid_link`.
- `persisted=false` and `persistenceAction=none`.
- User receives explicit invalid-link message.
- Audit contains normalization failure and final user outcome events.

## Scenario 5: Duplicate in Same Message

Objective: Ensure same canonical video identity is processed once per message.

1. Submit message containing same video in multiple URL variants.
2. Verify canonicalization maps all variants to same `canonicalVideoId`.

Expected:
- Single processing attempt for that canonical identity.
- One persistence action only for that identity.
- One user outcome entry for that identity.

## Scenario 6: Duplicate Across Submissions (Same Tenant)

Objective: Ensure idempotent refresh without creating duplicate active records.

1. Submit valid URL once (record created).
2. Submit same canonical video again under same tenant.

Expected:
- Existing artifact is refreshed, not duplicated.
- Response shows `persistenceAction=refreshed`.
- Active record count for `(tenant_id, canonical_video_id)` remains 1.

## Scenario 7: Same Video Across Tenants

Objective: Verify tenant isolation for shared video identities.

1. Submit same canonical video under tenant A.
2. Submit same canonical video under tenant B.

Expected:
- Two isolated records exist, one per tenant.
- No cross-tenant read/write leakage.

## Scenario 8: Confidence Visibility and Uncertainty Marking

Objective: Verify classification confidence is visible and low-confidence fields are marked.

1. Submit URL likely to produce low-confidence classification.
2. Inspect response classification payload.

Expected:
- `confidenceLabel` is always present.
- Low confidence includes `uncertainFields` entries (e.g., `summary`, `tags`).
- Confirmation text reflects confidence level transparently.

## Validation Checklist

- Deterministic provider order enforced
- Invalid links not persisted
- Per-message dedupe by canonical identity works
- Same-tenant resubmission refreshes existing record
- Cross-tenant isolation preserved
- Confidence label visible in user outcome
- Low-confidence uncertainty markers present

## References

- Spec: `specs/008-youtube-links/spec.md`
- Plan: `specs/008-youtube-links/plan.md`
- Research: `specs/008-youtube-links/research.md`
- Data model: `specs/008-youtube-links/data-model.md`
- Contract: `specs/008-youtube-links/contracts/youtube-link-capture-contract.yaml`