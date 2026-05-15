# API And Frontend Endpoint Consolidation Implementation Plan

Date: 2026-05-13  
Companion audit: `docs/api-frontend-endpoint-consolidation-audit-2026-05-13.md`  
Repository: `C:\Shared\NSCIM_PRODUCTION`

## Executive Summary

The consolidation should be implemented as a staged migration, not a route purge. The main risk is not backend compilation; it is breaking operator screens that currently depend on many narrow, direct API calls. The safest fix is to first establish canonical routes and typed frontend clients, then migrate callsites, then use endpoint telemetry to retire compatibility aliases.

Expected impact:

| Area | Impact |
| --- | --- |
| Operator UI | Medium risk. Dashboard, container details, completeness, ICUMS, monitoring, image analysis, X-ray inspector, and split review are the most exposed. |
| External/API callers | Medium risk. NickHR central auth clients, platform stats, scripts, and possible operator curl/runbook use need telemetry before route removal. |
| Backend services | Low to medium risk if controller behavior is delegated rather than rewritten. |
| Data model | Low risk for early phases. No database schema changes are required for route standardization and frontend client extraction. |
| Performance | Positive if screen fan-out is reduced. Neutral to slight negative during compatibility period if aliases add extra delegation layers. |
| Observability | Positive once endpoint paths are canonicalized and deprecated-route metadata is expanded. |
| Deployment | Safe if backend compatibility aliases ship before frontend rewiring and removals are delayed behind telemetry. |

Recommended sequence:

1. Fix confirmed stale frontend routes.
2. Add route inventory and telemetry guardrails.
3. Introduce typed frontend clients.
4. Migrate callsites by domain.
5. Add canonical route aliases where needed.
6. Retire compatibility routes only after usage proves they are safe to remove.

## 2026-05-14 Drift Addendum

A final source recheck after later work found three endpoint families that must be folded into this plan before implementation starts:

| Family | Routes | Plan impact |
| --- | --- | --- |
| Eagle A25 scanner | `/api/EagleA25/scans`, `/api/EagleA25/scans/{id:guid}`, `/api/EagleA25/sync-status`, `/api/EagleA25/sync` | Treat as a separate scanner module/BFF. Do not merge into container/cargo APIs. Add an `EagleA25Client` before moving UI calls. |
| Scan assets | `/api/scan-assets/resolve`, `/api/scan-assets/{sourceScanId}/image` | Treat as the canonical source-scan identity API for image URLs. Resolve whether planned `/scanner-data` and `/images` routes should be implemented or removed from the frontend fallback order. |
| Predictive preload cache | `/api/cache/predictive/*` | Treat as admin/cache support API. Keep separate from domain CRUD routes and expose through a typed cache/preload client if the WebApp consumes more than container context. |

The recheck also confirmed the existing image-splitter classification still holds: NSCIM `/api/image-splitter` and `/api/xray-inspector` remain operator-facing BFF/proxy routes; FastAPI `/api/split/*` and `/inspector/*` remain service-only/static-tool routes.

## 2026-05-15 Re-Analysis Update

The latest source re-analysis keeps the staged migration approach, but changes a few implementation details:

| Area | Update | Plan adjustment |
| --- | --- | --- |
| Current size | NSCIM API now scans at about 80 controller files and 615 attributed `[Http*]` actions. NSCIM WebApp source references now scan at about 556 `/api` references across 104 files. | Treat the original audit counts as baseline history, not the current target. Re-run inventory before each implementation phase. |
| Scan assets | `/api/scan-assets/{sourceScanId}/images` now exists. `/api/scan-assets/{sourceScanId}/scanner-data` is still referenced by `ContainerDetailsService` but is not implemented in `ScanAssetsController`. | Update `ScanAssetClient` to include `resolve`, `image`, and `images`. Decide explicitly whether `scanner-data` belongs in scan-assets or should remain under `ContainerDetails`. |
| Eagle A25 | `/api/EagleA25/assets/{id:guid}/content` was added beside scans/detail/status/sync. | Expand `EagleA25Client` to own asset-content URL building and ImageProxy/signed URL behavior. Treat this as binary/image-like fetch behavior, not ordinary JSON CRUD. |
| Split selection | New BFF command aliases exist under both `/api/image-analysis/records/{analysisRecordId}/...` and `/api/image-splitter/jobs/{jobId}/...` for choose/skip split. | Add a split-selection client method such as `ChooseSplitAsync(context)` and choose one canonical route per UI context. Keep the other paths as compatibility aliases until telemetry shows safe removal. |
| External `/api` paths | `/api/BOEScanData/*`, `/api/rm/scan/*`, `/api/tags`, and `/api/generate` are outbound external service paths. | Exclude these from local NSCIM route-removal work. Document them as service dependencies and validate through client configuration/tests instead of controller inventory. |

