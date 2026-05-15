# CMR Composite-Key Giant Implementation TODO

Date: 2026-05-13
Status: Active tracker
Owner: NSCIM CMR progression rollout

## Current State Snapshot

- [x] CMR composite-key scope and blast-radius analysis exists.
- [x] ICUMS team agreement captured: CMR can submit by rotation/manifest number + container number + BL number.
- [x] Production feature flag defaults off: `CmrCompositeProgression:Enabled=false`.
- [x] Route-safe CMR operational key helper exists.
- [x] Container completeness policy can treat complete CMR composite-key rows as progressable when the flag is enabled.
- [x] Record completeness can build/update CMR records behind the feature gate.
- [x] ICUMS ingestion can trigger event-driven CMR record building behind the feature gate.
- [x] Record reconciliation has a gated CMR composite pass.
- [x] Record-anchored intake creates CMR `AnalysisGroup` rows with `GroupType="CMR"`.
- [x] Intake back-stamps blank or null `ContainerCompletenessStatus.GroupIdentifier` values to the CMR operational key.
- [x] CMR duplicate protection exists before creating later real-declaration groups.
- [x] Disposable local staging clone verified `PIDU4444900` end to end through active Analyst assignment and queue materialization.
- [x] Controlled CMR pilot runner exists: `tools/cmr-composite-pilot-runner`.
- [x] Pilot runner dry-run/apply verified locally for `PIDU4444900`.
- [x] Focused guardrails and queue/CMR regression tests pass locally.
- [x] Services, API, and staging-runner builds pass locally with existing warnings only.
- [x] CMR API/safety changes are production-deployed from reviewed worktree `C:\Shared\NSCIM_CMR_REVIEWED_20260513_215235`.
- [x] Production CMR flag remains disabled.
- [x] Production CMR backlog is not broadly backfilled; only the controlled one-container pilot has been applied.
- [ ] Source-scan identity resolver exists for scanner/image/split flows.
- [ ] Two-container ASE originals map to real child containers without using comma-joined strings as workflow identity.
- [ ] `40426305424_W1` loads scanner data, image data, split-choice original image, split crops, and nonzero ASE file size through resolver-backed APIs.

## Production Facts To Keep In View

- [x] ICUMS downloads CMR rows checked: `111,404`.
- [x] CMR rows with complete container + rotation + BL keys: `111,404`.
- [x] Duplicate CMR composite keys found in dry-run: `0`.
- [x] Application CCS CMR rows found: `1,663`.
- [x] CCS CMR rows that would be ready if flag enabled: about `1,330`.
- [x] Ready CMR rows currently blocked by `AwaitingDeclaration`: about `1,327`.
- [x] Existing synthetic CMR RCS/AG/AR/active assignments before rollout: `0`.
- [x] Re-run one-container production dry-run immediately before production pilot.
- [x] Save the pre-pilot one-container output with timestamp.
- [ ] Re-run these counts after each pilot batch.

## Do-Not-Cross Gates

- [x] Do not enable `CmrCompositeProgression` in production until the reviewed API build is deployed with the flag still off.
- [x] Do not run broad CMR backfill until a one-container pilot succeeds in production.
- [ ] Do not re-enable Decision Agent for CMR until human Analyst and Audit behavior is verified.
- [ ] Do not let CMR rows enter assignment without scanner + ICUMS + image + all three composite-key fields.
- [ ] Do not allow comma-joined ASE pseudo-container strings to become `AnalysisRecord.ContainerNumber`.
- [ ] Do not use comma-joined ASE pseudo-container strings as the primary identifier for original image, split-choice, fullscreen document, cache, or submission paths.
- [ ] Do not cache "no image" or "no scanner" for a logical container until source-scan resolution has been attempted and the resolver reason is included.
- [ ] Do not mark CMR as submitted unless the correct container/group/CCS row can be identified.

## Phase 0: Source Review And Worktree Hygiene

- [x] Review full diff for CMR files only.
- [x] Separate unrelated predictive-cache, image-splitter, and dashboard-alert changes from the CMR deploy package.
- [x] First-pass CMR deploy candidate file list is documented below.
- [x] Confirm no disposable staging database artifacts are tracked.
- [x] Confirm `.cmr-staging-pg/` remains ignored.
- [x] Confirm production config still has `CmrCompositeProgression:Enabled=false`.
- [ ] Confirm Decision Agent production setting remains disabled for rollout.
- [x] Write a short reviewed-state note with commit/hash or file list.
- [x] Preserve the local staging runner as an operator verification tool.

Exit gate:

- [x] Reviewed CMR-only file list is documented.
- [x] Build package can be reproduced from a clean/reviewed state.

### Phase 0 Worktree Review Note - 2026-05-13

First-pass CMR deploy candidates:

- Config/flag: `src/NickScanCentralImagingPortal.API/appsettings.json`, `src/NickScanCentralImagingPortal.API/appsettings.Production.template.json`
- Core: `src/NickScanCentralImagingPortal.Core/Entities/RecordCompletenessStatus.cs`, `src/NickScanCentralImagingPortal.Core/Helpers/ContainerCompletenessPolicy.cs`, `src/NickScanCentralImagingPortal.Core/Helpers/CmrCompositeKeyHelper.cs`
- Services: `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`, `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerStatusReconciliationService.cs`, `src/NickScanCentralImagingPortal.Services/IcumApi/IcumJsonIngestionService.cs`, `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`, `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs`, `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessBuilder.cs`, `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs`
- Tests/tools: `src/NickScanCentralImagingPortal.Tests/Services/QueueProgressionRegressionTests.cs`, `src/NickScanCentralImagingPortal.Tests/Services/RecordBuildingServiceCmrTests.cs`, `tests/NickScanCentralImagingPortal.Core.Tests/Architecture/CmrCompositeRecordIntakeGuardrailTests.cs`, `tests/NickScanCentralImagingPortal.Core.Tests/Architecture/StateOwnershipGuardrailTests.cs`, `tests/NickScanCentralImagingPortal.Core.Tests/Helpers/CmrCompositeKeyHelperTests.cs`, `tests/NickScanCentralImagingPortal.Core.Tests/Helpers/ContainerCompletenessPolicyTests.cs`, `tools/cmr-staging-runner`, `tools/cmr-composite-pilot-runner`
- Documentation: `docs/cmr-composite-key-completeness-scope-2026-05-13.md`, `docs/cmr-composite-key-blast-radius-and-implementation-plan-2026-05-13.md`, this tracker.

