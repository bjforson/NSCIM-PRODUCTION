# Per-Service Analysis — NickScan Central Imaging Portal API

**Date:** 2026-03-19  
**Scope:** All active background/hosted services (33 total)

---

## Legend

| Symbol | Meaning |
|--------|---------|
| **REG** | Registered and active |
| **SCOPE** | Scoped (invoked by orchestrator) |
| **LEGACY** | Commented out / replaced |

---

## 1. LIFECYCLE & COORDINATION

### 1.1 ServiceLifecycleStartupService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | IHostedService (one-time) |
| **Interval** | Runs once at startup |
| **Purpose** | Discovers all IHostedService instances, registers those implementing IManagedService with ServiceLifecycleManager for status/control UI |
| **Dependencies** | IServiceProvider, IServiceLifecycleManager |
| **Output** | Logs "Found X hosted services" |

**Analysis:** Startup discovery only. No ongoing work. Required for Service Control Panel.

---

### 1.2 ServiceOrchestratorBackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Heartbeat every 60 minutes |
| **Purpose** | Logs startup sequence, heartbeat, and graceful shutdown coordination. Does NOT start/stop other services — .NET handles that |
| **Dependencies** | IServiceProvider |
| **Output** | Informational logs |

**Analysis:** Purely informational. No coordination logic. Could be removed if logs are not needed.

---

## 2. SCANNER SERVICES

### 2.1 FS6000BackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Runs FileSyncService.StartSyncAsync() and IngestionService.StartIngestionAsync() — both run continuously |
| **Purpose** | Orchestrator for FS6000: runs startup diagnostics, then starts FileSyncService (copy from Z:\23301FS01 to staging) and IngestionService (parse XML → DB, queue) |
| **Dependencies** | IFileSyncService, IIngestionService, FileSyncConfiguration |
| **Config** | FS6000:FileSync:SourceDirectory, DestinationDirectory, SyncIntervalMinutes |

**Analysis:** Single entry point for FS6000. FileSyncService and IngestionService run in same scope. Diagnostics gate startup.

---

### 2.2 AseBackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | IHostedService |
| **Interval** | ASE:SyncIntervalMinutes (default 15) |
| **Purpose** | Syncs scan records from external ASE SQL Server (10.0.0.3) to PostgreSQL. Queries InspectionCore, publishes to ContainerScanQueue |
| **Dependencies** | IAseDatabaseSyncService, AseConfiguration |
| **Config** | ASE:ConnectionString, SyncIntervalMinutes, StartDate |

**Analysis:** Standalone scanner sync. No orchestration. Requires NICKSCAN_ASE_PASSWORD env var.

---

## 3. ICUMS PIPELINE

### 3.1 IcumPipelineOrchestratorService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Adaptive (min 30s). Workflows: FileScanner, DownloadQueue, JsonIngestion, DataTransfer (adaptive), BackgroundService (every 30 min) |
| **Purpose** | Coordinates: (1) File scanner for new JSON files, (2) Download queue for individual container downloads, (3) JsonIngestion placeholder, (4) Data transfer ICUMS_Downloads → NS_CIS, (5) Batch download from UNIPASS API |
| **Dependencies** | IIcumApiService, IIcumDownloadsRepository, IICUMSDownloadQueueRepository, ICUMSConfigurationProvider |
| **Config** | IcumBackgroundService:Enabled (batch), BatchIntervalMinutes |

**Analysis:** JsonIngestion workflow is a placeholder — checks pending files but does not process. Actual ingestion is done by IcumJsonIngestionService. Other workflows (FileScanner, DownloadQueue, DataTransfer, BackgroundService) are implemented.

---

### 3.2 IcumJsonIngestionService
| Attribute | Value |
|-----------|-------|
| **Status** | REG (Program.cs) |
| **Type** | BackgroundService |
| **Interval** | 1 minute (from settings). Startup delay 45s |
| **Purpose** | Parses JSON files from ICUMS Downloads (BatchData, ContainerData, etc.), creates BOEDocuments, manifests, vehicles. Archives processed files |
| **Dependencies** | IIcumDownloadsRepository, IICUMSDownloadQueueRepository, ICUMSConfigurationProvider |
| **Config** | IcumJsonIngestionService:ProcessIntervalMinutes, StartupDelaySeconds |

**Analysis:** Full ingestion logic (~2000 lines). Runs independently of orchestrator. Orchestrator's JsonIngestion step is redundant.

---

### 3.3 FailedFileRetryService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 5 minutes (ICUMS:FailedFileRetry:IntervalMinutes) |
| **Purpose** | Retries files in Failed status. Dead-letter queue with exponential backoff |
| **Dependencies** | IIcumDownloadsRepository, ICUMSMetrics |
| **Config** | ICUMS:FailedFileRetry:IntervalMinutes, MaxRetriesPerCycle |

