# Whole-Repo API And Frontend Endpoint Consolidation Audit

Date: 2026-05-13  
Repository: `C:\Shared\NSCIM_PRODUCTION`  
Mode: static source audit only. No production code or route behavior was changed.

## Executive Summary

The concern is valid. The repo has several healthy service boundaries, but the NSCIM app in particular has accumulated a very broad API surface and a page/component layer that often calls narrow endpoints directly. The result is not only "many endpoints"; it is many overlapping endpoint families, mixed route naming styles, and repeated screen-level API calls that make it hard to know which route is canonical.

Static scan highlights:

| Surface | Finding |
| --- | --- |
| NSCIM API | 79 controllers, 596 attributed `[Http*]` actions, 72 API route segments in the original audit pass. The latest 2026-05-15 re-analysis sees about 80 controller files and 615 attributed `[Http*]` actions. |
| NSCIM WebApp local API | 3 local controllers, 5 actions: `Auth`, `ImageProxy`, `server/version`. |
| NickHR API | 41 controllers, 277 actions, 39 API route segments. |
| Whole repo source consumers | 707 literal `/api` callsites across 444 literal-ish endpoint shapes and 99 first path segments in the original pass. The latest source-only re-scan, excluding docs/deploy/bin/obj/logs, sees about 936 `/api` references across 517 shapes and 78 first path segments. |
| NSCIM WebApp consumers | 485 literal `/api` callsites across 106 files, 320 literal-ish endpoint shapes, and 65 first path segments in the original pass. The latest NSCIM WebApp re-scan sees about 556 `/api` references across 104 files, 354 shapes, and 60 first path segments. |
| NSCIM frontend page routes | 47 page files expose 127 `@page` routes; 30 pages have multiple route aliases. |
| NickHR frontend page routes | 118 page files expose 120 routes; only 1 multi-route page. |
| NickFinance frontend page routes | 38 page files expose 38 routes. |
| Platform portal | 2 page files expose 2 routes. |

Main conclusion:

- NSCIM needs endpoint consolidation first. It is the place where direct page/component calls, domain overlap, casing drift, and compatibility routes are most concentrated.
- NickHR is large but cleaner as a separate module: it has a broad HR domain API, one generic WebApp `ApiClient`, and a couple of explicit service-to-service clients.
- NickFinance and the platform portal should not be folded into NSCIM route cleanup. They use mostly module-local DB/services and a few dedicated HTTP endpoints.
- NickComms and image-splitter should be documented as backing/shared services. They should be consumed through typed clients or NSCIM proxy/BFF routes, not merged into the operator-facing NSCIM API namespace.

## Inventory Method

Included:

- C# controllers with `[Route]` plus `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]`, `[HttpPatch]`.
- Minimal API route declarations using `MapGet`, `MapPost`, `MapGroup`, `MapHealthChecks`, and `MapHub`.
- FastAPI route decorators in `services/image-splitter`.
- Blazor/Razor `@page` route declarations.
- Literal `/api` consumers in `.cs`, `.razor`, `.cshtml`, `.js`, `.ts`, `.py`, `.ps1`, `.psm1`, `.sh`, and `.bat`.

Excluded from source counts:

- `bin`, `obj`, `deploy-backups`, `node_modules`, logs, `.git`, `.vs`.
- Markdown docs/changelog text, except where used as human context.
- Runtime endpoint usage rows. This report identifies where to use runtime telemetry next; it did not query production telemetry.

Important limitation:

- Static string scanning cannot fully expand all dynamic URLs or route-group combinations. Treat counts as an audit map, not an OpenAPI contract. The mismatches listed below are still useful because each has a concrete source location.

## 2026-05-14 Final Drift Checkpoint

A final re-scan after later source changes found additional NSCIM endpoint families that were not present in the original 2026-05-13 report text. They do not invalidate the consolidation findings; they make the need for route ownership clearer.

| Surface | Current routes found | Current consumers found | Classification | Consolidation note |
| --- | --- | --- | --- | --- |
| Eagle A25 scanner API | `GET /api/EagleA25/scans`, `GET /api/EagleA25/scans/{id:guid}`, `GET /api/EagleA25/sync-status`, `POST /api/EagleA25/sync` | `EagleA25Panel`, `EagleA25RecordDialog`, `ScannerOverview` | screen-specific/BFF plus external scanner integration | Capture as a separate scanner module API. Do not merge it into container/cargo APIs. If route naming standard moves to lower-case/kebab-case, add compatibility aliases before rewiring `/api/EagleA25/*` callers. |
| Scan asset resolution | `GET /api/scan-assets/resolve`, `GET /api/scan-assets/{sourceScanId}/image` | `ContainerDetailsService`, `ImagesTab`, `AuditReviewDialog`, `ImageAnalysisViewer`, `ImageDecisionView`, `SplitChoiceDialog` | canonical candidate | This is the clearest new canonical direction for source-scan image identity. Keep it, but close the current contract gap: `ContainerDetailsService` already attempts planned `/api/scan-assets/{sourceScanId}/scanner-data` and `/api/scan-assets/{sourceScanId}/images` routes that are not implemented in `ScanAssetsController` yet. |
| Predictive preload cache | `GET /api/cache/predictive/status`, `POST /api/cache/predictive/run-once`, `POST /api/cache/predictive/invalidate/assignment/{groupId:guid}`, `POST /api/cache/predictive/invalidate/container/{containerNumber}`, `POST /api/cache/predictive/preload/container/{containerNumber}`, `GET /api/cache/predictive/assignment/{groupId:guid}`, `GET /api/cache/predictive/container/{containerNumber}` | Optional `ContainerDetailsService` predictive context call and admin/operator validation docs | admin/cache service API | Treat as an admin/cache support API, not a duplicate domain endpoint family. It should be owned by cache/preload services and protected from casual page-level sprawl. |
| Image splitter service/proxy drift | NSCIM `ImageSplitterController` currently has 20 attributed actions. FastAPI currently exposes `/api/health` plus 20 `/api/split/*` routes, including `wall-verdict`, `set-correct`, `ground-truth`, `describe`, and `verify-candidates`; inspector still exposes 16 `/inspector/*` routes. | Split review, X-ray inspector, and splitter tooling | BFF/proxy plus external/service-only | The original family classification still holds. Operator UI should prefer NSCIM `/api/image-splitter` and `/api/xray-inspector`; direct FastAPI routes remain service-only/static-tool routes. |

