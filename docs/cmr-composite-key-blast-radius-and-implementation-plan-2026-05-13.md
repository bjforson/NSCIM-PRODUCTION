# CMR Composite-Key Blast Radius And Implementation Plan

Date: 2026-05-13
Status: Implementation started; guarded CMR record-completeness/intake path implemented, production flag remains disabled

Tracking TODO: `docs/cmr-composite-key-giant-implementation-todo-2026-05-13.md`

## Implementation Progress

- 2026-05-14: Source-scan identity correction added before further CMR UI/image work.
  - The `40426305424_W1` investigation showed a second identity split: the analyst record can be keyed to one real container while the physical ASE source image is stored under the original two-container scan string.
  - The tactical comma-separated container lookup must not become the long-term fix.
  - The plan now requires a first-class source scan identity (`OriginalScanRecordId` / `SourceScanId`, plus `SplitJobId` and `SplitResultId`) so scanner tabs, image tabs, split-choice views, and preloads resolve the physical image without using comma-joined container numbers as identifiers.
- 2026-05-13: ICUMS submission state-sync prerequisite deployed API-only.
  - Acknowledged ICUMS payload reconciliation ran successfully.
  - `ICUMS-ACK-RECONCILE` updated 426 stale CCS rows from 619 acknowledged files.
  - Known submitted containers `CAXU9272575` and `MRSU8158853` now show `WorkflowStage=Submitted`.
- 2026-05-13: Phase 1/2 started.
  - Added off-by-default feature flag: `CmrCompositeProgression:Enabled=false`.
  - Added route-safe CMR operational key helper.
  - Extended completeness policy with optional CMR composite-key inputs while preserving default old behavior.
- 2026-05-13: Guarded container-completeness wiring added.
  - `ContainerCompletenessService` now reads `CmrCompositeProgression:Enabled`.
  - Step 1 and Step 2 can derive a `CMR-*` operational `GroupIdentifier` from rotation number, container number, and BL number when the flag is enabled.
  - Existing `CMR-*` group identifiers are preserved during Step 2 rechecks and data-integrity repair to avoid detaching analysis records.
  - Current config keeps the feature disabled until record completeness and duplicate-protection phases are implemented.
- 2026-05-13: Test/guardrail phase added.
  - `ContainerCompletenessPolicyTests` cover CMR rows becoming `Complete` / `ImageAnalysis` only when the feature flag is enabled and all composite-key parts are present.
  - `CmrCompositeKeyHelperTests` cover route-safe key creation, normalization, missing fields, and operational-key recognition.
  - `StateOwnershipGuardrailTests` now lock the cross-service rollout intent: record completeness must include CMR composite records behind the feature flag, reconciliation must pass CMR key inputs into the policy, record-anchored intake must use `GroupType = "CMR"`, and CMR duplicate protection must key records/groups through the synthetic operational key plus `RecordCompletenessStatusId`.
- 2026-05-13: Guarded record-completeness/intake implementation added by agent team.
  - `RecordCompletenessBuilder` can build CMR records keyed by `CMR-*`.
  - `RecordBuildingService` can build/update CMR records from rotation number, container number, and BL number when the caller passes the feature gate.
  - `RecordReconciliationWorker` has a gated CMR composite safety-net pass.
  - `IcumJsonIngestionService` can trigger event-driven CMR record building when the feature flag is enabled.
  - `ImageAnalysisOrchestratorService` creates CMR-backed groups as `GroupType="CMR"` and checks for existing CMR groups before creating later real-declaration duplicates.
  - `ContainerStatusReconciliationService` now passes CMR composite policy inputs and can derive the `CMR-*` group identifier when enabled.
  - Focused guardrails/tests passed and Services/API projects build with existing warnings only.
- 2026-05-13: Disposable local staging verification passed for `PIDU4444900`.
  - A local PostgreSQL staging clone was built from production schema plus the one target CMR container record; production data was read only.
  - The first staging run exposed a real intake edge: blank-string `ContainerCompletenessStatus.GroupIdentifier` values were not back-stamped, only `NULL` values were.
  - Record-anchored intake now back-stamps both `NULL` and blank group identifiers, including the existing-group path.
  - The staging harness now matches production Npgsql timestamp behavior so assignment readiness checks are faithful.
  - Final harness result: CMR `RecordCompletenessStatus=Ready`, `RecordExpectedContainer=Ready`, `AnalysisGroup=AnalystAssigned`, `GroupType=CMR`, active Analyst assignment present, materialized queue entry present, and duplicate counts all exactly one.
  - Focused core guardrails passed: 30 passed, 0 failed.
  - Focused service queue/CMR regressions passed: 5 passed, 0 failed.
  - Services, API, and CMR staging runner builds passed with 0 errors and the existing warning set only.

## Phase Progress And Rollout Checklist

