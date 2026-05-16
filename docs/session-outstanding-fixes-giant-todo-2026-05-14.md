# Session Outstanding Fixes Giant TODO

Date: 2026-05-14
Status: Active master tracker
Owner: Codex master coordinator
Scope: Remaining fixes from the CMR, split-image, assignment, ops, and cache/preload workstream.

## Current Reality

- [x] Eagle A25 missing table issue fixed in production.
- [x] Eagle A25 timestamp schema mismatch fixed in production.
- [x] Eagle A25 latest sync cycles are completing.
- [x] `/api/scan-assets/4812/image` request-cancel storm is no longer producing fresh global-exception rows.
- [x] Stale dashboard-facing error investigations were marked `Fixed` or `Ignored` with audit rows.
- [x] Scanner tab loads for `40426305424_W1`.
- [x] Image tab loads for `40426305424_W1`.
- [x] Fullscreen document icon loads for `40426305424_W1`.
- [x] Split-choice individual images now return for analyst choice when the splitter result list fails but stored A/B result IDs exist.
- [x] Split-choice WebApp now renders original and stored option A/B images through same-origin `/api/imageproxy`, avoiding direct browser cross-service image fetches.
- [x] Controlled WebApp-only split-choice proxy deploy completed on 2026-05-15 from `deploy-staging\webapp-split-choice-proxy-20260515-133000`.
- [x] Production WebApp config preserved during split-choice proxy deploy; pre-deploy WebApp files backed up to `deploy-backups\webapp-pre-split-choice-proxy-20260515-133000`.
- [x] Deployed WebApp DLL hash matches staged artifact: `6974BDE1FA4A856849D23594F160B970F24BE408EFCF7507FBA202D2C4E15E5B`.
- [x] Live WebApp proxy verification for split job `effda69a-d3a3-476d-8b14-8095d3f4e35f` returned nonzero images:
  - original: `200 image/jpeg`, `145754` bytes
  - option A lossless: `200 image/png`, `534551` bytes
  - option A preview: `200 image/jpeg`, `71684` bytes
  - option B lossless: `200 image/png`, `558536` bytes
  - option B preview: `200 image/jpeg`, `75134` bytes
- [x] Holistic split-choice display review saved to `docs\split-choice-image-display-root-cause-review-2026-05-15.md`.
- [x] Split-choice crop image rendering no longer uses browser lazy loading inside the tabbed/scrollable dialog.
- [x] Controlled WebApp-only eager-render deploy completed on 2026-05-15 from `deploy-staging\webapp-split-choice-eager-20260515-135520`.
- [x] Production WebApp config preserved during eager-render deploy; pre-deploy WebApp files backed up to `deploy-backups\webapp-pre-split-choice-eager-20260515-135520`.
- [x] Deployed eager-render WebApp DLL hash matches staged artifact: `FB2CE0C6EFC9563D7AA70A39EE1401D7EE13B6084FF7803E595E15D4CA574955`.
- [x] Post-deploy health green: API `5205/5206`, Web `5299/5300`, services `NSCIM_API` and `NSCIM_WebApp` running.
- [x] No new `Error`/`Fatal` application log rows appeared in the 10-minute post-deploy window.
- [x] Assignment queue rechecked after split-choice image display fix.
- [ ] CMR composite-key progression is not fully rolled out.
- [x] Raw historical `applicationlogs` still contain old rows by design; current tracker should treat them as resolved evidence unless fresh rows appear.
- [x] Ops probe showed 846 matching raw rows in the last 60 minutes, 0 matching raw rows in the last 15 minutes, newest matching row at `2026-05-14 20:12:38Z`.
- [ ] Migration runner Release artifact is stale compared with Debug and must be rebuilt or explicitly avoided for Eagle/Ops probes.
- [ ] Worktree remains highly dirty and must be packaged carefully before any broad deploy.
- [x] Merged validation pass completed from main workspace:
  - API build passed, 0 errors.
  - Web build passed, 0 errors.
  - focused backend split/CMR tests passed, 28/28.
  - focused core CMR/completeness tests passed, 22/22.
  - migration-runner Release build passed, 0 errors.
  - image-splitter Python syntax check passed.