No additional new app surface was found beyond these families in the final pass. The known stale/mismatch findings still remain: `/api/record-completeness/summary`, `/api/ManualBOERequest`, and relative `api/imageprocessing...` calls.

## 2026-05-15 Re-Analysis Checkpoint

Another source re-analysis on 2026-05-15 found no brand-new top-level app surface beyond the families already added on 2026-05-14. It did find important route drift inside those families.

| Surface | 2026-05-15 finding | Classification | Consolidation note |
| --- | --- | --- | --- |
| Eagle A25 scanner API | `EagleA25Controller` now has 5 attributed actions: `GET /api/EagleA25/scans`, `GET /api/EagleA25/scans/{id:guid}`, `GET /api/EagleA25/assets/{id:guid}/content`, `GET /api/EagleA25/sync-status`, `POST /api/EagleA25/sync`. | screen-specific/BFF plus binary asset route | Keep Eagle A25 as a separate scanner module. The asset-content endpoint is a binary/file route and should be handled with the same signed/proxy caution as image routes, not treated as generic CRUD. |
| Scan asset resolution | `ScanAssetsController` now exposes `GET /api/scan-assets/{sourceScanId}/images` in addition to `resolve` and single `image`. | canonical candidate | The previous `/images` gap is closed. The remaining active mismatch is `ContainerDetailsService` still attempting planned `/api/scan-assets/{sourceScanId}/scanner-data`, which is not implemented in `ScanAssetsController`. Either implement it or remove it from the fallback order. |
| Split selection BFF | `ImageSplitterController` now has about 24 attributed actions, including `POST /api/image-analysis/records/{analysisRecordId:int}/choose-split`, `POST /api/image-analysis/records/{analysisRecordId:int}/skip-split`, `POST /api/image-splitter/jobs/{jobId:guid}/choose-split`, and `POST /api/image-splitter/jobs/{jobId:guid}/skip-split`. | screen-specific/BFF plus compatibility aliases | This is acceptable if intentional, but it should be documented as split-selection workflow ownership. Prefer one canonical command path for the UI workflow and keep the others as compatibility aliases with usage telemetry. |
| External BOE scan/ICUMS API references | Source contains `/api/BOEScanData/FetchBatchBOEScanDocument`, `/api/BOEScanData/FetchBOEScanDocument`, `/api/BOEScanData/SubmitScanResult`, and `/api/rm/scan/*` references. | external/service-only | These are configured external ICUMS/BOE scan API paths, not local NSCIM controllers. Keep them out of NSCIM route cleanup, but document them as outbound service dependencies. |
| External model-provider references | Source contains Ollama-style `/api/tags` and `/api/generate` references. | external/service-only | These should not be flagged as missing NSCIM routes. Keep them under AI/model-provider client ownership. |

The known stale local WebApp findings still remain in this re-analysis: `/api/record-completeness/summary`, `/api/ManualBOERequest`, and relative `api/imageprocessing...` calls.

## Whole-Repo Surface Map

### NSCIM

Primary API:

- `src/NickScanCentralImagingPortal.API`
- Original audit pass: 79 controllers, 596 actions. The 2026-05-15 re-analysis sees about 80 controller files and 615 attributed actions.
- Hubs mapped in `Program.cs`: `/hubs/dashboard`, `/hubs/comprehensive-dashboard`, `/hubs/imageAnalysisDashboard`, `/hubs/userReadiness`, `/hubs/containerScanQueue`.
- Health endpoints: `/health`, `/health/live`, `/health/ready`.
- Module contract endpoints under `/api/_module`.

Primary frontend:

- `src/NickScanWebApp.New`
- `src/NickScanWebApp.Shared`
- Uses named `HttpClient` `NickScanAPI`, plus `NickScanWebApp.Shared.Services.ApiService`.
- Still has many direct `ApiService.GetAsync("/api/...")` style calls inside pages/components.
- Has 127 page routes over 47 page files, with heavy aliasing in monitoring, completeness, ICUMS, and image analysis screens.

Local WebApp controllers:

- `api/Auth`
- `api/ImageProxy`
- `api/server`

These are intentionally local to the Blazor Server host and should be documented separately from the external NSCIM API.

### NickHR

API:

- `NickHR/src/NickHR.API`
- 41 controllers, 277 actions.
- Main route families include recruitment, payroll, performance, leave, employees, claims, training, discipline, loans, survey, assets, travel, and platform module manifest.

Frontend:

- `NickHR/src/NickHR.WebApp`
- 118 page files, 120 routes.
- Uses `NickHR.WebApp.Services.ApiClient`, which creates a named `NickHR.API` client and attaches the current bearer token.
- Only a few literal `api/...` strings appear in pages. Many page interactions are routed through app services or dynamically passed URLs.

Service-to-service:

- `NickHR.Services.Auth.CentralAuthClient` calls NSCIM central auth and user provisioning endpoints:
  - `/api/auth/validate-credentials`
  - `/api/roles/service/list`
  - `/api/users/service/provision`
  - `/api/users/service/{username}/deactivate`
- `NickHR.Services.Communication.NickCommsClient` calls NickComms:
  - `/api/email/send`
  - `/api/email/bulk`
  - `/api/sms/send`
  - `/api/messages/history`

Recommendation:

- Treat NickHR as its own API module. Do not fold its HR route names into NSCIM conventions.
- Validate `ApiBaseUrl` configuration carefully. The WebApp default currently points to `https://localhost:5206`, while deployment notes refer to NickHR API around `5215`; this may be intentional config override, but it is a risk worth documenting in deployment validation.

### NickFinance

Frontend/API shape:

- `finance/NickFinance.WebApp`
- 38 page files, 38 routes.
- Has local minimal endpoints for PDF streaming:
  - `/pdf/invoice/{id}`
  - `/pdf/voucher/{id}`
  - `/pdf/receipt/{id}`
  - `/pdf/statement/{customerId}`
  - `/pdf/wht-certificate/{vendorId}/{year}`
  - `/pdf/wht-certificate-book/{year}`
- Has local API endpoints:
  - `/api/email/statement/{customerId}`
  - `/api/whatsapp/webhook` GET/POST.

External clients:

- Hubtel/eVAT uses configured external endpoints such as `/api/invoices`.
- Petty cash/WhatsApp/NickComms integrations should be treated as external adapter traffic, not NSCIM frontend API duplication.

Recommendation:

- Keep NickFinance endpoint cleanup separate.
- Standardize its local endpoints around document streaming and webhook boundaries only.

### Platform Portal

Frontend:

- `platform/NickERP.Portal`
- 2 page routes.

Data/API use:

- `StatsService` reads NickHR stats directly from Postgres and NSCIM stats from configured HTTP URLs.
- This is a portal dashboard aggregator, not a normal module API.

Recommendation:

- Prefer module manifests and typed stats feeds over hand-configured URLs as the platform matures.
- Do not merge platform portal calls into NSCIM route cleanup.

### NickComms Gateway

API:

- Minimal API service under `services/NickComms.Gateway`.
- Route groups:
  - `/api/email`: send, bulk, status.
  - `/api/sms`: send, bulk, status.
  - `/api/otp`: send, verify, resend.
  - `/api/messages/history`.
  - `/api/_module/manifest`.
  - `/api/health`.

Recommendation:

- This is already a clean shared-service boundary.
- NSCIM, NickHR, and finance modules should call it through typed clients only.
- Do not expose NickComms routes directly inside operator UI pages except via module-specific settings/history panels.

### Image Splitter

FastAPI service:

- `services/image-splitter/main.py`
- Around 20 `/api/split/*` routes for split jobs, uploads, review, ground truth, diagnostics, originals, and candidate verification.
- `services/image-splitter/inspector/routes.py` adds about 16 `/inspector/*` routes for X-ray inspector analysis and exports.

NSCIM proxy:

- `ImageSplitterController` exposes `/api/image-splitter/*`.
- `XrayInspectorController` exposes `/api/xray-inspector/*` and proxies to inspector-style backing routes.

Recommendation:

- Keep image-splitter as service-only.
- Canonical operator-facing routes should be the NSCIM proxy/BFF routes.
- Do not let Blazor pages call the FastAPI service directly except static splitter tools that intentionally run inside the splitter app.

## NSCIM High-Churn API Consumer Matrix

Top source consumers by literal API call count:

| File | Calls | Segments |
| --- | ---: | ---: |
| `src/NickScanWebApp.New/Pages/ImageAnalysis/Configuration.razor` | 23 | 2 |
| `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor` | 22 | 7 |
| `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs` | 20 | 4 |
| `services/image-splitter/main.py` | 20 | 2 |
| `src/NickScanWebApp.New/Components/Customs/IcumsIndividualDownloadPanel.razor` | 18 | 2 |
| `src/NickScanWebApp.New/Pages/ImageAnalysis/XrayInspector.razor` | 16 | 2 |
| `src/NickScanWebApp.Shared/Services/SettingsService.cs` | 15 | 1 |
| `src/NickScanWebApp.New/Components/Customs/IcumsBatchDownloadPanel.razor` | 14 | 1 |
| `src/NickScanWebApp.New/Pages/Index.razor` | 13 | 8 |
| `src/NickScanWebApp.New/Pages/Completeness/ContainerCompletenessLegacy.razor` | 13 | 5 |
| `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewDialog.razor` | 13 | 5 |
| `src/NickScanWebApp.New/Pages/Customs/IcumsDownloadQueue.razor` | 12 | 1 |
| `src/NickScanWebApp.New/Pages/Images/ImageViewer.razor` | 12 | 4 |

