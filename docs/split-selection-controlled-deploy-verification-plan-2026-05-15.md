# Split Selection Controlled Deploy Verification Plan

Date: 2026-05-15
Status: readiness review only; do not deploy from this document
Scope: outstanding split-selection items 1-5 from `docs/split-choice-image-display-root-cause-review-2026-05-15.md`.

## Current Deploy Layout

Canonical live folders from `Deploy.ps1` and service inspection:

- API service: `NSCIM_API`
  - live path: `C:\Shared\NSCIM_PRODUCTION\publish\API`
  - executable: `C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe`
  - service state at review time: `Running`
- Web service: `NSCIM_WebApp`
  - live path: `C:\Shared\NSCIM_PRODUCTION\publish\WebApp`
  - executable: `C:\Shared\NSCIM_PRODUCTION\publish\WebApp\NickScanWebApp.New.exe`
  - service state at review time: `Running`
- Related supervised services present but not in scope for this split-selection deploy: `NSCIM_NickComms`, `NSCIM_Portal`, `NickHR_API`, `NickHR_WebApp`, `NickFinance_WebApp`.
- Python image-splitter is not a separate Windows service. `Deploy.ps1` documents it as supervised under `NSCIM_API` by `ImageSplitterSupervisorService`.

Observed staging/backup conventions:

- Broad controlled API+Web stage: `deploy-staging\20260514-215653`
- Broad controlled API+Web backup: `deploy-backups\controlled-api-web-20260514-215653`
- Split fallback stages:
  - API: `deploy-staging\split-fallback-api-20260514-221707`
  - Web: `deploy-staging\split-fallback-web-20260514-221707`
  - reviewed list: `deploy-staging\reviewed-split-fallback-20260514-221820`
  - backup: `deploy-backups\split-fallback-20260514-221820`
- Web-only split-choice proxy stage: `deploy-staging\webapp-split-choice-proxy-20260515-133000`
- Web-only split-choice proxy backup: `deploy-backups\webapp-pre-split-choice-proxy-20260515-133000`
- Web-only eager image render stage: `deploy-staging\webapp-split-choice-eager-20260515-135520`
- Web-only eager image render backup: `deploy-backups\webapp-pre-split-choice-eager-20260515-135520`
- `deploy-staging\latest-controlled-deploy.txt` currently records `20260514-215653`.

Current production config hashes to preserve:

- `publish\API\appsettings.json`: `301D3B5D54DEBF2FE7EFB4E6C83D5CAB00B15BB79A93AA901237E571CED7C791`
- `publish\API\appsettings.Development.json`: `11FC5F19CEA018C50671CD46AC85428929504499240C7AFCDE3F059C40420D8C`
- `publish\API\appsettings.Logging.json`: `96B80BE610C791EC4FF2E30E16F112A4C64BBA9EB17B3B1E97C5395D6F5B3E55`
- `publish\API\appsettings.Production.template.json`: `F0A6ED429D67F49F0C7D30FE8FF23EAFD07CB6E95E429DF7C7A11F6E228116FD`
- `publish\WebApp\appsettings.json`: `EF25195F7858EAF951489A62D02F32DB0AD5E0595803DCD4ABAD70BF4CF4949D`
- `publish\WebApp\appsettings.Development.json`: `E90E8F2D058D04B73E54B4AB37D2F1874C790540FE34748616A8E935F32CEEAD`

Config preservation risk:

- `Deploy.ps1` publishes directly to `publish\API` and `publish\WebApp`. That is useful for normal deploys, but it can overwrite live `appsettings*.json`.
- For this controlled split-selection rollout, use staged publish folders and copy to live while excluding `appsettings*.json`.
- Recheck the hashes above before and after copy. Any drift in live config is a rollback-triggering event unless explicitly approved.

Current binary evidence at review time:

- `publish\API\NickScanCentralImagingPortal.API.dll`: `A9ABD9DC0D30E354B175B570DE33E3B2BDD126A84B39434296BB12B0459E956D`
- `publish\WebApp\NickScanWebApp.New.dll`: `9204BB16EC49C14F84F7A18935888DF977E4B31C188D981D3DFD2B089D3F7DF7`
- `publish\WebApp\NickScanWebApp.Shared.dll`: `AD3A6AC8B99F9E337EA3A465CB9200ADE7D827126DD1D167375942A5D29CB05C`