- [x] Controlled API + Web deploy completed on 2026-05-14 from staging `deploy-staging\20260514-215653`.
- [x] Live publish folders backed up to `deploy-backups\controlled-api-web-20260514-215653`.
- [x] Production `publish\API\appsettings.json` and `publish\WebApp\appsettings.json` hashes were preserved.
- [x] Deployed API/Web DLL hashes match the staged Release artifacts.
- [x] Split fallback controlled deploy completed on 2026-05-14 from:
  - API: `deploy-staging\split-fallback-api-20260514-221707`
  - Web: `deploy-staging\split-fallback-web-20260514-221707`
  - reviewed file list: `deploy-staging\reviewed-split-fallback-20260514-221820`
  - backup: `deploy-backups\split-fallback-20260514-221820`
- [x] Production config hashes preserved after split fallback deploy.
- [x] Live split-options verification for analysis record `3418` returned `Ready`, `isMultiContainer=true`, and 2 options:
  - `87936a62-3798-40a0-b622-af67cc6bd62e`
  - `919c2835-c5fc-4b83-9d62-0a14e4ea9902`
- [x] Live split crop verification returned nonzero analyst-grade lossless images:
  - option A: `200 image/png`, `534551` bytes
  - option B: `200 image/png`, `558536` bytes
- [x] Live original split image verification returned `200 image/jpeg`, `145754` bytes.
- [x] Raw splitter result-list endpoint still returns `500`; the production fix intentionally falls back to persisted split result IDs.
- [x] No new `Error`/`Fatal` application log rows appeared in the 3-minute post-restart stabilization window.

## Agent Team Board

| Team | Agent | Status | Ownership | Outcome |
| --- | --- | --- | --- | --- |
| Team A | Nash | completed | Split asset backend | Added scan-assets lossless split-crop proxy and source-scan fallback for `4812`/`40426305424_W1`; focused backend tests passed |
| Team B | Gauss | completed | Split-choice frontend | UI/client now prefers resolver/analysis-record/split-job identity, avoids comma-container source-image hints, and builds split-choice crop URLs from result identity |
| Team C | Ampere | completed | CMR/completeness | Tightened submitted rollup guard and added regression coverage for protected CCS rows |
| Team D | Hypatia | completed | Ops hardening audit | Confirmed stale raw-log evidence, service-loop cancellation risks, Eagle live schema state, stale runner artifact, and dirty-tree packaging risk |
| Master | Codex | in_progress | Tracker and integration | Merge findings, avoid overlap, validate, deploy only from reviewed state |

## Status Legend

- `[ ]` Not started
- `[in_progress]` Being worked
- `[review]` Patch/finding ready for review
- `[blocked]` Blocked by missing evidence or decision
- `[x]` Done

## Do-Not-Cross Gates

- [ ] Do not deploy from the dirty main worktree without a reviewed package or explicit file list.
- [ ] Do not overwrite `publish\API\appsettings*.json` or `publish\WebApp\appsettings*.json`.
- [ ] Do not use comma-separated dual-container strings as workflow identity.
- [ ] Do not use comma-separated dual-container strings as image identity.
- [ ] Do not downsample or recompress split-choice images when the user needs analyst-grade fidelity.
- [ ] Do not cache a "missing image" result until source-scan resolution and split-asset fallback have both been attempted.
- [ ] Do not broaden CMR progression while the split-choice image blocker is open.
- [ ] Do not mark CMR submitted unless the correct record, container, and child workflow state are identified.
- [ ] Do not clean raw `applicationlogs` unless explicitly requested; use investigation status/audit instead.

## Phase 0: Worktree And Evidence Freeze

- [x] Capture `git status --short`.
- [x] Identify active dirty areas:
  - predictive cache/preload
  - split image backend/frontend
  - CMR completeness/progression
  - Eagle A25 schema/API
  - ops logging/migration runner
  - dashboard alert work
- [x] Save a reviewed file list before the next deploy.
- [ ] Decide whether to create a temporary reviewed worktree for API/Web deploy.
- [ ] Separate deploy candidates from docs-only and local tooling changes.
- [ ] Split dirty tree into commit/package lanes:
  - Eagle A25 schema/services/API/UI/config placeholders
  - cancellation-noise hardening
  - dashboard alert/log investigation cleanup
  - image-splitter/scan-assets/preload/CMR work
  - generated deploy artifacts and sample data exclusions
