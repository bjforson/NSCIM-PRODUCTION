# Deep Service Analysis — Unique Task Verification

**Date:** 2026-03-19  
**Scope:** All 27 active hosted services  
**Method:** Traced actual DB tables read/written, external API calls, file I/O, and functional overlap per service

---

## FINDING 1: ICUMS JsonIngestion — DUPLICATE WORK

**Severity: MEDIUM**

| Service | What it does |
|---------|-------------|
| **IcumPipelineOrchestratorService** (RunJsonIngestionWorkflowAsync) | Calls `GetPendingFilesAsync()` every adaptive cycle. Logs count. **Does not process files.** |
| **IcumJsonIngestionService** | Calls `GetPendingFilesAsync()` every 1 minute. **Actually processes** files: parses JSON, creates BOEDocuments, ManifestItems, archives files. |

**Overlap:** Both services call `GetPendingFilesAsync()` on the same `DownloadedFiles` table every cycle. The orchestrator's call is wasted — it's a placeholder that was never completed.

**Impact:** Extra DB query every 30 seconds. No functional harm but confusing in logs and wastes a DB round-trip.

**Recommendation:** Remove `RunJsonIngestionWorkflowAsync` from the orchestrator's cycle, or skip it entirely — add a comment that ingestion is handled by the standalone `IcumJsonIngestionService`.

---

## FINDING 2: ICUMSMetricsCollectorService — READ-ONLY DUPLICATES (ACCEPTABLE)

**Severity: LOW**

| Shared Query | Also called by |
|-------------|----------------|
| `GetPendingFilesAsync()` | IcumPipelineOrchestratorService (work count), IcumJsonIngestionService |
| `GetQueueStatisticsAsync()` | IcumPipelineOrchestratorService (work count) |
| `GetPendingRetriesAsync()` | FailedFileRetryService |

**Analysis:** MetricsCollector runs these queries every 1 minute for gauge updates. The orchestrator runs the same queries for adaptive polling. Both are read-only.

**Impact:** Extra read queries. In PostgreSQL with connection pooling, this is negligible.

**Recommendation:** Acceptable as-is. If optimization is desired, the orchestrator could expose its work counts and MetricsCollector could read from those instead of re-querying.

---

## FINDING 3: ContainerCompletenessStatuses — THREE WRITERS

**Severity: LOW (by design)**

| Service | When it writes | What it updates |
|---------|---------------|-----------------|
| **ContainerCompletenessOrchestratorService** (CompletenessCheck) | Every 2–5 min | Creates new records from queue; re-checks existing (HasICUMSData, Status) |
| **ContainerCompletenessOrchestratorService** (PostICUMSValidation) | Every 2–5 min | Updates multi-container records: Status → Complete/Complete-CrossRecord |
| **ContainerStatusReconciliationService** | Every 6 hours | Corrects: Missing → Complete when BOE data exists |

**Analysis:** All three update `ContainerCompletenessStatuses` but for **different populations**:
- CompletenessCheck: new scans from queue + periodic re-check of ALL records
- PostICUMSValidation: only multi-container scans needing cross-record tracking
- Reconciliation: only records stuck in "Missing" status despite BOE data existing

**Overlap concern:** CompletenessCheck Step 2 (re-check existing) and Reconciliation both fix "Missing" records when BOE exists. However:
- CompletenessCheck re-checks records based on age (Complete: >24h, Missing: >1h, max 90 days)
- Reconciliation checks ALL Missing records regardless of age

**Impact:** A container could be updated by both services. Updates are idempotent (Missing → Complete), so no corruption risk.

**Recommendation:** Acceptable as defense-in-depth. Document that Reconciliation is the safety net for cases CompletenessCheck misses.

---

## FINDING 4: Orchestrator DataMapping & BOESelectivity — DEAD CODE

**Severity: LOW**

| Workflow | Status |
|----------|--------|
| `RunDataMappingWorkflowAsync` | Only calls `CanConnectAsync()` on both DBs. No actual logic. |
| `RunBOESelectivityWorkflowAsync` | Only calls `CanConnectAsync()` on both DBs. No actual logic. |

**Analysis:** These are stubs that contribute to the adaptive polling cycle but do no work. They run every cycle, each performing 2 DB connectivity checks.

**Impact:** 4 wasted `SELECT 1` queries per cycle (every 30–60 seconds).

**Recommendation:** Either implement the logic or remove the stubs and their work-count helpers.

---

## FINDING 5: Dashboard Broadcasts — SHARED TABLE QUERIES (ACCEPTABLE)

**Severity: LOW**

| Shared Table | ImageAnalysisDashboard (10s) | ComprehensiveDashboard (60s) |
|-------------|------------------------------|------------------------------|
| `ContainerCompletenessStatuses` | Workflow stage distribution, data integrity alerts | Pipeline stages, completeness counts |
| `Users` + `Roles` | Analyst/auditor readiness | User activity, RBAC stats |
| `BOEDocuments` | Data integrity (GroupIdentifier) | Table counts only |

**Analysis:** Both dashboards query these tables but extract **different projections** for **different UIs**:
- Image Analysis dashboard: workflow stages (Ready, AnalystAssigned, etc.), assignment utilization, data integrity
- Comprehensive dashboard: system-wide stats (scanner counts, ICUMS pipeline, queue depths)

