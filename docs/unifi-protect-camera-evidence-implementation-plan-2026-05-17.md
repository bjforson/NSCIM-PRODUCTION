# UniFi Protect Camera Evidence Implementation Plan

Date: 2026-05-17
Status: Draft implementation plan
Companion spec: `docs/unifi-protect-camera-evidence-system-spec-2026-05-17.md`
Repository: `C:\Shared\NSCIM_PRODUCTION`

## Executive Summary

Implement UniFi Protect camera evidence as an adjacent, quarantined NSCIM module. The first release should support several UniFi Protect NVRs across different locations from the beginning, ingest Protect alarm events, fetch camera frames, run OCR, and provide a review surface without feeding NSCIM core workflows.

The implementation is deliberately staged:

1. Prove the real Protect console behavior and site topology.
2. Add a disabled-by-default camera evidence module shell.
3. Add isolated multi-site data tables.
4. Add Protect API clients and health checks per site.
5. Add authenticated webhook intake.
6. Add queued media capture and local storage.
7. Add local OCR and review workflow.
8. Add optional external vision fallback behind cost and privacy gates.
9. Add read-only candidate linking only inside the camera evidence module.
10. Leave core display, decision support, and automation promotion for a later approved project.

Initial success means camera evidence works end to end inside its own boundary and proves it cannot alter completeness, audit, image analysis, ICUMS, scan asset, assignment, preload, or scanner workflows.

## Core Constraints

- Camera evidence is disabled by default.
- Multi-site and multi-NVR support is part of the first design, not a retrofit.
- All runtime routes stay under `/api/camera-evidence/*` and `/api/integrations/unifi-protect/*`.
- All UI routes stay under `/camera-evidence/*`.
- All tables use a `CameraEvidence` naming family.
- Camera evidence may read limited core reference data only when `CameraEvidence:CoreReadOnlyLookupEnabled=true`.
- NSCIM core services may not read from or write to camera evidence tables in the initial release.
- The initial release does not add camera OCR output to container details, image analysis, audit review, completeness dashboards, ICUMS queues, scan assets, scanner identity, predictive preload, or assignment routing.
- Any later core use must go through a promotion request, explicit feature flag, tests, monitoring, and rollback plan.

## Multi-Site Design From Day One

Add a site/NVR level above cameras:

```text
CameraEvidenceSite
  -> CameraEvidenceSource
      -> CameraEvidenceEvent
          -> CameraEvidenceFrame
              -> CameraEvidenceOcrResult
                  -> CameraEvidenceReviewDecision
```

Each site represents one UniFi Protect console/NVR or local Protect controller.

Required site fields:

- `Id`
- `SiteKey`
- `DisplayName`
- `LocationName`
- `BaseUrl`
- `ApiKeySecretName`
- `WebhookSecretName`
- `AllowedWebhookSourceCidrsJson`
- `VerifySsl`
- `RequestTimeoutSeconds`
- `IsEnabled`
- `CreatedAt`
- `UpdatedAt`

Rules:

- `SiteKey` is stable, short, and appears in webhook routes.
- Camera/source rows belong to exactly one site.
- Event, frame, OCR, review, audit, and candidate rows carry `SiteId` or inherit it through required relationships.
- Workers partition concurrency by site and camera so one location cannot starve the others.
- Health checks report per-site API status, webhook status, queue depth, and media fetch failures.
- Storage paths include site and camera identifiers.

Initial webhook route:

```http
POST /api/integrations/unifi-protect/sites/{siteKey}/webhooks/alarm
```

Optional later collector shape for remote sites:

```text
Remote site collector
  -> signs and forwards event/frame metadata
  -> central NSCIM CameraEvidence ingestion
```

The collector should use the same ingestion contract as central webhook intake so local-vs-central deployment does not change downstream processing.

## Workstream Map