Top literal API segments across source consumers:

| Segment | Calls | Unique shapes | Read |
| --- | ---: | ---: | --- |
| `image-analysis` | 52 | 33 | Workflow, user readiness, dashboard subpaths, image tools. |
| `split` | 43 | 20 | Mostly image-splitter service/static tools. |
| `icumsdownloadqueue` | 37 | 9 | Queue CRUD/actions are heavily used by multiple ICUMS panels. |
| `imageprocessing` | 36 | 15 | Image serving and image-tool endpoints. |
| `monitoring` | 32 | 17 | Health, API metrics, endpoint usage, deprecated route telemetry. |
| `image-analysis-management` | 28 | 19 | Assignment/config/agent operations. |
| `icums` | 23 | 18 | Batch ingestion and transfer endpoints. |
| `imageanalysisdecision` | 20 | 5 | Decision load/save repeated across viewers/dialogs. |
| `image-splitter` | 17 | 14 | NSCIM proxy for split review. |
| `containerdetails` | 16 | 13 | Container BFF/metadata/image endpoints. |
| `xray-inspector` | 16 | 13 | Inspector proxy/BFF. |
| `settings` | 15 | 13 | Centralized already via shared `SettingsService`. |
| `_module` | 14 | 9 | Platform module manifest/queue/diagnostic routes. |
| `systemadmin` | 14 | 10 | Service control page. |

Interpretation:

- The problem is screen-level fan-out, not just controller count.
- The same UI workflows often touch 5 to 8 route families.
- The most expensive cleanup is not renaming routes; it is introducing workflow-level typed clients/BFF endpoints so pages stop knowing every controller.

## Concrete Static Findings

### 1. Stale or Missing Endpoint Shapes

These are the source-level mismatches from static cross-checking:

| Source | Current call | Finding | Migration-safe action |
| --- | --- | --- | --- |
| `src/NickScanWebApp.New/Pages/Index.razor` | `/api/record-completeness/summary` | No matching controller route. The same file later uses `/api/recordcompleteness/summary`, which does exist via `RecordCompletenessController`. | Change KPI load to the existing canonical route and add a small regression/smoke check for dashboard KPI loading. |
| `src/NickScanWebApp.New/Pages/Containers/ContainerDetails.razor` | `/api/ManualBOERequest` | No `ManualBOERequestController` was found. Manual BOE request persistence exists as an entity/service, and `ContainerCompletenessController` exposes `POST /api/ContainerCompleteness/request-boe/{containerNumber}`. | Rewire the UI to the existing container-completeness command or add a deliberately named `manual-boe-requests` controller as the canonical command surface. |
| `src/NickScanWebApp.New/Components/Monitoring/ImageProcessingPanel.razor` | `api/imageprocessing{query}` and `api/imageprocessing/statistics` | Likely works in ASP.NET because routing is case-insensitive and relative URLs resolve, but it fragments style and static analysis. | Normalize to `/api/ImageProcessing` or the chosen lower-case canonical route and use a query builder helper. |
| `src/NickScanWebApp.New/Components/Monitoring/DebugPanel.razor` | `/api/...` | Placeholder text, not a real route. | Ignore in route inventory. |

### 2. Route Casing Is Fragmenting Observability

Examples:

- Backend route attributes use `api/Monitoring`, while frontend calls include both `/api/Monitoring/...` and `/api/monitoring/...`.
- Backend route attributes use `api/ImageProcessing`, while frontend calls include lower-case `api/imageprocessing`.
- Backend route attributes use `api/RecordCompleteness`, while UI calls include `/api/recordcompleteness/...`.

ASP.NET Core route matching is generally case-insensitive, so this does not always break behavior. It still matters because logs, endpoint usage rows, dashboards, curl runbooks, and static scans become fragmented by path spelling.

Migration-safe action:

- Pick a canonical spelling for every public route and update frontend callsites first.
- Add compatibility aliases only where external callers exist.
- Normalize endpoint usage reporting by lower-casing or canonicalizing paths before deprecation decisions.

### 3. Deprecated Route Detection Is Too Narrow

`PerformanceLoggingMiddleware` marks these patterns deprecated:

- `/api/ImageProcessing/image/`
- `/api/ImageProcessing/container/`
- `/api/image/`

That catches some image legacy traffic, but the repo contains broader compatibility surfaces:

- `ImageController` is explicitly deprecated in comments/attributes.
- `ImageProcessingController` has several old image aliases that should route to `/api/ImageProcessing/container/{containerNumber}/complete/image`.
- `ImageAnalysisController.SaveAuditDecision` is documented as deprecated in favor of `/api/AuditReview/submit`.
- `ImageAnalysisManagementController` keeps string-id assign/release/reassign compatibility endpoints.
- `IcumDataTransferController.TriggerTransfer` is a legacy transfer trigger that logs replacement by orchestrator cadence.