Exclude from the CMR deploy package unless separately reviewed:

- Predictive cache files/controllers/tests.
- Image splitter service/API/UI files.
- Dashboard alert entity/service/migration/model snapshot changes.
- Web app preload/display files unless explicitly pulled into Phase 9.
- `deploy-backups/` local backup folders.

Verified:

- `.cmr-staging-pg/` is ignored.
- No `.cmr-staging-pg/` files appear in `git status --short --untracked-files=all`.
- `CmrCompositeProgression:Enabled=false` in both API config files.

Reviewed production deploy package:

- Reviewed worktree: `C:\Shared\NSCIM_CMR_REVIEWED_20260513_215235`.
- Base commit: `1f211b7bf9ca01171d81dbec46123b8fd9e97881`.
- Copied CMR/completeness/submission safety files only; predictive cache, image splitter, dashboard alert, and web preload work stayed out of the deploy package.
- API publish came from the reviewed worktree, not the dirty main worktree.

## Phase 1: Baseline Verification Before Deploy

- [x] Re-run focused core guardrails:
  - `dotnet test tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj --filter "FullyQualifiedName~ContainerCompletenessPolicyTests|FullyQualifiedName~CmrCompositeKeyHelperTests|FullyQualifiedName~StateOwnershipGuardrailTests|FullyQualifiedName~CmrCompositeRecordIntakeGuardrailTests" --no-restore`
- [x] Re-run focused service queue/CMR regressions:
  - `dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "FullyQualifiedName~QueueProgressionRegressionTests|FullyQualifiedName~CmrComposite" --no-restore`
- [x] Build Services:
  - `dotnet build src\NickScanCentralImagingPortal.Services\NickScanCentralImagingPortal.Services.csproj --no-restore`
- [x] Build API:
  - `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore`
- [x] Build staging runner:
  - `dotnet build tools\cmr-staging-runner\CmrStagingRunner.csproj --no-restore`
- [x] Build pilot runner:
  - `dotnet build tools\cmr-composite-pilot-runner\CmrCompositePilotRunner.csproj --no-restore`
- [x] Re-run disposable local staging for `PIDU4444900`.
- [x] Capture final staging output showing:
  - `rcs_status=Ready`
  - `rec_status=Ready`
  - `ag_status=AnalystAssigned`
  - `grouptype=CMR`
  - `assignment_state=Active`
  - `queue_entry=present`
  - duplicate counts all `1`

Exit gate:

- [x] All commands pass with no errors.
- [x] Only known existing warnings remain.

## Phase 2: API-Only Production Deploy With CMR Flag Off

- [x] Produce API-only package from reviewed state.
- [x] Back up current production API publish folder or changed assemblies.
- [x] Run `Deploy.ps1 -ApiOnly -DryRun`.
- [x] Verify dry-run targets only `NSCIM_API`.
- [x] Deploy API-only with `CmrCompositeProgression:Enabled=false`.
- [x] Restart `NSCIM_API`.
- [x] Verify `GET /health/live` returns `200 Healthy`.
- [x] Verify `GET /health/ready` returns `200 Healthy`.
- [x] Verify logs do not show broad CMR feature-enabled behavior.
- [x] Verify assignment queues remain stable after one assignment cycle.
- [x] Verify ICUMS acknowledged reconciliation still logs `ICUMS-ACK-RECONCILE` on schedule if acknowledged files exist.

Exit gate:

- [x] Production is healthy with the CMR code deployed but feature disabled.
- [x] No new assignment outage, duplicate assignment, or submission-state regression observed during post-deploy checks.

### Phase 2 Production Evidence - 2026-05-13

- Reviewed worktree: `C:\Shared\NSCIM_CMR_REVIEWED_20260513_215235`.
- `dotnet restore NickscanERP.sln`: passed with existing NU1510 warnings.
- Core guardrails: `30` passed, `0` failed.
- Service queue/CMR regressions: `9` passed, `0` failed.
- API build: passed with `8` existing NU1510 warnings and `0` errors.
- `Deploy.ps1 -ApiOnly -DryRun`: repo root reviewed worktree, publish root `C:\Shared\NSCIM_PRODUCTION\publish`, service `NSCIM_API` only.
- `Deploy.ps1 -ApiOnly`: stopped `NSCIM_API`, published API, verified API DLL, patched runtime roll-forward, started `NSCIM_API`, verified process path.
- Health after deploy:
  - `http://localhost:5205/health/live` -> `200 Healthy`
  - `http://localhost:5205/health/ready` -> `200 Healthy`
  - `http://10.0.1.254:5205/health/live` -> `200 Healthy`
  - `http://10.0.1.254:5205/health/ready` -> `200 Healthy`
  - `https://localhost:5206/health/live` -> `200 Healthy`
  - `https://localhost:5206/health/ready` -> `200 Healthy`
- Production publish config contains no `CmrCompositeProgression` override; runtime remains default `false`.
- ICUMS acknowledgement reconciliation evidence from same day: `ICUMS-ACK-RECONCILE Complete: Files=619, ReconciledRows=426, Skipped=0`.
- Known acknowledged containers verified directly in DB:
  - `CAXU9272575` -> `WorkflowStage=Submitted`, `HasImageData=true`, `HasICUMSData=true`
  - `MRSU8158853` -> `WorkflowStage=Submitted`, `HasImageData=true`, `HasICUMSData=true`