**Analysis:** Support service for ICUMS. No overlap with orchestrator.

---

### 3.4 ICUMSMetricsCollectorService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 1 minute (ICUMS:Metrics:CollectionIntervalMinutes) |
| **Purpose** | Updates ICUMS gauges: queue depth, pending files, throughput |
| **Dependencies** | IIcumDownloadsRepository, IICUMSDownloadQueueRepository, ICUMSMetrics |
| **Config** | ICUMS:Metrics:CollectionIntervalMinutes |

**Analysis:** Metrics only. No overlap.

---

### 3.5 IcumFileArchiveService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Every 6 hours |
| **Purpose** | Archives processed ICUMS files (24h+ old) to organized archive structure. Optional compression |
| **Dependencies** | IIcumDownloadsRepository, ICUMSConfigurationProvider |
| **Config** | Hardcoded: 6h interval, 24h archive-after |

**Analysis:** Housekeeping. No overlap.

---

### 3.6 IcumBackgroundService (LEGACY)
| Attribute | Value |
|-----------|-------|
| **Status** | LEGACY — commented out |
| **Purpose** | Batch download from UNIPASS API. Replaced by IcumPipelineOrchestratorService.RunBackgroundServiceWorkflowAsync |

---

### 3.7 ICUMSDownloadBackgroundService (LEGACY)
| Attribute | Value |
|-----------|-------|
| **Status** | LEGACY — commented out |
| **Purpose** | Individual container downloads. Replaced by DownloadQueue workflow in orchestrator |

---

### 3.8 IcumFileScannerService (LEGACY)
| Attribute | Value |
|-----------|-------|
| **Status** | LEGACY — commented out |
| **Purpose** | Scanned for new JSON files. Replaced by FileScanner workflow in orchestrator |

---

### 3.9 IcumDataTransferService (LEGACY)
| Attribute | Value |
|-----------|-------|
| **Status** | LEGACY — commented out |
| **Purpose** | Transferred BOE data from ICUMS_Downloads to NS_CIS. Replaced by DataTransfer workflow in orchestrator |

---

## 4. CONTAINER COMPLETENESS

### 4.1 ContainerCompletenessOrchestratorService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Adaptive (2–5 min). Workflows: CompletenessCheck, DataMapping, BOESelectivity, PostICUMSValidation |
| **Purpose** | Coordinates: (1) Consume ContainerScanQueue, (2) Re-check existing containers, (3) Manual BOE selectivity, (4) Post-ICUMS multi-container validation |
| **Dependencies** | IContainerCompletenessService, IContainerDataMapperService, IManualBOESelectivityService, PostICUMSValidationService |
| **Config** | ContainerCompletenessOrchestratorService:CheckIntervalMinutes |

**Analysis:** Full orchestration. All workflows implemented. Uses scoped ContainerCompletenessService for core logic.

---

### 4.2 ContainerCompletenessService
| Attribute | Value |
|-----------|-------|
| **Status** | SCOPE — invoked by orchestrator |
| **Type** | IContainerCompletenessService (scoped) |
| **Purpose** | Core logic: consume queue, match scans to ICUMS, update ContainerCompletenessStatuses, publish to queue |
| **Dependencies** | IContainerScanQueueRepository, IIcumDownloadsRepository, IContainerScanQueuePublisher |

**Analysis:** Not a hosted service. Logic only. Orchestrator invokes it.

---

### 4.3 QueueRecoveryService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Every 2 hours |
| **Purpose** | Safety net: scans FS6000Scans and AseScans for records NOT in ContainerScanQueue; publishes missed scans |
| **Dependencies** | ApplicationDbContext, IContainerScanQueueRepository, IContainerScanQueuePublisher |
| **Config** | GoLiveOptions for date cutoff |

**Analysis:** Independent. Catches scans that were never queued (e.g. scanner published before queue was ready).

---

### 4.4 ContainerStatusReconciliationService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Every 6 hours |
| **Purpose** | Fixes stale "Missing" status: finds ContainerCompletenessStatuses with Status=Missing but BOE data exists; updates to Complete |
| **Dependencies** | ApplicationDbContext, IIcumDownloadsRepository |

**Analysis:** Safety net for race conditions. No overlap with orchestrator.

---

### 4.5 ICUMSSubmissionService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 10 minutes (from settings) |
| **Purpose** | Submits completed scan results to ICUMS (external API) |
| **Dependencies** | IIcumApiService, submission queue |
| **Config** | ICUMS:SubmissionIntervalMinutes |

**Analysis:** Standalone. Not part of ContainerCompletenessOrchestratorService.