Migration-safe action:

- Expand deprecated route classification into a table/config, not hard-coded string snippets.
- Track route family, canonical replacement, owner, first deprecated version/date, and removal condition.
- Surface this in `/api/Monitoring/deprecated-endpoints/summary`.

### 4. NSCIM Frontend Page Route Aliasing Is Also High

The API is not the only overloaded surface. The WebApp itself maps many URLs to the same page:

| Page | Route count | Examples |
| --- | ---: | --- |
| `ContainerCompletenessRecords.razor` | 13 | `/operations/completeness-records`, `/completeness`, `/validation/ccq`, `/operations/export-pending`, `/operations/completed-records`, etc. |
| `Monitoring/Diagnostics.razor` | 13 | diagnostics, database, gateway, ingestion, image-processing, debug routes. |
| `Monitoring/Health.razor` | 9 | monitoring, endpoint usage, performance, service-control aliases. |
| `Monitoring/ErrorsAndAudit.razor` | 7 | errors, error investigations, audit aliases. |
| `Customs/IcumsDownloadQueue.razor` | 7 | batch, individual, queue, and customs aliases. |

Migration-safe action:

- Keep aliases for navigation compatibility, but declare one canonical page URL per workflow.
- Update nav/menu links to canonical URLs first.
- Add redirects or route-level breadcrumb chips later if operators depend on older paths.

## Consolidation Recommendations By Domain

### Container And Cargo

Current overlapping families:

- `CargoGroupController`: group lookup, group details, subresources, `full`, AI summary.
- `ContainerDetailsController`: basic/full/scanner/icums/images/search and image serving variants.
- `ConsolidatedCargoController`: consolidated/non-consolidated lists plus declaration/container helpers.
- `ContainerProcessingController`: groups/summary/group detail.
- `GatewayController`: aggregate container data, image, search, reports, dashboard stats, cache admin.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical domain API | `CargoGroupController` | Make this the canonical cargo group aggregate API. Prefer `GET /api/cargogroup/{groupIdentifier}/full` with include flags for view composition. |
| Screen-specific/BFF | `ContainerDetailsController` | Keep as a container detail BFF while gradually moving repeated subresource calls to `CargoGroup` or one typed `ContainerDetailsService`. |
| Compatibility alias | `ContainerProcessingController` | Keep for legacy processing screens; internally delegate to cargo group service where possible. |
| Duplicate overlap | `ConsolidatedCargoController` | Fold read helpers into cargo group queries or mark them as compatibility helpers. |
| External/BFF | `GatewayController` | Keep for global search, external aggregate reads, and cache/admin operations. Do not use it as an internal page-level dumping ground. |

Frontend consolidation:

- Move container/cargo page calls behind 2 typed clients:
  - `CargoGroupClient`
  - `ContainerDetailClient`
- Pages should request workflow-level data, not individually call scanner, ICUMS, images, validation, and decision endpoints whenever a dialog opens.

### Completeness And Validation

Current families:

- `RecordCompletenessController`
- `ContainerCompletenessController`
- `ContainerValidationController`
- `CMRValidationController`
- `AdminMatchCorrectionController`
- `DiagnosticsController` record/cmr lifecycle endpoints.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical record pipeline | `RecordCompletenessController` | Use for operator lists, summary, record detail, declaration lookup, and audit age. |
| Canonical container command/orchestration | `ContainerCompletenessController` | Use for background checks, manual BOE requests, workflow sync. |
| Canonical human validation | `ContainerValidationController` | Use only for pending validation lists and approve/reject/annotation actions. |
| Canonical CMR repair/health | `CMRValidationController` | Keep as CMR quality and redownload operations. |
| Admin-only correction | `AdminMatchCorrectionController` | Keep separate under `/api/admin/*`; do not mix into user-facing completeness lists. |
| Diagnostics | `DiagnosticsController` | Keep diagnostic-only; do not wire normal pages to diagnostics for routine state. |

Immediate fix:

- Replace dashboard `/api/record-completeness/summary` with `/api/recordcompleteness/summary` or a deliberately added compatibility alias.

### Image, X-Ray, And Split Review

Current families:

- `ImageProcessingController`
- deprecated `ImageController`
- `ContainerDetailsController` image endpoints.
- `ImageAnalysisController` enhanced/detect/quality/annotation image tools.
- `ImageAnalysisDecisionController`
- `XrayInspectorController`
- `ImageSplitterController`
- FastAPI `/api/split/*` and `/inspector/*`.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical image serving | `ImageProcessingController` | Standardize on `/api/ImageProcessing/container/{containerNumber}/complete/image` plus `mode-capabilities`, `roi`, `pixel`, and `raw`. |
| Compatibility alias | `ImageController` | Keep temporarily; mark every endpoint deprecated with replacement and telemetry. |
| Metadata BFF | `ContainerDetailsController` | Keep image metadata only; avoid adding new image-render routes here. |
| Workflow analysis | `ImageAnalysisController` | Keep assignment/workflow and lightweight image-analysis helpers; move decision persistence to decision controller. |
| Decision API | `ImageAnalysisDecisionController` | Keep as canonical decision load/save/rectangle API. |
| Inspector BFF | `XrayInspectorController` | Keep as operator-facing proxy over FastAPI inspector routes. |
| Split review BFF | `ImageSplitterController` | Keep as operator-facing proxy over image-splitter service routes. |
| Service-only | FastAPI `/api/split/*`, `/inspector/*` | Do not call from NSCIM WebApp directly. |