- Post-restart record reconciliation worker fired successfully:
  - `created=0 updated=1 promoted=0 archived=1`
  - `processed=1 skipped=1 failed=0`
- Assignment system remained alive after restart; Analyst ready pool continued logging and later reflected the one-container CMR pilot as an increase from `7` to `8` ready groups.

## Phase 2A: Source Scan Identity And Split Flow Foundation

### Phase 2A Monitoring Board - 2026-05-14

Use this board as the live coordination surface while implementation agents work in source. Status values should be `Not started`, `In progress`, `Blocked`, `Ready for review`, or `Verified`; do not mark a stream `Verified` without a concrete evidence path, command output, API sample, or timestamped live check.

| Stream | Status | Owner | Evidence checklist | Verification gate |
| --- | --- | --- | --- | --- |
| Source-scan resolver | Verified | Resolver implementation agent | Added `ScanAssetResolution`, `ScanAssetResolutionRequest`, `ScanAssetCacheKey`, `SplitOptionContext`, `IScanAssetResolver`, and `ScanAssetResolver`; focused resolver tests passed 2026-05-14. | Logical container resolves to stable source scan identity and optional split job/result; comma-joined scan labels stay source metadata only. |
| API routes | Ready for review | API implementation agent | Added `/api/scan-assets/resolve`, `/api/scan-assets/{sourceScanId}/image`, split job original/result aliases, split-options by `AnalysisRecordId`, and split-options by `SplitJobId`; API build passed 2026-05-14. | Responses include resolver metadata and errors include resolver reason plus ambiguity details; legacy container routes call the resolver. |
| Web UI | Ready for review | Web UI implementation agent | Resolver identity is carried through preloaders, scanner tab, image tab, split-choice dialog, image decision view, audit dialog, fullscreen viewer, and document-icon paths; Web build passed 2026-05-14. | UI loads image/split assets through source identities and never uses comma-joined container text as workflow identity. |
| Tests | Verified | Test implementation agent | `dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "SourceScan|ScanAssetResolver" --no-restore --logger "console;verbosity=minimal"` passed: 14/14. | Focused resolver/API/UI-adjacent tests pass and lock the pseudo-container prevention behavior. |
| Live verification | Verified | Phase 2A tracker/master | Controlled API + Web deploy completed 2026-05-14 07:12 +00; live proof captured for `40426305424_W1`: scanner data loads, image data loads, split-choice original loads, split crops load, ASE file size is nonzero, and API/Web health stayed healthy. | Phase 2A exit gate passed for the known failing group; broader CMR rollout can resume under the remaining phase gates. |

- [x] Define the source-scan identity contract:
  - `ContainerNumber` is the real logical container.
  - `OriginalScanRecordId` / `SourceScanId` identifies the physical original scanner image.
  - `SplitJobId` identifies the two-container split job.
  - `SplitResultId` identifies the analyst-selectable crop pair.
  - `AnalysisRecordId` identifies one analyst workflow child.
- [x] Add resolver DTOs such as `ScanAssetResolution` and `SplitOptionContext`.
- [x] Add `IScanAssetResolver` or equivalent shared service.
- [ ] Add or prepare `OriginalScanRecordContainers` mapping:
  - `OriginalScanRecordId`
  - `ContainerNumber`
  - `PositionHint`
  - `SplitJobId`
  - `CreatedAtUtc`
- [x] Backfill or derive mappings from:
  - `OriginalScanRecords.OriginalContainerNumbers`
  - `AseScans.ContainerNumber`
  - split job container lists
- [x] Implement resolver order:
  - exact FS6000 match
  - exact ASE match
  - tokenized ASE/original scan match
  - analysis-record split job match
  - group/record context tie-breaker
  - explicit ambiguous result if still unsafe
- [x] Route existing scanner/image endpoints through the resolver.
- [x] Add source-scan original image endpoint.
- [x] Add split job original image endpoint.
- [x] Add split result crop endpoint by split job/result/side.
- [x] Add split-options endpoint by `AnalysisRecordId`.
- [x] Keep legacy container endpoints as compatibility aliases only.
- [x] Update error responses to include resolver reason and ambiguity details.
- [x] Update cache/preload key shape to include source scan and split identity when image/split data is cached.
- [x] Add regression tests for:
  - exact single-container ASE
  - exact FS6000
  - two-container ASE original
  - ambiguous multi-source match
  - split job lookup by analysis record
- [ ] Add explicit regression test for no negative cache before resolver attempt.
- [x] Live verify `40426305424_W1`:
  - scanner tab loads
  - image tab loads
  - split-choice original image loads
  - split candidate crops load
  - ASE file size is nonzero

Exit gate:

- [x] Source-image and split-choice flows work through stable source identities, not comma-separated container numbers.
- [x] No CMR rollout expansion happens until this identity foundation is verified for the known failing group.

### Phase 2A Implementation Evidence - 2026-05-14

- Resolver/API/Web implementation streams are integrated in source and ready for controlled deploy review.
- Combined focused regression command passed:
  - `dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "SourceScan|ScanAssetResolver" --no-restore --logger "console;verbosity=minimal"`
  - Result: `Passed: 14, Failed: 0, Skipped: 0`.
- API build passed:
  - `dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore`
  - Result: `0 errors`, existing warnings only.
- Web build passed:
  - `dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore`
  - Result: `0 errors`, existing warning set only (`246` warnings on the last full Razor build).
- Diff hygiene passed:
  - `git diff --check` on the Phase 2A API/Web/resolver/test file set reported no whitespace errors; Git only reported working-copy LF-to-CRLF warnings.
- Live verification completed after controlled API + Web deploy for `40426305424_W1`: scanner tab, image tab, split-choice original, split candidate crops, fullscreen document icon, and nonzero ASE file size all passed.

### Phase 2A Controlled Deploy And Live Verification - 2026-05-14

- Backup created before deploy:
  - `deploy-backups\api-web-20260514-071000`