| Workstream | Purpose | Initial owner boundary |
| --- | --- | --- |
| Protect topology proof | Confirm live API, payloads, camera IDs, and SSL behavior | Ops/integration proof only |
| Feature flags/options | Keep module dark until deliberately enabled | API/service registration |
| Data model | Store quarantined evidence independently | New CameraEvidence tables only |
| Protect client | Talk to each Protect site/NVR | `Services.CameraEvidence` |
| Webhook intake | Receive Protect alarm signals | `/api/integrations/unifi-protect/*` |
| Queue/workers | Decouple event intake from media/OCR work | CameraEvidence hosted services |
| Media storage | Store snapshots/frames outside scanner stores | CameraEvidence storage root |
| OCR | Extract text locally first | CameraEvidence OCR service |
| Review UI | Let operators confirm or reject results | `/camera-evidence/*` |
| Observability | Show per-site health and backlog | CameraEvidence metrics |
| External vision fallback | Improve low-confidence OCR where approved | Disabled by default |
| Candidate linking | Suggest possible core references for review | CameraEvidence UI only |
| Promotion | Future controlled core use | Out of scope for first release |

## Phase 0: Real Protect Console Proof

Goal: remove assumptions before code is coupled to Protect behavior.

Tasks:

- Inventory each location, Protect console/NVR, network path, and intended camera.
- Confirm the current Protect firmware and API behavior for each site.
- Create least-privilege Protect API keys per site.
- Verify `GET /proxy/protect/integration/v1/meta/info` or available equivalent.
- Verify `GET /proxy/protect/integration/v1/cameras`.
- Verify snapshot capture for each selected camera.
- Verify high-quality snapshot fallback behavior.
- Verify RTSPS stream URL access for cameras that may need rolling buffer support.
- Capture Alarm Manager webhook payloads from each site into a staging endpoint or request bin.
- Document webhook headers, device identifiers, timestamps, alarm names, and retry behavior.
- Decide central NSCIM access vs local collector for each site.
- Document SSL strategy for each site: trusted cert, pinned cert, or explicitly approved self-signed handling.

Deliverables:

- `docs/unifi-protect-camera-evidence-site-inventory-YYYY-MM-DD.md`
- Sample webhook payloads with secrets removed.
- Sample snapshot files stored outside the repo.
- Site connectivity and certificate matrix.
- Initial list of enabled cameras, expected text types, and capture modes.

Validation:

- A real API key can list cameras for every planned site.
- A real snapshot can be pulled for every planned first-wave camera.
- A real Alarm Manager webhook can reach a staging endpoint.
- No NSCIM application behavior is changed in this phase.

## Phase 1: Module Shell, Options, And Feature Flags

Goal: add the inactive module boundary and configuration model.

Tasks:

- Add `CameraEvidenceOptions`.
- Add `UniFiProtectOptions` with a `Sites` collection.
- Add feature flags matching the companion spec:
  - `Enabled`
  - `WebhookIngestionEnabled`
  - `MediaFetchEnabled`
  - `OcrEnabled`
  - `ExternalVisionFallbackEnabled`
  - `CoreReadOnlyLookupEnabled`
  - `CoreDisplayPromotionEnabled`
  - `CoreDecisionSupportEnabled`
  - `CoreAutomationEnabled`
- Register camera evidence services only behind the module boundary.
- Add a simple disabled/default health response.
- Add configuration defaults with every active feature set to false.
- Ensure secrets are represented by secret names or external configuration, not committed values.

Likely files:

- `src/NickScanCentralImagingPortal.Core/Configuration/CameraEvidenceOptions.cs`
- `src/NickScanCentralImagingPortal.Core/Configuration/UniFiProtectOptions.cs`
- `src/NickScanCentralImagingPortal.Services/CameraEvidence/*`
- `src/NickScanCentralImagingPortal.API/Extensions/*`
- `src/NickScanCentralImagingPortal.API/Program.cs`
- `appsettings*.json` templates only, with disabled defaults and no secrets.

Validation:

- Options bind from configuration.
- With `CameraEvidence:Enabled=false`, no webhook, media fetch, or OCR worker runs.
- Existing NSCIM health endpoints still behave as before.
- No existing core services reference camera evidence namespaces.

Rollback:

- Disable `CameraEvidence:Enabled`.
- Remove service registration if needed; no schema or data behavior depends on this phase yet.

## Phase 2: Isolated Data Model And Migrations

Goal: create durable storage for quarantined evidence without touching core workflow tables.

Tasks:

- Add entity classes under `Core.Entities.CameraEvidence`.
- Add DTOs under `Core.DTOs.CameraEvidence`.
- Add EF configuration for the new table family.
- Add migration for:
  - `CameraEvidenceSites`
  - `CameraEvidenceSources`
  - `CameraEvidenceEvents`
  - `CameraEvidenceFrames`
  - `CameraEvidenceOcrResults`
  - `CameraEvidenceReviewDecisions`
  - `CameraEvidenceCoreLinkCandidates`
  - `CameraEvidencePromotionRequests`
  - `CameraEvidenceAuditLog`
- Add indexes for:
  - `SiteKey`
  - `SiteId + ProtectCameraId`
  - `SiteId + IdempotencyKey`
  - `SiteId + EventTimestamp`
  - `SourceId + EventTimestamp`
  - `NormalizedText`
  - `ReviewStatus`
  - `PromotionState`
- Keep candidate links advisory. Do not add required lifecycle foreign keys into core workflow tables.
- Add retention columns or retention policy fields where needed.

Likely files:

- `src/NickScanCentralImagingPortal.Core/Entities/CameraEvidence/*.cs`
- `src/NickScanCentralImagingPortal.Core/DTOs/CameraEvidence/*.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Data/*`
- EF migration files.

Validation:

- Migration adds only `CameraEvidence*` tables and indexes.
- Migration does not add columns to existing core tables.
- Migration does not alter existing completeness, audit, image analysis, scan asset, ICUMS, scanner, preload, or assignment tables.
- Entity relationships keep camera evidence as the owning side of its data.

Rollback:

- Disable the module.
- Roll back the migration if no evidence has been collected, or retain tables unused if data preservation is required.

## Phase 3: UniFi Protect Client And Per-Site Health

Goal: encapsulate Protect API access per site/NVR.

Tasks:

- Add an `IUniFiProtectClient` abstraction.
- Add a client factory keyed by `SiteKey`.
- Add request signing/header behavior for `X-API-KEY`.
- Add TLS/certificate handling based on site configuration.
- Add operations:
  - Get Protect metadata.
  - List cameras.
  - Get camera snapshot.
  - Get RTSPS stream URL.
  - Validate API key health.
- Add per-site timeout and retry policy.
- Add per-site health endpoint under camera evidence routes.
- Add structured logs that include `SiteKey` and camera identifiers but never log secrets.

Likely files:

- `src/NickScanCentralImagingPortal.Services/CameraEvidence/UniFiProtect/*`
- `src/NickScanCentralImagingPortal.API/Controllers/CameraEvidence/CameraEvidenceHealthController.cs`

Validation:

- Fake HTTP tests verify URL construction and `X-API-KEY` headers.
- Per-site health returns healthy, degraded, or disabled without affecting global NSCIM health.
- A failing Protect site does not fail unrelated sites.

Rollback:

- Disable the site or module.
- Leave stored configuration inert.

## Phase 4: Site And Camera Administration

Goal: allow administrators to manage Protect sites and camera mappings without mixing them into scanner config.

Tasks:

- Add CRUD endpoints for camera evidence sites.
- Add CRUD endpoints for camera evidence sources/cameras.
- Add test-snapshot command for one configured camera.
- Add camera fields:
  - Protect camera ID.
  - Protect device key.
  - MAC address.
  - Display name.
  - Location name.
  - Operational zone.
  - Expected text type.
  - Capture mode.
  - OCR profile.
  - Enabled flag.
- Add admin UI under `/camera-evidence/settings`.
- Add per-site camera list import/sync from Protect.
- Preserve local operational names and OCR settings even if Protect display names change.

Likely routes:

```http
GET /api/camera-evidence/sites
POST /api/camera-evidence/sites
PATCH /api/camera-evidence/sites/{siteId}
GET /api/camera-evidence/sources
POST /api/camera-evidence/sources
PATCH /api/camera-evidence/sources/{sourceId}
POST /api/camera-evidence/sources/{sourceId}/test-snapshot
POST /api/camera-evidence/sites/{siteId}/sync-cameras
```

Validation:

- Admin can configure two Protect sites and cameras with overlapping Protect camera names.
- `SiteKey + camera ID` remains unambiguous.
- Test snapshot stores data only in camera evidence storage.

Rollback:

- Disable affected site/source rows.
- Keep settings data for audit unless explicitly purged.

## Phase 5: Webhook Intake

Goal: receive Protect alarm events quickly and safely.

Tasks:

- Add route `POST /api/integrations/unifi-protect/sites/{siteKey}/webhooks/alarm`.
- Authenticate using shared secret header and optional source CIDR allowlist.
- Validate that the site exists and webhook intake is enabled.
- Preserve raw payload JSON.
- Extract alarm name, trigger type, device key, camera candidate, event timestamp, and received timestamp.
- Build idempotency key from site, event ID or payload fingerprint, device, and event time.
- Persist `CameraEvidenceEvent`.
- Add queue item for media capture.
- Return `202 Accepted` quickly.
- Rate-limit intake per site.
- Add webhook rejection logging without secret values.

Likely files:

- `src/NickScanCentralImagingPortal.API/Controllers/Integrations/UniFiProtectWebhookController.cs`
- `src/NickScanCentralImagingPortal.Services/CameraEvidence/Ingestion/*`

Validation:

- Missing/invalid secret returns unauthorized.
- Unknown site returns not found or disabled response.
- Duplicate payload does not enqueue duplicate work.
- Handler returns quickly and does not fetch media inline.
- Core tables remain unchanged after webhook ingestion.

Rollback:

- Disable `CameraEvidence:WebhookIngestionEnabled`.
- Disable the Protect Alarm Manager webhook action at the site.

## Phase 6: Queue And Background Processing

Goal: decouple ingestion from media fetch and OCR.

Tasks:

- Add durable queue table or use existing background queue pattern if one exists and can be isolated by camera evidence type.
- Add queue payloads with event ID, site ID, source ID, desired capture mode, and attempt count.
- Add media fetch worker.
- Add OCR worker.
- Add retry/backoff and dead-letter status.
- Bound concurrency:
  - per site,
  - per camera,
  - globally for media fetch,
  - globally for OCR.
- Add worker health metrics and queue depth metrics.
- Keep workers disabled unless their feature flags are enabled.

Validation:

- Webhook event becomes queued work.
- Failed media fetch records error and retries without crashing the host.
- One failing site does not block another site.
- Worker disabled flags stop processing cleanly.

Rollback:

- Disable media fetch and OCR flags.
- Leave queued events pending for later reprocessing.

## Phase 7: Media Capture And Storage

Goal: store snapshots and selected frames as camera evidence, never scanner assets.

Tasks:

- Implement snapshot fetch using Protect API.
- Try high-quality snapshot where configured.
- Fall back to standard snapshot when high-quality fails.
- Store original bytes with content type, SHA-256, dimensions, and capture metadata.
- Define storage root outside scanner image stores.
- Include site, source, event, and frame IDs in storage path.
- Add secure frame image endpoint under `/api/camera-evidence/frames/{id}/image`.
- Add retention policy fields and cleanup job in disabled/dry-run mode first.
- For cameras needing event timing, add optional RTSPS rolling buffer design behind a separate flag.

Recommended storage path pattern:

```text
{CameraEvidenceStorageRoot}/{siteKey}/{sourceId}/{yyyy}/{MM}/{dd}/{eventId}/{frameId}.jpg
```

Validation:

- Event produces at least one stored frame for a configured camera.
- SHA-256 and content type persist.
- Frame endpoint enforces camera evidence permissions.
- Scanner image stores and scan asset routes are untouched.

Rollback:

- Disable `CameraEvidence:MediaFetchEnabled`.
- Retain or purge stored frames according to retention policy.

## Phase 8: Local OCR

Goal: extract text locally first with no per-call model cost.

Tasks:

- Choose initial OCR engine after testing site samples.
- Add an OCR engine abstraction.
- Add preprocessing profiles by expected text type:
  - container number,
  - license plate,
  - seal number,
  - mixed text,
  - unknown.
- Persist raw OCR output and normalized output separately.
- Persist confidence, engine name, engine version, bounding boxes, and preprocessing steps.
- Add advisory validators for container-like strings, including checksum where applicable.
- Lower confidence for validation failures; preserve the candidate.
- Add OCR reprocess command under camera evidence routes.

Validation:

- OCR result persists for a stored frame.
- Raw result remains immutable.
- Reprocessing creates a new result or versioned result without overwriting review history.
- No OCR result writes to core NSCIM data.

Rollback:

- Disable `CameraEvidence:OcrEnabled`.
- Existing frames remain available for later processing.

## Phase 9: Review UI

Goal: give operators a clean review workflow inside the camera evidence area.

Tasks:

- Add pages:
  - `/camera-evidence`
  - `/camera-evidence/events`
  - `/camera-evidence/review`
  - `/camera-evidence/settings`
- Add typed camera evidence frontend client.
- Add filters:
  - site,
  - camera,
  - location,
  - time range,
  - trigger type,
  - candidate type,
  - confidence,
  - review status.
- Add frame preview.
- Add OCR text and bounding box overlays where available.
- Add accept, reject, and correct actions.
- Add immutable review decision rows.
- Add audit history per OCR result.
- Add visible site/NVR context so operators know where evidence came from.

Validation:

- Reviewer can accept, reject, or correct an OCR result.
- Correction does not overwrite raw OCR.
- Review UI never places evidence into main container details, audit review, image analysis, completeness, ICUMS, or scanner panels.
- Typed frontend client uses only `/api/camera-evidence/*`.

Rollback:

- Hide navigation entry and disable module flags.
- Stored evidence remains queryable by administrators through API if needed.

## Phase 10: Observability And Operations

Goal: make site health, queue state, and evidence processing visible.

Tasks:

- Add metrics:
  - webhooks received,
  - webhooks rejected,
  - queue depth,
  - media fetch success/failure,
  - snapshot latency,
  - OCR latency,
  - OCR confidence distribution,
  - review backlog,
  - external vision calls,
  - estimated external vision cost,
  - promotion state counts.
- Add per-site health summary.
- Add operational dashboard inside `/camera-evidence`.
- Add retention job reporting.
- Add runbook for disabling a site, disabling ingestion, and replaying failed events.

Validation:

- A broken site is visible as site-specific degraded status.
- Global NSCIM core health remains independent.
- Operators can see backlog and failures without reading logs.

Rollback:

- Disable metrics collection for this module if needed.
- Keep core monitoring unchanged.

## Phase 11: Optional External Vision Fallback

Goal: allow low-confidence OCR escalation only when explicitly approved.

Tasks:

- Add provider abstraction for external vision.
- Add per-site and global budget controls.
- Add privacy/data-residency allowlist.
- Add confidence threshold routing.
- Add request logging with frame ID, provider, model, estimated tokens/cost, and outcome.
- Keep fallback results quarantined.
- Add UI marker showing which results came from external vision.

Validation:

- With `ExternalVisionFallbackEnabled=false`, no external calls occur.
- With fallback enabled, calls happen only for eligible low-confidence frames.
- Cost counters update.
- Fallback output still requires review.

Rollback:

- Disable `CameraEvidence:ExternalVisionFallbackEnabled`.
- Keep local OCR active.

## Phase 12: Read-Only Candidate Linking

Goal: let reviewers see possible core references without influencing core behavior.

Tasks:

- Add a read-only lookup service behind `CameraEvidence:CoreReadOnlyLookupEnabled`.
- Search core reference data by normalized candidate value.
- Create `CameraEvidenceCoreLinkCandidates` rows with match reason and confidence.
- Display candidate links only inside camera evidence pages.
- Add reviewer confirmation/rejection for candidate links.
- Add audit entries for candidate link decisions.

Validation:

- With `CoreReadOnlyLookupEnabled=false`, no core lookup happens.
- Candidate links are advisory and stored in camera evidence tables.
- Core tables are not updated.
- No core page consumes these links.

Rollback:

- Disable `CameraEvidence:CoreReadOnlyLookupEnabled`.
- Existing candidate links remain historical evidence only.

## Phase 13: Future Promotion Gate

Goal: reserve a controlled path for later use inside core NSCIM.

This phase is out of scope for the initial release.

Required before any promotion:

- Approved promotion request.
- Exact field and consumer named.
- Accuracy evidence from reviewed examples.
- False-positive and false-negative impact analysis.
- Operator workflow design.
- Feature flag defaulting to false.
- Tests for enabled and disabled behavior.
- Monitoring proving whether promoted data is used.
- Rollback plan.