---

## 5. IMAGE ANALYSIS

### 5.1 ImageAnalysisOrchestratorService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Adaptive. Workflows: Bootstrapper (once), Intake, Assignment, Submission, Housekeeping |
| **Purpose** | Consolidates: (1) Bootstrap, (2) Intake — create AnalysisGroups from complete ContainerCompletenessStatuses, (3) Assignment — auto-assign when Mode=Auto, (4) Submission — submit results, (5) Housekeeping — reclaim expired leases |
| **Dependencies** | ApplicationDbContext, IContainerCompletenessService, ReadyGroupsCacheService, AdaptivePollingHelper |
| **Config** | ImageAnalysis:Enabled, AssignmentMode |

**Analysis:** Full consolidation. Individual workers (IntakeWorker, AssignmentWorker, etc.) are commented out.

---

### 5.2 UserReadinessSyncService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 30 seconds |
| **Purpose** | Syncs SignalR user readiness state (UserReadinessStateProvider) to database for persistence |
| **Dependencies** | ApplicationDbContext |

**Analysis:** Support for SignalR. Syncs real-time state to DB for cross-session persistence.

---

## 6. BROADCAST & DASHBOARD

### 6.1 ImageAnalysisDashboardBroadcastService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 10 seconds |
| **Purpose** | Broadcasts Image Analysis Dashboard data to ImageAnalysisDashboardHub: workflow stages, assignments, throughput, alerts |
| **Dependencies** | IHubContext<ImageAnalysisDashboardHub>, ApplicationDbContext, IcumDownloadsDbContext |
| **Hub** | /hubs/imageAnalysisDashboard |

**Analysis:** Real-time UI for Image Analysis workflow. No overlap with DashboardBroadcastService.

---

### 6.2 DashboardBroadcastService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 30–60 seconds (from settings) |
| **Purpose** | Broadcasts comprehensive dashboard data via IComprehensiveDashboardService to ComprehensiveDashboardHub |
| **Dependencies** | IComprehensiveDashboardService, IHubContext<ComprehensiveDashboardHub> |
| **Hub** | /hubs/comprehensive-dashboard |

**Analysis:** System-wide dashboard. Different hub and data than Image Analysis.

---

## 7. MONITORING & HEALTH

### 7.1 ComprehensiveHealthCheckService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 5 minutes (CheckIntervalMinutes) |
| **Purpose** | Runs 13 health checks: Database, FileSystem, Network, FS6000, ASE, ICUMS, ImageProcessing, FileSync, Ingestion, WebAPI, WebApp, SystemResources, ImageAnalysisOrchestrator |
| **Dependencies** | IServiceProvider, IConfiguration |
| **Config** | ComprehensiveHealthCheck:CheckIntervalMinutes, StartupDelaySeconds |

**Analysis:** Central health monitoring. Updates in-memory status for /health and SignalR.

---

### 7.2 ErrorMonitoringBackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 2 minutes (from settings) |
| **Purpose** | Queries ApplicationLogs for new errors, groups similar ones, triggers ErrorInvestigationService |
| **Dependencies** | ApplicationDbContext, IErrorInvestigationService, ISettingsProvider |
| **Config** | ErrorMonitoring:CheckIntervalMinutes, LookbackMinutes |

**Analysis:** Error detection and investigation. Depends on ApplicationLogs table.

---

### 7.3 DuplicateDownloadMonitoringService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 30 minutes |
| **Purpose** | Layer 6 defense: checks boedocuments for duplicate (ContainerNumber, DeclarationNumber) in last 24h; raises alerts |
| **Dependencies** | IIcumDownloadsRepository, IcumDownloadsDbContext |
| **Config** | Hardcoded 30 min |

**Analysis:** Data integrity monitoring. No overlap.

---

### 7.4 EndpointUsageBufferService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService + IEndpointUsageBufferService |
| **Interval** | Flush every 10 seconds (Monitoring:EndpointUsageFlushIntervalSeconds) |
| **Purpose** | Buffers endpoint usage records. Flushes to DB in batches. Reduces per-request INSERT load |
| **Dependencies** | IServiceScopeFactory, ApplicationDbContext |
| **Config** | Monitoring:EndpointUsageFlushIntervalSeconds, EndpointUsageBatchSize |

**Analysis:** Middleware enqueues; this service flushes. Singleton + HostedService.

---

### 7.5 EndpointUsageCleanupBackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Cleanup every 24 hours; checks every hour |
| **Purpose** | Deletes endpoint usage logs older than retention (default 90 days) |
| **Dependencies** | IEndpointUsageService |
| **Config** | Monitoring:EndpointUsageRetentionDays |

**Analysis:** Housekeeping. Complements EndpointUsageBufferService.

