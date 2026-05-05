# 08 — Observability + Logging

**Audit:** NSCIM v1 cold audit, 2026-05-05
**Agent:** Observability + logging
**Scope:** Read-only review of every observability surface in the v1 stack — log
configuration per service, log-call discipline, CorrelationId/TraceId
propagation, health checks, BackgroundService visibility, dashboard truthfulness,
metrics, error surfacing, alerting, audit trails, performance signals, log
volume vs signal, restart visibility, time-zone discipline.

> Probed live 2026-05-05 against running NSCIM_API + NSCIM_WebApp + the five
> Postgres DBs. Probe sources: `C:\temp\nscim-probe\ObsProbe.cs`,
> `ObsProbe2.cs`, `ObsProbe3.cs` (added under this audit). Log files sampled
> from `C:\Shared\NSCIM_PRODUCTION\Data\Logs\` (live Serilog file sink) and
> queried `applicationlogs` (Postgres sink) directly.

## Diagnose-from-logs feasibility verdict

**PARTIAL.** The Serilog file sink at `Data\Logs\nickscan-{date}.txt` is healthy,
exhaustive, and well-structured for the API process — most of the cargo-pipeline
diagnoses CAN be done from log alone, IF you SSH onto the box. But **every other
observability surface that operators expect to use first is broken or wrong**:

- `applicationlogs` PG sink (the Warning+ DB log table that backs the in-app
  Log Management UI) has been **silently dead for 9 days** — RLS rejects every
  insert because the Serilog sink never sets `app.tenant_id`.
- `LogManagementController` (`/admin/logs`) returns **0 rows** for the same
  reason — RLS-blind reads.
- `ErrorMonitoringBackgroundService` has been **blind for 9 days** — it queries
  `applicationlogs` for new errors and gets nothing back.
- The `/health-ui` aggregator is throwing FileNotFoundException for `IdentityModel`
  every minute (1,326 errors/day in the file sink).
- NSCIM_WebApp / NickHR.WebApp / NickERP.Portal / NickComms.Gateway / NickFinance.WebApp
  have **no Serilog file sink at all**. WebApp logs are vanishing into a
  closed `webapp-stderr.txt` (last write 2026-03-21).
- Background-worker logs lack CorrelationId enrichment.

So an operator who does `tail -f nickscan-{today}.txt` on the box can diagnose
almost anything related to NSCIM_API. An operator who tries the in-app log UI,
the dashboard panels, or any other service — can diagnose nothing.

Operator-stated frustration ("production issues take too long to diagnose") is
fully consistent with this state.

## Scope confirmation

This pass exhaustively documents (a) Serilog and `Microsoft.Extensions.Logging`
configuration in all 7 services, (b) log destinations including the Postgres
`applicationlogs` sink, (c) the CorrelationId middleware + outbound-handler
chain and where it carries vs drops, (d) every health-check registration plus
downstream probe behaviour, (e) BackgroundService lifecycle/iteration log
discipline, (f) the SignalR dashboard hub data shape vs ground truth, (g)
metrics / counter surfaces, (h) error visibility paths, (i) alerting (none),
(j) audit-log tables, their writers, and population, (k) performance signals,
(l) log volume at production volumes, (m) restart visibility, (n) time-zone
discipline.

Out of scope: whether the underlying business operations are correct (other
agents own that). v2 trees. NickHR/NickFinance internals beyond their
log-config surface.

## Findings

| ID | Severity | File:Line | Issue | Evidence | Proposed Fix | Effort | Risk |
|---|---|---|---|---|---|---|---|
| 8.01 | **P1** | runtime / db | `applicationlogs` Postgres sink dead since 2026-04-25 19:53:21 — RLS rejects every Serilog insert because the PG sink uses raw Npgsql (not the EF interceptor) and never sets `app.tenant_id`; `tenant_isolation_applicationlogs` policy forces `tenant_id = COALESCE(current_setting('app.tenant_id'), '0')::bigint`. The sink has been silent for 9 days. | `ObsProbe2.cs` SECTION 2: `INSERT REJECTED: 42501: new row violates row-level security policy for table "applicationlogs"` from `nscim_app` without `app.tenant_id`. `ObsProbe.cs` 1B: `applicationlogs newest = 2026-04-25 19:53:21`. Policy at `pg_policy.tenant_isolation_applicationlogs` qual `(tenant_id = ...::bigint)`. | Remove RLS from `applicationlogs` (it's an ops table, not tenant data — currently single-tenant prod anyway). Alt: grant `nscim_app` BYPASSRLS, or extend Serilog PG sink config to issue `SET app.tenant_id = '1'` on connection (Npgsql allows `Options="-c app.tenant_id=1"`). | S | Low |
| 8.02 | **P1** | `LogManagementController.cs:46-83` | Admin Log Management UI is blind. Controller opens raw `NpgsqlConnection`, never sets `app.tenant_id`, so RLS returns 0 rows from `applicationlogs` even for tenant_id=1 data the table contains. Operators see "no logs" and assume nothing's logged. | `ObsProbe3.cs` confirms `SELECT COUNT(*) FROM applicationlogs` returns `0` from `nscim_app` without `SET LOCAL app.tenant_id`, vs `65352` with it. | Add `SET LOCAL app.tenant_id = '1'` after `await connection.OpenAsync()` (and within a transaction so the LOCAL scope persists). Same fix needed in `ErrorMonitoringBackgroundService.GetNewErrorsFromLogsAsync` (`Monitoring/ErrorMonitoringBackgroundService.cs:134`). | XS | Low |
| 8.03 | **P1** | `Monitoring/ErrorMonitoringBackgroundService.cs:134-159` | Error-monitoring → fix-proposal pipeline blind for 9 days. Same pattern as 8.02: queries `applicationlogs` with raw NpgsqlConnection, no `app.tenant_id` set. Compounded by 8.01: even fixing this query won't return rows because the table itself hasn't received writes since 2026-04-25. | Code at line 134. `errorinvestigations` table cardinality = 0 (probe `ObsProbe.cs` SECTION 4). Service runs every 5 min and finds 0 errors every cycle (visible in `Data\Logs\nickscan-20260505.txt`). | Same SET LOCAL fix as 8.02. Then 8.01 unblocks the pipeline. | XS | Low |
| 8.04 | **P1** | runtime | `HealthChecks.UI` collector throws `FileNotFoundException: IdentityModel, Version=5.2.0.0` once per minute. 1,326 errors yesterday, 459+ today. The `/health-ui` page is broken; the file-sink errors log is dominated by this noise (1,828 ContainerDataMapper port-mismatch ERRs + 459 IdentityModel ERRs = 99% of today's `nickscan-errors-20260505.txt`). | `Data\Logs\nickscan-errors-20260505.txt` count: `grep -c "HealthCheck collector" → 452 (8h)`. Stack trace `HealthCheckReportCollector.cs:line 72`. Likely a version mismatch from the .NET 10 retarget — `IdentityModel` 5.2.0.0 was a transitive of HealthChecks.UI prior to .NET 10. | Either pin a compatible `IdentityModel` package version in NSCIM_API.csproj, or swap HealthChecks.UI for the simpler `/health` JSON endpoint (dashboard could call `/health` directly). | S | Low |
| 8.05 | **P1** | `ContainerDataMapperService.cs:213-234` | Business-rule rejections logged at ERROR level. `PORT MISMATCH (mapper, cardinal)` lines fire at `_logger.LogError(...)` for every rejected CBR INSERT, generating **1,828 ERR rows in 8 hours** of today's errors log. These are intentional, designed-to-fire match-quality blocks (not bugs). | `Data\Logs\nickscan-errors-20260505.txt`: `grep -c "PORT MISMATCH (mapper, cardinal)" → 1828`. Sample: `2026-05-05 00:00:26.628 ERR ContainerDataMapperService: PORT MISMATCH (mapper, cardinal): MSNU1772462 scanner=FS6000 (expected TKD) but BOE.DeliveryPlace='WITMA1GFCL' (port=TMA). Rejecting CBR INSERT.` | Demote to `LogWarning` (or `LogInformation` with throttle); they're designed behavior, not failures. Real ERRORs (true exceptions/integrations) will be visible again. | XS | Low |
| 8.06 | **P1** | `WebApp.New/Program.cs:23-25`, `Portal/Program.cs`, `NickHR.WebApp/Program.cs:18`, `NickFinance.WebApp/Program.cs`, `NickComms.Gateway/Program.cs:50-55` | Five of seven services have **no file log sink**. WebApp / Portal / NickHR.WebApp / NickFinance.WebApp use only `AddConsole() + AddDebug()`. NickComms uses Serilog Console only. Output goes to stdout/stderr — but services run as Windows services (sc.exe-managed since `cbed9f4`) that capture nothing. `webapp-stderr.txt` last wrote 2026-03-21. | Files: `webapp-stderr.txt` 0 bytes since 2026-03-21 14:30; `webapp-stdout.txt` 126 bytes since same. `webapp-errors.txt` last write 2026-03-30. `Data\Logs` directory has zero NickHR/Finance/Portal/NickComms/WebApp log files. | Add `WriteTo.File(...)` Serilog sink (mirroring NSCIM_API's Logging.json) to each WebApp's Program.cs. NickComms has Serilog wired — just add the File sink. NickFinance has nothing wired yet — add `Log.Logger = new LoggerConfiguration()...UseSerilog()` block. | S | Low |
| 8.07 | **P2** | `Logging/StructuredLoggingService.cs` + `appsettings.Logging.json:9-81` + `Logging/LogFilterConfiguration.cs` | Three observability features registered but unused. (a) `IStructuredLoggingService` registered in DI but has no callers — operators are using `_logger.Log*` directly. (b) `appsettings.Logging.json:StructuredLogging` (full throttling+spam-ops config tree) is read by no code. (c) `LogFilterConfiguration` static dictionaries (`ServiceLogLevels`, `SpamOperations`, `ThrottleConfig`) are never referenced. | `Grep IStructuredLoggingService` → 0 callers in `src/`. `Grep ThrottleConfig\|SpamOperations\|EnableThrottling` → only in the source file + JSON. `appsettings.Logging.json` lines 9-81 are dead config. | Either delete the dead surface (XS) or wire it into `_logger` extensions and use it across BackgroundServices that emit spammy iteration logs. The orchestrator's `_lastNoUsersLogByRole` ad-hoc throttle already does what `ThrottledLogger` would do, but custom. | S | Low |
| 8.08 | **P2** | `Hubs/ImageAnalysisDashboardHub.cs:227-235`, `326-334` | Dashboard reports placeholder values where it can't compute the metric. `ContainersPerHour=0`, `DecisionsPerHour=0`, `PeakThroughput=0`, `ProcessingTimes.ReadyToAnalystAssigned=5min` (hard-coded), `AverageDatabaseQueryTime=50ms` (hard-coded), `ErrorRate=0.1` (hard-coded). | File:line direct: `throughput = new ImageAnalysisWorkflowThroughput { ContainersPerHour = 0, …, PeakThroughput = 0 }`. Lines 326-339 hardcode `ReadyToAnalystAssigned = TimeSpan.FromMinutes(5)` etc. | Either compute these from `imageanalysisdecisions` / `auditdecisions` timestamps (cheap windowed COUNT queries), or remove the fields from the contract. The dashboard pretending to show "live throughput" while emitting fixed strings is the worst kind of bug-by-misdesign. | M | Low |
| 8.09 | **P2** | `Hubs/ImageAnalysisDashboardHub.cs:442-505` | Dashboard's `PreventiveFixesLast24h` / `LastPreventiveFixTime` reads from `applicationlogs` (which is dead — see 8.01). Will read 0 forever even if operations are happening. Metric is misleading rather than absent. | Code reads `FROM applicationlogs WHERE serviceid LIKE '%COMPLETENESS%' …`. With `applicationlogs.newest = 2026-04-25`, the metric returns 0 for all post-4/25 windows. | Drop the metric or rebase on a real source (e.g. `matchqualityflags` or `containercompletenessstatuses.updatedat`). | S | Low |
| 8.10 | **P2** | `CorrelationIdMiddleware.cs:36-42` | CorrelationId enriches the HTTP request scope but not the Serilog log-context for **background-worker logs**. Since 35+ workers run inside NSCIM_API and never enter the request pipeline, none of their log entries carry CorrelationId. Tracing a "scan came in → BOE matched → CCS row appears → AG creates → assignment → decision" sequence is impossible: each hop is in a different worker, and each worker's logs are a separate stream with no shared key. | `Data\Logs\nickscan-20260505.txt`: orchestrator lines end with `{}` for `Properties:j` (no CorrelationId). HTTP-pipeline lines end with `{"...","CorrelationId":"…"}`. | For long-running operations triggered by an HTTP request (claim, decision submit, manual BOE) the orchestrator can read `HttpContext.Items["CorrelationId"]` if the work is inline. For autonomous workers, mint a per-cycle correlation id and `using var _ = _logger.BeginScope(new {CycleCorrelationId})`. Add a Serilog enricher to project this into every log line. | M | Low |
| 8.11 | **P2** | `Services/HealthChecks/HealthCheckServices.cs:18-31` | `AseHealthCheck.CheckHealthAsync` returns Healthy if `_aseService != null` — i.e. it checks DI registration, not the SQL Server connection it owns. `/health` returns `Healthy` even when ASE on `10.0.0.3` is down. | File:18-31. The check has zero side effects on the actual ASE database. `/health` JSON returns no `ASE` entry at all, so even this fake check isn't surfaced — only the three Postgres `AddDbContextCheck` + three `AddNpgSql` + downstream probes are surfaced. | Either wire `AseHealthCheck` into `AddHealthChecks().AddCheck<AseHealthCheck>(...)` AND make it do a `SELECT 1` against the connection string, or delete it and add an `AddAsyncCheck("ASE_SQL_Server", ...)` that probes `10.0.0.3` via SqlConnection. | XS | Low |
| 8.12 | **P2** | runtime / db | NSCIM_API audit-log surface present-but-empty. `auditlogs` has correct schema (id, timestamp, userid, username, eventtype, action, description, entitytype, entityid, severity, ipaddress, useragent, oldvalue, newvalue, success, additionaldatajson, tenant_id) but **0 rows**. `permissionauditlogs` has 0 rows. `nickhr.AuditLogs` has 0 rows. | `ObsProbe2.cs` sections 6+9. Code paths: only 7 callsites in `ImageAnalysisController.WriteAudit` for image-analysis events. Operationally idle (0 decisions in 4 days per CHANGELOG 2.16.0 ops note) explains the `auditlogs` zero, but `permissionauditlogs` should have many rows from permission CHECK/GRANT actions; `IAuditService.LogPermissionCheckAsync` has zero callers (`AuditService.cs` is wired in DI but never invoked). | Wire `AuditService.LogPermissionCheckAsync` into `PermissionAuthorizationHandler.HandleRequirementAsync` (every authz check). For `auditlogs`, add `WriteAudit` calls to user/role/permission CRUD endpoints (UsersController, RolesController, PermissionsController). | M | Low |
| 8.13 | **P2** | `ContainerCompletenessService.cs` (and 30+ other BackgroundService files) | BackgroundServices emit no per-iteration "I just ran and processed N items" line. The orchestrator does (cycle #N start), but most services either log nothing per iteration (when there's no work) or log results unconditionally without a structured "iteration_summary" event. From logs alone you can't tell whether a worker is alive-but-idle vs alive-with-bug vs hung. | `Data\Logs\nickscan-20260505.txt`: `IcumPipelineOrchestratorService` only logs `[BACKGROUND-SERVICE] ✅ Service enabled - starting batch download workflow` at start of each cycle, no end-of-cycle summary. `RecordReconciliationWorker` logs nothing at iteration boundaries when the queue is empty. | Standardize a `WorkerHeartbeatLog` extension method: every BackgroundService logs `{ServiceId, IterationN, ElapsedMs, ItemsProcessed, ItemsSkipped, ItemsFailed}` at the end of every iteration (not just when something interesting happens). | M | Low |
| 8.14 | **P2** | `appsettings.json:HealthChecks.NickHRHealthUrl = http://127.0.0.1:5215/api/_module/manifest` | NSCIM_API's NickHR health probe targets `/api/_module/manifest` instead of NickHR.API's actual `/api/health` endpoint. The manifest endpoint returns 200 even on partial NickHR breakage (it's a static config response). | `appsettings.json:78`. `Topology` agent §A health endpoints. NickHR.API exposes `/api/health` (per `NickHR.API/Program.cs:280-286`) which actually exercises the DB context. | Change the probe URL to `http://127.0.0.1:5215/api/health`. The downstream-probe code (`Program.cs:625`) is unchanged. | XS | Low |
| 8.15 | **P2** | runtime | NSCIM_API `Data\Logs` has 68 files / **792 MB** total. Daily file rolls fine (50 MB cap, kept 30 days), but the legacy 2026-04-26 `_001` ... `_021` cluster (~24 files) is a side-effect of an iteration storm. Probably benign but suggests one pathological day filled the disk. | `Data\Logs\nickscan-20260426_001.txt` … `nickscan-20260426_021.txt` exist (= 21 50MB rolls in one day = ~1 GB before retention). | Add a Serilog sink-level `flushToDiskInterval` and consider `bufferSize` tuning. Investigate what triggered the burst (likely tied to the security rollout that broke the PG sink — failed inserts spamming retry). | XS | Low |
| 8.16 | **P3** | `Hubs/ImageAnalysisDashboardHub.cs:660-665` | `GetClientCountAsync` returns `Task.FromResult(0)` with comment "SignalR doesn't provide a direct way…". So every dashboard log line says `Broadcasted dashboard update to 0 clients` regardless of actual subscriber count. | File:660-665 verbatim. | Track connections in `OnConnectedAsync`/`OnDisconnectedAsync` (already present) into a ConcurrentDictionary; return `dict.Count`. | XS | Low |
| 8.17 | **P3** | `Hubs/ImageAnalysisDashboardHub.cs:128-143` | Hub broadcast service catches generic `Exception` and logs an error every 5 seconds with `❌ Error broadcasting dashboard update`. Doesn't track consecutive failures or back off — under partial outage this becomes per-5-second log spam. | File:128-143. The retry interval bumps to 30s only for SqlException (we use Postgres). | Use exponential backoff or break out as soon as 5 consecutive failures hit. Distinguish PostgresException too. | XS | Low |
| 8.18 | **P2** | `appsettings.json:Performance:LogSlowRequestsOnly = true` + `PerformanceLoggingMiddleware.cs:53-66` | Slow-request logging exists, but the threshold is `1000ms` (1s). At p99 latency the system may log thousands of slow requests in a busy hour. There's no slow-query logging at the EF level — only HTTP-request slow logs. | `appsettings.json:97`. EF log overrides in `Program.cs:110-115` set EF Core to **`Fatal`** — slow queries are completely silenced. | Bring EF Core Database.Command back to `Information` and add a slow-query filter: `MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)` plus a custom `Filter.ByExcluding` for fast queries. Or use `OperationStopwatch` patterns explicitly in repositories. | M | Low |
| 8.19 | **P2** | runtime | NSCIM_API errors log dominated by 99% noise. Of ~2,288 ERR rows in today's errors file: **1,828 are designed match-rule rejections** (8.05) and **459 are HealthCheck UI failures** (8.04). Real exceptions are <1%. Operators searching for "what broke" wade through known-noise. | `nickscan-errors-20260505.txt:` count breakdown via `grep -oE 'ERR\] [^:]+' \| sort \| uniq -c`. | Fix 8.04 + 8.05; the errors log will then be primarily real exceptions. | XS | Low |
| 8.20 | **P3** | `Program.cs:1431` | "Application starting up..." log message is the only startup-complete signal — but it's emitted **before** `app.Run()` is called, so it's a "we're ABOUT to start" log, not a "we ARE serving" log. There's no log line from when Kestrel actually binds and the listener is hot. | File:1431. Prior is `app.Run()` at 1435. | Add a `app.Lifetime.ApplicationStarted.Register(() => Log.Information("API serving on {Urls}", urls))` callback. The Microsoft.Extensions.Hosting.Internal.Host logs do say "Application started" but at default level Microsoft="Warning" they're filtered out. | XS | Low |
| 8.21 | **P3** | runtime | When NSCIM_API restarts (which restarts all 35+ workers), there is no "all workers ready" sentinel log. Each worker logs "starting" but their startup is staggered (random 1-5s delays per `ContainerCompletenessService.cs:108-110`). An operator can't tell from logs alone when the system is actually ready to do work. | Sample `Data\Logs\nickscan-20260505.txt` lacks a "system ready" anchor line; the `_hasCompletedInitialIntake` static flag in `ImageAnalysisOrchestratorService` indicates this state internally but isn't logged. | When `_hasCompletedInitialIntake` flips, log `[ORCHESTRATOR] System ready: intake bootstrapped, X groups in flight`. Add similar "ready" sentinels to each worker. | XS | Low |
| 8.22 | **P3** | `appsettings.json:Logging:LogLevel.Default = "Information"` (but `Microsoft.EntityFrameworkCore = Fatal`) | EF logs completely silenced (`Fatal` for `Database.Command`, `Database.Transaction`, `Infrastructure`, `Query`, `Update`). Even severe DB errors (deadlock, FK violation) don't surface unless they bubble to the controller. | `Program.cs:110-115`. | Restore EF Core to `Warning` minimum. The audit's "EF1002 warnings cleared" commit (`4a7bbb1`) means we don't need Fatal anymore. | XS | Low |
| 8.23 | **P3** | runtime | `endpointusagelog` (singular table) has 766,848 rows since 2026-04-01, written via the `EndpointUsageBufferService` (10s flush). This IS working — fresh writes (newest=`2026-05-05 07:45:29Z`). Includes `correlationid uuid` column. **The good news:** an audit trail of every HTTP call exists. **The bad news:** no UI surfaces this for operators. | `ObsProbe.cs` SECTION 3, `ObsProbe2.cs` SECTION 4-5. Schema includes endpoint, method, statuscode, responsetimems, ipaddress, useragent, timestamp, isdeprecated, isphase3route, correlationid. | Build a simple "request audit" page in the WebApp admin area or extend `LogManagementController` with `/api/logs/endpoint-usage`. The data is rich (4xx/5xx counts per endpoint per hour, p99 latencies). | M | Low |
| 8.24 | **P3** | runtime / config | Time-zone discipline is good for code: every Serilog timestamp is `+00:00` (UTC), every `DateTime.UtcNow` is used. But the WebApp shows raw UTC timestamps to operators (no TZ conversion) — so dashboard "Last update at 07:45:29" is UTC, not local Accra time. | Sample dashboard JSON returns `Timestamp = now` (UTC). MudBlazor components show ISO strings. | Add a localization layer in the WebApp: `DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime()` at render time. Or stamp UTC in JSON and convert in JavaScript. | M | Low |
| 8.25 | **P2** | runtime | No alerting whatsoever. The dashboard `GetCurrentAlertsAsync` (`ImageAnalysisDashboardHub.cs:667-803`) generates in-memory `DashboardAlert` objects (Bottleneck, Performance, DataIntegrity) and broadcasts them to connected SignalR clients — but they are never persisted, never emailed, never SMS'd. If no operator has the dashboard open, alerts vanish. The CHANGELOG `5d1808a` "throttled Information for empty pool" log promotion does NOT drive any alert. | File:670-803. No `INSERT INTO alerts` anywhere. The `DailyDataQualityReportService` does send a daily email but it's a fixed-time digest, not an alert. | Wire alerts to NickComms.Gateway for email/SMS to on-call. Persist them to a `dashboardalerts` table for history. The infrastructure (NickComms email queue) exists per topology; just needs to call `_emailService.SendAsync(...)`. | M | Med |
| 8.26 | **P2** | runtime | No metrics emission for external collection (Prometheus, OpenTelemetry, etc.). `ICUMSMetricsCollectorService` collects internal counters and writes them somewhere internal (`icumsmetrics`?), and `PerformanceMetricsService` is a singleton in-memory collector. Nothing is scrapeable, nothing is shipped. | Source review confirms no Prometheus exporter, no OTel SDK reference. `Performance:EnableMetricsCollection=true` in appsettings just enables the in-memory collector. | If long-term metrics are wanted: add `OpenTelemetry.Exporter.Prometheus.AspNetCore` and expose `/metrics`. Otherwise the PG `endpointusagelog` table can be the audit trail (8.23) and a periodic reporting query the metric source. | L | Low |
| 8.27 | **P3** | `Hubs/ImageAnalysisDashboardHub.cs:340-347` | `SystemLoad` block in dashboard returns hardcoded zeros for `CpuUsage`, `MemoryUsage`, `DatabaseConnections`, `DiskIoRead`, `DiskIoWrite`, `NetworkBandwidth`. Field is in the contract but never populated. | File:340-347. The `PerformanceMonitoringService` collects this data per-process every 30s; the dashboard hub doesn't read from it. | Inject `IPerformanceMetricsService` into the broadcast service, query it, populate. | XS | Low |
| 8.28 | **P3** | `Logging\ServiceColorFormatter.cs` | Console color formatter applies ANSI escape sequences for service-name highlighting. Useful in interactive shells but the API runs as Windows Service → console is captured by SCM (or to nothing). The escape codes appear as garbage in any captured stream. | File exists, attached as `WriteTo.Console(new ServiceColorFormatter())` at `Program.cs:101`. SCM-managed services have no console. | Conditional: if `Console.IsOutputRedirected` use plain `outputTemplate`. Or remove the Console sink in production. | XS | Low |
| 8.29 | **P2** | runtime | `Microsoft.Extensions.Logging` providers (Console+Debug) added in `Program.cs:61-63` are paralleled by Serilog (`UseSerilog()` at line 118). The `ClearProviders()` call removes the EventLog provider but leaves `AddConsole+AddDebug`. So every log line goes through both sinks, doubling string-format work. Verified by the `[CONSOLE]` formatter writes happening in parallel with Serilog's own console sink. | `Program.cs:61-63` followed by `Log.Logger = …WriteTo.Console(...).Build()` at line 98+. | Drop `AddConsole+AddDebug` after `ClearProviders()` since Serilog has its own Console sink. | XS | Low |
| 8.30 | **P3** | runtime | `applicationlogs.tenant_id` column exists but `properties::jsonb` is never queried for tenant-scoping; the RLS policy is the only enforcement. If RLS were ever disabled (e.g. via the GRANT BYPASSRLS path), tenant data would mix. | Schema: `tenant_id BIGINT` column, `tenant_isolation_applicationlogs` policy. Single-tenant in prod (1 row in tenant_id distribution). | Phase 2 multi-tenancy concern only — log this as a phase-2 backlog. | XS | Low |
| 8.31 | **P2** | `NickHR/src/NickHR.API/Program.cs:63` | NickHR.API's Serilog file sink uses **relative path** `Logs/nickhr-.log`. LocalSystem service's WD = `C:\Windows\System32`, so logs land in `C:\Windows\System32\logs\` — a security smell and an unfindable location for operators. Files exist for 2026-04-17 through 2026-04-24, then stop (around the .NET 10 retarget; root cause needs Phase 3 investigation — could be writes succeeding silently to a different path, or could be logging fully broken since 4/24). | Files at `C:\Windows\System32\logs\nickhr-20260417.log` ... `nickhr-20260424.log`. No newer files found. NickHR.API is currently up (per `/api/_module/manifest` health probe responding 200). | Change `WriteTo.File("Logs/nickhr-.log", …)` to an absolute path: `WriteTo.File(@"C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickhr-.log", …)`. Move the existing System32 log files to the canonical location. | XS | Low |

## Narrative

The fundamental shape: NSCIM v1's observability has two halves. The
**file-sink half** (`Data\Logs\nickscan-{date}.txt`) is healthy, exhaustive,
and well-structured. The **DB-sink + UI-aggregator half** is silently broken.
Every diagnostic surface operators are *expected* to use — the in-app log
viewer at `/admin/logs`, the error-investigation pipeline, the
`/health-ui` aggregator dashboard, the WebApp Console+Debug stream — is dead,
and the file sink is the only honest signal left.

The file sink itself is, day-to-day, sufficient: Serilog rolls daily,
50 MB cap, 30-day retention, structured `outputTemplate` includes
`{SourceContext}`, `{Properties:j}`, exceptions, MachineName, ThreadId. The
ORCHESTRATOR cycle counter is visible (`cycle #971`, `cycle #1101`),
worker lifecycle starts log (`{ServiceId} starting`), and HTTP requests
emit CorrelationId via the structured-property pipeline.
Diagnose-from-logs feasibility on the file sink alone is **mostly OK** for
the cargo pipeline — though the operator must SSH the box.