Immediate cleanup:

- Update `ImageProcessingPanel.razor` to use canonical route spelling and leading slash.
- Expand deprecated endpoint telemetry beyond the three current image patterns.

### Image Analysis Workflow

Current families:

- `ImageAnalysisController`: assignments, available groups, claims, leases, intake, legacy decisions, image helpers.
- `ImageAnalysisManagementController`: service state, stage sync, assignment management, agent settings.
- `ImageAnalysisDashboardController`: dashboard metrics and exports.
- `UserReadinessController`: ready/heartbeat/readiness snapshots under `/api/image-analysis/user`.
- `AuditReviewController`: audit review detail and submit.
- `BLReviewController`: BL review.
- `ImageAnalysisDecisionController`: decisions.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical workflow API | `/api/image-analysis` | Assignments, available work, claim, lease renew, intake, group lookup. |
| Canonical readiness API | `/api/image-analysis/user` | Ready, heartbeat, snapshots. Keep separate because SignalR also mirrors readiness. |
| Canonical management API | `/api/image-analysis-management` | Admin/config/assignment repair/agent control only. |
| Screen-specific dashboard API | `/api/image-analysis/dashboard` | Keep dashboard aggregates as BFF-style reads. |
| Canonical decision API | `/api/ImageAnalysisDecision` or renamed alias | Use for all decision persistence/load. |
| Compatibility alias | string-id assign/release/reassign and deprecated bulk decision endpoints | Keep with telemetry until no callers. |

Frontend consolidation:

- Create one `ImageAnalysisWorkflowClient` for assignments/claims/readiness.
- Create one `ImageAnalysisDecisionClient` for decision load/save.
- Keep viewer-specific image tools in an `ImageToolClient`.
- Pages/dialogs should stop repeating raw `ApiService.GetAsync` calls for the same decision and image endpoints.

### ICUMS

Current families:

- `IcumBatchController`: file ingestion, logs, verification, health, warnings.
- `ICUMSDownloadQueueController`: download queue CRUD/actions/archive/requeue.
- `ICUMSSubmissionQueueController`: submission queue CRUD/actions.
- `ICUMSMetricsController`: counters/gauges/histograms.
- `IcumsPayloadController`: payload list/read/image/summary/verify.
- `IcumDataTransferController`: legacy transfer status/history/trigger.
- `IcumController`, `ICUMSManualController`, `ICUMSArchiveController`: older or secondary route families.
- `LooseCargoController`: loose cargo views.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical ingestion ops | `/api/icums/batch` | Keep file ingestion, logs, records, verification, health, warnings. |
| Canonical download queue | `/api/ICUMSDownloadQueue` or future `/api/icums/download-queue` | Keep queue list, enqueue, retry, delete, priority, archive/requeue. Standardize casing later via alias. |
| Canonical submission queue | `/api/ICUMSSubmissionQueue` or future `/api/icums/submission-queue` | Keep submission queue actions. |
| Metrics | `/api/ICUMSMetrics` | Keep service/operator metrics separate from queue operations. |
| Payload admin | `/api/IcumsPayload` | Keep admin payload inspection separate. |
| Compatibility/service-only | `Icum`, `ICUMSManual`, `ICUMSArchive`, `icums/transfer` | Review telemetry before keeping. Do not build new UI on these. |
| Domain view | `LooseCargoController` | Keep as customs/loose-cargo view API. |

Frontend consolidation:

- `IcumsDownloadQueue.razor`, `IcumsIndividualDownloadPanel.razor`, and `IcumsBatchDownloadPanel.razor` should share one typed `IcumsDownloadQueueClient`.
- Batch ingestion views should share one `IcumsBatchClient`.
- Queue archive/requeue actions should not be repeated in multiple components with different route strings.

### Monitoring, Diagnostics, And Admin

Current families:

- `MonitoringController`
- `PerformanceController`
- `PerformanceMetricsController`
- `SystemAdminController`
- `QueueHealthController`
- `LogManagementController`
- `DiagnosticsController`
- `DatabaseAdminController`
- `DebugController`
- `_module/*`
- health checks.

Recommended canonical model:

| Classification | Route family | Recommendation |
| --- | --- | --- |
| Canonical operator monitoring | `MonitoringController` | Health overview, services, DB stats, performance metrics, recent events, API metrics, endpoint usage telemetry. |
| Queue health | `QueueHealthController` | Keep queue-specific diagnostics and publishing health. |
| Admin service control | `SystemAdminController` | Keep service start/stop/restart and system info here. |
| Log admin | `LogManagementController` | Keep logs/statistics/cleanup/services/levels. |
| Deep diagnostics | `DiagnosticsController`, `DatabaseAdminController`, `DebugController` | Keep admin-only; avoid normal UI pages depending on them for routine display. |
| Performance metrics overlap | `PerformanceController` and `PerformanceMetricsController` | Choose one canonical operator metrics surface; keep the other as compatibility if still used. |
| Module contract | `_module/*` | Keep per `PLATFORM.md`; do not mix with operator diagnostics. |