- Done: CMR operational key helper exists and emits route-safe `CMR-*` keys.
- Done: Container completeness policy can allow CMR with scanner + ICUMS + image + complete composite key when `CmrCompositeProgression:Enabled` is true.
- Done: Container-completeness creation/recheck preserves existing CMR operational keys instead of overwriting them with blank declarations.
- Done: Source guardrails cover record-completeness, reconciliation, intake, and duplicate-protection behavior.
- Done: Record building/reconciliation can create/update CMR `RecordCompletenessStatus` and `RecordExpectedContainer` rows behind `CmrCompositeProgression:Enabled`.
- Done: Container status reconciliation computes and passes CMR composite inputs instead of treating blank CMR declaration as incomplete.
- Done: Record-anchored image intake materializes CMR analysis groups with `GroupType = "CMR"`, real child container numbers, and `RecordCompletenessStatusId`.
- Done: Duplicate protection checks for existing CMR-backed records/groups before creating later real-declaration analysis groups.
- Done: Disposable local staging run with `CmrCompositeProgression:Enabled=true` and Decision Agent absent/disabled verified one known production CMR container through assignment queue materialization.
- Remaining: Review the final diff, then perform API-only deployment with production `CmrCompositeProgression:Enabled=false`; enable the CMR flag only in a controlled production pilot after post-deploy health and assignment queues stay clean.

## Controlled Verification Run: 2026-05-13

Sequential recommendation run status:

- Discovery found only the root production deploy script, `Deploy.ps1`; no separate NSCIM staging deploy target or staging database was exposed in this workspace.
- The API does have a staging safety switch: `StagingVerification:DisableBackgroundServices=true`.
- API startup still runs `PermissionSeeder`, so starting a staging-mode API against `nickscan_production` is still a write action and was not performed.
- Production config remains safe: `CmrCompositeProgression:Enabled=false`.
- Database control check was read-only and showed `DecisionAgentSettings.Enabled=false`, `AnalysisSettings.Enabled=true`, and `AssignmentMode=Auto`.
- Focused core guardrails passed: 29 passed, 0 failed.
- Focused service/queue regressions passed: 11 passed, 0 failed.
- Services build passed with 0 errors and existing NU1510 warnings only.
- API build passed with 0 errors and existing NU1510 warnings only.
- `Deploy.ps1 -ApiOnly -DryRun` confirmed it targets `NSCIM_API` in `C:\Shared\NSCIM_PRODUCTION\publish\API`; no deploy was performed.

Read-only dry-run data checks:

- ICUMS downloads CMR rows: 111,404.
- CMR rows with complete container + rotation + BL keys: 111,404.
- CMR rows missing container, rotation, or BL: 0.
- Duplicate CMR composite keys: 0.
- Application CCS CMR rows: 1,663.
- CCS CMR rows that would be ready if the feature flag were enabled: 1,330.
- Ready CMR rows currently blocked by `AwaitingDeclaration`: 1,327.
- Existing synthetic CMR `RecordCompletenessStatus` rows: 0.
- Existing synthetic CMR `AnalysisGroup` rows: 0.
- Existing CMR-backed `AnalysisRecord` rows: 0.
- Existing active CMR assignments: 0.
- Duplicate CMR RCS keys: 0.
- Duplicate CMR AG keys: 0.
- Duplicate AR `(GroupId, ContainerNumber)` rows across the app: 0.
- Duplicate active assignments across the app: 0.

Known container verification:

- `PIDU4444900` has one ICUMS downloads CMR row: BOE document id `116314`, rotation `26PIL000026`, BL `SHSE60424600`.
- Its CMR operational key would be `CMR-C40FEA9B3C7FA383450D`.
- Application CCS row `6619` has scanner + ICUMS + image present, scanner `ASE`, inspection `84722`, BOE document id `116314`.
- Current CCS state is `Status=AwaitingDeclaration`, `WorkflowStage=Pending`, `GroupIdentifier` blank.
- No `RecordCompletenessStatus`, `RecordExpectedContainer`, `AnalysisGroup`, `AnalysisRecord`, or active assignment exists yet for this container.

Disposable local staging result:

- A local PostgreSQL staging clone on port `55432` was created from production schemas and targeted `PIDU4444900` rows only.
- The clone was run with `CmrCompositeProgression:Enabled=true`.
- First run created the CMR record/group but no assignment because blank `GroupIdentifier` values were not being back-stamped; this would have left `ReadyGroupsCacheService` unable to see the CCS row by the new CMR group key.
- The code now back-stamps `ContainerCompletenessStatus.GroupIdentifier` when it is either `NULL` or blank, and does so for both new and existing record-backed groups.
- Final run passed with:
  - `rcs_status=Ready`, `workflow=ImageAnalysis`
  - `rec_status=Ready`
  - `ag_status=AnalystAssigned`, `grouptype=CMR`, linked to `RecordCompletenessStatusId=1`
  - `ar_status=Ready`, `container=PIDU4444900`
  - `assignment_state=Active`, assigned to `cmr.stage.analyst`
  - `queue_entry=present`
  - `ccs_groupidentifier=CMR-C40FEA9B3C7FA383450D`
  - duplicate counts: `rcs=1`, `ag=1`, `ar=1`, `active_assignments=1`, `queue_entries=1`

Post-fix verification commands:

- `dotnet run --project tools\cmr-staging-runner\CmrStagingRunner.csproj -- PIDU4444900`
- `dotnet test tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj --filter "FullyQualifiedName~ContainerCompletenessPolicyTests|FullyQualifiedName~CmrCompositeKeyHelperTests|FullyQualifiedName~StateOwnershipGuardrailTests|FullyQualifiedName~CmrCompositeRecordIntakeGuardrailTests" --no-restore`
- `dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "FullyQualifiedName~QueueProgressionRegressionTests|FullyQualifiedName~CmrComposite" --no-restore`
- `dotnet build src\NickScanCentralImagingPortal.Services\NickScanCentralImagingPortal.Services.csproj --no-restore`
- `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore`
- `dotnet build tools\cmr-staging-runner\CmrStagingRunner.csproj --no-restore`

Execution gate:

- No production mutation was performed during this CMR verification pass.
- Local staging mutation was limited to the disposable clone and one target CMR container.
- Next safe step is code review, then API-only deploy with the production CMR feature flag still disabled. The production CMR flag should be enabled only for a monitored pilot window after the deployed baseline is healthy.

## Executive Summary

The fix is not only a CMR validation change and not only an image-analysis queue change.

CMR progression is blocked because the old system treats `DeclarationNumber` / BOE as the only valid record identity. The new ICUMS agreement lets us progress CMR using a composite key:

- Rotation number / manifest number
- Container number
- BL number

The durable implementation must introduce that composite key into the normal record-backed flow:

`ICUMS CMR data + scanner + image -> ContainerCompletenessStatus -> RecordCompletenessStatus -> RecordExpectedContainer -> AnalysisGroup -> AnalysisRecord -> AnalysisQueueEntry -> analyst/audit -> ICUMS submission`

Bypassing record completeness would create a second path and repeat the assignment failures we just finished stabilizing.

The split-image flow adds one more rule: logical container identity and physical scan-image identity must be separated. A two-container ASE scan may create one analysis record per real container, but the original image should still be addressed through a stable source scan identity and split job, not through `MSMU1683356, MRKU8254509` or any other comma-joined pseudo-container string.

## Current Production Impact

From the data checks already performed:

- All checked CMR records have container number, rotation number, and BL number.
- The CMR composite key is unique across checked records.
- The composite key length fits current `RecordCompletenessStatus.DeclarationNumber` and `AnalysisGroup.GroupIdentifier` limits.
- Roughly 1,323 production CMR rows currently have scanner + ICUMS + image but remain blocked at `AwaitingDeclaration`.
- Some ASE rows use comma-joined composite scan strings. Those must be split/mapped to real container identities before creating analysis records or submission payloads.
- `40426305424_W1` proves the split identity issue: the active analysis record is for `MSMU1683356`, but the ASE scan row is stored as `MSMU1683356, MRKU8254509`; exact-match scanner/image endpoints fail, the UI reports `0B`, while the split service already has a completed job and usable crop images.

## Source Scan Identity And Split Flow Update

### Problem Statement

The system currently reuses `ContainerNumber` for too many meanings:

- The real container being analysed.
- A scanner-source lookup key.
- A two-container original scan label.
- A split-service job label.
- A cache key and route parameter.

That worked while most scanner records were one image to one container. It breaks when one physical image contains two containers and the split process correctly creates one child analysis record per container.

### Canonical Identities

- `ContainerNumber`: the real logical container that progresses through completeness, assignment, decision, audit, and submission.
- `OriginalScanRecordId` / `SourceScanId`: the physical scanner-source record that owns the original image, including two-container originals.
- `SplitJobId`: the split-service processing job for the original image.
- `SplitResultId`: the candidate crop pair the analyst can choose from.
- `AnalysisRecordId`: the analyst workflow row for one logical container.
- Optional future abstraction: `ScanAssetId`, a scanner-neutral wrapper over ASE, FS6000, and any future source image store.

### Contract

- Scanner and image tabs must resolve from logical container context to a source scan identity before loading image bytes or metadata.
- Split-choice views must load by `AnalysisRecordId` or `SplitJobId`; the original image loads by source scan identity, and candidate crops load by split job/result/side.
- Comma-joined container strings are allowed only as legacy scanner-source labels and compatibility inputs. They must not become `AnalysisRecord.ContainerNumber`, `AnalysisGroup.GroupIdentifier`, submission identity, or primary UI route identity.
- Predictive preload and view caches must include source scan identity, split job, and split result where image/split data is cached.
- Negative cache entries such as "no scanner data" or "no image data" must include the resolver reason and must not be written before source-scan resolution has been attempted.

### Proposed Endpoint Shape

- `GET /api/scan-assets/resolve?containerNumber={container}&groupIdentifier={group}`
- `GET /api/scan-assets/{sourceScanId}/image`
- `GET /api/image-splitter/jobs/{splitJobId}/original`
- `GET /api/image-splitter/jobs/{splitJobId}/results/{splitResultId}/image/{side}`
- `GET /api/image-analysis/records/{analysisRecordId}/split-options`

Existing container-based endpoints can remain as compatibility aliases, but they should call the resolver and return resolution metadata.

### Data Model And Backfill

- Add an explicit source-scan-to-container mapping, for example `OriginalScanRecordContainers`, with:
  - `OriginalScanRecordId`
  - `ContainerNumber`
  - `PositionHint`
  - `CreatedAtUtc`
  - optional `SplitJobId`
- Backfill the mapping from `OriginalScanRecords.OriginalContainerNumbers`, `AseScans.ContainerNumber`, and existing split job container lists.
- Keep `ScanAssets` as an optional later table if we want a scanner-neutral asset abstraction; the immediate fix can start with a resolver and mapping table.