## Guiding Principles

- Do not delete or rename live routes first.
- Do not mix service-only APIs into operator-facing API namespaces.
- Prefer typed clients and domain/BFF APIs over adding more direct page-level calls.
- Keep current controllers working while moving the frontend to canonical routes.
- Use `/api/Monitoring/all-endpoints/summary`, `/api/Monitoring/deprecated-endpoints/summary`, and `/api/Monitoring/endpoint-callers` before any route removal.
- Treat page route aliases separately from API route aliases. Both surfaces are noisy, but they need different migration paths.

## Phase 0: Baseline And Safety Net

Goal: make route usage measurable and reproducible before behavior changes.

Changes:

- Add a non-mutating route inventory tool under `tools/diagnostics` or `scripts` that emits:
  - backend controller routes,
  - minimal API routes,
  - FastAPI routes,
  - Blazor/Razor `@page` routes,
  - source `/api` consumers,
  - unmatched consumer route segments,
  - casing variants.
- Expand endpoint deprecation metadata from hard-coded checks in `PerformanceLoggingMiddleware` into a small config/table shape:
  - endpoint pattern,
  - canonical replacement,
  - owner,
  - reason,
  - deprecation date,
  - safe-removal window.
- Update endpoint usage reporting to canonicalize route casing for reporting, while keeping raw path available for diagnostics.

Impact:

- User-facing impact: none.
- API compatibility impact: none.
- Operational impact: positive; gives a trustworthy baseline.
- Risk: low.

Validation:

- Inventory output count roughly matches the audit plus drift addenda: NSCIM API should now be around 80 controller files and 615 attributed actions in the current checkout; the original 2026-05-13 audit baseline was around 79 controllers and 596 actions.
- Endpoint usage panel still loads deprecated endpoints, phase routes, safe-to-remove, all endpoints, and caller dialogs.

Rollback:

- Remove or disable the diagnostic script/config. No runtime behavior should depend on it yet.

## Phase 1: Fix Confirmed Stale Or Fragile Calls

Goal: remove known broken or ambiguous frontend calls without broader redesign.

Changes:

- Dashboard KPI call:
  - Replace `/api/record-completeness/summary` with existing `/api/recordcompleteness/summary`, or add a deliberate compatibility route if canonical naming is being changed immediately.
- Manual BOE request:
  - Replace `/api/ManualBOERequest` with existing `POST /api/ContainerCompleteness/request-boe/{containerNumber}`, or add a proper `manual-boe-requests` controller if that workflow needs its own API.
- Image processing monitor:
  - Normalize `api/imageprocessing{query}` and `api/imageprocessing/statistics` to the chosen canonical spelling with a leading slash.
- Audit any remaining no-leading-slash `api/...` calls in Blazor pages.

Impact:

| Workflow | Potential impact | Mitigation |
| --- | --- | --- |
| Dashboard | KPI cards may change from `N/A` to live values. If DTO shape differs, cards may fail. | Use existing `RecordCompletenessController.GetSummary` DTO and smoke-test `/dashboard`. |
| Container details | Manual BOE request button behavior changes from likely failing/missing endpoint to real container-completeness command. | Confirm snackbar messages and queue side effects. |
| Monitoring image processing | Search/statistics requests become explicit and easier to track. | Smoke-test `/monitoring/image-processing`. |

Risk:

- Low to medium. These are small callsite fixes, but they touch visible operator screens.

Validation:

- Load `/dashboard`; confirm health, record summary, readiness, and scan stats load.
- Load `/containers/{container}`; trigger manual BOE request with a known test container or guarded test environment.
- Load `/monitoring/image-processing`; run search and load statistics.
- Check API logs for 404s on the old stale routes.

Rollback:

- Revert individual frontend callsite changes.
- If a compatibility backend alias was added, leave it in place until telemetry is reviewed.

## Phase 2: Define Canonical Route Contracts

Goal: choose canonical routes and compatibility labels before moving large callsite groups.

Changes:

- Add a route contract document or constants file for canonical NSCIM frontend-consumed routes.
- Classify current routes as:
  - canonical,
  - compatibility alias,
  - screen-specific/BFF,
  - duplicate overlap,
  - stale/broken,
  - external/service-only.