**The five highest-impact P1 findings** are all consequences of the 2026-04
security/RLS rollout silently breaking observability:

1. **Postgres `applicationlogs` sink dead since 2026-04-25 19:53:21** (8.01).
   RLS-fail-closed rejects every Serilog insert because the PG sink uses
   raw Npgsql, not the EF tenancy interceptor. The exact moment of the
   Week-1 security deploy (per CHANGELOG `001f0fc`) is when log writes
   started failing silently. Nine days of warning+ logs lost.

2. **`LogManagementController` returns 0 rows** (8.02). Same pattern. The
   admin "View Logs" page is empty — even of pre-RLS data still in the
   table — because raw NpgsqlConnection without `app.tenant_id` returns
   nothing under the current policy. Confirmed: nscim_app SELECT without
   tenant_id returns 0 rows; with it returns 65,352.

3. **`ErrorMonitoringBackgroundService` blind for 9 days** (8.03). Detects 0
   errors per cycle. The "fix proposal" surface (`ErrorInvestigationService`)
   is fed by an empty pipeline.

4. **HealthChecks UI throwing FileNotFoundException** every minute (8.04).
   `IdentityModel 5.2.0.0` was a transitive of HealthChecks.UI prior to
   the .NET 10 retarget. **1,326 errors yesterday**, 459 today (~one per
   minute). The `/health-ui` aggregator never produces output.

