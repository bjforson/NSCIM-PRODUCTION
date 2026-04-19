# NickScan WebApp - Complete Page Functional Audit Report

**Generated:** March 23, 2026  
**Total Pages:** 61  
**Source:** `src\NickScanWebApp.New\Pages\`

---

## Table of Contents

1. [Authentication](#authentication)
2. [Dashboard & Core](#dashboard--core)
3. [Containers](#containers)
4. [Operations](#operations)
5. [ICUMS](#icums)
6. [Validation](#validation)
7. [Scanners](#scanners)
8. [Images](#images)
9. [Vehicles](#vehicles)
10. [Services (Backend)](#services-backend)
11. [Administration](#administration)
12. [Reports](#reports)
13. [Monitoring & Diagnostics](#monitoring--diagnostics)
14. [Notifications](#notifications)
15. [CMR](#cmr)
16. [Summary: Stubs & Placeholders](#summary-stubs--placeholders)
17. [Summary: Duplicate / Overlapping Pages](#summary-duplicate--overlapping-pages)

---

## Authentication

### Login
- **File:** `Authentication\Login.razor`
- **Route(s):** `/`, `/login`, `/authentication/login`
- **Auth:** `[AllowAnonymous]`
- **Function:** Branded login form. Posts credentials to `/api/Authentication/login`, stores JWT token via auth provider, loads permission claims, then redirects to `/dashboard`. Shows inactivity/restart reason messages via query string. If already authenticated, auto-redirects to dashboard.
- **API Endpoints:**
  - `POST /api/Authentication/login`
  - `GET /api/permissions/my-permissions` (post-login)
- **Actions:** Login form submit, password visibility toggle, remember me checkbox.

---

## Dashboard & Core

### Dashboard (Index)
- **File:** `Index.razor`
- **Route(s):** `/dashboard`
- **Auth:** `Permissions.PagesDashboardView`
- **Function:** Landing page with KPI stat cards (currently hardcoded values), scan volume chart, CMR quality widget (live from API), and quick-action links to containers, validation, service control, and help.
- **API Endpoints:**
  - `GET /api/CMRValidation/statistics` (via CMRValidationWidget child component)
- **Actions:** Quick-action navigation buttons, widget refresh.
- **Notes:** KPI numbers are hardcoded -- not wired to live data.

### Analytics
- **File:** `Dashboard\Analytics.razor`
- **Route(s):** `/analytics`, `/dashboard/analytics`
- **Auth:** `Permissions.PagesDashboardAnalytics`
- **Function:** System-wide analytics dashboard: health overview, DB statistics, performance metrics, background service health, event timeline. SignalR live updates via `hubs/comprehensive-dashboard` plus 30s polling fallback.
- **API Endpoints:**
  - `GET /api/Monitoring/health/overview`
  - `GET /api/Monitoring/database/statistics`
  - `GET /api/Monitoring/performance/metrics`
  - `GET /api/Monitoring/health/services`
  - `GET /api/Monitoring/events/recent`
  - `GET /api/Monitoring/filesystem/status`
  - `GET /api/Monitoring/api-metrics`
- **Actions:** Passive viewing with live/disconnected status chip.

### Search
- **File:** `Search.razor`
- **Route(s):** `/search`
- **Auth:** `Permissions.PagesSearch`
- **Function:** Global search UI. Reads optional `?q=` from URL, posts to gateway search, displays typed results (containers, ICUMS, vehicles) with relevance scores. Clicking a result navigates to the entity-specific page.
- **API Endpoints:**
  - `POST /api/Gateway/search`
- **Actions:** Search field with enter/click submit, result row click navigates.

### Performance
- **File:** `Performance.razor`
- **Route(s):** `/performance`
- **Auth:** `Permissions.PagesPerformance`
- **Function:** Performance monitoring: health status, alert count, memory/CPU/DB/active services cards, top memory consumers. Auto-refreshes every 30 seconds.
- **API Endpoints:**
  - `GET /api/Performance/summary`
  - `GET /api/Performance/metrics`
- **Actions:** Refresh button, retry on error, timer-based auto-refresh.

---

## Containers

### Container List
- **File:** `Containers\ContainerList.razor`
- **Route(s):** `/containers`
- **Auth:** `Permissions.PagesContainersView`
- **Function:** Lists cargo groups with filters (type, clearance, search text) and pagination. Click navigates to container details page with group identifier.
- **API Endpoints:**
  - `CargoGroupService.GetCargoGroupsAsync(...)` → `GET /api/cargogroup?...`
- **Actions:** Add Manual Entry, Export, Refresh, filter apply/clear, pagination, view group details.

### Container Details
- **File:** `Containers\ContainerDetails.razor`
- **Route(s):** `/containers/{ContainerNumber}`, `/containers/details`
- **Auth:** `Permissions.PagesContainersDetails`
- **Function:** Full single-container view: validation status, completeness state, ICUMS data, scanner context, images. Can render `CargoGroupView` when group info is known, else tabbed fallback view.
- **API Endpoints:**
  - `ContainerViewPreloader.LoadAsync`
  - `ContainerDetailsService.GetFullBOEDataAsync`
  - `CargoGroupService.GetGroupIdentifierByContainerAsync`
  - Navigation to `/images/viewer/{ContainerNumber}`
- **Actions:** Approve, Reject, Submit to ICUMS, Export Report, Refresh, Fetch ICUMS (simulated), Run Validation, Edit, Add Annotation, View Full Image.
- **Notes:** `FetchICUMSData` is simulated with `Task.Delay` -- not wired to live API.

### Container Processing
- **File:** `ContainerProcessing.razor`
- **Route(s):** `/container-processing`
- **Auth:** `Permissions.PagesContainerProcessing`
- **Function:** Shows summary stats and lists standardized cargo groups with filters and pagination. Navigates to container details for a selected group.
- **API Endpoints:**
  - `GET /api/ContainerProcessing/summary`
  - `GET /api/cargogroup?type=...&clearanceType=...&page=...&pageSize=...`
- **Actions:** Refresh, Help, filter apply/clear, pagination, view group details.

---

## Operations

### Image Analysis (Analyst Workbench)
- **File:** `Operations\ImageAnalysis.razor`
- **Route(s):** `/operations/image-analysis`
- **Auth:** `Permissions.PagesImageAnalysisView`
- **Function:** Analyst workbench: view assigned groups, claim from available queue, toggle readiness for new assignments, renew leases. Opens `ImageAnalysisViewDialog` for making decisions on container images.
- **API Endpoints:**
  - `GET /api/image-analysis-management/service-state`
  - `GET /api/image-analysis/my-assignments?role=Analyst`
  - `GET /api/image-analysis/available?role=Analyst`
  - `POST /api/image-analysis/groups/{groupId}/claim`
  - `POST /api/image-analysis/groups/{groupIdentifier}/lease/renew`
  - `POST /api/image-analysis/user/ready`
  - `POST /api/image-analysis/user/heartbeat`
  - SignalR: `hubs/userReadiness`
- **Actions:** Ready toggle, Refresh, Claim, View (dialog), Renew lease, status/scanner filters.

### Audit (Reviewer Workbench)
- **File:** `Operations\Audit.razor`
- **Route(s):** `/operations/audit`
- **Auth:** `Permissions.PagesImageAnalysisAudit`
- **Function:** Audit reviewer workbench: same claim/readiness pattern as Analyst, but for the Audit role. Opens `AuditReviewDialog` for group review.
- **API Endpoints:**
  - `GET /api/image-analysis-management/service-state`
  - `GET /api/image-analysis/my-assignments?role=Audit`
  - `GET /api/image-analysis/available?role=Audit`
  - `POST /api/image-analysis/groups/{groupId}/claim`
  - `POST /api/image-analysis/groups/{groupIdentifier}/lease/renew`
  - `GET /api/AuditReview/group/{groupIdentifier}?scannerType=...`
  - `POST /api/image-analysis/user/ready`
  - `POST /api/image-analysis/user/heartbeat`
  - SignalR: `hubs/userReadiness`
- **Actions:** Ready toggle, Refresh, Claim, Review (dialog), Renew lease.

### Image Analysis Operations Dashboard
- **File:** `Operations\ImageAnalysisOperationsDashboard.razor`
- **Route(s):** `/operations/image-analysis-dashboard`, `/image-analysis/operations-dashboard`
- **Auth:** `Permissions.PagesImageAnalysisView`
- **Function:** Operations overview dashboard: workflow pipeline counts, user assignments, productivity metrics, quality stats, bottlenecks, predictions, alerts, and system health. SignalR live updates.
- **API Endpoints:**
  - `GET /api/image-analysis/dashboard/overview`
  - `GET /api/image-analysis/dashboard/user-productivity`
  - `GET /api/image-analysis/dashboard/quality`
  - `GET /api/image-analysis/dashboard/bottlenecks`
  - `GET /api/image-analysis/dashboard/predictions`
  - `GET /api/image-analysis/dashboard/alerts`
  - `POST /api/image-analysis/dashboard/alerts/{alertId}/acknowledge`
  - `GET /api/image-analysis/dashboard/system-health`
  - `GET /api/image-analysis/dashboard/export/csv`
  - `GET /api/image-analysis/dashboard/export/pdf`
  - SignalR: `hubs/imageAnalysisDashboard`
- **Actions:** Refresh, export CSV/PDF with date range, click workflow stage cards for detail dialog, acknowledge alerts.

### Container Completeness Records
- **File:** `Operations\ContainerCompletenessRecords.razor`
- **Route(s):** `/operations/completeness-records`
- **Auth:** `Permissions.PagesContainerCompleteness`
- **Function:** Lists pending container validation records with filters and pagination. Tabs for non-consolidated and consolidated cargo groups. Approve/reject per container.
- **API Endpoints:**
  - `GET /api/containervalidation/pending?...`
  - `POST /api/containervalidation/approve/{containerNumber}`
  - `POST /api/containervalidation/reject/{containerNumber}`
  - `GET /api/consolidatedcargo/non-consolidated?pageSize=25`
  - `GET /api/consolidatedcargo/consolidated?pageSize=25`
- **Actions:** Refresh, Export (not wired), filters, pagination, copy container, View modal, Approve, Reject, auto-refresh timer.

### Completed Records
- **File:** `Operations\CompletedRecords.razor`
- **Route(s):** `/operations/completed-records`
- **Auth:** `Permissions.PagesCompletedRecords`
- **Function:** Read-only gallery of completed audit groups ready for ICUMS. Filters by decision and scanner type, client-side search.
- **API Endpoints:**
  - `GET /api/AuditReview/completed?scannerType=...&decision=...`
- **Actions:** Refresh, filter apply, expand audit trail view.
- **Notes:** Export and "Prepare ICUMS Submission" buttons are present but disabled.

### Cross Record Scans
- **File:** `Operations\CrossRecordScans.razor`
- **Route(s):** `/operations/cross-record-scans`
- **Auth:** `Permissions.PagesCrossRecordScans`
- **Function:** Analytics for scans where multiple containers belong to different BOEs/importers. Filtered by date range.
- **API Endpoints:**
  - `GET /api/CrossRecordScans/analytics?startDate=...&endDate=...`
- **Actions:** Date pickers, preset ranges (Today, Last 7 Days), Apply Filter, View row detail dialog, auto-refresh.

### Error Monitor
- **File:** `Operations\ErrorMonitor.razor`
- **Route(s):** `/operations/errors`
- **Auth:** `Permissions.PagesOperationsErrors`
- **Function:** Paginated operational log viewer with 24h statistics, filters for log level, date range, and search.
- **API Endpoints:**
  - `GET /api/LogManagement/logs?...`
  - `GET /api/LogManagement/statistics?hoursBack=24`
- **Actions:** Live auto-refresh toggle, manual refresh, filter, pagination, exception detail dialog.

---

## ICUMS

### ICUMS Dashboard
- **File:** `ICUMS\ICUMSDashboard.razor`
- **Route(s):** `/icums`
- **Auth:** `Permissions.PagesIcumsView`
- **Function:** ICUMS landing dashboard: connection status chip, download/submission stats, recent activity lists, performance metrics, and quick actions.
- **API Endpoints:**
  - `GET /api/ICUMSMetrics/snapshot`
  - `GET /api/ICUMSDownloadQueue/stats`
  - `GET /api/ICUMSDownloadQueue?limit=10&status=Completed`
  - `GET /api/ICUMSSubmissionQueue/stats`
  - `GET /api/ICUMSSubmissionQueue?limit=10`
  - `GET /api/Monitoring/database/statistics`
- **Actions:** Refresh, Manual BOE Request link, Sync Queues (snackbar only), Retry Failed (snackbar only).
- **Notes:** Sync Queues and Retry Failed buttons only show snackbars -- no API calls.

### Download Queue
- **File:** `ICUMS\DownloadQueue.razor`
- **Route(s):** `/icums/download-queue`
- **Auth:** `Permissions.PagesIcumsDownloadQueue`
- **Function:** BOE download queue management with stat cards and tabs (All/Pending/Processing/Completed/Failed/Archive).
- **API Endpoints:**
  - `GET /api/ICUMSDownloadQueue?limit=100`
  - `GET /api/ICUMSDownloadQueue/stats`
  - `GET /api/ICUMSDownloadQueue/archive/stats`
  - `POST /api/ICUMSDownloadQueue/retry/{id}`
  - `DELETE /api/ICUMSDownloadQueue/{id}`
  - `POST /api/ICUMSDownloadQueue/requeue`
  - `POST /api/ICUMSDownloadQueue/requeue-pending`
  - `PUT /api/ICUMSDownloadQueue/priority`
- **Actions:** Refresh, Manual Request link, per-row retry/delete, bulk delete/priority, Requeue All/High Priority, Archive requeue.
- **Notes:** "Retry All Failed" only updates local list state -- does NOT call API.

### Submission Queue
- **File:** `ICUMS\SubmissionQueue.razor`
- **Route(s):** `/icums/submission-queue`
- **Auth:** `Permissions.PagesIcumsSubmissionQueue`
- **Function:** ICUMS submission queue monitoring with stat cards and tabs (All/Pending/Submitting/Successful/Failed).
- **API Endpoints:**
  - `GET /api/ICUMSSubmissionQueue?limit=100`
  - `GET /api/ICUMSSubmissionQueue/stats`
  - `POST /api/ICUMSSubmissionQueue/retry/{id}`
  - `POST /api/ICUMSSubmissionQueue/cancel/{id}`
- **Actions:** Refresh, per-row retry/cancel, tab filtering.
- **Notes:** "Retry All Failed" only updates local list state -- does NOT call API.

### BOE Request
- **File:** `ICUMS\BOERequest.razor`
- **Route(s):** `/icums/boe-request`
- **Auth:** `Permissions.PagesIcumsBoeRequest`
- **Function:** Manual BOE data request form. Supports container number search type (BOE/BL types show warning and do not enqueue).
- **API Endpoints:**
  - `POST /api/ICUMSDownloadQueue/enqueue`
- **Actions:** Submit request, Clear form, Back to Dashboard, row retry (loads fields only).
- **Notes:** Recent requests table is populated with random mock data, not from server. BOE/BL search types are not implemented.

### Batch Download Management
- **File:** `ICUMS\BatchDownloadManagement.razor`
- **Route(s):** `/icums/batch-download`
- **Auth:** `Permissions.PagesIcumsBatchDownload`
- **Function:** Batch download service control: status, interval config, stats, tabs for downloaded files, containers, data transfer, and activity logs.
- **API Endpoints:**
  - `GET /api/icums/batch/status`
  - `GET /api/icums/batch/stats`
  - `GET /api/icums/batch/files?...`
  - `GET /api/icums/batch/containers?...`
  - `GET /api/icums/batch/logs?...`
  - `POST /api/icums/batch/toggle`
  - `POST /api/icums/batch/config`
  - `POST /api/icums/batch/trigger`
  - `POST /api/icums/batch/files/{id}/retry`
  - `DELETE /api/icums/batch/files/{id}`
  - `GET /api/icums/transfer/status`
  - `GET /api/icums/transfer/statistics`
  - `GET /api/icums/transfer/history?...`
  - `POST /api/icums/transfer/trigger`
- **Actions:** Start/Stop service, Save Config, Run Now, Trigger Transfer, file retry/delete, container view, CSV export, log detail dialog, pagination.

### Loose Cargo
- **File:** `ICUMS\LooseCargo.razor`
- **Route(s):** `/icums/loose-cargo`
- **Auth:** `Permissions.PagesIcumsLooseCargo`
- **Function:** Non-containerized (loose/break bulk) cargo listing with summary cards, expandable filters, server-side paging, and detail dialog. Auto-refreshes every 60 seconds.
- **API Endpoints:**
  - `GET /api/LooseCargo/stats`
  - `GET /api/LooseCargo?...` (with extensive query params)
- **Actions:** Refresh, search/filters, pagination, row detail dialog.

### ICUMS Analytics
- **File:** `ICUMS\ICUMSAnalytics.razor`
- **Route(s):** `/icums/analytics`
- **Auth:** `Permissions.PagesIcumsAnalytics`
- **Function:** Multi-tab analytics: Performance (success rates, processing time), Errors (breakdowns from failed downloads), Trends (30-day volume), Capacity (queue utilization). Built by aggregating queue data.
- **API Endpoints:**
  - `GET /api/ICUMSDownloadQueue/stats`
  - `GET /api/ICUMSDownloadQueue?limit=1000`
  - `GET /api/ICUMSSubmissionQueue/stats`
  - `GET /api/ICUMSDownloadQueue?status=Failed&limit=100`
  - `GET /api/ICUMSDownloadQueue?status=Completed&limit=1000`
  - Historical queries with date ranges
- **Actions:** Refresh, period selection (7/30/90 days) for trends tab.

---

## Validation

### Container Completeness
- **File:** `Operations\ContainerCompleteness.razor`
- **Route(s):** `/validation`
- **Auth:** `Permissions.PagesValidationCompleteness`
- **Function:** Broad completeness dashboard: stats, missing/complete lists, validation table, cargo groups, queue health, manual completeness check, BOE requests, approve/reject.
- **API Endpoints:**
  - `GET /api/containercompleteness/stats`
  - `GET /api/containercompleteness/missing`
  - `GET /api/containercompleteness/complete`
  - `POST /api/containercompleteness/trigger-check`
  - `POST /api/containercompleteness/request-boe/{containerNumber}`
  - `POST /api/containercompleteness/request-boe-bulk`
  - `GET /api/containervalidation/pending?...`
  - `POST /api/containervalidation/approve/{containerNumber}`
  - `POST /api/containervalidation/reject/{containerNumber}`
  - `GET /api/consolidatedcargo/non-consolidated`
  - `GET /api/consolidatedcargo/consolidated`
  - `GET /api/QueueHealth/publishing`
- **Actions:** Check Now, filters, bulk BOE, per-row BOE, approve/reject, copy, view, navigate to container detail.
- **Notes:** ToggleService and restart buttons are UI-only placeholders (show snackbar, no API).

### Container Completeness Queue (CCQ)
- **File:** `Validation\ContainerCompletenessQueue.razor`
- **Route(s):** `/validation/ccq`
- **Auth:** `Permissions.PagesValidationCompleteness`
- **Function:** CCQ queue table with stats, filters, sorting, pagination, and SignalR live updates for real-time queue changes.
- **API Endpoints:**
  - `GET /api/QueueHealth/items?...`
  - `GET /api/QueueHealth/statistics`
  - SignalR: `hubs/containerScanQueue` (QueueItemAdded, QueueItemUpdated, QueueStatisticsUpdated)
- **Actions:** Refresh, filter by scanner/status/search, clear, sort, paginate, copy container, View Details dialog.

### Image Analysis Management
- **File:** `Validation\ImageAnalysisManagement.razor`
- **Route(s):** `/validation/image-analysis-management`
- **Auth:** `Permissions.PagesImageAnalysisManagement`
- **Function:** Admin UI for image-analysis service: settings, ready queues, assignments, assignment metrics. Enable/disable service, sync stages, rebuild intake, assign/release/reassign groups.
- **API Endpoints:**
  - `GET /api/image-analysis-management/groups/ready`
  - `GET /api/image-analysis-management/assignments`
  - `GET /api/image-analysis-management/stats`
  - `GET /api/image-analysis-management/service-state`
  - `GET /api/image-analysis-management/analysts`
  - `GET /api/image-analysis/metrics`
  - `POST /api/image-analysis-management/service-state`
  - `POST /api/image-analysis-management/sync-stages`
  - `POST /api/image-analysis-management/rebuild-intake`
  - `POST /api/image-analysis-management/{groupId}/assign`
  - `POST /api/image-analysis-management/release`
  - `POST /api/image-analysis-management/reassign`
- **Actions:** Enable service, Sync Stages, Rebuild Intake, Save Settings, Get Next, Assign, Release, Reassign, Open group, Refresh Metrics.

### Business Rules
- **File:** `Validation\BusinessRules.razor`
- **Route(s):** `/validation/rules`
- **Auth:** `Permissions.PagesValidationRules`
- **Function:** CRUD for business validation rules. Create, edit, toggle active, delete, with search and category/severity filters.
- **API Endpoints:**
  - `GET /api/BusinessRules`
  - `PUT /api/BusinessRules/{id}/status`
  - `POST /api/BusinessRules`
  - `PUT /api/BusinessRules/{id}`
  - `DELETE /api/BusinessRules/{id}`
- **Actions:** Create rule, edit, delete (confirm), toggle active, search/filter/clear.

### New Container Completeness Model
- **File:** `Validation\NewContainerCompletenessModel.razor`
- **Route(s):** `/validation/new-completeness-model`
- **Auth:** `Permissions.PagesValidationCompleteness`
- **Function:** Updated completeness view: stats plus expandable cargo group tables (non-consolidated/consolidated) with BOE request capability.
- **API Endpoints:**
  - `GET /api/containercompleteness/missing`
  - `GET /api/containercompleteness/complete`
  - `GET /api/consolidatedcargo/non-consolidated?...`
  - `GET /api/consolidatedcargo/consolidated?...`
  - `POST /api/containercompleteness/request-boe/{containerNumber}`
- **Actions:** Refresh, filter tabs/fields, BOE request.

---

## Scanners

### Scanner Overview
- **File:** `Scanners\ScannerOverview.razor`
- **Route(s):** `/scanners`
- **Auth:** `Permissions.PagesScannersView`
- **Function:** Overview of all three scanners (FS6000, ASE, Heimann Smith) with stats, recent activity timeline, and links to detail pages.
- **API Endpoints:**
  - `GET /api/FS6000/statistics`
  - `GET /api/FS6000/scans?...`
  - `GET /api/Ase/sync-status`
  - `GET /api/Ase/scans?...`
  - `GET /api/FS6000/stats?...`
  - `GET /api/Ase/stats`
- **Actions:** Refresh, View Details per scanner, tabbed shortcuts.

### FS6000 Scanner
- **File:** `Scanners\FS6000Scanner.razor`
- **Route(s):** `/scanners/fs6000`
- **Auth:** `Permissions.PagesScannersFs6000`
- **Function:** FS6000 health-style cards, statistics, and server-side MudDataGrid of scans with filters.
- **API Endpoints:**
  - `GET /api/FS6000/statistics`
  - `GET /api/FS6000/scans?...`
  - `GET /api/FS6000/stats?...`
- **Actions:** Refresh, search/clear grid filters, row View opens ContainerDetailsModal.

### ASE Scanner
- **File:** `Scanners\ASEScanner.razor`
- **Route(s):** `/scanners/ase`
- **Auth:** `Permissions.PagesScannersAse`
- **Function:** ASE sync/scan metrics, status tables, searchable grid, and 7-day volume chart.
- **API Endpoints:**
  - `GET /api/Ase/sync-status`
  - `GET /api/Ase/scans?...`
  - `GET /api/Ase/stats`
- **Actions:** Refresh, Search/Clear, View row in ContainerDetailsModal.

### Heimann Smith Scanner
- **File:** `Scanners\HeimannSmithScanner.razor`
- **Route(s):** `/scanners/heimann-smith`
- **Auth:** `Permissions.PagesScannersHeimann`
- **Function:** **PLACEHOLDER** -- integration pending. No API calls. Displays informational "not configured" tiles for API Endpoint, Data Pipeline, and Image Processing.
- **API Endpoints:** None.
- **Actions:** Read-only (breadcrumbs only).

---

## Images

### Image Viewer
- **File:** `Images\ImageViewer.razor`
- **Route(s):** `/images/viewer/{ContainerNumber}`
- **Auth:** `[Authorize]` (authenticated, no named policy)
- **Function:** Full-screen image viewer with zoom, rotate, marking, OCR/detection/quality overlays, and decision/rectangle saving.
- **API Endpoints:**
  - `GET /api/image/container/{ContainerNumber}`
  - `GET /api/image-analysis/{ContainerNumber}/ocr`
  - `GET /api/image-analysis/{ContainerNumber}/detect`
  - `GET /api/image-analysis/{ContainerNumber}/quality`
  - `GET /api/ImageAnalysisDecision/container/{ContainerNumber}`
  - `POST /api/ImageAnalysisDecision/rectangles`
  - `POST /api/ImageAnalysisDecision`
- **Actions:** Back, zoom, rotate, draw/mark, reset, download, print, save tags/decisions.

### Image Analysis (BL Review)
- **File:** `ImageAnalysis.razor`
- **Route(s):** `/image-analysis`
- **Auth:** `Permissions.PagesImageAnalysisView`
- **Function:** BL Review shell page with explainer cards and Quick Guide. Work is delegated to embedded `BLReviewList` component which uses `BLReviewService`.
- **API Endpoints (via BLReviewList/BLReviewService):**
  - `GET /api/BLReview/groups`
  - `GET /api/BLReview/details/{masterBlNumber}`
  - `POST /api/BLReview/save`
  - `GET /api/BLReview/history/{masterBlNumber}`
  - `GET /api/BLReview/statistics`
  - `GET /api/BLReview/container/completeness/{containerNumber}`
- **Actions:** How it Works, Quick Guide expand, search/filter/refresh/review flows via BLReviewList.

---

## Vehicles

### Vehicle List
- **File:** `Vehicles\VehicleList.razor`
- **Route(s):** `/vehicles`
- **Auth:** `Permissions.PagesVehiclesView`
- **Function:** Vehicle import table with stat cards, client-side search, pagination. Links to container detail pages.
- **API Endpoints:**
  - `GET /api/vehicleimport/search?page=1&pageSize=200`
- **Actions:** Client-side search, pagination, link to container details.

---

## Services (Backend)

### Ingestion
- **File:** `Services\Ingestion.razor`
- **Route(s):** `/services/ingestion`
- **Auth:** `Permissions.PagesServicesIngestion`
- **Function:** Pending ingestion files and service status. Per-file and bulk actions.
- **API Endpoints:**
  - `GET /api/ingestion/pending-files`
  - `GET /api/ingestion/service-status`
  - `POST /api/ingestion/process-file/{id}`
  - `POST /api/ingestion/reset-file-status/{id}`
  - `POST /api/ingestion/trigger`
- **Actions:** Refresh, Trigger Ingestion, Process per file, Reset per file.

### Access Review
- **File:** `Services\AccessReview.razor`
- **Route(s):** `/services/access-review`
- **Auth:** `Permissions.PagesServicesAccessReview`
- **Function:** User access review list with approve/revoke actions.
- **API Endpoints:**
  - `GET /api/accessreview/users?...`
  - `POST /api/accessreview/users/{userId}/approve`
  - `POST /api/accessreview/users/{userId}/revoke`
- **Actions:** Approve, Revoke, Refresh.

### FS6000 Completeness
- **File:** `Services\FS6000Completeness.razor`
- **Route(s):** `/services/fs6000-completeness`
- **Auth:** `Permissions.PagesServicesFs6000Completeness`
- **Function:** FS6000 image completeness stats and table of scans missing images.
- **API Endpoints:**
  - `GET /api/fs6000imagecompleteness/stats`
  - `GET /api/fs6000imagecompleteness/scans-without-images?limit=100`
- **Actions:** Refresh.

### ASE Sync
- **File:** `Services\AseSync.razor`
- **Route(s):** `/services/ase-sync`
- **Auth:** `Permissions.PagesServicesAseSync`
- **Function:** ASE sync status, history, and manual sync trigger.
- **API Endpoints:**
  - `GET /api/asesync/statistics`
  - `GET /api/asesync/history`
  - `POST /api/asesync/trigger`
- **Actions:** Trigger Sync Now (confirm dialog), Refresh Status.

### Performance Metrics
- **File:** `Services\PerformanceMetrics.razor`
- **Route(s):** `/services/performance-metrics`
- **Auth:** `Permissions.PagesServicesPerformanceMetrics`
- **Function:** API performance summary, percentiles, endpoint table, slowest endpoints.
- **API Endpoints:**
  - `GET /api/PerformanceMetrics`
  - `GET /api/PerformanceMetrics/slowest`
- **Actions:** Refresh Metrics.

### Debug
- **File:** `Services\Debug.razor`
- **Route(s):** `/services/debug`
- **Auth:** `Permissions.PagesServicesDebug`
- **Function:** Ad-hoc API tester (GET/POST to any user-entered path) plus system info tab. Development tool.
- **API Endpoints:**
  - Dynamic GET/POST to user-entered path
  - `GET /api/debug/system`
- **Actions:** Test API, Refresh System Info, tab switch.

### Consolidated Cargo
- **File:** `Services\ConsolidatedCargo.razor`
- **Route(s):** `/services/consolidated-cargo`
- **Auth:** `Permissions.PagesServicesConsolidatedCargo`
- **Function:** Search consolidated containers and navigate to container details.
- **API Endpoints:**
  - `GET /api/consolidatedcargo/containers?containerNumber=...`
- **Actions:** Search, View Details (navigate).

### Image Processing
- **File:** `Services\ImageProcessing.razor`
- **Route(s):** `/services/image-processing`
- **Auth:** `Permissions.PagesServicesImageProcessing`
- **Function:** Image search tab and processing statistics tab.
- **API Endpoints:**
  - `GET /api/imageprocessing?...`
  - `GET /api/imageprocessing/statistics`
- **Actions:** Search Images, View row, Refresh Statistics.

### Monitoring
- **File:** `Services\Monitoring.razor`
- **Route(s):** `/services/monitoring`
- **Auth:** `Permissions.PagesServicesMonitoring`
- **Function:** System health, background services table, DB statistics.
- **API Endpoints:**
  - `GET /api/monitoring/health/overview`
  - `GET /api/monitoring/health/services`
  - `GET /api/monitoring/database/statistics`
- **Actions:** Refresh on services toolbar.

### Gateway
- **File:** `Services\Gateway.razor`
- **Route(s):** `/services/gateway`
- **Auth:** `Permissions.PagesServicesGateway`
- **Function:** Container search with optional includes, global search, dashboard stats.
- **API Endpoints:**
  - `GET /api/gateway/container/{number}?...`
  - `GET /api/gateway/search?...`
  - `GET /api/gateway/dashboard/stats`
- **Actions:** Search container, global search, View Full Details/View on results.

### Diagnostics
- **File:** `Services\Diagnostics.razor`
- **Route(s):** `/services/diagnostics`
- **Auth:** `Permissions.PagesServicesDiagnostics`
- **Function:** Per-container diagnostics, system diagnostics, memory/GC diagnostics (tabs).
- **API Endpoints:**
  - `GET /api/Diagnostics/container/{n}`
  - `GET /api/Diagnostics/system`
  - `GET /api/MemoryDiagnostics/status`
  - `GET /api/MemoryDiagnostics/gc/stats`
  - `POST /api/MemoryDiagnostics/gc/collect?generation=2`
- **Actions:** Run container diagnosis, Run System Diagnostics, GC collect (memory tab).

### Database
- **File:** `Services\Database.razor`
- **Route(s):** `/services/database`
- **Auth:** `Permissions.PagesServicesDatabase`
- **Function:** DB connections browser, read-only query runner, table list.
- **API Endpoints:**
  - `GET /api/DatabaseAdmin/connections`
  - `GET /api/DatabaseAdmin/tables`
  - `POST /api/DatabaseAdmin/query`
- **Actions:** Execute Query, Clear, browse connections/tables.

---

## Administration

### System Settings
- **File:** `Administration\SystemSettings.razor`
- **Route(s):** `/admin/settings`
- **Auth:** `Permissions.PagesAdminSettings`
- **Function:** View/edit system configuration from database or appsettings.json. Per-category change history. Test ICUMS/Email connections. Shutdown/restart API and WebApp services.
- **API Endpoints:**
  - Via `SettingsService`: categories, values, bulk update, reset defaults, test connection, recent changes
  - Via `SettingsService`: app settings sections load/save
  - `POST /api/SystemAdmin/shutdown`
  - `POST /api/SystemAdmin/restart`
  - `POST /api/SystemAdmin/shutdown-webapp`
  - `POST /api/SystemAdmin/restart-webapp`
- **Actions:** Toggle DB vs appsettings, tabs, save/refresh/reset, test connection, view history dialog, shutdown/restart API/Web/All.

### Service Control Panel
- **File:** `Administration\ServiceControlPanel.razor`
- **Route(s):** `/admin/service-control`, `/administration/service-control`
- **Auth:** `Permissions.PagesAdminServiceControl`
- **Function:** Static catalog of background services merged with live status from API. Start/stop/restart per service, system info and performance, architecture/dependency views.
- **API Endpoints:**
  - `GET /api/SystemAdmin/services` (also on 15s timer)
  - `GET /api/SystemAdmin/system-info`
  - `GET /api/SystemAdmin/performance`
  - `POST /api/SystemAdmin/service/{name}/restart`
  - `POST /api/SystemAdmin/service/{name}/stop`
  - `POST /api/SystemAdmin/service/{name}/start`
- **Actions:** Refresh, Edit Config link, per-row Start/Stop/Restart/Retry/Details, system admin tab refresh.

### ICUMS Payload Viewer
- **File:** `Administration\IcumsPayloadViewer.razor`
- **Route(s):** `/admin/icums-payloads`
- **Auth:** `Permissions.PagesIcumsPayloads`
- **Function:** Browse ICUMS payload folder files, view JSON content with optional scan image and field validation, verify container status against ICUMS.
- **API Endpoints:**
  - `GET /api/IcumsPayload/summary`
  - `GET /api/IcumsPayload/list?subfolder=...`
  - `GET /api/IcumsPayload/read?fileName=...&subfolder=...`
  - `POST /api/IcumsPayload/verify-status`
  - Image: `GET /api/ImageProcessing/container/{containerNumber}/complete/image?size=full`
- **Actions:** Tabs, refresh, view payload detail dialog, verify status.

### Log Viewer
- **File:** `Administration\LogViewer.razor`
- **Route(s):** `/admin/logs`
- **Auth:** `Permissions.PagesAdminLogs`
- **Function:** Paginated application log browser with level, date range, and search filters.
- **API Endpoints:**
  - `GET /api/LogManagement/logs?...`
- **Actions:** Select level, date range, search, Apply, Previous/Next pagination.

### Users Management
- **File:** `Administration\UsersManagement.razor`
- **Route(s):** `/admin/users/management`
- **Auth:** `Permissions.PagesAdminUsers`
- **Function:** Full user CRUD: list users with stats, inline create/edit forms, password reset, activate/deactivate, delete.
- **API Endpoints:**
  - `GET /api/Users`
  - `POST /api/Users` (create)
  - `PUT /api/Users/{id}` (update, status toggle)
  - `POST /api/Users/{id}/reset-password`
  - `DELETE /api/Users/{id}`
  - Via `RoleLookupService`: `GetRolesAsync` (dropdown population)
- **Actions:** Create User, Refresh, edit, reset password (dialog), toggle active, delete (confirm), filterable grid.

### User List
- **File:** `Administration\UserList.razor`
- **Route(s):** `/admin/users`
- **Auth:** `Permissions.PagesAdminUsers`
- **Function:** User list with stats, search/role/status filters, and action menus.
- **API Endpoints:**
  - `GET /api/Users`
  - Via `RoleLookupService`: `GetRolesAsync`
  - Navigation: `/admin/users/{UserId}/activity`
- **Actions:** Add User (stub), filter/search, menu: Edit, Reset Password, View Activity, Activate/Deactivate, Delete.
- **Notes:** Most action menu items are snackbar-only stubs. `UsersManagement.razor` is the fully functional version.

### Roles Management
- **File:** `Administration\RolesManagement.razor`
- **Route(s):** `/admin/roles`
- **Auth:** `Permissions.PagesAdminRoles`
- **Function:** Role card grid with permission/user counts. Dialogs to create/edit roles, manage permissions, and delete non-system roles.
- **API Endpoints:**
  - `GET /api/Roles`
  - `GET /api/Roles/{id}/permissions`
  - `GET /api/Roles/{id}/users`
  - `DELETE /api/Roles/{id}?deletedBy=...`
  - Via `RoleLookupService`: cache clear and refresh
  - Create/Edit/ManagePermissions via dialogs (separate components)
- **Actions:** Create Custom Role, Manage Permissions, Edit, Delete (non-system only).

### Permissions Management
- **File:** `Administration\PermissionsManagement.razor`
- **Route(s):** `/admin/permissions`
- **Auth:** `Permissions.PagesAdminPermissions`
- **Function:** Lists permissions grouped by category with counts.
- **API Endpoints:**
  - `GET /api/Permissions/by-category`
- **Actions:** Refresh, Create Permission, Edit, Delete.
- **Notes:** Create/Edit/Delete are "coming soon" snackbar stubs -- no API calls.

### Audit Logs
- **File:** `Administration\AuditLogs.razor`
- **Route(s):** `/admin/audit`
- **Auth:** `Permissions.PagesAdminAudit`
- **Function:** Recent audit entries loaded from API, filtered client-side by event type, severity, and user.
- **API Endpoints:**
  - `GET /api/Audit?limit=100`
- **Actions:** Filter dropdowns and user text field, Filter button, paged grid.

---

## Reports

### Reports Dashboard
- **File:** `Reports\ReportsDashboard.razor`
- **Route(s):** `/reports`
- **Auth:** `Permissions.PagesReportsView`
- **Function:** Report cards with date/format filters. View loads JSON data, Export downloads binary file. Covers container summary, scanner performance, ICUMS activity, user activity, vehicle imports, validation summary.
- **API Endpoints:**
  - `GET /api/Reports/container-summary?...`
  - `GET /api/Reports/scanner-performance?...`
  - `GET /api/Reports/icums-activity?...`
  - `GET /api/Reports/user-activity?...`
  - `GET /api/Reports/vehicle-imports?...`
  - `GET /api/Reports/validation-summary?...`
  - Export variants: `GET /api/Reports/.../export?...`
- **Actions:** View, Export per report, Refresh All, date/format filters.

### Report Templates
- **File:** `Reports\ReportsPage.razor`
- **Route(s):** `/reports/templates`
- **Auth:** `Permissions.PagesReportsTemplates`
- **Function:** Static report template cards and custom report builder.
- **API Endpoints:** None.
- **Actions:** Generate, Export menu (Excel/PDF/CSV), Build Report.
- **Notes:** All generation/export actions are **snackbar-only stubs** -- no backend wired.

---

## Monitoring & Diagnostics

### Endpoint Usage
- **File:** `Monitoring\EndpointUsage.razor`
- **Route(s):** `/monitoring/endpoint-usage`
- **Auth:** `Permissions.PagesServicesMonitoring`
- **Function:** Deprecated endpoints, Phase 3 routes, safe-to-remove list, all endpoints summary, caller detail dialogs.
- **API Endpoints:**
  - `GET /api/monitoring/deprecated-endpoints/summary`
  - `GET /api/monitoring/phase3-routes/summary`
  - `GET /api/monitoring/safe-to-remove?...`
  - `GET /api/monitoring/all-endpoints/summary`
  - `GET /api/monitoring/endpoint-usage/{endpoint}/callers`
- **Actions:** Refresh/Export per tab, View Callers dialog.

### Permission Diagnostics
- **File:** `Diagnostics\PermissionDiagnostics.razor`
- **Route(s):** `/diagnostics/permissions`
- **Auth:** `Permissions.PagesAdminSettings`
- **Function:** Shows auth state, permission claims, manual and bulk permission tests, export diagnostics JSON.
- **API Endpoints:** None (uses `AuthenticationStateProvider` and `PermissionGuard` client-side).
- **Actions:** Test Permission, Test All Permissions, Refresh Diagnostics, Export Diagnostics.

---

## Notifications

### Notifications
- **File:** `Notifications\Notifications.razor`
- **Route(s):** `/notifications`
- **Auth:** `Permissions.PagesNotifications`
- **Function:** User notification list with filters (type, status, search) and bulk operations.
- **API Endpoints:**
  - `GET /api/Notifications/user/{username}?...`
  - `PUT /api/Notifications/...` (read/unread/read-all)
  - `DELETE /api/Notifications/...` (clear-read/clear-all)
- **Actions:** Mark All Read, Clear Read, Clear All, Refresh, search/type/status filters, per-notification actions.

---

## CMR

### CMR Validation
- **File:** `CMRValidation.razor`
- **Route(s):** `/cmr-validation`
- **Auth:** `Permissions.PagesCmrValidation`
- **Function:** Multi-tab CMR data quality: statistics, problematic records table, re-download queue with recent items and queue statistics. Supports queuing and queue maintenance operations.
- **API Endpoints:**
  - `GET /api/CMRValidation/statistics`
  - `GET /api/CMRValidation/problematic-records`
  - `GET /api/CMRValidation/queue-status`
  - `GET /api/CMRValidation/queue-statistics`
  - `POST /api/CMRValidation/queue-redownload`
  - `POST /api/CMRValidation/queue-batch-redownload`
  - `POST /api/CMRValidation/process-queue`
  - `POST /api/CMRValidation/clear-completed`
- **Actions:** Tab navigation, Refresh per tab, Queue for Re-download (row and bulk), Process Now, Clear Completed, timer-based refresh.

---

## Summary: Stubs & Placeholders

The following pages or features have actions that are **not wired to any backend API** (snackbar-only, mock data, or disabled):

| Page | Stub / Issue |
|------|-------------|
| **Dashboard (Index)** | KPI stat cards are hardcoded, not from live data |
| **Container Details** | `FetchICUMSData` is simulated with `Task.Delay` |
| **ICUMS Dashboard** | "Sync Queues" and "Retry Failed" buttons are snackbar-only |
| **Download Queue** | "Retry All Failed" only mutates local list, no API call |
| **Submission Queue** | "Retry All Failed" only mutates local list, no API call |
| **BOE Request** | Recent requests table uses random mock data; BOE/BL search types not implemented |
| **Completed Records** | Export and "Prepare ICUMS Submission" buttons are disabled |
| **Container Completeness** | ToggleService and restart buttons are UI-only placeholders |
| **Completeness Records** | Export button handler not wired |
| **User List** | Most action menu items are stubs (Edit, Reset Password, etc.) |
| **Permissions Management** | Create/Edit/Delete are "coming soon" stubs |
| **Report Templates** | All generation/export actions are snackbar-only stubs |
| **Heimann Smith Scanner** | Entire page is a placeholder -- no backend integration |

---

## Summary: Duplicate / Overlapping Pages

| Pages | Overlap |
|-------|---------|
| **User List** (`/admin/users`) vs **Users Management** (`/admin/users/management`) | Both list users with the same `Permissions.PagesAdminUsers` policy. `UsersManagement` is the fully functional CRUD version; `UserList` is a read-heavy view with stubbed actions. |
| **Container Completeness** (`/validation`) vs **New Completeness Model** (`/validation/new-completeness-model`) | Both show missing/complete stats and cargo groups with BOE request capability. The "new" model appears to be an updated version of the same concept. |
| **Error Monitor** (`/operations/errors`) vs **Log Viewer** (`/admin/logs`) | Both browse application logs from `/api/LogManagement/logs`. Error Monitor adds 24h statistics and live mode; Log Viewer is simpler with level/date/search filters. |
| **Performance** (`/performance`) vs **Performance Metrics** (`/services/performance-metrics`) | Both show performance data. `/performance` shows summary + metrics from `/api/Performance/...`; `/services/performance-metrics` shows endpoint-level data from `/api/PerformanceMetrics/...`. Different API sources but conceptually overlapping. |

---

*End of report.*
