# UniFi Protect Camera Evidence System Specification

Date: 2026-05-17
Status: Draft for review
Repository: `C:\Shared\NSCIM_PRODUCTION`

## Mission

Build a UniFi Protect camera evidence ingestion and OCR system that can receive Protect alarm events, pull camera snapshots or video frames, extract text, and present the result for review without affecting NSCIM core operations.

The system is an adjacent evidence layer. It must not become an implicit input into clearance, completeness, image analysis, audit, ICUMS, scanner resolution, or container workflow decisions unless a later approved promotion explicitly allows that data to be used.

## Hard Boundary

The first implementation must be quarantine-only.

Camera-derived data may be stored, searched, reviewed, and exported from the camera evidence module. It must not:

- Mark a container complete or incomplete.
- Change `ContainerCompletenessStatus`.
- Change `RecordCompletenessStatus`.
- Create, assign, release, progress, or complete analyst/audit work.
- Create or modify `ImageAnalysisDecision` or `AuditDecision` rows.
- Modify ICUMS, CMR, BOE, scanner, or source-scan records.
- Feed `ScanAssetResolver`, `ScanAssetsController`, Eagle A25, ASE, FS6000, or image-splitter source identity.
- Appear in the normal container details UI as authoritative evidence.
- Trigger automatic alerts that operators could confuse with core NSCIM workflow decisions.
- Be used for predictive preload, readiness, capacity, or assignment routing.

Any future use inside core NSCIM must go through the promotion protocol in this document.

## Source Research Snapshot

Research date: 2026-05-17.

Observed UniFi Protect state:

- Public release notes show UniFi Protect `7.1.60` as the latest public release found during research.
- The UniFi developer portal exposes Protect documentation through `v7.1.46`.
- Protect Alarm Manager supports custom webhook actions.
- Protect Integration API exposes camera metadata, snapshots, RTSPS streams, and WebSocket event/device subscriptions.
- Protect webhooks should be treated as event signals, not as guaranteed image payloads.

Relevant Protect endpoints:

```http
GET /proxy/protect/integration/v1/cameras
GET /proxy/protect/integration/v1/cameras/{cameraId}/snapshot
GET /proxy/protect/integration/v1/cameras/{cameraId}/rtsps-stream
GET /proxy/protect/integration/v1/subscribe/events
GET /proxy/protect/integration/v1/subscribe/devices
POST /proxy/protect/integration/v1/alarm-manager/webhook/{id}
```

Authentication uses a Protect API key in the `X-API-KEY` header. The production integration should prefer the local console path over cloud connector access for latency, privacy, and operational independence.

## System Role

The camera evidence system answers questions such as:

- What did a gate, yard, bay, or scanner-adjacent camera see around a given time?
- What text was visible in the frame?
- Which candidate container numbers, plates, seal numbers, or reference numbers were detected?
- Which camera and event produced the evidence?
- What did a reviewer confirm or reject?

It does not answer:

- Is this container complete?
- Is this record ready for audit?
- Should this analyst receive this assignment?
- Is this scanner image valid?
- Should ICUMS be updated?
- Should a container move between workflow stages?

Those remain NSCIM core responsibilities.

## Architecture

High-level flow:

```text
UniFi Protect Alarm Manager
  -> NSCIM camera evidence webhook endpoint
  -> camera evidence event table
  -> camera evidence queue
  -> Protect media fetch worker
  -> local frame store
  -> local OCR and optional vision fallback
  -> quarantined OCR result table
  -> camera evidence review UI
```

Preferred frame acquisition:

1. Alarm webhook arrives with alarm, trigger, device, and timestamp.
2. NSCIM records the event and immediately returns `202 Accepted`.
3. A background worker maps the Protect device to a configured camera.
4. The worker pulls one or more snapshots around the event time.
5. For cameras where exact timing matters, a local RTSPS rolling buffer supplies pre-event and post-event frames.
6. OCR runs locally first.
7. Low-confidence frames may be sent to a configured external vision provider only if enabled.
8. Results are stored as quarantined camera evidence.
9. Reviewers can confirm, reject, or correct extracted text.

## Isolation Model

### Data Isolation

Use new camera evidence tables only. Do not add camera OCR columns to core tables.

Required table family:

- `CameraEvidenceSources`
- `CameraEvidenceEvents`
- `CameraEvidenceFrames`
- `CameraEvidenceOcrResults`
- `CameraEvidenceReviewDecisions`
- `CameraEvidenceCoreLinkCandidates`
- `CameraEvidencePromotionRequests`
- `CameraEvidenceAuditLog`