5. **Five of seven services have no file log sink** (8.06). NSCIM_WebApp,
   Portal, NickHR.WebApp, NickFinance.WebApp, NickComms.Gateway either
   use only Console+Debug or have Serilog Console-only. Their windows
   service captures vanish into nothing. A WebApp Blazor exception is
   undiagnosable from the box without attaching a debugger.

**The P1-noise-pollution finding** is 8.05: `ContainerDataMapperService` logs
**1,828 PORT MISMATCH ERR rows in 8 hours** for designed-to-fire match-rule
rejections. Combined with 8.04, **99% of today's errors log is known noise**.
A real exception buried in this stream is operationally invisible.

**Background-worker observability** (8.10, 8.13) is the deepest design gap
behind the file-sink. CorrelationId enriches the HTTP request scope but
not the orchestrator's autonomous cycles. So tracing "scan came in →
matched → AG created → assigned → decided → submitted" across the 35
in-process workers requires manual timestamp correlation — there's no
shared key. And per-iteration "I just ran" telemetry is missing for most
workers — operators can't distinguish alive-and-idle from alive-and-buggy.

**Dashboard truthfulness** (8.08, 8.09, 8.16, 8.27) is poor. The hub
hard-codes throughput, processing-time, system-load fields rather than
either computing or omitting them. The "preventive fixes last 24h" reads
from the dead `applicationlogs`. The "broadcasted to N clients" line
always says 0. An operator who relies on the dashboard for "is the system
performing well?" is reading staged numbers.