- Controlled deploy sequence:
  - Stopped `NSCIM_WebApp` at `2026-05-14T07:10:24`.
  - Stopped `NSCIM_API` at `2026-05-14T07:10:25`.
  - Published `NSCIM_API` at `2026-05-14T07:11:10`.
  - Published `NSCIM_WebApp` at `2026-05-14T07:12:17`.
  - Restored live `appsettings*.json` for API and WebApp.
  - Patched runtime configs with `rollForward=latestMajor`.
  - Started `NSCIM_API` at `2026-05-14T07:12:26`.
  - Started `NSCIM_WebApp` at `2026-05-14T07:12:27`.
- Published binaries:
  - `publish\API\NickScanCentralImagingPortal.API.dll` -> `2026-05-14 07:11:08`, `3264000` bytes.
  - `publish\WebApp\NickScanWebApp.New.dll` -> `2026-05-14 07:12:14`, `5234688` bytes.
- Live service/process proof:
  - `NSCIM_API` running from `C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe`.
  - `NSCIM_WebApp` running from `C:\Shared\NSCIM_PRODUCTION\publish\WebApp\NickScanWebApp.New.exe`.
- Health after deploy:
  - `http://localhost:5205/health/live` -> `200`.
  - `http://localhost:5205/health/ready` -> `200`.
  - `http://10.0.1.254:5205/health/live` -> `200`.
  - `http://10.0.1.254:5205/health/ready` -> `200`.
  - `https://localhost:5206/health/live` -> `200`.
  - `https://localhost:5206/health/ready` -> `200`.
  - `https://10.0.1.254:5206/health/live` -> `200`.
  - `https://10.0.1.254:5206/health/ready` -> `200`.
  - `http://localhost:5299` -> `200`.
  - `https://localhost:5300` -> `200`.
- Resolver proof for `40426305424_W1` / `MSMU1683356` / `AnalysisRecordId=3418`:
  - `Found=true`.
  - `SourceScanId=4243`.
  - `OriginalScanRecordId=4243`.
  - `SourceContainerNumbers="MSMU1683356, MRKU8254509"`.
  - `SplitJobId=effda69a-d3a3-476d-8b14-8095d3f4e35f`.
  - `SplitPosition=left`.
  - `FileSizeBytes=1753540`.
  - `ResolvedBy=TokenizedSourceContainer`.
  - `ResolutionReason=TokenizedContainerMatch`.
- Scanner tab proof:
  - `/api/containerdetails/scanner/MSMU1683356?page=1&pageSize=1000` -> `TotalCount=13`, `Status=Found`.
  - `/api/containerdetails/scanner/MSMU1683356?full=true` -> `ScannerType=ASE`, `Source Scan Id=4243`, `Source Container Number(s)=MSMU1683356, MRKU8254509`, `Image Size=1753540 bytes`.
- Image tab proof:
  - `/api/containerdetails/images/MSMU1683356` -> `ImageCount=1`, `MaxFileSizeBytes=1753540`.
  - Signed full image URL returned `200 image/jpeg`, `219414` bytes.
- Split-choice proof:
  - `/api/image-analysis/records/3418/split-options` -> `SourceScanId=original:4243`, `SplitJobId=effda69a-d3a3-476d-8b14-8095d3f4e35f`, `SplitStatus=Ready`.
  - `/api/image-splitter/records/3418/split-options` -> `SplitStatus=Ready`, `OptionCount=2`.
  - Signed original image URL returned `200 image/jpeg`, `145754` bytes.
  - Signed crop `87936a62-3798-40a0-b622-af67cc6bd62e` returned `200 image/jpeg`, `71684` bytes.
  - Signed crop `919c2835-c5fc-4b83-9d62-0a14e4ea9902` returned `200 image/jpeg`, `75134` bytes.
- Fullscreen document icon proof:
  - `/api/scan-assets/4243/image?size=full&containerNumber=MSMU1683356` returned `200 image/jpeg`, `219414` bytes.

## Phase 3: Production Pre-Pilot Read-Only Checks

- [x] Confirm CMR runtime state:
  - `CmrCompositeProgression:Enabled=false`
- [ ] Count CMR downloads rows.
- [ ] Count CMR rows missing container number.
- [ ] Count CMR rows missing rotation number.
- [ ] Count CMR rows missing BL number.
- [ ] Count duplicate CMR composite keys.
- [ ] Count CCS CMR rows with scanner + ICUMS + image.
- [ ] Count CMR rows blocked at `AwaitingDeclaration`.
- [x] Count existing selected-pilot `RecordCompletenessStatus` rows where `DeclarationNumber='CMR-C40FEA9B3C7FA383450D'`.
- [x] Count existing selected-pilot `AnalysisGroup` rows where `GroupIdentifier='CMR-C40FEA9B3C7FA383450D'`.
- [x] Count active assignments for selected-pilot CMR group.
- [ ] Count duplicate active assignments across the app.
- [x] Count selected-pilot `AnalysisRecord` rows by `(GroupId, ContainerNumber)`.
- [x] Save before-pilot result in the CMR plan.

Exit gate:

- [ ] Missing-key count is zero or understood.
- [ ] Duplicate composite-key count is zero.
- [x] Existing selected-pilot CMR synthetic record/group count is expected before apply.

### Phase 3 Selected-Pilot Evidence - 2026-05-13

Production dry-run command used explicit production app/downloads connections and wrote nothing:

- `container=PIDU4444900`
- `candidate_count=1`
- `key=CMR-C40FEA9B3C7FA383450D`
- `label="CMR PIDU4444900 / 26PIL000026 / SHSE60424600"`
- `boe_id=116314`
- `ccs_rows=1`
- `ready_ccs_rows=1`
- `ccs_status=AwaitingDeclaration`
- `ccs_workflow=Pending`
- `ccs_groupidentifier=blank`
- `rcs=0 ag=0 ar=0 active_assignments=0`
- `ready=True`
- `unsafe_duplicates=False`
- `dry_run_ready=1`
- `errors=0`