Note: the live WebApp DLL hash is newer/different than the prior eager-render stage hash `FB2CE0C6EFC9563D7AA70A39EE1401D7EE13B6084FF7803E595E15D4CA574955`, so the next controlled package must be built from the reviewed current source state, not by reusing the old WebApp staging folder.

## Outstanding Items 1-5

The five outstanding items from the root-cause review are:

1. Add a split-choice state callback so `ImageAnalysisViewDialog` hides `ImageDecisionView` until the analyst chooses or skips the split.
2. Change split-choice identity to prefer `analysisRecordId` and `splitJobId` over `ContainerNumber`, especially in consolidated/group views.
3. Add lightweight browser load/error telemetry for each split option image source.
4. Add a focused UI regression that asserts stored option A/B render as eager proxied image URLs, not lazy direct API URLs.
5. Separately repair or suppress the raw splitter result-list `500` so it stops muddying operational diagnosis.

Current source inspection shows items 1 and 2 are partly implemented in the working tree:

- `ImageAnalysisViewDialog.razor` wires `OnSplitChosen`, `OnSplitSkipped`, and `OnSplitStateChanged`, and gates `ImageDecisionView` behind `ShouldShowImageDecisionView(...)`.
- `SplitChoiceDialog.razor` prefers identity routes when `analysisRecordId` or `splitJobId` is available, then falls back to container route.
- Choose/skip now posts by analysis record first, then split job, then container fallback.

Items 3 and 4 are not proven deploy-ready from current inspection.

Item 5 is not fully repaired. The raw splitter result-list endpoint is still known to return `500` for job `effda69a-d3a3-476d-8b14-8095d3f4e35f`; the analyst path is protected by stored-ID fallback, but the raw endpoint remains an ops-noise source.

## Reviewed Deploy File List

Deploy only the files needed for the selected item set. Because the tree is highly dirty, do not deploy from the broad working tree without a reviewed package.

### API Files

Required if deploying identity routes, split-option fallback route changes, choose/skip by record/job, lossless result routes, signed route coverage, or raw result-list suppression:

- `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ScanAssetsController.cs`
- `src/NickScanCentralImagingPortal.API/Middleware/SignedImageUrlMiddleware.cs`
- `src/NickScanCentralImagingPortal.Core/DTOs/ScanAssets/*`
- `src/NickScanCentralImagingPortal.Core/Interfaces/IScanAssetResolver.cs`
- `src/NickScanCentralImagingPortal.Services.ImageProcessing/ScanAssetResolver.cs`

API deploy routes to verify:

- `GET /api/image-analysis/records/{analysisRecordId}/split-options`
- `POST /api/image-analysis/records/{analysisRecordId}/choose-split`
- `POST /api/image-analysis/records/{analysisRecordId}/skip-split`
- `GET /api/image-splitter/records/{analysisRecordId}/split-options`
- `GET /api/image-splitter/jobs/{jobId}/split-options?containerNumber={container}`
- `POST /api/image-splitter/jobs/{jobId}/choose-split`
- `POST /api/image-splitter/jobs/{jobId}/skip-split`
- `GET /api/image-splitter/jobs/{jobId}/original`
- `GET /api/image-splitter/jobs/{jobId}/results/{resultId}/lossless/{side}`
- `GET /api/image-splitter/jobs/{jobId}/results/{resultId}/image/{side}`
- `GET /api/scan-assets/resolve?containerNumber={container}&groupIdentifier={group}&analysisRecordId={id}&splitJobId={job}`
- `GET /api/scan-assets/{sourceScanId}/image?containerNumber={container}&splitJobId={job}&splitResultId={result}&side={left|right}`

### WebApp Files

Required if deploying split-choice UI gating, identity preference, same-origin proxy use, eager image rendering, image load/error telemetry, or UI regression coverage:

- `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewDialog.razor`
- `src/NickScanWebApp.New/Components/Operations/SplitChoiceDialog.razor`
- `src/NickScanWebApp.New/Components/Operations/ImageDecisionView.razor`
- `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor`
- `src/NickScanWebApp.New/Controllers/ImageProxyController.cs`
- `src/NickScanWebApp.Shared/Models/ContainerDetailsModels.cs`
- `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`
- `src/NickScanWebApp.Shared/Services/IContainerDetailsService.cs`

Web routes to verify:

- `GET /api/imageproxy?url={base64RelativeOrApiUrl}`
- Analyst UI route containing the Images/Decisions workflow for assignment `40426305424_W1`.
- Fullscreen document/source image viewer route opened from the same assignment.

### Test/Validation Files