Allowed order if approved later:

1. Display-only inside a clearly labeled core UI area.
2. Decision support that requires explicit operator action.
3. Automation only after a separate approval and strong accuracy evidence.

Initial implementation should support promotion request records but should not expose approval endpoints until needed.

## Files Likely To Change

Backend:

- `src/NickScanCentralImagingPortal.Core/Configuration/CameraEvidenceOptions.cs`
- `src/NickScanCentralImagingPortal.Core/Configuration/UniFiProtectOptions.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/CameraEvidence/*`
- `src/NickScanCentralImagingPortal.Core/DTOs/CameraEvidence/*`
- `src/NickScanCentralImagingPortal.Services/CameraEvidence/*`
- `src/NickScanCentralImagingPortal.API/Controllers/CameraEvidence/*`
- `src/NickScanCentralImagingPortal.API/Controllers/Integrations/UniFiProtectWebhookController.cs`
- `src/NickScanCentralImagingPortal.API/Program.cs`
- EF migrations and DbContext configuration.

Frontend:

- `src/NickScanWebApp.New/Pages/CameraEvidence/*`
- `src/NickScanWebApp.New/Shared/NavMenu*` or equivalent navigation file, only for a gated camera evidence entry.
- `src/NickScanWebApp.Shared/Services/CameraEvidenceClient.cs`
- Related DTO/client registration files.

Configuration and docs:

- `appsettings*.json` templates with disabled defaults only.
- `docs/unifi-protect-camera-evidence-system-spec-2026-05-17.md`
- This implementation plan.
- Future site inventory and operations runbook docs.

## Files And Systems Intentionally Not Touched Initially

- Container completeness services and controllers.
- Record completeness services and controllers.
- `ImageAnalysisDecision` services, controllers, and tables.
- Audit review services, controllers, and tables.
- ICUMS ingestion/submission services.
- `ScanAssetResolver` and `ScanAssetsController`.
- Eagle A25, ASE, FS6000, and image-splitter source identity logic.
- Predictive preload services and `/api/cache/predictive/*`.
- Assignment, readiness, and capacity services.
- Scanner image storage roots.
- Main container details UI.
- Image analysis workbench.
- Audit review dialog.
- Completeness dashboards.

## Validation Matrix

| Area | Validation |
| --- | --- |
| Configuration | All camera evidence flags default false and bind correctly. |
| Route isolation | New endpoints exist only under `/api/camera-evidence/*` and `/api/integrations/unifi-protect/*`. |
| Dependency isolation | Core services do not inject camera evidence services. |
| Schema isolation | Migrations add only `CameraEvidence*` tables and indexes. |
| Multi-site | Two sites with overlapping camera names process independently. |
| Webhook security | Missing, invalid, or wrong-site secrets are rejected. |
| Idempotency | Duplicate webhook payloads do not duplicate event work. |
| Queue resilience | Media/OCR failures are retried and dead-lettered without host failure. |
| Media storage | Frames are stored outside scanner image stores. |
| OCR integrity | Raw OCR is immutable and corrections are separate review rows. |
| UI isolation | Camera evidence appears only in `/camera-evidence/*` pages. |
| Core no-op | Webhook-to-review flow does not change completeness, audit, ICUMS, image analysis, scan assets, assignment, readiness, or preload state. |
| Observability | Per-site health, queue depth, and failure counts are visible. |
| External fallback | No external calls occur unless fallback flag and budget allow it. |

## Suggested Automated Test Coverage

Unit tests:

- Options binding and default disabled flags.
- Protect client request construction.
- Webhook authentication.
- Idempotency key generation.
- Camera mapping resolution by site and device.
- Snapshot fallback selection.
- OCR normalization and advisory validation.
- Review decision immutability.

Integration tests:

- Webhook POST persists event and returns `202`.
- Duplicate webhook is safe.
- Queued media fetch persists frame metadata.
- OCR worker persists result without core writes.
- Review action creates immutable decision row.
- `CoreReadOnlyLookupEnabled=false` prevents core lookup.
- Disabled module prevents active processing.

Regression guard tests:

- Container completeness status is unchanged after camera evidence processing.
- Record completeness status is unchanged.
- Image analysis decisions are unchanged.
- Audit decisions are unchanged.
- ICUMS records are unchanged.
- Scan asset resolution is unchanged.
- Predictive preload cache behavior is unchanged.

## Deployment Plan

Initial deployment is dark:

```json
{
  "CameraEvidence": {
    "Enabled": false,
    "WebhookIngestionEnabled": false,
    "MediaFetchEnabled": false,
    "OcrEnabled": false,
    "ExternalVisionFallbackEnabled": false,
    "CoreReadOnlyLookupEnabled": false,
    "CoreDisplayPromotionEnabled": false,
    "CoreDecisionSupportEnabled": false,
    "CoreAutomationEnabled": false
  }
}
```

Controlled enablement order:

1. Deploy code with all flags false.
2. Enable module health for administrators only.
3. Configure one non-critical test site.
4. Sync/list cameras for that site.
5. Enable webhook intake for one test alarm.
6. Enable media fetch for one camera.
7. Enable OCR for one camera.
8. Enable review UI for administrators.
9. Expand to more cameras at the same site.
10. Expand to additional sites.
11. Enable optional external fallback only after local OCR metrics justify it.
12. Enable read-only candidate lookup only after review workflow is stable.

Rollback:

- Set `CameraEvidence:Enabled=false`.
- Disable site rows or specific source rows.
- Disable Protect Alarm Manager webhook actions.
- Stop camera evidence workers.
- Preserve stored evidence unless retention or privacy policy requires purge.

## Operational Runbooks To Add

- Configure a new Protect site.
- Rotate Protect API key for one site.
- Rotate webhook secret for one site.
- Disable one camera.
- Disable one site.
- Replay failed event processing.
- Purge expired frames.
- Investigate low OCR confidence.
- Review external fallback cost.
- Export reviewed evidence.
- Verify core no-op behavior after an incident.

## Risk Register

| Risk | Mitigation |
| --- | --- |
| Protect API behavior differs by firmware | Phase 0 proof per site and typed client tests. |
| Webhook payload lacks exact event frame | Use snapshot first, add rolling buffer for timing-critical cameras. |
| Remote sites have unreliable connectivity | Per-site retries, queue isolation, optional local collector. |
| One busy site starves others | Per-site and per-camera concurrency limits. |
| Camera OCR false positives | Quarantined output, confidence scoring, reviewer confirmation, advisory validation. |
| External vision cost grows unexpectedly | Disabled default, budgets, metrics, low-confidence threshold. |
| Camera data leaks into core workflows | Route isolation, service dependency tests, schema isolation, promotion flags default false. |
| Operators mistake camera evidence for core truth | Separate UI, labels, review states, no display in core pages initially. |
| Storage grows too quickly | Retention policy, frame sampling, rolling buffer limits, metrics. |
| Secrets appear in logs or config | Secret-name configuration, redaction, authentication tests. |

## Definition Of Done For Initial Release

- Multi-site site/NVR configuration exists.
- At least two configured sites can be represented independently.
- Protect camera list and snapshot health can be tested per site.
- Webhook intake persists events with idempotency.
- Media fetch stores frames in camera evidence storage.
- Local OCR stores raw and normalized results.
- Review UI supports accept, reject, and correct decisions.
- Review decisions are immutable and auditable.
- Per-site health, queue depth, processing failures, OCR metrics, and backlog are visible.
- All active behavior is controlled by feature flags.
- All core promotion flags remain false.
- Core completeness, audit, image analysis, ICUMS, scan assets, scanner identity, assignment, readiness, and preload workflows remain untouched.
- Tests prove the module can run a webhook-to-review flow without core writes.

## Open Decisions

- Which OCR engine should be first after testing real camera samples?
- What is the evidence storage root and retention policy?
- Which first-wave sites and cameras should be enabled?
- Which cameras need snapshot-only mode vs rolling buffer mode?
- Will any site require a local collector because of network or firewall constraints?
- What certificate strategy should be used for each Protect console?
- What exact text types matter first: container number, vehicle plate, seal, reference number, or mixed?
- Who can review evidence and who can administer site settings?
- Should promotion request records be implemented in the first release or deferred until a real promotion proposal exists?