`CameraEvidenceCoreLinkCandidates` may contain candidate references to core entities, but those references are advisory only. They must not be foreign-keyed in a way that creates lifecycle coupling with core workflow rows. If a core row is deleted, corrected, re-keyed, or migrated, camera evidence must remain an independent historical record.

### API Isolation

Use a new route family:

```http
/api/camera-evidence/*
/api/integrations/unifi-protect/*
```

Do not extend these route families for initial delivery:

```http
/api/scan-assets/*
/api/image-analysis/*
/api/ImageAnalysisDecision/*
/api/AuditReview/*
/api/container-completeness/*
/api/record-completeness/*
/api/cache/predictive/*
/api/EagleA25/*
```

### Service Isolation

Use a separate service namespace:

```text
NickScanCentralImagingPortal.Services.CameraEvidence
NickScanCentralImagingPortal.API.Controllers.CameraEvidence
NickScanCentralImagingPortal.Core.Entities.CameraEvidence
NickScanCentralImagingPortal.Core.DTOs.CameraEvidence
```

Do not inject camera evidence services into core services during initial delivery. The following classes must not depend on camera evidence:

- Container completeness services.
- Record completeness services.
- Image analysis orchestrator/services.
- Audit review services.
- ICUMS ingestion or submission services.
- Scan asset resolver/services.
- Predictive preload services.
- Assignment/readiness services.

The allowed dependency direction is one-way:

```text
CameraEvidence may read limited core reference data for display/search.
Core NSCIM may not read or write CameraEvidence data.
```

### UI Isolation

Camera evidence should have its own operator/admin surface, for example:

```text
/camera-evidence
/camera-evidence/events
/camera-evidence/review
/camera-evidence/settings
```

Initial UI must not place camera OCR results inside:

- The main container details tabs.
- Analyst image-analysis workbench.
- Audit review dialog.
- Completeness dashboards.
- ICUMS queues.
- Scanner image panels.

If linking is useful for investigation, it should be presented as "camera evidence candidate" inside the camera evidence module only.

## Core Data Promotion Protocol

No camera-derived data may influence core NSCIM until a promotion is approved.

A promotion must include:

1. The exact data field to promote.
2. The source camera/event/frame/OCR provenance.
3. The proposed core consumer.
4. The expected operator workflow.
5. Accuracy evidence from reviewed examples.
6. False-positive and false-negative impact analysis.
7. A rollback plan.
8. A feature flag name.
9. New tests covering enabled and disabled behavior.
10. Monitoring to prove whether the promoted data is being used.

Promotion states:

```text
Quarantined -> Candidate -> Reviewed -> Approved For Display -> Approved For Decision Support -> Approved For Automation
```

Initial implementation may support only:

```text
Quarantined
Candidate
Reviewed
```

The first allowed promotion, if approved later, should be display-only. It should not automate any workflow transition.

Feature flags:

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

`CoreDecisionSupportEnabled` and `CoreAutomationEnabled` must remain false until separately specified and approved.

## Functional Requirements

### Webhook Intake

Add a webhook endpoint:

```http
POST /api/integrations/unifi-protect/webhooks/alarm
```

Responsibilities:

- Authenticate webhook using a shared secret header, source allowlist, or both.
- Accept Protect Alarm Manager POST payloads.
- Preserve the raw payload.
- Extract alarm name, source device, trigger keys, event timestamp, and received timestamp.
- Generate an idempotency key.
- Persist an event row.
- Enqueue media capture work.
- Return quickly with `202 Accepted`.

Webhook intake must not fetch media synchronously.

### Protect API Client

Add a typed client around Protect Integration API.

Required operations:

- Get Protect application info.
- List cameras.
- Get camera snapshot.
- Get RTSPS stream URLs.
- Validate API key health.

Configuration:

```json
{
  "UniFiProtect": {
    "BaseUrl": "https://10.0.1.x/proxy/protect/integration",
    "ApiKey": "",
    "VerifySsl": true,
    "AllowedWebhookSourceCidrs": [],
    "WebhookSecretHeader": "X-NSCIM-Webhook-Secret",
    "WebhookSecret": "",
    "RequestTimeoutSeconds": 10
  }
}
```

Secrets must not be committed in `appsettings.json`.

### Camera Mapping

The system must map Protect camera IDs or MAC addresses to local operational context.

Example fields:

- Camera ID.
- MAC address.
- Display name.
- Location name.
- Purpose.
- Lane, gate, bay, or zone.
- Expected text type: container, plate, seal, mixed, unknown.
- Frame capture mode: snapshot, rolling buffer, both.
- OCR profile.
- Enabled flag.

This mapping is owned by camera evidence settings, not by scanner or container workflow config.

### Media Capture

Snapshot mode:

- Capture a live snapshot as soon as the webhook is processed.
- Optionally request high quality.
- Fall back to standard quality if high quality fails.
- Store original bytes with content type and SHA-256.

Rolling buffer mode:

- Use RTSPS stream URLs from Protect.
- Keep a short local buffer for selected cameras.
- Extract frames from `N` seconds before and after the event.
- Never write rolling buffer frames into core scanner image stores.

Recommended initial buffer:

- 10 seconds before event.
- 10 seconds after event.
- 1 to 3 frames per second.
- Retain raw buffer only as long as necessary for evidence extraction.

### OCR

Use local OCR first.

OCR output must include:

- Raw text.
- Normalized text.
- Candidate type: container number, plate, seal, reference, unknown.
- Confidence.
- Bounding boxes where available.
- Frame timestamp.
- OCR engine and version.
- Preprocessing operations.

Candidate validation must be advisory only.

For container-like strings, the system may validate format and checksum when applicable. A failed checksum must not delete the candidate; it should lower confidence and mark the reason.

### External Vision Fallback

External model calls are optional and disabled by default.

Fallback may run only when:

- `ExternalVisionFallbackEnabled=true`.
- Local OCR confidence is below a configured threshold.
- The frame is not excluded by privacy or data-residency policy.
- The request budget is available.

Fallback results remain quarantined and must not bypass review.

### Review UI

Provide a camera evidence review surface with:

- Event list.
- Camera/location filters.
- Time range filters.
- Trigger type filters.
- OCR confidence filters.
- Frame preview.
- Detected text overlays where available.
- Accept/reject/correct actions.
- Link candidate to container/reference as advisory only.
- Audit history for every correction.

Review actions should create immutable decision rows. Corrections must not overwrite raw OCR output.

### Search

Search should support:

- Camera/location.
- Time range.
- Raw OCR text.
- Normalized OCR text.
- Candidate type.
- Review status.
- Protect alarm name.
- Protect device ID.

Initial search must not query or mutate core workflow tables except read-only display lookups behind `CoreReadOnlyLookupEnabled`.

## Non-Functional Requirements

### Security

- Use least-privilege Protect API keys.
- Store API keys in secure configuration, not source-controlled files.
- Validate inbound webhook authenticity.
- Prefer TLS with certificate validation.
- If self-signed certificates are unavoidable, document certificate pinning or local trust-store setup.
- Rate-limit webhook intake.
- Log authentication failures without logging secrets.

### Privacy And Retention

Define retention separately for:

- Raw webhook payloads.
- Raw snapshots.
- Extracted frames.
- OCR text.
- Reviewed decisions.
- Audit logs.

Recommended defaults:

- Raw rolling buffer: hours or days, not indefinite.
- Event snapshots/frames: configurable, initially 30 to 90 days.
- OCR/review metadata: longer retention if operationally required.

### Reliability

- Webhook intake must be resilient to Protect bursts.
- Media fetch must retry transient failures.
- Duplicate webhook deliveries must collapse by idempotency key.
- Protect API failures must not affect NSCIM core health.
- Camera evidence workers must have independent health checks and metrics.

### Performance

- Do not run frame extraction on request threads.
- Bound concurrent media fetches per camera and globally.
- Bound OCR concurrency.
- Avoid continuous frame processing unless a camera is explicitly configured for rolling buffer mode.
- Prefer event-driven capture over constant analysis.

### Observability

Add metrics for:

- Webhooks received.
- Webhooks rejected.
- Queue depth.
- Media fetch success/failure.
- Snapshot latency.
- OCR latency.
- OCR confidence distribution.
- Review backlog.
- External vision calls and cost estimates.
- Promotion state counts.

Camera evidence metrics must be labeled separately from NSCIM core workflow metrics.

## Data Model Draft

### CameraEvidenceSources

- `Id`
- `Provider` = `UniFiProtect`
- `ProtectCameraId`
- `ProtectDeviceKey`
- `MacAddress`
- `DisplayName`
- `LocationName`
- `OperationalZone`
- `ExpectedTextType`
- `CaptureMode`
- `IsEnabled`
- `CreatedAt`
- `UpdatedAt`