---

### 7.6 PerformanceMonitoringService
| Attribute | Value |
|-----------|-------|
| **Status** | REG (AddScoped + AddHostedService) |
| **Type** | BackgroundService, IPerformanceMonitoringService |
| **Interval** | Collect every 30 seconds |
| **Purpose** | Collects: system resources, DB performance, memory, background service metrics, API performance. Exposes via IPerformanceMonitoringService for API |
| **Dependencies** | IMemoryCache, IServiceProvider |

**Analysis:** Dual registration: hosted instance runs collection loop; scoped instance used by PerformanceController for reads.

---

## 8. CMR & VALIDATION

### 8.1 CMRRedownloadBackgroundService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 5 minutes |
| **Purpose** | Processes CMR re-download queue. Re-downloads CMR documents for failed/invalid containers |
| **Dependencies** | ICMRRedownloadService |
| **Config** | CMRRedownloadBackgroundService:ProcessIntervalMinutes |

**Analysis:** CMR-specific. No overlap.

---

### 8.2 CMRMetricsRecorderService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | 60 minutes |
| **Purpose** | Records CMR validation metrics (total, valid, invalid, success rate) to database |
| **Dependencies** | ICMRValidationService, ICMRRedownloadService |
| **Config** | CMRMetricsRecorderService:IntervalMinutes |

**Analysis:** Metrics only. No overlap.

---

## 9. COMPLIANCE & REPORTING

### 9.1 AccessReviewService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Quarterly (90 days). Checks daily |
| **Purpose** | ISO 27001: Performs access review (excessive permissions, inactive users, no recent login) |
| **Dependencies** | IUserRepository, IPermissionService |
| **Config** | AccessReview:Enabled |

**Analysis:** Compliance. Runs quarterly. Logs warnings for users with issues.

---

### 9.2 DailyDataQualityReportService
| Attribute | Value |
|-----------|-------|
| **Status** | REG |
| **Type** | BackgroundService |
| **Interval** | Daily at 8:00 AM |
| **Purpose** | Generates and sends daily data quality report via email |
| **Dependencies** | IEmailService, IReportsService |
| **Config** | DailyDataQualityReportService:Enabled, Email settings |

**Analysis:** Reporting. Requires Email:Enabled and SMTP config.

---

## 10. SUMMARY TABLE

| # | Service | Domain | Interval | Status |
|---|---------|--------|----------|--------|
| 1 | ServiceLifecycleStartupService | Lifecycle | Once | REG |
| 2 | ServiceOrchestratorBackgroundService | Lifecycle | 60 min | REG |
| 3 | FS6000BackgroundService | Scanner | Continuous | REG |
| 4 | AseBackgroundService | Scanner | 15 min | REG |
| 5 | IcumPipelineOrchestratorService | ICUMS | Adaptive | REG |
| 6 | IcumJsonIngestionService | ICUMS | 1 min | REG |
| 7 | FailedFileRetryService | ICUMS | 5 min | REG |
| 8 | ICUMSMetricsCollectorService | ICUMS | 1 min | REG |
| 9 | IcumFileArchiveService | ICUMS | 6 hours | REG |
| 10 | ContainerCompletenessOrchestratorService | Completeness | Adaptive | REG |
| 11 | QueueRecoveryService | Completeness | 2 hours | REG |
| 12 | ContainerStatusReconciliationService | Completeness | 6 hours | REG |
| 13 | ICUMSSubmissionService | Completeness | 10 min | REG |
| 14 | ImageAnalysisOrchestratorService | Image Analysis | Adaptive | REG |
| 15 | UserReadinessSyncService | Image Analysis | 30 sec | REG |
| 16 | ImageAnalysisDashboardBroadcastService | Broadcast | 10 sec | REG |
| 17 | DashboardBroadcastService | Broadcast | 30–60 sec | REG |
| 18 | ComprehensiveHealthCheckService | Monitoring | 5 min | REG |
| 19 | ErrorMonitoringBackgroundService | Monitoring | 2 min | REG |
| 20 | DuplicateDownloadMonitoringService | Monitoring | 30 min | REG |
| 21 | EndpointUsageBufferService | Monitoring | 10 sec | REG |
| 22 | EndpointUsageCleanupBackgroundService | Monitoring | 24 hours | REG |
| 23 | PerformanceMonitoringService | Monitoring | 30 sec | REG |
| 24 | CMRRedownloadBackgroundService | CMR | 5 min | REG |
| 25 | CMRMetricsRecorderService | CMR | 60 min | REG |
| 26 | AccessReviewService | Compliance | 90 days | REG |
| 27 | DailyDataQualityReportService | Reporting | Daily 8AM | REG |

**Total active: 27 hosted services**