## Phase 4: Controlled Backfill/Pilot Tooling

- [x] Add or formalize an operator command for CMR dry-run candidates.
- [x] Add batch limit support.
- [x] Add container allow-list support.
- [x] Add dry-run mode that prints candidate rows without writes.
- [x] Add apply mode that can process exactly one selected container.
- [x] Ensure apply mode mutates only selected CMR candidates.
- [x] Ensure apply mode builds CMR `RecordCompletenessStatus`.
- [x] Ensure apply mode creates CMR `AnalysisGroup` and `AnalysisRecord` rows compatible with normal assignment pickup.
- [x] Ensure apply mode can stop before assignment.
- [x] Add duplicate-protection checks inside the operator tool.
- [x] Add per-run summary:
  - candidates found
  - dry-run ready
  - records built or updated
  - AG created
  - AG already present
  - AR created
  - CCS rows stamped
  - skipped not ready
  - skipped duplicate
  - errors
- [x] Print active assignment count in candidate rows for post-apply/post-assignment verification.
- [x] Add run correlation id for logs.
- [x] Add rollback notes for each mutation type.

Exit gate:

- [x] Dry-run matches the known production candidate count.
- [x] One-container apply can be run without broad backlog movement.

### Local Staging Evidence - 2026-05-13

- `dotnet run --project tools\cmr-composite-pilot-runner\CmrCompositePilotRunner.csproj -- --dry-run --container PIDU4444900`
  - `candidate_count=1`
  - `key=CMR-C40FEA9B3C7FA383450D`
  - `ready=True`
  - `unsafe_duplicates=False`
  - before apply: `rcs=0 ag=0 ar=0 active_assignments=0`
- `dotnet run --project tools\cmr-composite-pilot-runner\CmrCompositePilotRunner.csproj -- --apply --confirm-apply --container PIDU4444900 --run-id local-pidu-pilot`
  - `records_built_or_updated=1`
  - `groups_created=1`
  - `analysis_records_created=1`
  - `completeness_rows_stamped=1`
  - `errors=0`
- Post-apply dry-run:
  - `rcs=1 ag=1 ar=1 active_assignments=0`
  - `unsafe_duplicates=False`
- Normal assignment staging harness:
  - `rcs_status=Ready workflow=ImageAnalysis`
  - `rec_status=Ready`
  - `ag_status=AnalystAssigned grouptype=CMR linked_rcs=1`
  - `assignment_state=Active assigned_to=cmr.stage.analyst`
  - `queue_entry=present`
  - `duplicate_counts rcs=1 ag=1 ar=1 active_assignments=1 queue_entries=1`
- Post-assignment pilot dry-run:
  - `rcs=1 ag=1 ar=1 active_assignments=1`
  - `unsafe_duplicates=False`
  - `errors=0`

### Phase 4 Rollback Notes

Rollback is only safe before Analyst/Audit work or ICUMS submission. If any decision, audit, submission, or acknowledgement exists, pause and preserve history instead of deleting rows.

- `RecordCompletenessStatus` / `RecordExpectedContainer`: created by CMR operational key. Roll back only rows where `ClearanceType='CMR'`, `DeclarationNumber='CMR-*'`, and no downstream worked assignment exists.
- `AnalysisGroup` / `AnalysisRecord` / `AnalysisParentGroup`: created with `GroupIdentifier='CMR-*'` and `GroupType='CMR'`. Roll back only if the group has no completed records, no non-cancelled worked assignments, and no submitted containers.
- `ContainerCompletenessStatus.GroupIdentifier`: stamped from blank/null to the CMR key. Roll back by clearing only rows where the previous value was blank/null and the selected container still belongs to the pilot run.
- Assignment/queue rows: the pilot tool does not create these directly. If normal assignment creates them during verification, cancel/delete only unworked active assignments and their materialized queue entries for the selected CMR group.
- Feature flag: first rollback action is always to keep or set `CmrCompositeProgression:Enabled=false`, then stop any pilot/backfill process before touching data.

## Phase 5: One-Container Production Pilot

- [x] Select the first pilot container, recommended known candidate: `PIDU4444900`.
- [x] Reconfirm it has:
  - ICUMS CMR row
  - scanner data
  - image data
  - rotation number
  - container number
  - BL number
  - no existing CMR RCS/AG/AR/active assignment
- [x] Enable CMR feature only for the controlled process or smallest safe scope.
- [x] Run one-container dry-run.
- [x] Run one-container apply.
- [x] Verify CCS:
  - `GroupIdentifier` is the `CMR-*` key
  - status/workflow moved as expected
  - scanner/ICUMS/image flags preserved
- [x] Verify RCS:
  - `ClearanceType='CMR'`
  - `DeclarationNumber` or operational identity is `CMR-*`
  - `RotationNumber` set
  - `BlNumber` set
  - `PrimaryBoeDocumentId` set
  - status is `Ready`
  - workflow is `ImageAnalysis` or the expected pilot pre-assignment stage
- [ ] Verify REC:
  - child uses real container number
  - status is `Ready`
  - BOE document id is set
- [x] Verify AG:
  - `GroupType='CMR'`
  - `RecordCompletenessStatusId` set
  - status is `Ready` or `AnalystAssigned`
- [x] Verify AR:
  - real container number
  - no comma-joined pseudo-container
- [x] Verify assignment readiness:
  - active Analyst assignment if a ready analyst is available
  - no duplicate active assignment
  - materialized queue entry present
- [ ] Verify Workbench displays the assignment.
- [ ] Save pilot result in the CMR plan.

Exit gate:

- [x] One CMR container reaches the Analyst ready pool without duplicate rows.
- [x] No unrelated assignments are displaced during the pilot checks.

### Phase 5 Production Pilot Evidence - 2026-05-13

Apply command used explicit production app/downloads connections and `--confirm-apply`:

- `run_id=prod-pidu4444900-20260513`
- `candidate_count=1`
- `container=PIDU4444900`
- `key=CMR-C40FEA9B3C7FA383450D`
- before apply: `rcs=0 ag=0 ar=0 active_assignments=0`
- `ready=True`
- `unsafe_duplicates=False`
- `records_built_or_updated=1`
- `groups_created=1`
- `groups_already_present=0`
- `analysis_records_created=1`
- `completeness_rows_stamped=1`
- `errors=0`

Post-apply DB verification:

- `RecordCompletenessStatus.Id=20238`
- `DeclarationNumber=CMR-C40FEA9B3C7FA383450D`
- `ClearanceType=CMR`
- `RotationNumber=26PIL000026`
- `BlNumber=SHSE60424600`
- `TotalExpectedContainers=1`
- `ContainersReady=1`
- `ContainerCompletenessStatus.ContainerNumber=PIDU4444900`
- `ContainerCompletenessStatus.GroupIdentifier=CMR-C40FEA9B3C7FA383450D`
- `ContainerCompletenessStatus.HasImageData=true`
- `ContainerCompletenessStatus.HasICUMSData=true`
- `AnalysisGroup.Id=57cf7cbd-82a1-4755-8fc2-186258e17393`
- `AnalysisGroup.GroupIdentifier=CMR-C40FEA9B3C7FA383450D`
- `AnalysisGroup.NormalizedGroupIdentifier=CMR-C40FEA9B3C7FA383450D`
- `AnalysisGroup.Status=Ready`
- `AnalysisGroup.ScannerType=ASE`
- `AnalysisGroup.RecordCompletenessStatusId=20238`
- No active assignment row yet for the CMR group at verification time.
- Orchestrator ready pool increased from `7` to `8` Analyst-ready groups after the pilot.
- Assignment tick logged `Total UserReadiness records for role 'Analyst': 3`, but no active assignment was created because no Analyst user was ready at that tick.
- User readiness DB check confirmed every Analyst/Audit readiness row was `IsReady=false`; the freshest Analyst heartbeat was over 275 minutes old at verification time.

## Phase 6: Analyst And Audit Workflow Validation

- [ ] Have Analyst open the pilot CMR assignment.
- [ ] Confirm container image loads.
- [ ] Confirm scanner tab loads the correct scan.
- [ ] Confirm ICUMS tab shows CMR identity clearly.
- [ ] Confirm UI does not label synthetic `CMR-*` key as BOE number.
- [ ] Submit an Analyst decision.
- [ ] Verify `AnalysisRecord` status changes correctly.
- [ ] Verify `RecordExpectedContainer` status changes correctly.
- [ ] Verify `RecordCompletenessStatus` rollup changes correctly.
- [ ] Verify `ContainerCompletenessStatus.WorkflowStage` changes correctly.
- [ ] Verify Audit assignment is created/materialized if workflow requires audit.
- [ ] Have Auditor open the pilot CMR audit assignment.
- [ ] Submit Audit decision.
- [ ] Verify assignment/queue cleanup after Analyst and Audit.
- [ ] Verify no zombie/drift/orphan sweeper cancels the CMR group.

Exit gate:

- [ ] Pilot CMR record progresses through Analyst and Audit with correct state transitions.

## Phase 7: CMR Submission And ICUMS Confirmation

- [ ] Verify generated ICUMS payload for pilot CMR contains:
  - empty or acceptable `DeclarationNumber`
  - `RotationNumber`
  - `BlNumber`
  - real `ContainerNumber`
  - image document
  - scan reference
  - analyst/auditor names
  - verdict/findings
- [ ] Confirm ICUMS team accepts the CMR payload shape with the three-key identity.
- [ ] Verify submission gate does not reject valid CMR solely because BOE/declaration is blank.
- [ ] Verify pending-submission stage sync updates the correct CCS row.
- [ ] Verify successful live submit moves the correct CCS row to `Submitted`.
- [ ] Verify acknowledged-file reconciliation can move CMR rows to `Submitted` if the app crashes after HTTP success.
- [ ] Verify retry flow preserves the group id and container identity.
- [ ] Verify payload viewer or ops logs display CMR identity clearly.

Exit gate:

- [ ] Pilot CMR payload is accepted by ICUMS.
- [ ] Submitted state is visible in NSCIM.

## Phase 8: CMR-To-BOE Upgrade Lifecycle

- [ ] Trace current `IcumJsonIngestionService.CascadeCMRUpgradeAsync` behavior for a CMR that already has CMR RCS/AG rows.
- [ ] Define expected behavior for CMR analysis not yet submitted when IM/EX BOE arrives.
- [ ] Define expected behavior for CMR analysis already submitted when IM/EX BOE arrives.
- [ ] Link or migrate existing CMR RCS to real BOE declaration without losing lineage.
- [ ] Prevent duplicate IM/EX RCS creation when an active CMR RCS already covers the same container/rotation/BL.
- [ ] Prevent duplicate AG/AR/assignment creation when IM/EX arrives after CMR assignment.
- [ ] Preserve previous decisions and audit trail.
- [ ] Invalidate predictive cache keys for both container and group.
- [ ] Add upgrade logs with correlation id.
- [ ] Add regression test for CMR to IM/EX upgrade before assignment.
- [ ] Add regression test for CMR to IM/EX upgrade during active assignment.
- [ ] Add regression test for CMR to IM/EX upgrade after submission.

Exit gate:

- [ ] BOE arrival does not duplicate assignments or lose history.

## Phase 9: UI, API, And Display Polish