### CameraEvidenceEvents

- `Id`
- `ProviderEventId`
- `IdempotencyKey`
- `SourceId`
- `AlarmName`
- `TriggerKey`
- `TriggerType`
- `ProtectDeviceKey`
- `EventTimestamp`
- `ReceivedAt`
- `RawPayloadJson`
- `ProcessingStatus`
- `ProcessingError`

### CameraEvidenceFrames

- `Id`
- `EventId`
- `SourceId`
- `CaptureMode`
- `FrameTimestamp`
- `RelativeOffsetMs`
- `StoragePath`
- `ContentType`
- `Sha256`
- `Width`
- `Height`
- `IsHighQuality`
- `ProtectSnapshotParametersJson`
- `CreatedAt`

### CameraEvidenceOcrResults

- `Id`
- `FrameId`
- `Engine`
- `EngineVersion`
- `RawText`
- `NormalizedText`
- `CandidateType`
- `Confidence`
- `ValidationStatus`
- `ValidationReasonsJson`
- `BoundingBoxesJson`
- `CreatedAt`

### CameraEvidenceReviewDecisions

- `Id`
- `OcrResultId`
- `ReviewerUserId`
- `Decision`
- `CorrectedText`
- `CorrectedCandidateType`
- `Notes`
- `CreatedAt`

### CameraEvidenceCoreLinkCandidates

- `Id`
- `EventId`
- `OcrResultId`
- `CandidateValue`
- `CandidateType`
- `CoreEntityType`
- `CoreEntityKey`
- `MatchConfidence`
- `MatchReason`
- `PromotionState`
- `CreatedAt`

This table is advisory only.

### CameraEvidencePromotionRequests

- `Id`
- `RequestedByUserId`
- `RequestedAt`
- `DataField`
- `CoreConsumer`
- `ProposedUse`
- `RiskAssessment`
- `AccuracyEvidence`
- `RollbackPlan`
- `FeatureFlag`
- `Status`
- `ApprovedByUserId`
- `ApprovedAt`

## API Draft

Webhook:

```http
POST /api/integrations/unifi-protect/webhooks/alarm
```

Admin/config:

```http
GET /api/camera-evidence/sources
POST /api/camera-evidence/sources
PATCH /api/camera-evidence/sources/{id}
POST /api/camera-evidence/sources/{id}/test-snapshot
GET /api/camera-evidence/protect/health
```

Operations:

```http
GET /api/camera-evidence/events
GET /api/camera-evidence/events/{id}
GET /api/camera-evidence/events/{id}/frames
GET /api/camera-evidence/frames/{id}/image
GET /api/camera-evidence/ocr-results
POST /api/camera-evidence/ocr-results/{id}/review
POST /api/camera-evidence/events/{id}/reprocess
```

Promotion:

```http
GET /api/camera-evidence/promotion-requests
POST /api/camera-evidence/promotion-requests
POST /api/camera-evidence/promotion-requests/{id}/approve
POST /api/camera-evidence/promotion-requests/{id}/reject
```

Promotion endpoints are administrative and should not exist until needed.

## Implementation Phases

### Phase 0: Proof Against Real Protect Console

- Generate a least-privilege Protect API key.
- Verify `GET /v1/meta/info`.
- Verify `GET /v1/cameras`.
- Verify snapshot capture for each camera class.
- Verify high-quality fallback behavior.
- Verify Alarm Manager POST payload against a local request bin or staging endpoint.
- Verify whether exact event timestamps are available in the payload.
- Decide which cameras need rolling buffer mode.

Exit criteria:

- Known camera ID/MAC mapping.
- Snapshot response saved locally for test cameras.
- Webhook payload captured and documented.
- Known SSL/certificate strategy.

### Phase 1: Quarantined Webhook And Event Store

- Add options and secure config.
- Add webhook controller.
- Add camera evidence tables.
- Add idempotency.
- Add event queue.
- Add basic admin health endpoint.
- Add metrics and logging.

Exit criteria:

- Webhook events persist.
- Duplicate webhook delivery is safe.
- Core NSCIM tables are untouched.
- Feature flags can disable the whole module.

### Phase 2: Media Fetch And Frame Store

- Add Protect typed client.
- Add camera source mapping.
- Add snapshot fetch worker.
- Add storage path policy.
- Add retry and failure handling.
- Add frame image endpoint under `/api/camera-evidence`.

Exit criteria:

- Event produces stored frame(s).
- Protect outage does not affect core health.
- Raw frame can be viewed from camera evidence UI/API only.

