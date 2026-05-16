# Scan Image Identity System-Wide Implementation Plan

Date: 2026-05-16

## Executive Summary

The current ASE failure for `TEMU2527526, TIIU2732427` is not only a scanner-tab display bug. It is a symptom of a broader identity problem: several parts of NSCIM still infer image identity from `ContainerNumber`, while one physical scan can contain one or more containers and one container can participate in multiple operational records.

The enduring fix is to make image/source-scan identity first-class across acquisition, ingestion, completeness, image analysis, and ICUMS submission. Container numbers should remain searchable metadata and declaration-matching keys, but they should not be the canonical identity for a physical image.

## Current Failure Pattern

- ASE ingestion preserves the raw source row in `AseScans.ContainerNumber` and `OriginalScanRecord.OriginalContainerNumbers`.
- ASE queue publishing already splits comma-pair containers into child queue rows so completeness can process one container at a time.
- Recovery, lookup, scanner-detail, resolver, and analysis paths still sometimes rediscover images by exact or semi-normalized container strings.
- For two-container scans, the raw image belongs to one source scan, but downstream rows are split into two child container workflows. Without a durable source image reference, later stages can lose the link back to the image.

The short-term comma-pair patch can reduce immediate lookup misses, but it cannot be the final fix because it keeps the system dependent on parsing human/source container labels.

## Canonical Principle

Use this identity rule everywhere:

1. A physical scanner acquisition has a stable source identity.
2. A source acquisition can have one or many image assets or renditions.
3. A source acquisition can be linked to one or many containers.
4. A container can be linked to one or many declarations/records.
5. Completeness, analysis, and submission should carry the source image identity forward instead of rediscovering the image from `ContainerNumber`.

## Proposed Canonical Model

### Source Scan

Use `OriginalScanRecord` as the near-term canonical source scan because it already exists and already preserves raw scanner output. Extend or wrap it with a formal scan-asset identity if needed.

Required identity fields:

- `OriginalScanRecordId`: canonical source scan ID for FS6000 and ASE.
- `ScannerType`: `ASE`, `FS6000`, `EagleA25`, future scanners.
- scanner-native ID: `AseScans.InspectionId`, `FS6000Scans.Id`, `EagleA25Scans.Id` or source accession IDs.
- raw source container label: preserved for audit only.
- scan time and ingestion time.

### Image Asset

Introduce a first-class `ScanImageAsset` concept when a source scan can have multiple image files or renditions.

Suggested fields:

- `Id` as stable GUID or bigint primary key.
- `OriginalScanRecordId` nullable for scanners already using that table.
- `ScannerType`.
- `ScannerNativeId`.
- `AssetKind`: `source`, `thumbnail`, `split-crop`, `fallback`, `annotated`, `submission`.
- `StorageKind`: database blob, file path, local cache, proxy, external path.
- `SourcePath`, `LocalPath`, `MimeType`, `FileSizeBytes`, `Hash`.
- `CreatedAtUtc`, `UpdatedAtUtc`.

For Eagle A25, this can bridge to existing `EagleA25ScanAsset` rows instead of replacing them immediately.

### Container Link

Introduce `ScanAssetContainerLink` or `SourceScanContainerLink`.

Suggested fields:

- `Id`.
- `OriginalScanRecordId` or `ScanImageAssetId`.
- `ContainerNumber`.
- `NormalizedContainerNumber`.
- `Position`: `single`, `left`, `right`, `unknown`.
- `Confidence`: `source`, `split-model`, `operator-confirmed`, `backfill`.
- `SplitJobId`, `SplitResultId` when applicable.
- optional `BoeDocumentId`, `RecordExpectedContainerId`.
- `CreatedAtUtc`, `UpdatedAtUtc`.

This is the table that allows `TEMU2527526` and `TIIU2732427` to each point to the same source image while still allowing individual declarations and analyst decisions.

## System-Wide Flow

### 1. Image Acquisition And Scanner Ingestion

Required changes:

- ASE, FS6000, and Eagle ingestion must create or resolve a canonical source scan identity.
- Ingestion must write container-link rows for every container token discovered from the scanner metadata.
- Raw scanner labels remain stored for audit, but child workflow rows should reference the source scan/link identity.
- Recovery/backfill services must use the same identity writer as primary ingestion.

Affected areas:

- `AseDatabaseSyncService`
- FS6000 ingestion and image ingestion paths
- `EagleA25SyncService`
- `OriginalScanRecord`
- `AseScans`, `FS6000Scans`, `EagleA25Scans`, `EagleA25ScanAssets`
- `ContainerScanQueuePublisherService`
- `QueueRecoveryService`

### 2. Completeness Processing

Required changes:

- `ContainerScanQueue` should carry `OriginalScanRecordId` or `ScanAssetId` plus the individual `ContainerNumber`.
- `ContainerCompletenessStatus` should retain `ContainerNumber` for BOE matching but carry source scan/image identity for image evidence.
- Image evidence should be determined through the source scan/container link, not exact container matching against scanner tables.
- `RecordExpectedContainer` should store the source image/link when a scan event binds the expected container.

Affected areas:

- `ContainerScanQueue`
- `ContainerCompletenessStatus`
- `RecordCompletenessStatus`
- `RecordExpectedContainer`
- `ContainerCompletenessService`
- `RecordBuildingService`
- `RecordReconciliationWorker`
- `ContainerCompletenessOrchestratorService`

### 3. Image Analysis

Required changes:

- `AnalysisRecord` should carry `OriginalScanRecordId` or `ScanImageAssetId`.
- Split identity should remain attached to the source image via `SplitJobId`, `SplitResultId`, and `SplitPosition`.
- Group creation should use record/declaration identity plus linked source image identity.
- Analysts should open images by source image ID or analysis record ID, with container-number lookup only as a compatibility alias.

Affected areas:

- `AnalysisRecord`
- `AnalysisGroup`
- `ImageAnalysisOrchestratorService`
- `TwoContainerSplitIntakeService`
- `ImageAnalysisDecisionController`
- `ImageAnalysisController`
- `ScanAssetsController`
- `ScanAssetResolver`

### 4. ICUMS Submission

Required changes:

- Submission payload generation should use analysis/completeness rows that already carry source image identity.
- `ICUMSSubmissionQueue.ImagePaths` should be backed by source image asset IDs or generated submission renditions, not only by container-derived image path strings.
- Submission audit should record which source scan/image asset was submitted for each declaration/container.

Affected areas:

- `ICUMSSubmissionQueue`
- `ICUMSSubmissionService`
- `ContainerDataMapperService`
- `ImageAnalysisDecisionController`
- `AnalysisSubmission`
- payload/outbox writers under `Data\ICUMS\Outbox`

## Compatibility Layer

Compatibility is still required during migration:

- Keep container-number endpoints and scanner tabs working.
- Allow `/api/scan-assets/...` and container-details endpoints to resolve by container number, but implement that as a lookup against canonical source/container links.
- Keep tokenized matching helper logic as a defensive fallback only.
- Instrument fallback usage so the team knows when legacy container-string resolution can be retired.

## Phased Implementation

### Phase 0: Guardrail And Inventory

Goal: prove all current identity paths and prevent new string-only image flows.

Work:

- Inventory every write/read of scanner image identity.
- Add tests that reproduce:
  - ASE pair source scan with two child containers.
  - ASE single-container source scan.
  - FS6000 source scan with image.
  - Eagle A25 source scan with asset fallback.
  - CMR with image but no declaration entering image analyst review.
- Add diagnostic report for rows where `HasImageData=true` but no durable source image identity exists.

Risk: low.

### Phase 1: Schema Additive Migration

Goal: add durable identity without breaking existing behavior.

Work:

- Add source image/container link table.
- Add nullable identity columns to:
  - `ContainerScanQueues`
  - `ContainerCompletenessStatuses`
  - `RecordExpectedContainers`
  - `AnalysisRecords`
  - `ICUMSSubmissionQueues` or related submission payload metadata.
- Add indexes for source scan ID, normalized container number, scanner type, and split IDs.

Risk: medium because it touches core tables, but additive and nullable.

### Phase 2: Shared Identity Writer

Goal: all ingestion and recovery paths write identity the same way.

Work:

- Build `IScanIdentityService`.
- Centralize:
  - token extraction.
  - raw label preservation.
  - source scan lookup/create.
  - container-link upsert.
  - queue item creation with source identity.
- Update ASE, FS6000, Eagle, queue recovery, and backfill paths to call it.

Risk: medium.

### Phase 3: Completeness Cutover

Goal: completeness image evidence comes from canonical identity.

Work:

- Update completeness processing to use source/container links for `HasImageData`.
- Bind `RecordExpectedContainer` rows to source scan/image identity when scan evidence arrives.
- Update record rollups to check linked images, not only container-number exact matches.

Risk: high because this drives workflow progression.

### Phase 4: Analysis Cutover

Goal: image analyst review opens the correct physical image every time.

Work:

- Populate `AnalysisRecord` with source image identity.
- Update split intake to write source identity into all sibling records.
- Update `ScanAssetsController` and `ScanAssetResolver` so analysis ID/source ID are primary and container number is fallback.
- Update frontend scanner tabs and analyst review pages to pass source/analysis identity where available.

Risk: high because analyst workflow and image display are user-facing.

### Phase 5: ICUMS Submission Cutover

Goal: submission payloads have auditable source image lineage.

Work:

- Generate image paths/renditions from source image identity.
- Persist submitted image/source identity in queue and submission audit.
- Keep legacy `ImagePaths` JSON until downstream consumers are proven to use the new metadata.

Risk: medium-high because it affects external submission behavior.

### Phase 6: Backfill And Retirement

Goal: remove dependency on container-string heuristics after evidence.

Work:

- Backfill links for existing ASE raw comma-pair scans.
- Backfill links for existing FS6000 and Eagle rows.
- Backfill analysis and completeness identity where deterministic.
- Monitor fallback/container-string lookup usage.
- Retire fallback only when telemetry shows it is unused and diagnostics show no unlinked active image rows.

Risk: medium.

## Immediate Handling Of Current Partial Patch

The current branch contains a narrow compatibility patch in:

- `ContainerNumberListMatcher`
- `ContainerDetailsController`
- `ScanAssetResolver`

This should not be treated as the enduring fix. It can be kept only as a temporary compatibility shim after the canonical identity plan begins, or parked/reverted if the schema-first migration starts immediately.

## Acceptance Criteria

- A scanner row with a single physical image and two containers creates one source identity and two container links.
- Each child container can load scanner details and image evidence without querying the raw comma-joined label as its identity.
- `ContainerCompletenessStatus.HasImageData` is true because of linked image identity, not string coincidence.
- `RecordExpectedContainer` can move to `Ready` using linked image evidence.
- `AnalysisRecord` carries source image identity.
- Image analyst review opens the correct image using analysis/source identity.
- ICUMS submission output can trace each submitted image back to source scan/image identity.
- Container-number lookup remains available but is visibly a compatibility lookup.
- Tests cover ASE pair, ASE single, FS6000, Eagle, CMR-with-image, and submission lineage.