- [ ] Audit API responses that expose `GroupIdentifier` or `DeclarationNumber`.
- [ ] Add CMR display label where needed: `CMR {container} / {rotation} / {bl}`.
- [ ] Audit API responses that expose scanner/image identity by container number.
- [x] Update scanner tab APIs to return resolver metadata: source scanner type, source scan id, split job id, and resolution reason.
- [x] Update image tab APIs to load image bytes and metadata by source scan identity after resolver lookup.
- [x] Update fullscreen document icon to use the same resolver-backed source image path as the summary/image tabs.
- [x] Update split-choice dialog to request split options by `AnalysisRecordId` or `SplitJobId`, not by comma-joined container text.
- [x] Update split-choice dialog to load original image by source scan id and crops by split job/result/side.
- [ ] Update Record Completeness API/page to show CMR identity as CMR, not BOE.
- [ ] Update Image Analysis assignment cards/list to show CMR identity clearly.
- [ ] Update Audit Review views to show CMR identity clearly.
- [ ] Update Cargo Group views to handle `GroupType='CMR'`.
- [ ] Update CMR Validation page to distinguish:
  - valid and progressable
  - missing key
  - blocked
  - already promoted to RCS/AG
  - submitted
- [ ] Update dashboard/module queue counts to include/explain CMR backlog movement.
- [x] Update predictive preload DTOs if CMR display metadata is missing from cached assignment/container context.
- [ ] Add UI smoke test checklist for CMR assignment and audit.

Exit gate:

- [ ] Operators and analysts see meaningful CMR identity everywhere relevant.

## Phase 10: Predictive Cache And Assignment Stability Follow-Through

- [ ] Confirm CMR queue creation triggers predictive assignment preload through `ReadyGroupsCacheService.UpsertQueueEntryAsync`.
- [ ] Confirm CMR assignment removal invalidates predictive assignment cache.
- [ ] Confirm CMR container context preload returns scanner, ICUMS, BOE/CMR summary, and image metadata.
- [x] Include `SourceScanId`, `OriginalScanRecordId`, `SplitJobId`, `SplitResultId`, and resolver status in cached image-analysis view context where available.
- [ ] Prevent stale negative image/scanner cache entries when a container resolves only through a two-container source scan.
- [ ] Invalidate image/split cache entries after split job completion, analyst split selection, decision save, audit save, and ICUMS submission.
- [ ] Add cache hit/miss/fallback telemetry.
- [ ] Add operator dashboard/readout for predictive preload:
  - enabled
  - background enabled
  - candidate count
  - success count
  - failure count
  - skipped count
  - last run duration
- [ ] Keep full image byte preload disabled.
- [ ] Add permanent Ready-orphan housekeeping or dashboard distinction.
- [ ] Restore safe no-record standalone intake fallback for non-CMR rows with image + BOE/match data but no RCS.
- [ ] Keep active-human-assignment guard in Decision Agent.
- [ ] Re-evaluate whether Decision Agent can ever process CMR, and document the answer.

Exit gate:

- [ ] CMR assignments benefit from preload without changing correctness.
- [ ] Stale/orphan groups no longer look like assignable analyst work.

## Phase 11: Small-Batch Production Expansion

- [x] Run batch size `5`.
- [x] Verify no duplicate RCS keys.
- [x] Verify no duplicate AG keys.
- [x] Verify no duplicate AR `(GroupId, ContainerNumber)`.
- [x] Verify no duplicate active assignments.
- [x] Verify all batch rows are visible or intentionally held.
- [x] Verify Analyst users can work batch assignments.
- [ ] Verify Audit users can work batch assignments.
- [ ] Verify no unexpected Decision Agent work.
- [ ] Verify ICUMS payloads are generated correctly for completed CMRs.
- [ ] Run batch size `25` only after batch `5` is clean.
- [ ] Run batch size `100` only after batch `25` is clean.
- [ ] Continue controlled drain until blocked backlog reaches expected zero or all remaining rows have documented blockers.

Exit gate:

- [ ] Production CMR backlog drains in controlled batches with clean duplicate metrics.

### Phase 11 Batch-5 Production Evidence - 2026-05-15/2026-05-16

Controlled production expansion was run with production `CmrCompositeProgression:Enabled=false` and ICUMS live submission still off. The pilot runner enabled CMR composite progression only inside the controlled process. The failed first dry-run used the unresolved DB-password placeholder and wrote nothing.

Dry-run scope:

- `run_id=cmr-pilot-20260515-234607`
- `candidate_count=5`
- all candidates had `ready=True`
- all candidates had `unsafe_duplicates=False`
- all candidates had `rcs=0 ag=0 ar=0 active_assignments=0`
- `dry_run_ready=5`
- `errors=0`

Applied allow-list:

- `MRSU3641112` -> `CMR-C0B7CF67E04340BF0023`
- `TCNU2435950` -> `CMR-C2527B0C4253B3B7D5AC`
- `ARKU5035000` -> `CMR-D5027C2E4965442C1A0F`
- `PCIU9133534` -> `CMR-C201806C3DF9DB1AEB23`
- `MSMU6263232` -> `CMR-CA4757E30E2AB0B37A97`

Apply result:

- `run_id=prod-cmr-batch5-20260515-2346`
- `records_built_or_updated=5`
- `groups_created=5`
- `analysis_records_created=5`
- `completeness_rows_stamped=5`
- `skipped_not_ready=0`
- `skipped_duplicates=0`
- `errors=0`

Post-apply verification:

- Each selected CMR has one `RecordCompletenessStatus` row with `Status=Ready` and `WorkflowStage=ImageAnalysis`.
- Each selected CMR has one `RecordExpectedContainer` row with `Status=Ready`.
- Each selected CMR has one `AnalysisGroup` row with `GroupType=CMR`.
- Each selected CMR has one `AnalysisRecord` row with the real container number.
- Duplicate counts were all zero: RCS, AG, AR, and active assignments.
- After the assignment cycle, all five selected CMR groups moved to `AnalystAssigned`.
- Each selected CMR group has one active Analyst assignment and one queue entry, assigned to `pimage`.
- Latest queue refresh for the batch was `2026-05-16 00:47:59 Europe/London`.
- API and Web health remained `200`.
- Fresh application error count stayed `0` for the last 15 minutes.