**Impact:** Repeated queries on `ContainerCompletenessStatuses` (every 10s + every 60s). With typical table sizes (<10k rows), impact is negligible.

**Recommendation:** Acceptable. If table grows large, consider a shared cache (e.g. `IMemoryCache` with 10s TTL) that both services read from.

---

## FINDING 6: Health Check vs Dashboard vs Performance Monitoring — NO OVERLAP

**Severity: NONE**

| Service | What it tracks | Storage |
|---------|---------------|---------|
| **ComprehensiveHealthCheckService** | Service alive/dead, DB connectivity | In-memory (`_serviceStatuses`) |
| **PerformanceMonitoringService** | CPU, memory, GC, threads, DB response time | In-memory (`ConcurrentDictionary`) |
| **ComprehensiveDashboardService** | Business metrics (scan counts, queue depths, errors) | None (queries on-demand) |
| **ErrorMonitoringBackgroundService** | Application log errors | `ErrorInvestigations`, `FixProposals` tables |

**Analysis:** Each serves a distinct purpose:
- Health check: "Is it alive?"
- Performance: "How fast is it?"
- Dashboard: "What has it processed?"
- Error monitoring: "What went wrong?"

No functional overlap. Different tables, different metrics, different consumers.

---

## FINDING 7: QueueRecoveryService — UNIQUE ROLE

**Severity: NONE**

| Service | Table | Purpose |
|---------|-------|---------|
| **QueueRecoveryService** | `FS6000Scans`, `AseScans` → `ContainerScanQueues` | Finds scans that were NEVER queued |
| **ContainerCompletenessService** | `ContainerScanQueues` → `ContainerCompletenessStatuses` | Processes queued items |

**Analysis:** QueueRecovery operates at the **scanner → queue** boundary. ContainerCompleteness operates at the **queue → status** boundary. Completely different layers. No overlap.

---

## FINDING 8: DuplicateDownloadMonitoringService — UNIQUE ROLE

**Severity: NONE**

Reads `boedocuments` for duplicate (ContainerNumber + DeclarationNumber) combinations. No other service performs this check. Writes to `icumsdownloadaudit` when duplicates found.

---

## FINDING 9: Endpoint Usage — COMPLEMENTARY PAIR

**Severity: NONE**

| Service | Role |
|---------|------|
| **EndpointUsageBufferService** | Batches per-request records → bulk INSERT |
| **EndpointUsageCleanupBackgroundService** | Deletes records older than retention |

Same table (`EndpointUsageLog`), opposite operations (write vs delete). Complementary by design.

---

## FINDING 10: All Other Services — UNIQUE

| Service | Unique Role |
|---------|-------------|
| **FS6000BackgroundService** | Startup diagnostics + starts FileSync and Ingestion |
| **AseBackgroundService** | Syncs from external SQL Server |
| **FailedFileRetryService** | Retries failed ICUMS files (reads `FailedProcessingQueue`) |
| **IcumFileArchiveService** | Compresses old ICUMS files (reads `ArchivedFiles`) |
| **ICUMSSubmissionService** | Submits to ICUMS API (reads `ICUMSSubmissionQueues`) |
| **CMRRedownloadBackgroundService** | Re-downloads failed CMR documents |
| **CMRMetricsRecorderService** | Records CMR validation metrics hourly |
| **ImageAnalysisOrchestratorService** | Intake/Assignment/Submission/Housekeeping for image analysis |
| **UserReadinessSyncService** | Syncs SignalR state → DB |
| **AccessReviewService** | ISO 27001 quarterly access review |
| **DailyDataQualityReportService** | Daily email report at 8 AM |
| **ServiceLifecycleStartupService** | One-time service discovery |
| **ServiceOrchestratorBackgroundService** | Heartbeat + shutdown logging |

Each uses unique tables and/or unique external systems. No overlapping work.

---

## SUMMARY

| # | Finding | Severity | Action Needed |
|---|---------|----------|---------------|
| 1 | Orchestrator's JsonIngestion placeholder duplicates `GetPendingFilesAsync` query | MEDIUM | Remove placeholder |
| 2 | MetricsCollector re-runs same read queries as orchestrator | LOW | Acceptable |
| 3 | Three services write `ContainerCompletenessStatuses` | LOW | By design (defense-in-depth) |
| 4 | DataMapping + BOESelectivity stubs do nothing but `SELECT 1` | LOW | Remove stubs or implement |
| 5 | Both dashboards query `ContainerCompletenessStatuses` | LOW | Acceptable |
| 6 | Health/Performance/Dashboard/ErrorMonitor — no overlap | NONE | — |
| 7 | QueueRecovery is unique | NONE | — |
| 8 | DuplicateDownloadMonitor is unique | NONE | — |
| 9 | EndpointUsage pair is complementary | NONE | — |
| 10 | All remaining services are unique | NONE | — |

---

## VERDICT

**25 out of 27 services perform entirely unique tasks.** The two issues are:

1. **IcumPipelineOrchestratorService.RunJsonIngestionWorkflowAsync** — placeholder that duplicates a query without doing work. Should be removed.
2. **DataMapping + BOESelectivity stubs** — dead code running `SELECT 1` every cycle. Should be removed or implemented.

No service performs the same end-to-end task as another. The system is well-structured with intentional redundancy (safety nets) in the container completeness domain.
