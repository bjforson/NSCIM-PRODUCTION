# Predictive Preload Cache Implementation TODO

Date: 2026-05-11
Status: Active

CMR rollout dependency tracker: `docs/cmr-composite-key-giant-implementation-todo-2026-05-13.md`

## Mission

Build a system-wide predictive preload layer that warms likely-needed Analyst and Audit assignment data before users click into work.

## Current Implementation

- Phase 1 backend assignment preload is implemented.
- Phase 2 central queue hooks are implemented; direct assignment and decision bypass audit is complete for the known assignment controllers and decision side effects.
- Phase 3 backend container context preload is implemented for summaries, first-page data, BOE summaries, and image metadata.
- Diagnostics/operator controls are implemented, including cached assignment and container reads.
- Phase 4 WebApp cache-first integration is implemented for container view preloaders and the Audit Review dialog path, with direct-load fallback preserved.
- Controlled staging support is implemented via `StagingVerification:DisableBackgroundServices=true`, allowing cache endpoints to run without starting ingestion, assignment, scanner, archive, reconciliation, dashboard, or monitoring hosted loops.

## Implemented Files

- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadOptions.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadKeys.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadDtos.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadState.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/IPredictivePreloadService.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadService.cs`
- `src/NickScanCentralImagingPortal.Services/Caching/PredictivePreloadBackgroundService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/PredictivePreloadController.cs`
- `src/NickScanCentralImagingPortal.API/Monitoring/MonitoringServiceExtensions.cs`
- `src/NickScanCentralImagingPortal.API/Program.cs`
- `src/NickScanCentralImagingPortal.API/appsettings.json`
- `src/NickScanCentralImagingPortal.API/appsettings.Production.template.json`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`
- `src/NickScanWebApp.New/Services/AuditReviewViewPreloader.cs`
- `src/NickScanWebApp.New/Services/ContainerViewPreloader.cs`
- `src/NickScanWebApp.New/Pages/ImageAnalysis/AuditReview.razor`
- `src/NickScanWebApp.New/Components/Operations/AuditReviewDialog.razor`
- `src/NickScanWebApp.Shared/Models/ContainerDetailsModels.cs`
- `src/NickScanWebApp.Shared/Services/ApiService.cs`
- `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`

## Operator Endpoints

All endpoints require the `AdminOnly` policy.

- `GET /api/cache/predictive/status`
- `POST /api/cache/predictive/run-once`
- `POST /api/cache/predictive/invalidate/assignment/{groupId}`
- `POST /api/cache/predictive/invalidate/container/{containerNumber}`
- `POST /api/cache/predictive/preload/container/{containerNumber}`
- `GET /api/cache/predictive/assignment/{groupId}`
- `GET /api/cache/predictive/container/{containerNumber}`

## Phase 1: Assignment Prediction

- [x] Add predictive preload options.
- [x] Add cache key helper.
- [x] Add compact assignment context DTOs.
- [x] Add predictive preload service.
- [x] Add background worker.
- [x] Register service and hosted worker.
- [x] Add config.
- [x] Cache top Analyst and Audit candidates from `ReadyGroupsCacheService`.
- [x] Cache assignment context under `preload:assignment:{groupId}`.
- [x] Cache candidate IDs under `preload:role:{role}:assignments`.

## Phase 2: Invalidation and Triggers

- [x] Trigger best-effort assignment preload after `ReadyGroupsCacheService.UpsertQueueEntryAsync`.
- [x] Invalidate assignment context after queue entry removal.
- [x] Invalidate role candidate list after queue entry removal.
- [x] Invalidate stale assignment contexts during queue reconciliation.
- [x] Invalidate predictive role candidate lists when existing ready-groups caches are invalidated.
- [x] Audit direct assignment and decision paths for any bypass of queue hooks.
- [x] Add targeted tests for predictive preload service and diagnostics controller.
- [x] Add diagnostic cached assignment-context read endpoint.

### Phase 2 Audit Notes

- Assignment creation paths in `ImageAnalysisController`, `ImageAnalysisManagementController`, `ImageAnalysisDecisionController`, `UserReadinessController`, and `ImageAnalysisOrchestratorService` call `ReadyGroupsCacheService.UpsertQueueEntryAsync`, so predictive assignment preload is reached through the central queue hook.
- Assignment release/removal paths in `ImageAnalysisDecisionController`, `AuditReviewController`, `DecisionSideEffectsService`, `ImageAnalysisController`, and `ImageAnalysisOrchestratorService` route through `ReadyGroupsCacheService.RemoveQueueEntryAsync` or `RemoveQueueEntriesForGroupAsync`, so predictive assignment invalidation is reached through the central queue hook.
- Analyst-completed status transitions already call `ReadyGroupsCacheService.InvalidateCache("Audit", "AnalystCompleted")` in the controller path; `InvalidateCacheAsync` now also invalidates predictive role candidate lists so Audit preloading refreshes without controller-specific cache knowledge.

## Phase 3: Container Context Preload