### Resolver Order

1. Exact FS6000 match.
2. Exact ASE match.
3. Tokenized ASE/original scan match where the source value contains multiple container numbers.
4. Analysis-record split job match.
5. Record-completeness/wave group context to break ties.
6. Return an ambiguous result instead of choosing arbitrarily when multiple physical sources remain possible.

Every result should include `ResolvedBy`, `SourceScannerType`, `SourceScanId`, optional `SplitJobId`, optional `SplitResultId`, and whether the source was exact, tokenized, or ambiguous.

## Blast Radius Rings

### Ring 1: Must Modify

These services or models must change for the feature to work.

#### Core Identity And Policy

- `src/NickScanCentralImagingPortal.Core/Helpers/ContainerCompletenessPolicy.cs`
- New helper: `src/NickScanCentralImagingPortal.Core/Helpers/CmrCompositeKeyHelper.cs`
- Possible touch: `src/NickScanCentralImagingPortal.Core/Helpers/GroupIdentifierHelper.cs`
- Possible touch: `src/NickScanCentralImagingPortal.Core/Helpers/WorkflowStageStatusHelper.cs`
- Possible touch: `src/NickScanCentralImagingPortal.Core/Helpers/AnalysisStatusValidator.cs`

Why affected:

- Current policy intentionally blocks declaration-less CMR as `AwaitingDeclaration`.
- We need one canonical place to validate and build CMR operational keys.
- If key format includes characters that routes or date-suffix normalization dislike, `GroupIdentifierHelper` must be protected.

#### Entity Model And EF Mapping

- `src/NickScanCentralImagingPortal.Core/Entities/ContainerCompletenessStatus.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/RecordCompletenessStatus.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/RecordExpectedContainer.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/Analysis/AnalysisGroup.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/Analysis/AnalysisRecord.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/Analysis/AnalysisQueueEntry.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Data/ApplicationDbContext.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Data/IcumDownloadsDbContext.cs`
- New or updated EF migration if we add indexes or explicit CMR identity columns.

Why affected:

- The existing model says record identity is `DeclarationNumber`.
- We can reuse the existing `DeclarationNumber`/`GroupIdentifier` columns for a synthetic CMR operational key, but the semantics and comments must be corrected.
- We should add/verify a performant lookup on CMR `ClearanceType + ContainerNumber + RotationNumber + BlNumber`.

#### Source Scan Identity And Split Resolver

- New core contract: `IScanAssetResolver` or equivalent.
- New DTOs: `ScanAssetResolution`, `SplitOptionContext`, and source-scan mapping models.
- New or updated entity: `OriginalScanRecordContainer` or equivalent mapping table.
- `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeWorker.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/ASEImagePipeline.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/ASESourceRetriever.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/Kernel/ScannerTypeDetector.cs`
- `src/NickScanCentralImagingPortal.Services/ImageProcessing/ImageProcessingService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ContainerDetailsController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageProcessingController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs`

Why affected:

- Exact container matches fail when ASE stores the original image under a two-container string.
- Split jobs already know the original combined image and crop candidates, but the UI often asks for image data by logical container only.
- The source-image resolver becomes the shared answer for scanner tab, image tab, split-choice dialog, fullscreen document icon, cache preload, and future scanner integrations.
- This must be done before broad CMR rollout because CMR groups will expose more scanner/source edge cases.

#### Container Completeness

- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerDataMapperService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerStatusReconciliationService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ManualBOESelectivityService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/PostICUMSValidationService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ConsolidatedCargoCompletenessHelper.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/MultiContainerValidationService.cs`

Why affected:

- Completeness creation and recheck both currently derive `GroupIdentifier` from declaration number.
- Preventive correction logic currently assumes `BOEDocumentId` implies declaration-number `GroupIdentifier`.
- Reconciliation and manual BOE update paths can mark rows `Complete`/`ImageAnalysis`; they must not regress CMR into old semantics.
- CMR should no longer be treated as incomplete merely because `DeclarationNumber` is blank.

#### Record Completeness

- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs`
- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs`
- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessBuilder.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/RecordCompletenessController.cs`
- `src/NickScanWebApp.New/Pages/Completeness/RecordCompleteness.razor`

Why affected:

- Current worker only ingests `IM` / `EX` rows with nonblank `DeclarationNumber`.
- Current builder uses the first BOE `DeclarationNumber` as the parent record identity.
- The API and UI display `DeclarationNumber` as a BOE number, which will be misleading if we store a synthetic CMR key there.
- This is the central bridge to assignments.

#### ICUMS Ingestion And CMR Upgrade

- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumJsonIngestionService.cs`
- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumPipelineOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/IcumApi/ICUMSDownloadQueueService.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Repositories/IcumDownloadsRepository.cs`
- `src/NickScanCentralImagingPortal.Core/Interfaces/IIcumDownloadsRepository.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerValidation/ICUMSDataCacheService.cs`

Why affected:

- CMR ingest already validates composite key and upgrades CMR to BOE by container + rotation + BL.
- When a CMR has already entered analysis and later upgrades to IM/EX, the cascade must update or link record completeness and analysis state without duplicate assignments or duplicate submission.
- ICUMS cache invalidation after CMR upgrade must account for the CMR operational key.