- For each domain, document canonical route ownership:
  - cargo/container,
  - completeness/validation,
  - scan asset identity,
  - Eagle A25 scanner integration,
  - predictive preload/cache support,
  - image/X-ray/split,
  - image-analysis workflow,
  - ICUMS,
  - monitoring/admin.

Impact:

- User-facing impact: none if this is documentation/constants only.
- Developer impact: positive; gives one source of truth.
- Risk: low.

Validation:

- Route contract matches the live controller/action inventory.
- No route is marked removable without telemetry or explicit owner signoff.

Rollback:

- None needed for documentation. Constants can be adjusted before callsites consume them.

## Phase 3: Introduce Typed Frontend Clients

Goal: reduce direct page/component dependency on raw endpoint strings.

New or expanded clients:

| Client | Owns |
| --- | --- |
| `CargoGroupClient` | Cargo group list/detail/full/lookup/summary calls. |
| `ContainerDetailClient` | Container details, scanner/ICUMS/images metadata, validation detail helpers. |
| `CompletenessClient` | Record completeness, container completeness, validation queue, match correction operations. |
| `ImageToolClient` | ImageProcessing image URLs, ROI, raw, pixel, mode capabilities. |
| `ImageAnalysisWorkflowClient` | assignments, available work, claim, lease, readiness, wave context. |
| `ImageAnalysisDecisionClient` | decision load/save/delete, rectangles, overall group decision. |
| `ScanAssetClient` | source-scan resolution, image metadata, and image URL generation for `/api/scan-assets/*`. |
| `EagleA25Client` | Eagle A25 scan list/detail, asset-content URL/proxy handling, sync status, and guarded manual sync. |
| `SplitSelectionClient` | choose/skip split commands by analysis record, split job, or container context, hiding BFF alias selection from components. |
| `PredictivePreloadClient` | cache status, run-once/admin invalidation, and optional container context reads. |
| `IcumsBatchClient` | `/api/icums/batch` status, files, logs, records, verification, health, warnings. |
| `IcumsDownloadQueueClient` | download queue list/enqueue/retry/delete/archive/requeue/priority. |
| `IcumsSubmissionQueueClient` | submission queue list/stats/retry/cancel/delete. |
| `MonitoringClient` | health, database stats, endpoint usage, logs, service control reads. |

Implementation detail:

- Keep `NickScanWebApp.Shared.Services.ApiService` as the HTTP wrapper.
- Typed clients should expose workflow methods, not route strings:
  - `GetRecordSummaryAsync()`
  - `RequestBoeAsync(containerNumber)`
  - `GetCompleteContainerImageUrl(containerNumber, options)`
  - `RetryDownloadAsync(id)`
- For image `src` URLs, preserve signed URL behavior and `ImageProxy` behavior. Do not accidentally move browser image fetches to bearer-token-only routes.

Impact:

| Area | Potential impact | Mitigation |
| --- | --- | --- |
| Compile-time safety | Positive. Routes move from scattered strings to typed methods. | Add focused unit tests for URL builders where possible. |
| UI behavior | Medium. Refactors can change error handling/loading states. | Migrate one domain at a time and smoke-test pages. |
| Auth | Medium for image and SignalR-adjacent flows. | Preserve named `NickScanAPI`, bearer injection, signed image URLs, and hub auth patterns. |
| Performance | Neutral initially. | Avoid adding sequential calls where pages currently call in parallel. |

Validation:

- Build `src/NickScanWebApp.New`.
- Smoke-test each migrated page.
- Compare API logs before/after to ensure route count is stable or reduced.

Rollback:

- Revert one client migration at a time.
- Since backend routes remain unchanged, rollback is mostly frontend-only.

## Phase 4: Migrate High-Fan-Out Domains

Goal: move pages/components to typed clients and canonical routes domain by domain.

### 4A: Monitoring And Dashboard

Scope:

- `Index.razor`
- `Monitoring/Health.razor`
- `Monitoring/Diagnostics.razor`
- `Monitoring/ErrorsAndAudit.razor`
- `Components/Monitoring/*`

Impact:

- High operator visibility.
- Low domain complexity.
- Good first full migration because monitoring already has endpoint usage telemetry.

Validation:

- `/dashboard`
- `/monitoring/health`
- `/monitoring/diagnostics`
- `/monitoring/endpoint-usage`
- `/monitoring/service-control`

### 4B: ICUMS

Scope:

- `IcumsDownloadQueue.razor`
- `IcumsIndividualDownloadPanel.razor`
- `IcumsBatchDownloadPanel.razor`
- `IcumsSubmissionQueue.razor`
- `IcumsPayloadViewer.razor`

Impact:

- Medium to high. ICUMS workflows affect operations and data ingestion.
- Route consolidation should not change queue side effects.