- [x] Add container context DTO.
- [x] Preload container summary.
- [x] Preload scanner first page.
- [x] Preload ICUMS first page.
- [x] Preload compact BOE summary.
- [x] Preload image metadata only.
- [x] Keep full image preload disabled.
- [x] Add diagnostic cached container-context read endpoint.
- [x] Add diagnostic manual container preload endpoint.
- [x] Add diagnostic container invalidation endpoint.

### Phase 3 Implementation Notes

- `PreloadAssignmentAsync` now warms container context for each capped assignment container when container preload options are enabled.
- Container context is cached under `preload:container:{containerNumber}:context` and individual subsections are cached under summary, scanner first page, ICUMS first page, BOE, and image metadata keys.
- Image preloading intentionally stores metadata only. It does not cache image bytes, and `PreloadFullImages` remains false in appsettings and production template.

## Phase 4: WebApp Integration

- [x] Add cache-first calls to existing view preloaders.
- [x] Wire `AuditReviewViewPreloader`.
- [x] Stop duplicate manual preloading in `AuditReviewDialog`.
- [x] Preserve direct-load fallback.

### Phase 4 Implementation Notes

- `ContainerDetailsService` checks the optional predictive container context endpoint before falling back to existing container detail endpoints.
- `ContainerViewPreloader` keeps the existing full-tab default page size (`1000`) unless operators explicitly configure `ViewContextPreloading:ContainerFirstPageSize`; this avoids accidentally rendering partial scanner/ICUMS tabs.
- `AuditReview.razor` now builds an `AuditReviewViewContext` before opening the dialog.
- `AuditReviewDialog` consumes supplied preloaded context and only direct-loads missing images, scanner data, or ICUMS data.
- Predictive cache calls use best-effort `ApiService.TryGetAsync`, so 404/403/timeout/unavailable cache endpoints quietly fall back to existing direct loads.
- Predictive `run-once` preloads each concurrent assignment in its own DI scope so EF Core DbContexts are not shared across concurrent candidate warmups.

## Validation

- [x] `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore`
- [x] `dotnet test tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj --no-restore` (15 passed)
- [x] `dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj -c Release -p:UseSharedCompilation=false`
- [x] `dotnet test tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj -c Release -p:UseSharedCompilation=false --no-restore --filter "FullyQualifiedName~Caching"` (11 passed)
- [x] `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj -c Release -p:UseSharedCompilation=false --no-restore` after staging-safety changes.
- [x] `dotnet test tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj -c Release -p:UseSharedCompilation=false --no-restore --filter "FullyQualifiedName~Caching"` after scoped-concurrency fix (11 passed).
- [x] Publish controlled staging package: `publish\_staging\predictive-cache-20260512-151803`.
- [x] Start controlled staging API on `http://localhost:6205` with `NSCIM_SKIP_SINGLE_INSTANCE=1`, `StagingVerification:DisableBackgroundServices=true`, `PredictivePreload:Enabled=true`, and `PredictivePreload:BackgroundEnabled=false`.
- [x] Verify staging health: `GET http://localhost:6205/health/live` returned `200 Healthy`.
- [x] Verify worker gate: staging logs showed no assignment/intake/scanner/reconciliation/monitoring hosted loop startup.
- [x] Verify manual diagnostics endpoints with staging-only admin token:
  - `GET /api/cache/predictive/status`: `Enabled=true`, `BackgroundEnabled=false`, `IsRunning=false`.
  - `POST /api/cache/predictive/run-once`: `CandidateCount=5`, `SuccessCount=5`, `FailureCount=0`, `SkippedCount=0`.
  - `POST /api/cache/predictive/preload/container/UETU2878297`: success, `ScannerFieldCount=21`, `IcumFieldCount=0`, `ImageMetadataCount=4`.
  - `GET /api/cache/predictive/container/UETU2878297`: returned cached context with summary, scanner page, ICUMS page, and 4 image metadata records.
  - `POST /api/cache/predictive/invalidate/container/UETU2878297`: success.
  - `GET /api/cache/predictive/container/UETU2878297` after invalidation: `404`.

## Production Incident Notes