**Audit trails** (8.12) are present-but-empty. `auditlogs`,
`permissionauditlogs`, `nickhr.AuditLogs` all have 0 rows. Permission
checks happen but are never recorded. The infrastructure is wired (DI,
schema, RLS) but the call sites are missing.

**There is no alerting** (8.25). Dashboard alerts are constructed
in-memory and broadcast only to connected SignalR clients. NickComms can
send email/SMS but isn't called from the alerting path. Off-hours
incidents will not page anyone.

**Quick wins** (XS effort, Low risk, high payoff): fix 8.01 (one Npgsql
connection-option change), fix 8.02+8.03 (add `SET LOCAL app.tenant_id`
to two raw-connection paths), fix 8.05 (demote PORT MISMATCH from Error
to Warning), pin `IdentityModel` to fix 8.04. After these four changes,
the in-app log viewer works, error monitoring resumes, and the errors
log becomes 99% real signal again.

## Open questions

1. **Were the 9 days of lost `applicationlogs` writes detected by anyone?**
   The Serilog PG sink failures should themselves be logged (Serilog's own
   `SelfLog`). Did they spam the file sink? `Data\Logs\nickscan-20260426_*.txt`
   has 21 50-MB rolls in one day — this could be the failed-insert retry
   storm. Phase 3 should grep the late-April log files for `applicationlogs`
   or `42501` errors.