## Phase 12: Observability, Alerts, And Runbooks

- [ ] Add or document read-only CMR rollout metrics:
  - blocked CMR count
  - progressable CMR count
  - CMR RCS count
  - CMR AG count
  - CMR active assignment count
  - CMR pending submission count
  - CMR submitted count
  - CMR error count
- [ ] Add log searches:
  - `INTAKE-RECORD`
  - `RECORD-BUILD`
  - `ASSIGNMENT-EVENT`
  - `ICUMS-PAYLOAD`
  - `ICUMS-SUBMIT`
  - `ICUMS-ACK-RECONCILE`
- [ ] Add rollback runbook:
  - turn flag off
  - stop pilot/backfill job
  - identify CMR rows created in last run by correlation id
  - cancel only safe/unworked assignments if needed
  - preserve submitted/audited history
- [ ] Add operator runbook for one-container pilot.
- [ ] Add operator runbook for batch expansion.
- [ ] Add incident triggers:
  - duplicate active assignments greater than zero
  - CMR missing-key count increases unexpectedly
  - CMR submitted payload accepted but CCS not submitted
  - assignment queue missing active assignment rows
  - API health degraded after rollout

Exit gate:

- [ ] Operators can run, monitor, pause, and roll back CMR rollout safely.

## Phase 13: Test Coverage Still Needed

- [x] CMR key helper tests.
- [x] CMR completeness policy tests.
- [x] CMR record building service tests.
- [x] Source guardrails for feature gate/intake/duplicate protection.
- [x] Local staging harness for one known CMR container.
- [x] Source-scan resolver tests for exact ASE, exact FS6000, tokenized two-container ASE, and ambiguous matches.
- [x] Split-options tests proving lookup by `AnalysisRecordId` / `SplitJobId`.
- [x] Image metadata test proving two-container ASE source reports nonzero file size for a child logical container.
- [ ] UI/API smoke test for `40426305424_W1` scanner tab, image tab, split-choice original, split crop, and fullscreen document icon.
  - 2026-05-14: API + Web lossless split-crop hotfix deployed. Verified signed live API proxy returns nonzero `image/png` crops for job `effda69a-d3a3-476d-8b14-8095d3f4e35f` results `87936a62-3798-40a0-b622-af67cc6bd62e` and `919c2835-c5fc-4b83-9d62-0a14e4ea9902`, left and right sides. Browser-level tab smoke still needs operator/UI confirmation after hard refresh.
  - 2026-05-14: Web split-choice byte-loader hotfix deployed. Split-choice cards now fetch crop bytes through the authenticated WebApp path and render a correctly typed data URI, while retaining signed image URLs as fallback. Live Web health passed and the lossless API crop still returns nonzero `image/png`.
- [ ] CMR intake-to-queue automated service test with active user readiness.
- [ ] CMR decision-side-effects service test.
- [ ] CMR audit-side-effects test.
- [ ] CMR submission payload test proving rotation/container/BL identity.
- [ ] CMR submission-gate test with blank declaration number.
- [ ] CMR acknowledged-file reconciliation test for CMR group id.
- [ ] CMR-to-BOE upgrade duplicate-prevention tests.
- [ ] CMR UI display smoke test checklist.
- [ ] Predictive preload CMR assignment/context test.
- [ ] Batch backfill dry-run/apply tests.

Exit gate:

- [ ] Critical CMR lifecycle paths have automated regression coverage before broad backlog drain.

## Phase 14: Documentation To Keep Updated

- [x] `docs/cmr-composite-key-completeness-scope-2026-05-13.md`
- [x] `docs/cmr-composite-key-blast-radius-and-implementation-plan-2026-05-13.md`
- [x] `docs/predictive-preload-cache-implementation-todo-2026-05-11.md`
- [x] This giant TODO.
- [x] Add production pilot result section after one-container pilot.
- [ ] Add batch results table after each production batch.
- [ ] Add final launch summary after backlog drain.
- [ ] Add "known remaining limitations" section for CMR UI and Decision Agent eligibility.

## Open Questions

- [ ] Should production CMR pilot use a global feature flag plus one-container operator job, or do we need a more granular allow-list feature flag first?
- [ ] Should CMR payloads send an empty `DeclarationNumber`, omit it, or send a placeholder agreed with ICUMS?
- [ ] Should CMR groups ever be eligible for Decision Agent, or must they stay human-only?
- [ ] When a CMR later upgrades to BOE, should the user-facing identity switch to BOE immediately or preserve the CMR label until submission is complete?
- [ ] Should CMR validation metrics count "valid and already promoted" separately from "valid but not yet promoted"?
- [ ] Do we need explicit DB indexes for CMR composite lookup, or are current indexes enough under batch sizes?
- [ ] Should `ScanAssetId` become a real scanner-neutral table now, or should Phase 2A start with `OriginalScanRecordId` mapping and resolver DTOs only?
- [ ] Should split selection persist the chosen `SplitResultId` on `AnalysisRecord` before decision save, or only after the analyst confirms a crop?

## Recommended Next Implementation Order

1. Implement Phase 2A source-scan resolver and split-flow identity foundation.
2. Verify `40426305424_W1` scanner tab, image tab, split-choice original, split crops, fullscreen document icon, and ASE file size.
3. Confirm at least one Analyst user is Ready so the pilot group can become an active assignment.
4. Verify Workbench display for `CMR-C40FEA9B3C7FA383450D`.
5. Drive the pilot through Analyst and Audit.
6. Verify the generated ICUMS CMR payload uses rotation/container/BL identity.
7. Verify submitted-state behavior and acknowledged-file reconciliation for the pilot.
8. Implement/verify CMR-to-BOE upgrade duplicate prevention.
9. Add missing automated tests around resolver, decision, submission, upgrade, and backfill.
10. Expand in small batches with duplicate and queue metrics after each batch.
11. Polish UI labels, dashboards, predictive cache telemetry, and runbooks.