- [x] 2026-05-12: stopped the controlled staging API on port `6205`; production API on port `5205` remained healthy.
- [x] 2026-05-12: verified predictive preload/cache work was not deployed to production.
- [x] 2026-05-12: investigated analyst assignment outage report. Production assignment loop was alive and creating Audit assignments, but Analyst assignment had zero eligible groups.
- [x] 2026-05-12: confirmed all 49 raw `Ready` analysis groups were stale orphan/export-hold groups: records and images existed, but no BOE document and no active container-BOE relation.
- [x] 2026-05-12: ran a narrow production cleanup for the same orphan predicate used by `ReadyGroupsCacheService`, moving 49 stale `Ready` groups to `Cancelled` and writing 49 audit rows with correlation id `2026-05-12-analysis-assignment-orphan-cleanup`.
- [x] 2026-05-12: post-cleanup verification showed `Ready=0`, remaining ready-orphan candidates `0`, production health `200/200`, and existing Audit queue entries unaffected.
- [x] 2026-05-12: followed up after Analyst users still saw no active assignment. Verified there were no assignable `Ready` / `AnalystCompleted` analysis groups, then found valid container-completeness rows stranded before `AnalysisGroup` creation because retired legacy container-grouping intake no longer covers no-record standalone rows.
- [x] 2026-05-12: seeded a conservative 5-group production repair batch from rows with image + BOE/match data, no existing `AnalysisGroup`, no `RecordCompletenessStatus`, and no existing `AnalysisRecord`.
- [x] 2026-05-12: paused `DecisionAgentSettings.Enabled=false` after it raced the repair groups (`Ready -> AgentProcessing -> Ready`) while human Analyst assignments were being created.
- [x] 2026-05-12: repaired the two active Analyst assignment groups to `AnalystAssigned` and refreshed their queue rows. Verification showed `pimage` has 2 active Analyst assignments, `paudit` has 2 active Audit assignments, and queue rows are materialized.
- [x] 2026-05-12: diagnosed the remaining Analyst visibility issue as malformed ASE two-container assignments: queue rows exposed one comma-joined pseudo-container (`"A, B"`) instead of two real containers, so the workbench could receive an assignment while not surfacing all actionable container work.
- [x] 2026-05-12: repaired the two live `pimage` Analyst assignments in production by converting the comma `AnalysisRecord` into the first real container, adding the second real container record, linking both records to their completed `image_split_jobs`, refreshing `analysisqueueentries.containersjson`, moving composite CCS rows to `SplitSuperseded`, and extending leases to `2026-05-12T19:59:47+01:00`.
- [x] 2026-05-12: added local permanent-code fixes in `TwoContainerSplitIntakeService` to promote future composite comma records into individual split-linked container records and refresh assignment queue/completeness rows; added local `DecisionAgentWorker` guard so the agent skips active human assignments. These code fixes build cleanly but have not been production-deployed because the wider predictive-cache worktree is still intentionally undeployed.
- [x] 2026-05-12: after the live repair, production created two duplicate single-container Analyst assignments (`15791`, `15792`) from the same split jobs. Verified both had zero analyst/audit decisions, cancelled those duplicate assignments and groups, deleted their queue rows, and wrote status-transition audit rows with correlation id `2026-05-12-split-duplicate-assignment-rollback`.
- [x] 2026-05-12: extended the two corrected `pimage` Analyst assignments (`15786`, `15787`) to `2026-05-12T21:15:36+01:00` for retesting. After one assignment cycle, verification showed `ready_groups=0`, `active_assignments_total=2`, and only the two corrected two-container `pimage` assignments remained active.
- [x] 2026-05-12: tightened the local `TwoContainerSplitIntakeService` fix so newly promoted/created split sibling records are saved before materialized queue rows are refreshed.
- [x] 2026-05-13: deployed a targeted production `NickScanCentralImagingPortal.Services.dll` only, built from clean `HEAD` plus the already-deployed stale-assignment reclaim guard in `ImageAnalysisOrchestratorService`, the composite split-record promotion fix in `TwoContainerSplitIntakeService`, and the active-human-assignment guard in `DecisionAgentWorker`. Predictive cache source changes were not included in the deployed binary.
- [x] 2026-05-13: backed up the replaced production Services assembly to `publish/_backup/assignment-stability-20260513-093528/API` and preserved the exact targeted package at `publish/_staging/assignment-stability-20260513-093528/API`. Deployed DLL hash: `A7B700E3096A004EFA3FA7433C1088FEAC85255D2A2D54176FF876A14226C258`.
- [x] 2026-05-13: restarted `NSCIM_API` after the targeted DLL swap. `GET /health/live` and `GET /health/ready` returned `200 Healthy`; process restarted as PID `48468`.
- [x] 2026-05-13: post-deploy DB verification showed `DecisionAgentSettings.Enabled=false`, two active two-container Analyst assignments (`15797` for `ECMU2701970/ECMU2821168`, `15798` for `MSMU1683356/MRKU8254509`), and one remaining `Ready` group waiting for analyst capacity.
- [x] 2026-05-13: post-deploy logs showed the API and supervised splitter restarted. The splitter continued the pre-existing `PIL.UnidentifiedImageError` upload failure pattern for an invalid/unidentified image payload; track separately from the assignment-stability deployment.

## Next Recommended Work

1. Add a permanent, conservative Ready-orphan housekeeping path or dashboard distinction so stale export-hold groups do not look like assignable analyst work.
2. Monitor the deployed assignment-stability fix for at least 1-2 active assignment cycles and confirm the Analyst Workbench surfaces the two-container assignments correctly.
3. Restore a safe no-record container-grouping intake fallback for standalone container groups that have images + BOE/match data but no `RecordCompletenessStatus`.
4. Review staging logs for a short soak window, then package a production deployment with the staging gate left default-off.
5. Add lightweight telemetry around predictive cache hit/miss/fallback rates.
6. Add partial-page hydration in scanner/ICUMS tabs before lowering `ViewContextPreloading:ContainerFirstPageSize` below `1000`.