Do not deploy test files, but require them for package approval:

- `src/NickScanCentralImagingPortal.Tests/Services/SourceScanSplitFlowRegressionTests.cs`
- `src/NickScanCentralImagingPortal.Tests/Services/ScanAssetResolverTests.cs`
- `src/NickScanCentralImagingPortal.Tests/Services/SourceScanResolverPhase2ARegressionTests.cs`
- Add or identify a UI-focused regression for item 4 before calling item 4 deploy-ready.

### Excluded From This Split-Selection Deploy

- `services/image-splitter/*` unless item 5 is repaired in the Python service and explicitly staged with the API.
- Eagle A25, CMR, dashboard alert, predictive preload, ICUMS, and unrelated ops-hardening files unless a separate reviewed deploy list includes them.
- `src/NickScanCentralImagingPortal.API/appsettings*.json`
- `publish\API\appsettings*.json`
- `publish\WebApp\appsettings*.json`

## Controlled Packaging Pattern

Use a timestamped stage and backup. Example pattern only; do not run as part of readiness:

```powershell
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$stageRoot = "C:\Shared\NSCIM_PRODUCTION\deploy-staging\split-selection-$ts"
$backupRoot = "C:\Shared\NSCIM_PRODUCTION\deploy-backups\split-selection-$ts"

dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore --verbosity minimal
dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore --verbosity minimal

dotnet publish src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj -c Release -o "$stageRoot\API"
dotnet publish src\NickScanWebApp.New\NickScanWebApp.New.csproj -c Release -o "$stageRoot\WebApp"

New-Item -ItemType Directory -Force "$backupRoot\API", "$backupRoot\WebApp" | Out-Null
Copy-Item C:\Shared\NSCIM_PRODUCTION\publish\API\* "$backupRoot\API" -Recurse -Force
Copy-Item C:\Shared\NSCIM_PRODUCTION\publish\WebApp\* "$backupRoot\WebApp" -Recurse -Force
```

Prefer this hand-staged pattern over `.\Deploy.ps1` for this rollout because production config must be preserved. `Deploy.ps1 -DryRun` is still useful to confirm service selection and canonical paths:

```powershell
.\Deploy.ps1 -DryRun
.\Deploy.ps1 -ApiOnly -DryRun
.\Deploy.ps1 -WebAppOnly -DryRun
```

Controlled copy pattern:

```powershell
$apiConfigBefore = Get-FileHash C:\Shared\NSCIM_PRODUCTION\publish\API\appsettings*.json -Algorithm SHA256
$webConfigBefore = Get-FileHash C:\Shared\NSCIM_PRODUCTION\publish\WebApp\appsettings*.json -Algorithm SHA256

Stop-Service NSCIM_WebApp -Force
Stop-Service NSCIM_API -Force

robocopy "$stageRoot\API" C:\Shared\NSCIM_PRODUCTION\publish\API /MIR /XF appsettings*.json
robocopy "$stageRoot\WebApp" C:\Shared\NSCIM_PRODUCTION\publish\WebApp /MIR /XF appsettings*.json

Start-Service NSCIM_API
Start-Service NSCIM_WebApp

$apiConfigAfter = Get-FileHash C:\Shared\NSCIM_PRODUCTION\publish\API\appsettings*.json -Algorithm SHA256
$webConfigAfter = Get-FileHash C:\Shared\NSCIM_PRODUCTION\publish\WebApp\appsettings*.json -Algorithm SHA256
Compare-Object $apiConfigBefore $apiConfigAfter -Property Path,Hash
Compare-Object $webConfigBefore $webConfigAfter -Property Path,Hash
```

Important: `robocopy` returns nonzero success codes. Treat `0`, `1`, `2`, `3`, `5`, `6`, and `7` as non-fatal copy statuses; investigate `8+`.

## Rollback Pattern

Rollback must restore the exact pre-deploy publish folders and preserve the pre-deploy config hashes.

```powershell
$backupRoot = "C:\Shared\NSCIM_PRODUCTION\deploy-backups\split-selection-<timestamp>"

Stop-Service NSCIM_WebApp -Force
Stop-Service NSCIM_API -Force

robocopy "$backupRoot\API" C:\Shared\NSCIM_PRODUCTION\publish\API /MIR
robocopy "$backupRoot\WebApp" C:\Shared\NSCIM_PRODUCTION\publish\WebApp /MIR

Start-Service NSCIM_API
Start-Service NSCIM_WebApp

Get-CimInstance Win32_Service -Filter "Name='NSCIM_API'"
Get-CimInstance Win32_Service -Filter "Name='NSCIM_WebApp'"
Invoke-WebRequest http://localhost:5205/health -UseBasicParsing
Invoke-WebRequest https://localhost:5206/health -UseBasicParsing -SkipCertificateCheck
Invoke-WebRequest http://localhost:5299/ -UseBasicParsing
Invoke-WebRequest https://localhost:5300/ -UseBasicParsing -SkipCertificateCheck
```