Important existing strength:

- NSCIM already has endpoint usage telemetry:
  - request logging and endpoint usage capture in `PerformanceLoggingMiddleware`.
  - storage/querying through `EndpointUsageService`.
  - API routes for deprecated endpoints, phase3 routes, safe-to-remove, all-endpoints summary, and callers in `MonitoringController`.
  - UI surface in `EndpointUsagePanel.razor`.

Use this before removing anything.

## Recommended Route Standard

For existing routes:

- Do not rename live routes in one sweep.
- First declare a canonical spelling in this audit/backlog.
- Update internal frontend callsites to canonical spelling.
- Add aliases only for external callers or old UI links.
- Use endpoint usage telemetry to retire aliases.

For new routes:

- Prefer lower-case kebab-case resource names:
  - `/api/record-completeness`
  - `/api/container-completeness`
  - `/api/image-analysis`
  - `/api/icums/download-queue`
- Prefer plural nouns for normal resources:
  - `/api/users`
  - `/api/roles`
  - `/api/notifications`
- Use command subroutes only for real commands:
  - `POST /api/container-completeness/checks`
  - `POST /api/container-validation/{containerNumber}/approve`
  - `POST /api/icums/download-queue/{id}/retry`
- Use query parameters for filtering/includes:
  - `?page=1&pageSize=50`
  - `?include=scanner,icums,images`
  - `?scannerType=FS6000`
- Keep operator BFF/dashboard endpoints explicit:
  - `/api/image-analysis/dashboard/overview`
  - `/api/monitoring/health/overview`
- Keep admin/diagnostic endpoints under obvious prefixes:
  - `/api/admin/*`
  - `/api/diagnostics/*`
  - `/api/debug/*`

## Migration Backlog

### Phase 0: Preserve The Map

Risk: low  
Owner: platform/API maintainer

- Add this report to docs.
- Optionally add a non-mutating route inventory script later under `tools/diagnostics` that emits:
  - backend routes,
  - frontend consumers,
  - unmatched consumer routes,
  - route casing variants,
  - duplicate endpoint shapes.
- Do not remove endpoints yet.

Acceptance:

- Audit doc exists.
- Static route inventory can be reproduced without touching production state.

### Phase 1: Quick Fixes And Canonical Spelling

Risk: low to medium  
Owner: NSCIM WebApp/API

- Fix `Index.razor` KPI call from `/api/record-completeness/summary` to `/api/recordcompleteness/summary` or add a deliberate compatibility alias.
- Fix or intentionally replace `/api/ManualBOERequest`.
- Normalize `ImageProcessingPanel.razor` route spelling and leading slash.
- Pick canonical casing/spelling for the top 20 NSCIM frontend call segments and update internal callsites.
- Normalize endpoint usage aggregation by canonical path so `/api/Monitoring/*` and `/api/monitoring/*` do not split telemetry.

Acceptance:

- Dashboard KPIs and container manual BOE request work through real backend routes.
- Monitoring panel image search/statistics still loads.
- Endpoint usage panel shows canonicalized route rows.

### Phase 2: Typed Frontend Clients

Risk: medium  
Owner: NSCIM WebApp

- Create typed clients for the highest fan-out areas:
  - `ImageAnalysisWorkflowClient`
  - `ImageAnalysisDecisionClient`
  - `ImageToolClient`
  - `IcumsDownloadQueueClient`
  - `IcumsBatchClient`
  - `CargoGroupClient`
  - `ContainerDetailClient`
  - `MonitoringClient`
- Move direct route strings out of high-churn pages/components.
- Keep `ApiService` as the HTTP wrapper, but stop exposing raw string endpoints to every page.

Acceptance:

- Top fan-out files reduce direct `/api` literals substantially.
- No user-facing workflow loses behavior.
- Shared clients own error handling and route spelling.

### Phase 3: Domain Canonical APIs

Risk: medium to high  
Owner: API domain owners

- Container/cargo:
  - make `CargoGroupController` the canonical aggregate API.
  - keep `ContainerDetailsController` as a BFF/compat layer.
  - delegate duplicate reads internally where possible.
- Image/X-ray:
  - make `ImageProcessingController` canonical for image serving/tools.
  - keep `ImageController` as deprecated alias only.
  - keep `XrayInspectorController` and `ImageSplitterController` as BFF/proxy APIs.
- ICUMS:
  - split queue, ingestion, metrics, payload-admin responsibilities explicitly.
  - stop adding new UI calls to old `Icum`, `ICUMSManual`, `ICUMSArchive`, and `icums/transfer` surfaces.
- Monitoring:
  - choose one canonical performance metrics API.
  - keep diagnostics/debug/database admin separate from normal monitoring pages.

Acceptance:

- Each domain has documented canonical routes and compatibility routes.
- Compatibility routes internally delegate to canonical services/routes when practical.
- Deprecated route telemetry is visible for every compatibility family.

### Phase 4: Deprecation And Removal

Risk: medium  
Owner: API/platform maintainer

- Add deprecation metadata/config for every compatibility route:
  - route,
  - replacement,
  - owner,
  - deprecation date/version,
  - required no-usage window,
  - external caller notes.