### Phase 3: Local OCR

- Add OCR engine wrapper.
- Add preprocessing profiles.
- Add OCR result table.
- Add candidate normalization and advisory validation.
- Add confidence thresholds.

Exit criteria:

- OCR result persists beside frame.
- Raw and normalized text are preserved separately.
- No core data writes occur.

### Phase 4: Review UI

- Add event/review pages.
- Add filters.
- Add frame preview and OCR overlay.
- Add accept/reject/correct workflow.
- Add audit history.

Exit criteria:

- Reviewer correction creates immutable review row.
- Review backlog and metrics are visible.
- Operators cannot accidentally promote data into core workflows.

### Phase 5: Optional Vision Fallback

- Add provider abstraction.
- Add budget controls.
- Add confidence-based fallback.
- Add cost metrics.
- Keep fallback disabled by default.

Exit criteria:

- External calls occur only when explicitly enabled.
- Costs are observable.
- Fallback output remains quarantined.

### Phase 6: Candidate Linking

- Add read-only lookup for possible core references.
- Add advisory candidate links.
- Add review confirmation for links.
- Keep display inside camera evidence module.

Exit criteria:

- Link candidates can be reviewed.
- No core UI or workflow consumes them.
- `CoreReadOnlyLookupEnabled=false` removes core lookup behavior.

### Phase 7: Future Promotion

This phase is intentionally out of scope for initial delivery.

Any promotion must use the protocol above and start with display-only use.

## Testing Requirements

Doc-only planning tests:

- Route ownership review: new endpoints stay under `/api/camera-evidence` and `/api/integrations/unifi-protect`.
- Dependency review: core services do not inject camera evidence services.
- Migration review: new tables do not alter core workflow tables.

Automated tests when implemented:

- Webhook authentication rejects missing/invalid secrets.
- Webhook intake stores payload and returns `202`.
- Duplicate idempotency key does not create duplicate event work.
- Protect client sends `X-API-KEY`.
- Snapshot fallback works when high quality fails.
- Worker failure records error without throwing through host.
- OCR result persists without updating core tables.
- Review correction preserves raw OCR.
- Feature flags disable ingestion, media fetch, OCR, fallback, and core lookup independently.
- Promotion flags default to false.

Integration tests:

- Staging Protect console smoke test.
- Local image fixture OCR test.
- End-to-end webhook-to-review flow in staging.
- Core regression test sweep proving no completeness, assignment, image-analysis, or audit side effects.

## Deployment And Operations

Initial deployment should be dark:

```json
"CameraEvidence": {
  "Enabled": false
}
```

Controlled enablement order:

1. Enable config and health checks only.
2. Enable webhook intake for one test alarm.
3. Enable media fetch for one test camera.
4. Enable OCR for one camera.
5. Enable review UI for admins.
6. Expand camera mappings.

Rollback:

- Disable `CameraEvidence:Enabled`.
- Disable Protect Alarm Manager webhook action.
- Stop camera evidence workers.
- Keep stored evidence for investigation unless retention policy requires purge.

## Acceptance Criteria

The system is acceptable when:

- It can receive Protect webhooks.
- It can fetch frames for configured cameras.
- It can run OCR and store results.
- It can expose a review workflow.
- It has observable metrics and failure states.
- It is disabled by default.
- It cannot change core NSCIM workflow data.
- Core services have no dependency on camera evidence.
- Any future use of camera evidence inside NSCIM core is blocked by documented promotion gates and disabled feature flags.

## Explicit Non-Goals

- Replacing existing NSCIM scanner ingestion.
- Replacing `ScanAssetsController`.
- Feeding scanner image resolution.
- Automating completeness or clearance decisions.
- Automating audit submission.
- Assigning analyst work based on camera OCR.
- Running continuous AI analysis on all video.
- Storing all camera footage in NSCIM.
- Using cloud connector as the default production path.

## References

- UniFi Protect developer portal: https://developer.ui.com/protect
- Protect webhook help: https://help.ui.com/hc/en-us/articles/25478744592023-Send-UniFi-Protect-Alerts-to-Web-Services-using-Webhooks
- UniFi Alarm Manager help: https://help.ui.com/hc/en-us/articles/27721287753239-UniFi-Alarm-Manager-Customize-Alerts-Integrations-and-Automations-Across-UniFi
- Protect 7.1.60 release notes: https://community.ui.com/releases/UniFi-Protect-Application-7-1-60/470e3f55-27fe-4437-918f-1983562f459a