- [x] Record live production health before new deploy:
  - `NSCIM_API` state/path/PID
  - `NSCIM_WebApp` state/path/PID
  - API health `5205` and `5206`
  - Web health/home route
- [ ] Record known sample records before fix:
  - `40426305424_W1`
  - source scan `4812`
  - container `TGBU5483870`
  - CMR key `CMR-C40FEA9B3C7FA383450D`
  - containers `MSMU1683356`, `MRKU8254509`
- [x] Record live production health after deploy:
  - `NSCIM_API` running from `publish\API`
  - `NSCIM_WebApp` running from `publish\WebApp`
  - API health green on `5205` and `5206`
  - Web home route green on `5299` and `5300`

Exit gate:

- [ ] Master has a reviewed deployment file list.
- [ ] No agent writes overlap without explicit integration review.

## Phase 1: Split Source-Image Identity Model

Goal: preserve a unique original image identity for a two-container scan and use child container identities only for child workflow records.

- [ ] Inventory current identifiers used by:
  - split service original job
  - split service result/crop rows
  - `AnalysisRecord.ContainerNumber`
  - `ContainerCompletenessStatus.ContainerNumber`
  - `ScanAssetsController`
  - `ScanAssetResolver`
  - `SplitChoiceDialog`
  - fullscreen document icon
  - image tab
  - scanner tab
- [ ] Define canonical fields:
  - original source scan id
  - original scan asset id
  - split job id
  - split result id
  - physical child container number
  - record/group identifier
- [ ] Confirm where the comma-separated identifier is introduced.
- [ ] Stop passing comma-separated identifier into split-choice image URL generation.
- [ ] Preserve child container numbers for child records and assignments only.
- [ ] Add fallback resolver diagnostics that explain why a source image/crop was not found.

Exit gate:

- [ ] The original two-container image can be addressed by stable source identity.
- [ ] Each split crop can be addressed by split result identity.
- [ ] No new workflow record is keyed by `CONTAINER1, CONTAINER2`.

## Phase 2: Backend Split Asset Retrieval

Team A owner: Nash

- [x] Verify `/api/scan-assets/4812/image` replacement/source route for the current record returns 200 and nonzero bytes.
- [ ] Verify whether split crops are stored as:
  - database bytes
  - local file paths
  - split service URLs
  - derived crop paths
- [x] Add or repair resolver route for split-choice original image.
- [x] Add or repair resolver route for split-choice crop/result image.
- [x] Ensure response content type is correct.
- [x] Ensure response content length is nonzero where possible.
- [x] Ensure signed URL middleware allows the split-choice asset routes.
- [ ] Ensure resolver does not cache failed lookup before trying fallback identities.
- [ ] Add focused tests for:
  - [x] original source scan id resolution
  - [x] dual-container original resolution
  - [x] split crop resolution
  - [x] no lossy recompression
  - missing asset diagnostic reason
- [x] Backend focused tests passed: `SourceScanSplitFlowRegressionTests|ScanAssetResolverTests|SourceScanResolverPhase2ARegressionTests` 15/15.

Exit gate:

- [x] Backend can fetch original and both crops for `40426305424_W1`; live API/Web deploy verified.
- [x] Backend test slice passes.

## Phase 3: Frontend Split Choice Display

Team B owner: Gauss

- [x] Trace `SplitChoiceDialog` image URL construction.
- [x] Replace comma-container image lookup with source/split identity lookup using either:
  - `/api/scan-assets/{sourceScanId}/image?splitJobId={job}&splitResultId={result}&side={left|right}`
  - existing `/api/image-splitter/jobs/{job}/results/{result}/lossless/{side}`
- [x] Keep single-container records compatible.
- [x] Show original image, left/right crop choices, and selected crop state.
- [x] Use stable dimensions so failed/loading images do not shift layout.
- [x] Add clear but compact error state if a crop is missing.
- [ ] Avoid visible instructions or feature descriptions in the app UI.
- [x] Confirm fullscreen document icon remains working.
- [x] Confirm summary, scanner, and image tabs still load.
- [x] Web build passed in Team B lane: `dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore`.