#### CMR Validation And Redownload

- `src/NickScanCentralImagingPortal.Services/Validation/CMRValidationService.cs`
- `src/NickScanCentralImagingPortal.Services/Validation/CMRRedownloadService.cs`
- `src/NickScanCentralImagingPortal.Services/Validation/CMRRedownloadBackgroundService.cs`
- `src/NickScanCentralImagingPortal.Services/Validation/CMRMetricsRecorderService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/CMRValidationController.cs`
- `src/NickScanWebApp.New/Pages/Completeness/CmrValidation.razor`

Why affected:

- CMR validation becomes the canonical readiness rule for progression, not just a data-quality report.
- The redownload queue should only target missing-key or stale CMR rows, not valid CMR rows that are now expected to progress.
- Metrics should distinguish valid-and-progressable, missing-key, blocked, and already-promoted CMR.

#### Image Analysis Intake And Queue

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionSideEffectsService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ZombieAnalysisGroupSweeperService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/UserReadinessSyncService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionAgent/DecisionAgentWorker.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionAgent/DecisionAgentScoringEngine.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionAgent/Evaluators/MultipleHouseBLEvaluator.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Data/AnalysisGroupStateMachine.cs`

Why affected:

- Record-anchored intake creates groups from `RecordCompletenessStatus.DeclarationNumber`.
- Queue cache counts containers through `GroupIdentifier`, `RecordCompletenessStatusId`, and `AnalysisRecords`.
- Decision side effects advance `RecordExpectedContainer` and CCS workflow stage.
- Zombie sweeper checks missing CCS rows by `GroupIdentifier`; CMR synthetic keys must not be falsely swept.
- Decision agent may pick up new CMR groups if enabled; we must explicitly decide whether CMR is agent-eligible.

#### Assignment And Review Controllers

- `src/NickScanCentralImagingPortal.API/Controllers/UserReadinessController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisManagementController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDecisionController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/AuditReviewController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDashboardController.cs`
- `src/NickScanCentralImagingPortal.API/Hubs/ImageAnalysisDashboardHub.cs`

Why affected:

- User readiness creates immediate assignments from ready groups.
- Manual assignment and release paths upsert/remove queue entries.
- Decision and audit controllers match decisions by group identifiers and container lists.
- Dashboard counts will change when the blocked CMR backlog starts flowing.

#### Submission And Payload

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerValidation/ContainerValidationService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerValidation/ClearanceTypeDetectionService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ICUMSSubmissionService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/IcumsPayloadController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ICUMSSubmissionQueueController.cs`
- `src/NickScanWebApp.New/Pages/Administration/IcumsPayloadViewer.razor`
- `src/NickScanWebApp.New/Pages/Customs/IcumsSubmissionQueue.razor`

Why affected:

- CMR payloads must submit rotation + container + BL, not BOE/declaration.
- Submission gate must not reject valid CMR because `DeclarationNumber` is missing.
- Submission success currently updates CCS by container number in places; composite scan rows need safer matching.
- Payload viewer and queue screens should display CMR keys clearly.

#### Cargo Group And Container Detail Views

- `src/NickScanCentralImagingPortal.Services/CargoGrouping/GroupResolver.cs`
- `src/NickScanCentralImagingPortal.Services/CargoGrouping/CargoGroupService.cs`
- `src/NickScanCentralImagingPortal.Services/CargoGrouping/CargoSummaryService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/CargoGroupController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ContainerDetailsController.cs`
- `src/NickScanWebApp.New/Services/CargoGroupService.cs`
- `src/NickScanWebApp.New/Services/CargoGroupViewPreloader.cs`
- `src/NickScanWebApp.New/Services/ImageAnalysisViewPreloader.cs`
- `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`
- `src/NickScanWebApp.Shared/Models/ContainerDetailsModels.cs`
- `src/NickScanWebApp.New/Pages/Containers/ContainerDetails.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupView.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupICUMSDataTab.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupSummary.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupSummaryTab.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupImagesTab.razor`
- `src/NickScanWebApp.New/Components/CargoGroup/CargoGroupScannerDataTab.razor`

Why affected:

- Group resolver currently resolves AG, RCS by declaration, RCS by container group key, or BOE rows.
- Cargo views often assume group identifier is declaration or BL.
- CMR must display as CMR with container + rotation + BL.

#### WebApp Image Analysis And Audit UI

- `src/NickScanWebApp.New/Pages/ImageAnalysis/Workbench.razor`
- `src/NickScanWebApp.New/Pages/ImageAnalysis/AuditReview.razor`
- `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewDialog.razor`
- `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor`
- `src/NickScanWebApp.New/Components/Operations/ImageDecisionView.razor`
- `src/NickScanWebApp.New/Components/Operations/AuditReviewDialog.razor`
- `src/NickScanWebApp.New/Components/Operations/AuditDecisionDialog.razor`
- `src/NickScanWebApp.New/Models/ViewContexts.cs`
- `src/NickScanWebApp.New/Models/AuditModels.cs`

Why affected:

- These screens receive group identifiers, containers, image lists, cargo data, and decision state.
- Synthetic CMR keys must not break URL encoding, dialog loading, or decision lookups.
- Analysts should see useful labels, not only internal keys.