2. **Is the WebApp truly silent or is there a captured stream we missed?**
   `webapp-stderr.txt` last write 2026-03-21 14:30 was *before* the SCM
   migration (cbed9f4 in 2.16.1, 2026-05-04). What captured the WebApp's
   stdout before 2026-03-21, and where does it go now? Possibly the SCM
   `nssm` config (since defunct) was redirecting stderr/stdout to those
   files, and the sc.exe migration broke it.

3. **NickHR.API log writes appear to have stopped 2026-04-24** despite the
   service being up and responding to health checks today. Files in
   `C:\Windows\System32\logs\` (where the relative-path sink writes —
   captured as 8.31) exist for 2026-04-17 through 2026-04-24, then nothing.
   Phase 3 should determine whether (a) the .NET 10 retarget broke Serilog
   File sink writes, (b) Serilog's internal SelfLog has the failure
   reason, or (c) the working directory changed and writes are landing
   elsewhere undiscovered. NickHR.API is currently logless to all the
   operator's known surfaces.

4. **Are there any Cloudflare-tunnel-side request logs?** Per
   `Program.cs:306-314` NSCIM_WebApp accepts `X-Forwarded-*` from
   Cloudflare tunnel `scan.nickscan.net`. If CF-side logs exist, they'd
   complement the local sink for the WebApp's blindness (8.06). Operator
   should know whether they have CF Access logs to lean on.

5. **What is `endpointusagelog`'s retention?** The
   `EndpointUsageCleanupBackgroundService` is registered (per Topology
   §D) at hourly cadence and trims `> 90 days`. With `766,848` rows in
   34 days (= ~22.5K/day), at 90 days the table would hold ~2M rows.
   Confirm pruning is actually running and the table doesn't blow out.