Validation:

- List queues.
- Enqueue a test request where safe.
- Retry/delete guarded test items.
- Load batch files, file records, verification, warnings, ingestion logs.

### 4C: Container, Cargo, Completeness

Scope:

- `ContainerList.razor`
- `ContainerDetails.razor`
- `ContainerCompletenessRecords.razor`
- `ContainerCompletenessLegacy.razor`
- `RecordCompleteness.razor`
- `MatchCorrections.razor`
- `CargoGroup*` components.

Impact:

- High. This is the densest business workflow area.
- It touches scanner data, ICUMS data, images, validation status, cargo grouping, and manual correction flows.

Validation:

- List cargo groups.
- Open a normal container.
- Open a consolidated cargo group.
- Load scanner, ICUMS, images, validation detail.
- Approve/reject a guarded test item.
- Load record completeness list/detail/summary.
- Open match correction detail and run non-mutating detail paths.

### 4D: Image Analysis, X-Ray, Split Review

Scope:

- `Workbench.razor`
- `AuditReview.razor`
- `BlReview.razor`
- `AiReview.razor`
- `Configuration.razor`
- `SplitReview.razor`
- `XrayInspector.razor`
- `ImageAnalysisViewer.razor`
- `ImageAnalysisViewDialog.razor`
- `AuditReviewDialog.razor`
- `ImageDecisionView.razor`

Impact:

- Highest risk. This area includes SignalR readiness, assignments, leases, image rendering, signed URLs, raw/ROI/pixel tools, decisions, audit submit, split review, and service proxying.

Validation:

- Ready toggle and heartbeat.
- Assignment list and available list.
- Claim and lease renew in test-safe conditions.
- Open viewer, load image, switch modes, load raw/ROI/pixel tools.
- Save decision in test-safe conditions.
- Audit submit path.
- Split review job list/detail/image/approve/reject only in a controlled test set.
- X-ray inspector search/image/composite/export paths.

Rollback:

- This phase should be split into small PRs/commits by workflow. Revert one workflow at a time if needed.

## Phase 5: Backend Compatibility Aliases And Delegation

Goal: make canonical APIs real without breaking old callers.

Changes:

- Add canonical lower-case/kebab-case aliases where the chosen standard differs from legacy route spelling.
- Internally delegate compatibility routes to the same service methods as canonical routes.
- Add deprecation metadata for old routes.
- Add response headers on compatibility routes if useful:
  - `Deprecation: true`
  - `Link: <canonical-url>; rel="successor-version"`
  - optional internal warning header for non-browser clients.

High-value aliases:

- `/api/record-completeness/*` to `RecordCompletenessController`, if this becomes the canonical spelling.
- `/api/container-completeness/*` to `ContainerCompletenessController`.
- `/api/container-validation/*` to `ContainerValidationController`.
- `/api/image-processing/*` to `ImageProcessingController`, if lower-case/kebab-case is adopted.
- `/api/icums/download-queue/*` to `ICUMSDownloadQueueController`.
- `/api/icums/submission-queue/*` to `ICUMSSubmissionQueueController`.

Impact:

- User-facing impact: none intended.
- External API impact: positive; compatibility becomes explicit.
- Backend risk: medium if alias route templates conflict with existing parameterized routes.

Validation:

- Route conflict check at startup.
- Integration tests for canonical and compatibility route pairs.
- Endpoint usage telemetry separates canonical and compatibility usage but reports replacements clearly.

Rollback:

- Disable aliases or revert route attributes. Existing legacy routes remain in place.

## Phase 6: Deprecate And Remove Dead Routes

Goal: reduce the endpoint surface only after usage proves routes are dead.

Removal gates:

- Route is marked compatibility or stale.
- Canonical replacement exists and is tested.
- Internal frontend no longer calls it.
- No external callers in telemetry for at least 30 days, or a consciously accepted longer window for operator scripts.
- No recent 2xx traffic from non-internal callers.
- Runbook/changelog notes are updated.

Candidate families for eventual removal:

- Deprecated `ImageController` endpoints after image clients use `ImageProcessing`.
- Old `ImageProcessing` image aliases after all image renderers use `complete/image`.
- Legacy image-analysis bulk decision endpoints after all decisions use `ImageAnalysisDecision` or `AuditReview`.
- Old ICUMS transfer trigger route if telemetry confirms no active operator dependency.
- Duplicate consolidated-cargo helper reads after cargo-group aggregate endpoints own the workflow.

Impact:

- User-facing impact: low if telemetry gates are followed.
- External caller impact: medium if hidden scripts exist.
- Operational impact: positive; smaller API surface and less support ambiguity.

Rollback:

- Keep removal commits small.
- If a hidden caller appears, restore the alias route and mark it as active compatibility.

## Impact Assessment By Workflow

| Workflow | Impact level | Why |
| --- | --- | --- |
| Dashboard | Medium | Currently has at least one stale route and multiple API families. Visible immediately to operators. |
| Monitoring | Medium | Many route aliases, but mostly read-only. Strong telemetry exists. |
| Container details | High | Combines cargo group, container details, validation, BOE request, images, scanner and ICUMS data. |
| Completeness records | High | Central operational workflow with many page aliases and multiple backend families. |
| ICUMS queues | High | Queue commands have side effects. Must preserve retry/delete/enqueue semantics. |
| Image analysis workbench | Very high | Assignments, readiness, leases, SignalR, decision saves, and image loading are tightly coupled. |
| X-ray inspector | High | Proxies backing service and binary/image/export flows. |
| Split review | High | Proxies FastAPI split service and can mutate review decisions. |
| Scan asset resolution | High | New canonical image identity path. The `/images` endpoint now exists, but unresolved `/scanner-data` ownership can still create fallback noise or stale assumptions. |
| Eagle A25 scanner | Medium to high | New scanner integration touches sync, scanner overview UI, and scanner-specific data tables. Keep separate from container/cargo until lifecycle is stable. |
| Eagle A25 asset content | Medium to high | Binary asset route can affect image/canvas loading, signed URL behavior, and same-origin proxy behavior. Treat like image-serving routes. |
| Split selection commands | High | Multiple command aliases exist across image-analysis and image-splitter namespaces. Incorrect canonical choice can break analyst split-choice workflows. |
| Predictive preload | Medium | Cache endpoints are mostly support/admin flows, but stale context can change perceived UI data freshness. |
| NickHR central auth | Medium | Service-to-service routes must remain compatible. Do not rename without aliasing. |
| NickFinance | Low | Mostly separate; avoid unnecessary changes. |
| NickComms | Low to medium | Shared service API is clean. Protect typed clients and auth. |
| Platform portal | Medium | Depends on configured NSCIM stats URLs; canonical route changes can break dashboard if config is stale. |

## Testing Strategy

Static:

- Route inventory diff before and after each phase.
- Consumer inventory diff showing reduced raw page-level `/api` strings.
- Search for old route strings after migration.

Build:

- `dotnet build src/NickScanWebApp.New/NickScanWebApp.New.csproj`
- `dotnet build src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj`
- Domain-specific test projects where touched.

Integration/smoke:

- Use API health and WebApp routes first.
- Smoke the exact operator pages, not just controllers.
- For side-effectful operations, use controlled test records or dry-run-compatible paths.

Telemetry:

- Track 404/401/403 spikes after frontend rewiring.
- Track deprecated endpoint calls by route and caller.
- Track response time changes for consolidated/BFF routes.

Manual UI:

- Verify regular in-app routes, not only standalone/fullscreen tools.
- For monitoring pages, verify visible live state and timestamps still update.
- For image tools, verify browser image fetches still work with signed URL/proxy constraints.

## Rollout Plan

Deployment order:

1. Backend telemetry/deprecation metadata.
2. Backend compatibility aliases where needed.
3. Frontend typed clients and callsite migration.
4. Runtime monitoring for 7 to 30 days.
5. Compatibility route removal only after gates pass.

Release strategy:

- Ship small domain batches.
- Prefer one high-fan-out page family per deployment.
- Avoid combining image-analysis workflow changes with ICUMS queue changes in the same release.
- Keep route alias changes separate from route removal changes.

Rollback strategy:

- Frontend rewiring can be reverted independently because legacy routes remain.
- Backend aliases can remain harmlessly during rollback.
- Route removals must be last and individually reversible.

## Definition Of Done

Technical:

- Known stale calls are fixed.
- Canonical route contract exists.
- Typed clients own the top endpoint families.
- Direct `/api` strings in high-churn pages are substantially reduced.
- Compatibility routes are explicitly marked and visible in telemetry.

Operational:

- Dashboard, monitoring, container, completeness, ICUMS, image-analysis, X-ray, and split-review workflows pass smoke checks.
- Deprecated route usage is measured for at least 30 days before removal.
- External/service clients are documented and unaffected.

Outcome:

- Fewer route spellings.
- Fewer page-level API dependencies.
- Clear canonical APIs by domain.
- Safer future frontend work because screen components call workflow clients instead of assembling endpoint strings themselves.