#### Predictive Preload / Cache

- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadService.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadBackgroundService.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadDtos.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadKeys.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/PredictivePreloadController.cs`
- `src/NickScanWebApp.New/Services/AuditReviewViewPreloader.cs`
- `src/NickScanWebApp.New/Services/ContainerViewPreloader.cs`
- `src/NickScanWebApp.New/Services/ViewContextCache.cs`

Why affected:

- CMR groups will become normal assignment candidates.
- Preload assignment context and container context must include CMR display metadata.
- Cache invalidation must still fire after CMR queue creation, assignment release, decisions, and submission.

### Ring 2: Must Verify, Change Only If Failing

These services may not require code edits, but they are in the behavioral blast radius and need explicit regression checks.

#### Scanner And Image Intake

- `src/NickScanCentralImagingPortal.Services/ASE/AseBackgroundService.cs`
- `src/NickScanCentralImagingPortal.Services/ASE/AseDatabaseSyncService.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/ASEImagePipeline.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/FS6000ImagePipeline.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/FS6000/FS6000RawChannelIngester.cs`
- `src/NickScanCentralImagingPortal.Services/ImageProcessing/ImageCacheService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeWorker.cs`
- `src/NickScanCentralImagingPortal.Services/ImageSplitter/ImageSplitterSupervisorService.cs`

Why verify:

- The CMR fix depends on image existence and real container-number records.
- Composite ASE scan rows were part of the outage history.
- Image split promotion must not create comma-string pseudo-containers.

#### Gateway, Reports, And Dashboards

- `src/NickScanCentralImagingPortal.Services/Gateway/GatewayOrchestrationService.cs`
- `src/NickScanCentralImagingPortal.Services/Gateway/GlobalSearchService.cs`
- `src/NickScanCentralImagingPortal.Services/Gateway/DashboardStatsService.cs`
- `src/NickScanCentralImagingPortal.Services/Dashboard/ComprehensiveDashboardService.cs`
- `src/NickScanCentralImagingPortal.Services/Monitoring/ComprehensiveHealthCheckService.cs`
- `src/NickScanCentralImagingPortal.Services/Monitoring/DashboardAlertService.cs`
- `src/NickScanCentralImagingPortal.Services/Monitoring/DuplicateDownloadMonitoringService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ModuleQueuesController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ModuleDiagnosticsController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/PublicStatsController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ContainerCompletenessController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ContainerProcessingController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/BusinessRulesController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ReportsController.cs`
- `src/NickScanWebApp.New/Components/Monitoring/DiagnosticsPanel.razor`
- `src/NickScanWebApp.New/Components/Monitoring/LivePipelinePanel.razor`
- `src/NickScanWebApp.New/Components/ImageAnalysis/OperationsDashboardPanel.razor`
- `src/NickScanWebApp.New/Components/Dashboard/CMRValidationWidget.razor`
- `src/NickScanWebApp.New/Pages/Monitoring/Diagnostics.razor`

Why verify:

- CMR backlog movement will change counts in ready groups, CMR validation, record completeness, and module queue views.
- Alerts must not misclassify valid progressing CMR as drift or broken records.

#### AI And Summary Context

- `src/NickScanCentralImagingPortal.Services/AiCargoSummary/AiCargoSummaryBackgroundService.cs`
- `src/NickScanCentralImagingPortal.Services/AiWorkflow/AiImageAssistService.cs`
- `src/NickScanCentralImagingPortal.Services/AiWorkflow/IcumsCompletenessHintService.cs`
- `src/NickScanCentralImagingPortal.Services/AiWorkflow/AiSuggestionAutoTriggerService.cs`
- `src/NickScanCentralImagingPortal.Services/AiWorkflow/AiWorkflowLineageService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/AiWorkflowController.cs`

Why verify:

- These consumers may key off `GroupIdentifier`, `BlNumber`, or CMR completeness hints.
- Decision agent and AI assist must not start auto-processing CMR unless approved.

#### Admin/Correction Paths

- `src/NickScanCentralImagingPortal.API/Controllers/AdminMatchCorrectionController.cs`
- `src/NickScanWebApp.New/Pages/Completeness/MatchCorrectionDetailDialog.razor`
- `src/NickScanCentralImagingPortal.Services/Validation/DriftSweepService.cs`
- `src/NickScanCentralImagingPortal.Services/Validation/BackfillValidationService.cs`

Why verify:

- Manual match correction and drift sweeps inspect BOE, CCS, GroupIdentifier, and CMR fields.
- Valid CMR synthetic keys should not look like denormalization drift.

### Ring 3: Operationally Affected

These may not need code, but rollout must account for them.

- Production database: `nickscan_production`
- ICUMS downloads database: `nickscan_downloads`
- Existing `cmrredownloadqueue`
- Existing `analysisqueueentries`
- Existing active assignments and `DecisionAgentSettings.Enabled`
- Predictive cache keys in Redis/memory cache
- Staging gate: `StagingVerification:DisableBackgroundServices=true`
- Production service: `NSCIM_API`
- Current targeted Services DLL deployment history and backup packages

## Recommended Identity Strategy

### Preferred For This Implementation

Use a route-safe CMR operational key internally, with explicit display fields carried alongside it:

- `Operational key`: `CMR-{shortHash(rotation|container|bl)}`
- Display label: `CMR {container} / {rotation} / {bl}`
- Store rotation and BL in existing `RotationNumber` and `BlNumber`.
- Keep `PrimaryBoeDocumentId` linked to the CMR BOE document row.

Reason:

- Avoids route/path issues with `|`, slash, spaces, or long BL strings.
- Avoids synthetic keys looking like actual BOE numbers.
- Keeps existing unique indexes and group-identifier joins usable.

### Simpler But Riskier Alternative

Use readable key: `CMR|{rotation}|{container}|{bl}`.

This fits current database lengths but increases risk in URLs, logs, group-identifier normalization, and UI display.

## Redrawn 10-Phase Plan

### Phase 1: Guardrails And Baseline

- Freeze unrelated predictive-cache deployment work.
- Keep Decision Agent disabled until CMR assignment behavior is verified.
- Add SQL baseline scripts for:
  - blocked CMR count
  - ready CMR count
  - CMR with missing keys
  - CMR duplicate composite keys
  - active assignments
  - queue entries
  - record completeness counts
- Add a feature flag, recommended name: `CmrCompositeProgression:Enabled`.

Exit gate:

- Baseline report saved.
- Feature flag defaults off.

### Phase 2: Composite Key Helper And Tests

- Add `CmrCompositeKeyHelper`.
- Add validation: container, rotation, BL required.
- Add normalized key generation.
- Add display label generation.
- Add unit tests for casing, whitespace, missing fields, duplicate-safe output, and route safety.

Exit gate:

- Helper tests pass.
- No service behavior changed yet.

### Phase 2A: Source Scan Identity And Split Flow Foundation

- Add the source-scan resolver contract and result DTO.
- Add a mapping/backfill path for original scanner source records to real container numbers.
- Add resolver support for exact single-container matches and tokenized two-container ASE originals.
- Add split-context lookup by `AnalysisRecordId` and `SplitJobId`.
- Add compatibility behavior so existing container-based routes call the resolver instead of duplicating exact-match logic.
- Add cache key shape for source-scan and split identities.

Exit gate:

- `40426305424_W1` resolves from `MSMU1683356` to the physical two-container ASE source and completed split job.
- Exact one-container ASE and FS6000 records still resolve exactly.
- Ambiguous multi-source cases return an explicit ambiguity instead of silently selecting the wrong image.

### Phase 3: Container Completeness Policy

- Update `ContainerCompletenessPolicy`.
- Update `ContainerCompletenessService` creation path.
- Update `ContainerCompletenessService` recheck path.
- Protect preventive `GroupIdentifier` repair so it does not overwrite CMR synthetic keys with blank declaration.
- Verify `ContainerDataMapperService`, `ContainerStatusReconciliationService`, and `ManualBOESelectivityService` do not revert CMR rows.

Exit gate:

- CMR with scanner + ICUMS + image + composite key becomes `Complete` / `ImageAnalysis`.
- CMR missing any key remains blocked.
- IM/EX tests unchanged.

### Phase 4: Record Completeness CMR Path

- Add `BuildOrUpdateCmrRecordAsync` or equivalent to `IRecordBuildingService`.
- Extend `RecordBuildingService` for CMR composite-key rows.
- Extend `RecordCompletenessBuilder` or add CMR-specific builder method.
- Extend `RecordReconciliationWorker` to ingest CMR rows behind the feature flag.
- Store CMR display metadata.
- Update `RecordCompletenessController` and WebApp record completeness page to label CMR identity correctly.

Exit gate:

- CMR `RecordCompletenessStatus` and `RecordExpectedContainer` rows are created.
- Record rollup reaches `Ready` when expected container is ready.
- IM/EX record creation remains unchanged.

### Phase 5: Backfill And Repair Tooling

- Add dry-run operator query or job to identify blocked CMR candidates.
- Add small-batch backfill:
  - recompute CCS
  - build CMR records
  - do not create duplicate AG/AR rows
- Backfill source-scan container mappings for comma-joined ASE/original scan rows before those rows are used by UI, preload, or submission paths.
- Do not create analysis records from comma-joined pseudo-container strings.

Exit gate:

- Dry-run count matches expected candidate set.
- A tiny batch produces correct CCS and record rows without queue duplication.

### Phase 6: Analysis Intake And Assignment Queue

- Update record-anchored intake in `ImageAnalysisOrchestratorService`.
- Use `GroupType = "CMR"`.
- Ensure `AnalysisRecord.ContainerNumber` is a real container, never a comma-joined pseudo-container.
- Ensure `AnalysisGroup.RecordCompletenessStatusId` is set.
- Harden `ReadyGroupsCacheService` for CMR display, counts, orphan guard, and queue materialization.
- Verify `UserReadinessController` immediate assignment path.

Exit gate:

- CMR groups enter `AnalysisGroups` as `Ready`.
- `analysisqueueentries` materialize for active assignments.
- Analyst workbench receives correct container list.

### Phase 7: Analyst, Audit, And Side Effects

- Verify decision save through `ImageAnalysisDecisionController`.
- Verify `DecisionSideEffectsService` updates:
  - `AnalysisRecord`
  - `RecordExpectedContainer`
  - `RecordCompletenessStatus`
  - `ContainerCompletenessStatus`
- Verify audit review via `AuditReviewController`.
- Verify queue removal/refresh after analyst and audit completion.
- Keep Decision Agent blocked for CMR unless separately approved.

Exit gate:

- CMR record progresses through Analyst -> Audit -> PendingSubmission.
- No orphan sweep or drift sweep cancels valid CMR groups.

### Phase 8: CMR Submission And ICUMS Payload

- Update payload generation for CMR to use rotation + container + BL.
- Update `ContainerValidationService.ValidateSubmissionGateAsync` for CMR.
- Verify `ClearanceTypeDetectionService` behavior.
- Update success marking so the correct CCS and record children become `Submitted`.
- Verify retry and payload viewer flows.

Exit gate:

- Valid CMR submits without BOE/declaration.
- Submission success updates CCS and record completeness correctly.
- Failed submission retries keep enough identity to retry safely.

### Phase 9: CMR To BOE Upgrade Lifecycle

- Update `IcumJsonIngestionService.CascadeCMRUpgradeAsync`.
- Ensure existing CMR `RecordCompletenessStatus` and `AnalysisGroup` are linked or migrated when BOE arrives.
- Prevent duplicate IM/EX record creation for already-active CMR analysis.
- Preserve audit lineage and previous submission state.
- Invalidate ICUMS and predictive caches for both container and operational key.

Exit gate:

- CMR upgraded to IM/EX does not create duplicate assignments.
- Existing CMR analysis history remains traceable.
- Newly arrived BOE data becomes visible.

### Phase 10: UI, Cache, Staging, And Production Rollout

- Update WebApp labels for CMR in workbench, audit, cargo group, record completeness, payload viewer, and CMR validation pages.
- Update image analysis scanner tab, image tab, summary/document icon, and split-choice dialog to use source-scan/split identities from the resolver.
- Update predictive preload DTOs to include CMR display fields if needed.
- Update predictive preload and view caches to key image/split data by source scan and split identity, not only by container/group strings.
- Run full targeted tests.
- Start controlled staging with hosted services disabled.
- Run manual CMR backfill on staging first.
- Deploy behind feature flag.
- Enable tiny production batch.
- Monitor assignment queue, CMR validation metrics, record completeness, audit progression, submission results, and logs.
- Expand batch size only after no duplicate assignments and no stuck submissions.

Exit gate:

- Production batch verified end to end.
- Feature can remain on safely.
- Backlog drain can continue in controlled batches.

## Test Matrix

### Unit Tests

- `ContainerCompletenessPolicyTests`
- New `CmrCompositeKeyHelperTests`
- New/updated `RecordCompletenessBuilder` tests
- `AnalysisStatusValidatorTests`
- `SplitAnalysisStatusTests`

### Service Regression Tests

- `RecordReconciliationWorkerRegressionTests`
- `QueueProgressionRegressionTests`
- `AssignmentQueueHardeningTests`
- New CMR record-build test
- New CMR intake-to-queue test
- New CMR decision-side-effects test
- New CMR submission-gate test
- New CMR-to-BOE upgrade test
- Composite ASE pseudo-container prevention test

### Integration Tests

- Existing predictive preload caching tests
- Existing state machine E2E tests
- New CMR end-to-end staging test:
  - CMR BOE row with rotation/container/BL
  - scanner row
  - image row
  - CCS ready
  - RCS ready
  - AG/AR ready
  - assignment active
  - analyst decision
  - audit decision
  - payload generated
  - submitted state

## Deployment Safety Plan

1. Build with feature flag off.
2. Run tests.
3. Deploy to controlled staging with hosted services disabled.
4. Manually run one CMR candidate through the recompute/backfill path.
5. Enable hosted staging loops only after manual verification.
6. Deploy production with feature flag off.
7. Enable feature flag for one small batch.
8. Verify no duplicate AG/AR/queue rows.
9. Verify no active assignments are displaced.
10. Increase batch size gradually.

## Main Risks And Mitigations

- Risk: incomplete CMR enters analyst queue.
  - Mitigation: feature flag + helper validation + missing-key tests.

- Risk: duplicate assignments when CMR later becomes IM/EX.
  - Mitigation: upgrade-cascade tests and duplicate AG/AR guards.

- Risk: comma-joined ASE scan string becomes a fake analysis container.
  - Mitigation: split-linked real-container mapping test before rollout.

- Risk: payload submission marks wrong CCS row.
  - Mitigation: submission update by record/group/BOE identity, not only raw container string.

- Risk: UI shows synthetic CMR key as BOE.
  - Mitigation: explicit display label and CMR identity fields.

- Risk: Decision Agent processes CMR unexpectedly.
  - Mitigation: keep disabled or add CMR exclusion until approved.

- Risk: drift/zombie sweepers cancel valid CMR groups.
  - Mitigation: update/verify sweep predicates and add CMR regression checks.

## Immediate Next Step

Before touching production code, implement Phase 1 and Phase 2 only:

1. Add feature flag default-off.
2. Add CMR composite key helper.
3. Add helper tests.
4. Add no-behavior-change baseline queries.

Only after that should we modify completeness and record completeness.