Exit gate:

- [x] Analyst split options for `40426305424_W1` return two live lossless crop URLs after controlled API+Web deploy.
- [x] Web build passes.

## Phase 4: Assignment Queue Recovery

- [x] Re-run assignment/service-state check after split image fix.
- [ ] Count ready AG records for image analysis queue.
- [ ] Count ready records with complete ICUMS + image + scanner data not picked up.
- [x] Check whether `40426305424_W1` has active assignment.
- [ ] Check whether child containers from dual original are represented as separate child records.
- [ ] Verify no active assignment uses comma-separated container identity.
- [ ] Verify cache invalidation after decision or split-choice selection.
- [ ] Verify predictive preload does not serve stale "no image" or "no assignment" results.

Exit gate:

- [x] Known analyst user receives assignment.
- [review] Assignment opens with scanner tab, image tab, split choices, and document icon; API/live asset verification complete, final browser confirmation remains user-facing.

## Phase 5: CMR Composite-Key Progression

Team C owner: Ampere

Business rule: CMR records can progress without BOE when all three keys exist:
manifest/rotation number, container number, and BL number.

- [ ] Confirm all existing CMR rows used for progression have:
  - rotation/manifest number
  - container number
  - BL number
  - scanner/image data when required
  - ICUMS data when required
- [ ] Keep BOE-required rule for non-CMR records.
- [ ] Keep `CmrCompositeProgression:Enabled=false` until pilot gates pass.
- [ ] Recheck record completeness rule for CMR parent records.
- [ ] Recheck container completeness rule for CMR child containers.
- [ ] Recheck rollup sync after analyst decision.
- [ ] Recheck rollup sync after audit decision.
- [x] Recheck rollup sync after ICUMS submission acknowledgement.
- [ ] Ensure CMR parent record reflects child `Decided` state correctly.
- [x] Ensure CMR parent record reflects container `Submitted` state correctly only when a matching CCS row moved to `Submitted` or was already `Submitted`.
- [ ] Add tests for CMR complete-without-BOE and non-CMR blocked-without-BOE.
- [x] Add tests for CMR rollup after submission, including protected `SplitSuperseded` CCS rows.
- [ ] Add/verify CMR submission payload coverage for rotation/container/BL without BOE.
- [ ] Add CMR-to-BOE upgrade duplicate-prevention tests before broader batches.

Exit gate:

- [x] Focused CMR and completeness tests pass:
  - `QueueProgressionRegressionTests|RecordBuildingServiceCmrTests`: 13/13 passing
  - `ContainerCompletenessPolicyTests|CmrCompositeKeyHelperTests`: 17/17 passing
- [ ] One-container CMR production pilot succeeds before any broader batch.

## Phase 6: ICUMS Submission And ACK Reconciliation

- [ ] Verify system submits ICUMS data only when enabled and intended.
- [ ] Verify `ICUMS-ACK-RECONCILE` still appears after API deploy.
- [ ] Confirm known submitted containers flip to `Submitted`.
- [ ] Confirm CMR parent state updates after child submission.
- [ ] Confirm no duplicate submission for CMR composite keys.
- [ ] Confirm `LiveSubmitEnabled` production setting before any live submission expectations.

Exit gate:

- [ ] ICUMS submission/ack state is reproducible for known containers.

## Phase 7: Ops Logging And Cancellation Hardening

Team D owner: Hypatia

- [x] Request-aborted `OperationCanceledException` no longer logs as global Error.
- [x] Identify service-loop cancellation paths still logging as workflow errors:
  - container completeness orchestrator
  - image analysis orchestrator
  - ready groups cache reconcile
  - BOE selectivity workflow
  - post-ICUMS validation workflow
- [ ] Also review shutdown cancellation boundaries for:
  - `POST-ICUMS-VALIDATION`
  - `BOE-SELECTIVITY`
  - `DATA-MAPPING`
  - `CONTAINER-COMPLETENESS`
  - `HOUSEKEEPING`
  - `WAVE-*`
  - `QUEUE-RECONCILE`