Rollback triggers:

- API or Web service fails to return to `Running`.
- Any health endpoint stays down after the normal restart window.
- Production config hash changes unexpectedly.
- `40426305424_W1` loses assignment visibility or cannot open the image workflow.
- Option A/B routes return `404`, zero bytes, or wrong content type.
- Choose/skip path writes the wrong identity, especially any comma-separated group as workflow identity.
- Fresh Error/Fatal application logs or `errorinvestigations` rows appear for split-selection paths.

## Pre-Deploy Gates

Do all of these before deployment:

- Capture `git status --short` and confirm the reviewed file list has not widened.
- Rebuild from current reviewed source into a new timestamped staging folder. Do not reuse older staging folders.
- Confirm build/test baseline:

```powershell
dotnet build src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj --no-restore --verbosity minimal
dotnet build src\NickScanWebApp.New\NickScanWebApp.New.csproj --no-restore --verbosity minimal
dotnet test src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj --filter "FullyQualifiedName~ScanAssetResolver|FullyQualifiedName~SourceScan|FullyQualifiedName~Split" --no-restore
```

- Add or identify a UI regression for item 4 before including item 4 in the deploy-ready set.
- Record pre-deploy service state, process path, artifact hashes, config hashes, and health results.
- Record known sample identifiers:
  - group: `40426305424_W1`
  - analysis record: `3418`
  - split job: `effda69a-d3a3-476d-8b14-8095d3f4e35f`
  - option A result: `87936a62-3798-40a0-b622-af67cc6bd62e`
  - option B result: `919c2835-c5fc-4b83-9d62-0a14e4ea9902`
  - known active assignment from prior verification: assignment `15937`, user `pimage`
  - child containers: `MSMU1683356`, `MRKU8254509`

## Live Verification Checklist

### Service And Health

```powershell
Get-CimInstance Win32_Service -Filter "Name='NSCIM_API'" | Select-Object Name,State,PathName
Get-CimInstance Win32_Service -Filter "Name='NSCIM_WebApp'" | Select-Object Name,State,PathName
Get-Item C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe
Get-Item C:\Shared\NSCIM_PRODUCTION\publish\WebApp\NickScanWebApp.New.exe
Invoke-WebRequest http://localhost:5205/health -UseBasicParsing
Invoke-WebRequest https://localhost:5206/health -UseBasicParsing -SkipCertificateCheck
Invoke-WebRequest http://localhost:5299/ -UseBasicParsing
Invoke-WebRequest https://localhost:5300/ -UseBasicParsing -SkipCertificateCheck
```

Expected:

- `NSCIM_API` running from `publish\API`.
- `NSCIM_WebApp` running from `publish\WebApp`.
- API health returns `200` on `5205` and `5206`.
- Web route returns `200` on `5299` and `5300`.

### 40426305424_W1 Option A/B

Routes to verify, authenticated or signed as appropriate:

```powershell
$job = 'effda69a-d3a3-476d-8b14-8095d3f4e35f'
$optionA = '87936a62-3798-40a0-b622-af67cc6bd62e'
$optionB = '919c2835-c5fc-4b83-9d62-0a14e4ea9902'

Invoke-WebRequest "http://localhost:5205/api/image-splitter/jobs/$job/split-options?containerNumber=40426305424_W1" -UseBasicParsing
Invoke-WebRequest "http://localhost:5205/api/image-splitter/jobs/$job/original" -UseBasicParsing
Invoke-WebRequest "http://localhost:5205/api/image-splitter/jobs/$job/results/$optionA/lossless/left" -UseBasicParsing
Invoke-WebRequest "http://localhost:5205/api/image-splitter/jobs/$job/results/$optionB/lossless/left" -UseBasicParsing
```

Expected:

- Split-options response is `Ready` or already terminal after choose/skip, includes two options, and includes `analysisRecordId` plus `splitJobId`.
- Original image returns nonzero JPEG bytes.
- Option A and option B lossless routes return nonzero PNG bytes.
- If the group resolves to the right-side child in the current UI context, repeat lossless probes with `/right`; the UI must use the side returned by resolver/options, not hard-coded side.
- Browser UI shows option images through `/api/imageproxy`, with eager image loading, not lazy direct cross-service image URLs.

### Skip Path

Use authenticated UI for final confirmation. API route order in the WebApp should be:

1. `POST /api/image-analysis/records/{analysisRecordId}/skip-split`
2. fallback `POST /api/image-splitter/jobs/{jobId}/skip-split`
3. fallback `POST /api/image-splitter/container/{containerNumber}/skip-split`

Expected:

- Skip records `SplitStatus='Skipped'` or equivalent terminal state on the correct analysis record.
- `ImageDecisionView` becomes visible after skip.
- The image shown after skip is the original combined/source image.
- Assignment/progression does not use `MSMU1683356, MRKU8254509` or another comma-separated value as workflow identity.

### Choose Option A/B Path

Use authenticated UI for final confirmation. API route order in the WebApp should be:

1. `POST /api/image-analysis/records/{analysisRecordId}/choose-split`
2. fallback `POST /api/image-splitter/jobs/{jobId}/choose-split`
3. fallback `POST /api/image-splitter/container/{containerNumber}/choose-split`

Expected for Option A:

- Choosing `87936a62-3798-40a0-b622-af67cc6bd62e` saves the result ID against the correct analysis record.
- Split state callback changes to `Resolved`.
- `ImageDecisionView` remounts/reloads after the choice and shows the chosen crop, not the unsplit image.
- Assignment stays active or progresses according to analyst decision rules.

Expected for Option B:

- Repeat the same check with `919c2835-c5fc-4b83-9d62-0a14e4ea9902`.
- Verify no stale image cache hides the new choice after switching or re-opening the assignment.

### Assignment And Progression

```powershell
pwsh scripts/postgres/Check-AssignmentQueueTarget.ps1 -GroupIdentifier '40426305424_W1' -Username 'pimage'
```

Expected:

- Known analyst user receives or retains assignment for `40426305424_W1`.
- Assignment group is `40426305424_W1`, not a comma-separated physical container list.
- Assignment opens scanner tab, image tab, split-choice state, and fullscreen document icon.
- After choose/skip, the normal decision flow can save a decision and progress the queue according to existing workflow rules.
- Cache invalidation after choose/skip prevents stale "no image", original-image-only, or no-assignment views.

### Ops Error Watch

Run immediately before deploy, then after restart at 3 minutes and 10 minutes:

```powershell
& .\tools\migration-runner\bin\Debug\net10.0\migration-runner.exe --ops-error-probe
& .\tools\migration-runner\bin\Debug\net10.0\migration-runner.exe --eagle-a25-probe
```

Also inspect application logs/error investigations for fresh rows containing:

- `/api/image-splitter/jobs/effda69a-d3a3-476d-8b14-8095d3f4e35f/results`
- `/api/image-splitter/jobs/effda69a-d3a3-476d-8b14-8095d3f4e35f/original`
- `/api/image-splitter/jobs/effda69a-d3a3-476d-8b14-8095d3f4e35f/results/{option}/lossless`
- `/api/imageproxy`
- `/api/scan-assets`
- `OperationCanceledException`
- `ImageSplitter search cancellation`
- `Signed URL rejected`

Expected:

- No fresh `Error` or `Fatal` application log rows tied to split-choice, image proxy, scan-assets, assignment, or controlled restart cancellation.
- No new `errorinvestigations` rows for expected request aborts or restart cancellation.
- The raw splitter `/results` `500` remains either repaired or explicitly demoted/suppressed for this known job; it must not be used as evidence that the analyst option A/B path is broken if stored result IDs still serve images.

## Deployment Decision

- Items 1 and 2: deploy API + Web together if the reviewed current source is accepted, because the UI gating depends on API identity routes and split-state persistence routes.
- Item 3: Web deploy if telemetry is browser-only; API + Web if telemetry posts to a new backend endpoint.
- Item 4: no production deploy until a focused UI regression exists and is passing. Tests are validation-only and should not be copied to publish folders.
- Item 5: API deploy if suppression is inside `ImageSplitterController`/ops monitoring; API + Python child package if the raw image-splitter service itself is repaired under `services/image-splitter/*`.

Do not deploy directly from the dirty worktree. Build a timestamped reviewed package, preserve config, verify hashes, then do controlled service restart and live probes.