- Use `/api/Monitoring/deprecated-endpoints/summary` and `/api/Monitoring/endpoint-callers` before removal.
- Only remove routes after:
  - no usage for agreed window, usually 30 days minimum,
  - no external callers,
  - release note/handoff created,
  - tests updated.

Acceptance:

- Removed routes have telemetry evidence.
- Rollback is possible through aliases if a hidden caller appears.

## Suggested Canonical Route Decisions

| Domain | Canonical now | Compatibility / BFF | Service-only |
| --- | --- | --- | --- |
| Cargo group | `/api/CargoGroup` | `/api/ContainerDetails`, `/api/ContainerProcessing`, parts of `/api/ConsolidatedCargo` | none |
| Container validation | `/api/ContainerValidation` | old validation page routes | none |
| Container completeness | `/api/ContainerCompleteness` | legacy completeness page aliases | background workers |
| Record completeness | `/api/RecordCompleteness` or alias `/api/recordcompleteness` | `/api/Diagnostics/record-completeness` for diagnostics only | record workers |
| Image serving/tools | `/api/ImageProcessing/container/{containerNumber}/complete/image` and related tools | `/api/Image`, old image aliases | scanner/image services |
| Image analysis workflow | `/api/image-analysis` | legacy decision endpoints in `ImageAnalysisController` | orchestrator workers |
| Image analysis management | `/api/image-analysis-management` | string-id assign/release/reassign | assignment services |
| Decisions | `/api/ImageAnalysisDecision` | old bulk decision endpoints | none |
| Audit submit | `/api/AuditReview/submit` | `ImageAnalysisController.SaveAuditDecision` | none |
| ICUMS batch ingestion | `/api/icums/batch` | old transfer trigger/status | file ingestion service |
| ICUMS download queue | `/api/ICUMSDownloadQueue` | future lower-case alias possible | ICUMS worker |
| ICUMS submission queue | `/api/ICUMSSubmissionQueue` | future lower-case alias possible | submission worker |
| Monitoring | `/api/Monitoring` | `Performance*` overlap until consolidated | metrics services |
| Service control | `/api/SystemAdmin` | old service-control page aliases | Windows services |
| Scan asset identity | `/api/scan-assets/resolve`, `/api/scan-assets/{sourceScanId}/image`, `/api/scan-assets/{sourceScanId}/images` | planned `/api/scan-assets/{sourceScanId}/scanner-data` until implemented or intentionally dropped | scanner/image resolver services |
| Predictive preload cache | `/api/cache/predictive` | admin/operator cache controls | preload background service/cache |
| Eagle A25 scanner | `/api/EagleA25` currently; consider future `/api/eagle-a25` alias if route standard changes | scanner overview, Eagle A25 panel/dialog, and asset-content proxying | Eagle A25 sync/background service |
| External ICUMS/BOE scan API | none local | `/api/BOEScanData/*`, `/api/rm/scan/*` are outbound external paths | external ICUMS/BOE scan service |
| External model-provider API | none local | `/api/tags`, `/api/generate` are outbound Ollama/model-provider paths | external AI/model provider |
| NickComms | `/api/email`, `/api/sms`, `/api/otp`, `/api/messages` | none | Hubtel/SMTP clients |
| Image splitter | NSCIM `/api/image-splitter`, `/api/xray-inspector` for operators | FastAPI UI/static tools | FastAPI `/api/split`, `/inspector` |

## Validation Checklist For Later Implementation

Static checks:

- Re-run controller/minimal/FastAPI route inventory after each cleanup phase.
- Re-run literal consumer inventory and compare against backend segments.
- Check for mixed casing variants of the same endpoint family.
- Check for frontend route aliases that are no longer linked from navigation.

Runtime checks:

- Use `/api/Monitoring/all-endpoints/summary` for active route inventory.
- Use `/api/Monitoring/deprecated-endpoints/summary` for compatibility route usage.
- Use `/api/Monitoring/endpoint-callers?ep=...` before removing or redirecting a route.
- Keep an eye on status code and error rate columns after rewiring callsites.

Build/test checks:

- Run targeted builds for the changed app surface.
- For NSCIM route rewiring, run Core/API/WebApp relevant tests where available.
- Smoke test operator routes:
  - `/dashboard`
  - `/containers`
  - `/completeness`
  - `/image-analysis/workbench`
  - `/monitoring/health`
  - `/customs/icums/downloads`
  - `/image-analysis/xray-inspector`

UI checks:

- Dashboard KPI cards load without silently falling back to `N/A`.
- Manual BOE request command succeeds from container details.
- Image processing monitoring search/statistics load.
- Split review and X-ray inspector load through NSCIM proxy routes.
- Endpoint usage panel shows callers and deprecated route summaries.

## Final Recommendation

Do not start by deleting controllers. Start by removing ambiguity.

The highest-value path is:

1. Fix the small stale/mismatched route callsites.
2. Choose canonical spelling for active NSCIM route families.
3. Move repeated page-level strings into typed frontend clients.
4. Mark compatibility routes explicitly and route telemetry through the existing endpoint usage system.
5. Consolidate domain families one at a time, beginning with container/cargo, image tools, ICUMS queues, and monitoring.

That gives operators a stable UI while making the API surface simpler, measurable, and eventually removable where it is truly duplicate.