- [ ] Convert expected shutdown/service-stop cancellation to Debug/Information.
- [ ] Keep real timeout/cancellation failures visible with path, worker, and elapsed time.
- [ ] Add guard so Error Monitoring does not create noisy investigations for expected shutdown cancellation.
- [x] Keep raw logs available; clean dashboard-facing investigations by status/audit only.
- [x] Add ops probe commands to tracker.

Ops probe commands:

```powershell
& .\tools\migration-runner\bin\Debug\net10.0\migration-runner.exe --ops-error-probe
& .\tools\migration-runner\bin\Debug\net10.0\migration-runner.exe --eagle-a25-probe
Get-CimInstance Win32_Service -Filter "Name='NSCIM_API'"
Get-Item C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe
Invoke-WebRequest http://localhost:5205/health -UseBasicParsing
Invoke-WebRequest https://localhost:5206/health -UseBasicParsing -SkipCertificateCheck
```

Exit gate:

- [ ] No fresh cancellation investigations are generated during controlled restart.

## Phase 8: Predictive Cache And Preload Safety

- [ ] Review cache keys for image analysis view:
  - assignment state
  - scanner tab
  - image tab
  - split choices
  - summary tab
  - fullscreen document icon
- [ ] Ensure source-scan/split-asset resolution result is part of cache identity.
- [ ] Ensure negative image lookup results have short TTL or are not cached until resolver completes all fallbacks.
- [ ] Ensure cache invalidates after:
  - split job created
  - split result chosen
  - analyst decision
  - audit decision
  - ICUMS submission acknowledgement
  - record completeness rollup
- [ ] Add telemetry for preload hit/miss on split-choice assets.
- [ ] Keep `PreloadFullImages=false` unless explicitly approved.

Exit gate:

- [ ] Cache cannot hide newly available split crops or assignments.

## Phase 9: Build, Test, And Reviewed Packaging

- [x] Run backend focused tests from changed lane.
- [x] Run frontend build.
- [x] Run split service syntax/test check if Python files changed.
- [x] Run CMR/completeness focused tests if those files changed.
- [x] Run migration-runner build if tooling changed.
- [x] Refresh or explicitly avoid stale `tools\migration-runner\bin\Release\net10.0\migration-runner.exe`; Release was rebuilt successfully.
- [x] Generate reviewed deploy file list.
- [x] Create backup of live publish folders before deploy.
- [x] Skip API-only path because frontend split-choice changes landed.
- [x] Deploy API + Web if frontend split-choice changes land.
- [x] Preserve production config files during copy.

Suggested commands:

```powershell
dotnet build tools\migration-runner\migration-runner.csproj --no-restore --verbosity minimal
dotnet build tools\migration-runner\migration-runner.csproj --configuration Release --no-restore --verbosity minimal
dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore --verbosity minimal
dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore --verbosity minimal
dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "FullyQualifiedName~ScanAssetResolver|FullyQualifiedName~SourceScan|FullyQualifiedName~CmrComposite|FullyQualifiedName~QueueProgression" --no-restore
dotnet test tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj --filter "FullyQualifiedName~ContainerCompletenessPolicyTests|FullyQualifiedName~CmrCompositeKeyHelperTests|FullyQualifiedName~CmrCompositeRecordIntakeGuardrailTests" --no-restore
```

Exit gate:

- [x] Builds/tests pass with known warnings only.
- [x] Reviewed package is ready and deployed.

Reviewed deploy file list:

- API/backend split assets:
  - `src/NickScanCentralImagingPortal.API/Controllers/ScanAssetsController.cs`
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/ScanAssetResolver.cs`
  - `src/NickScanCentralImagingPortal.Core/DTOs/ScanAssets/*`
  - `src/NickScanCentralImagingPortal.Core/Interfaces/IScanAssetResolver.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs`
  - `src/NickScanCentralImagingPortal.API/Middleware/SignedImageUrlMiddleware.cs`
- Web split-choice and image view:
  - `src/NickScanWebApp.New/Components/Operations/SplitChoiceDialog.razor`
  - `src/NickScanWebApp.New/Components/Operations/ImageDecisionView.razor`
  - `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor`
  - `src/NickScanWebApp.Shared/Models/ContainerDetailsModels.cs`
  - `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`
  - `src/NickScanWebApp.Shared/Services/IContainerDetailsService.cs`
- CMR/submission rollup safety:
  - `src/NickScanCentralImagingPortal.Services/ImageAnalysis/SubmissionWorkflowStageSync.cs`
  - `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessRollupSync.cs`
  - `src/NickScanCentralImagingPortal.Tests/Services/QueueProgressionRegressionTests.cs`
- Ops deploy support:
  - `src/NickScanCentralImagingPortal.API/Middleware/GlobalExceptionHandlerMiddleware.cs`
  - `tools/migration-runner/Program.cs`

## Phase 10: Controlled Live Verification

- [x] Verify service state:
  - `NSCIM_API`
  - `NSCIM_WebApp`
- [x] Verify deployed artifact hashes match staging:
  - `NickScanCentralImagingPortal.API.dll`
  - `NickScanWebApp.New.dll`
  - `NickScanWebApp.Shared.dll`
- [x] Verify API health endpoints:
  - `http://localhost:5205/health`
  - `https://10.0.1.254:5206/health`
- [x] Verify Web route loads.
- [review] Verify `40426305424_W1`:
  - [x] scanner-tab backing ASE row exists for `MSMU1683356, MRKU8254509`
  - [x] image-tab backing ASE row has nonzero bytes: `1,753,540`
  - [x] split-choice original loads: `200 image/jpeg`, `145,754` bytes
  - [x] individual split crops load:
    - option A `200 image/png`, `534,551` bytes
    - option B `200 image/png`, `558,536` bytes
  - [x] scan-assets crop proxy loads:
    - option A `200 image/png`, `534,551` bytes
    - option B `200 image/png`, `558,536` bytes
  - [blocked] analyst choose action needs an authenticated UI/user click; no DB/API bypass was used during verification
  - [x] fullscreen document/source image route loads: `200 image/jpeg`, `219,414` bytes
  - [x] ASE file size is nonzero
- [review] Verify source scan `4812`:
  - [blocked] `4812` is not the current source for the TGBU sample; it returns `404` for `TGBU5483870`
  - [x] current TGBU source scan `5511` returns `200 image/jpeg`, `266,980` bytes
  - [x] legacy ASE thumbnail for `TGBU5483870` returns `200 image/jpeg`, `266,980` bytes
  - [x] no fresh global exception storm
- [x] Verify assignment:
  - active assignment exists: `40426305424_W1`, assignment `15937`, user `pimage`, `Active`
  - no comma-separated identity in active assignment: assignment group is `40426305424_W1`, record container is `MSMU1683356`
  - assignment queue entry exists for assignment `15937`
- [ ] Verify CMR:
  - one pilot record progresses only when flag and gates allow it
  - parent rollup reflects child decisions/submissions
- [review] Verify ops:
  - [x] no fresh `eaglea25synclogs` missing-table errors
  - [x] no fresh `/api/scan-assets/4812/image` global exception storm
  - [x] no stale investigations reopened
  - [x] `errorinvestigations` remains clear for the probed error set
  - [review] one fresh warning row appeared from the signed verification probe because uid `codex-live-verify` has no `tenant_id` claim; no error investigation was created
  - [review] one fresh warning row appeared for ImageSplitter search cancellation during/after restart; no error investigation was created

Exit gate:

- [review] User can complete the analyst split-choice workflow live; final confirmation is the authenticated choose click in the UI.
- [x] Assignment is recovered for `40426305424_W1`.
- [review] CMR progression is safe to resume under existing feature flags and pilot gates.

## Open Risks

- [ ] Dirty worktree could mix unrelated features into deploy package.
- [ ] Split service may store crop references outside the API's current signed asset model.
- [ ] Existing cached "missing image" result may mask a fixed crop until invalidated.
- [ ] CMR feature flag may allow many records to progress if enabled broadly.
- [ ] Raw log views may still show historical rows even after investigation cleanup.
- [ ] Frontend may need both API and Web deploy for split-choice fix.

## Integration Notes

- Team A and Team B must agree on the image URL contract before either patch is deployed.
- Team C should not depend on split-choice image display, but broad CMR progression should wait until split-choice is fixed.
- Team D recommendations should become deployment gates, not broad refactors.
- Master coordinator must update this file after each team reports back.
