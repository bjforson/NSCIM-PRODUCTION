using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.Utilities;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageSplitter;
using NickScanCentralImagingPortal.Services.Logging;
using NickScanCentralImagingPortal.Services.Monitoring;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Orchestrates all Image Analysis workflows in a single coordinated service
    /// Consolidates: Bootstrapper, IntakeWorker, AssignmentWorker, SubmissionWorker, HousekeepingWorker
    /// Benefits: Reduced memory (~250MB → ~50MB), single DbContext scope, shared cached data
    /// </summary>
    public class ImageAnalysisOrchestratorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ImageAnalysisOrchestratorService> _logger;
        private readonly ReadyGroupsCacheService _readyGroupsCache;
        private readonly IConfiguration _configuration;
        private readonly AdaptivePollingHelper _adaptivePolling;
        private readonly ServiceHealthMonitor _healthMonitor;

        // Wave #4 (1.11.0): per-container Pending-without-images timeout.
        // A WavePendingContainer in "Pending" status that hasn't received
        // images within this many hours is marked NoImageAvailable on the
        // next partial wave scan, instead of waiting for the parent group's
        // 30-day auto-close. Tunable here if operational reality changes;
        // promoted to a database-backed setting later if needed.
        private const int PendingContainerStuckHours = 72;

        // Track last execution times for each workflow
        private DateTime _lastBootstrapperRun = DateTime.MinValue;
        private DateTime _lastIntakeRun = DateTime.MinValue;
        private DateTime _lastAssignmentRun = DateTime.MinValue;
        private DateTime _lastSubmissionRun = DateTime.MinValue;
        private DateTime _lastHousekeepingRun = DateTime.MinValue;
        private DateTime _lastMetricsCleanup = DateTime.MinValue;
        private DateTime _lastDecisionAgentRun = DateTime.MinValue;
        private DateTime _lastSplitDetectionRun = DateTime.MinValue;
        private DateTime _lastIcumsAckReconciliationRun = DateTime.MinValue;

        // Track last validation time for assignment worker
        private static DateTime _lastValidationTime = DateTime.MinValue;

        // Track last assigned user per role for RoundRobin
        private static readonly Dictionary<string, string> _lastAssignedUserByRole = new();

        // ✅ FIX: Gate Assignment until Intake has completed at least once (avoids 14+ min of empty Assignment runs)
        private static volatile bool _hasCompletedInitialIntake = false;
        // ✅ FIX: Surface Intake errors for observability (health checks can read this)
        private static volatile string? _lastIntakeError;
        /// <summary>Exposes last Intake error for health checks.</summary>
        public static string? GetLastIntakeError() => _lastIntakeError;
        // Monitor duplicate key races (two workers inserting same group) - for alerting if excessive
        private static int _duplicateGroupRaceCount;

        // Throttle "no ready users" log to once per 5 minutes per role
        private readonly Dictionary<string, DateTime> _lastNoUsersLogByRole = new();
        // Same shape for "no ready groups" — added 2026-05-04 so Audit-role polling
        // is visible at Information level even when there are zero AnalystCompleted
        // groups (otherwise the early-return at AutoAssignByRoleAsync logs Debug
        // and gets filtered out by the default Information log level).
        private readonly Dictionary<string, DateTime> _lastNoGroupsLogByRole = new();
        private DateTime _lastDisabledLogTime = DateTime.MinValue;
        private static readonly TimeSpan _logThrottleInterval = TimeSpan.FromMinutes(5);

        // Resilience item 3 (2026-05-09) — dead-mans-switch state for the audit
        // pool. Pattern: when the orchestrator detects backlog AND no auditor
        // Ready, we mark _firstAuditPoolEmptyAt; if the condition still holds 30
        // minutes later we raise a Critical AuditPoolEmpty alert. _lastAlertAt
        // gates re-firing within a 60-minute window so we don't spam ops while
        // the situation is being investigated. Both reset to MinValue on each
        // recovery (auditor goes Ready OR backlog drains).
        //
        // Reasoning for thresholds:
        //   - 30-min dwell — matches the existing audit-stage backlog surface
        //     log (uses the same auditBacklogStaleCutoff) and the doubled lease
        //     window from the 2026-05-09 audit-queue work. Anything shorter
        //     fires during normal lunch-break gaps.
        //   - 60-min idempotency — the dedupe path in IDashboardAlertService
        //     already collapses (Type, Title) collisions within 30 min; doubling
        //     it here means a Critical alert that's been seen + acked won't
        //     re-page the same on-call within the rest of an hour-long shift.
        private DateTime _firstAuditPoolEmptyAt = DateTime.MinValue;
        private DateTime _lastAuditPoolEmptyAlertAt = DateTime.MinValue;
        private static readonly TimeSpan _auditPoolEmptyAlertDwell = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan _auditPoolEmptyAlertCooldown = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan _auditPoolReadinessFreshness = TimeSpan.FromMinutes(60);

        // Query result cache within cycle (Phase 3.3 optimization)
        private Dictionary<string, object>? _cycleCache;

        public ImageAnalysisOrchestratorService(
            IServiceScopeFactory scopeFactory,
            ILogger<ImageAnalysisOrchestratorService> logger,
            ReadyGroupsCacheService readyGroupsCache,
            IConfiguration configuration,
            AdaptivePollingHelper? adaptivePolling = null,
            ServiceHealthMonitor? healthMonitor = null)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _readyGroupsCache = readyGroupsCache;
            _configuration = configuration;
            _adaptivePolling = adaptivePolling ?? new AdaptivePollingHelper(logger);
            // Create health monitor with proper logger type
            if (healthMonitor == null)
            {
                using var tempScope = scopeFactory.CreateScope();
                var loggerFactory = tempScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
                var healthLogger = loggerFactory.CreateLogger<ServiceHealthMonitor>();
                _healthMonitor = new ServiceHealthMonitor(healthLogger);
            }
            else
            {
                _healthMonitor = healthMonitor;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[IMAGE-ANALYSIS-ORCHESTRATOR] Starting Image Analysis Orchestrator Service");

            // Phase 1: Run bootstrapper once on startup
            await RunBootstrapperAsync(stoppingToken);

            // Phase 2: Main orchestration loop with adaptive polling
            _logger.LogInformation("[ORCHESTRATOR] Entering main orchestration loop");
            var cycleCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2): mint per-cycle CorrelationId so every
                // log line emitted during this iteration carries the same key.
                using var _cycleScope = _logger.BeginCycle(nameof(ImageAnalysisOrchestratorService));
                // Audit 8.13 (Sprint 5G2): track elapsed time for the heartbeat
                // line emitted at the bottom of the iteration.
                var _cycleStartedAt = DateTime.UtcNow;
                try
                {
                    cycleCount++;
                    // ✅ DIAGNOSTIC: Log cycle entry (every 10 cycles to avoid spam)
                    if (cycleCount % 10 == 1 || cycleCount <= 5)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Main loop cycle #{Cycle} started", cycleCount);
                    }

                    // Clear cycle cache at start of each cycle (Phase 3.3 optimization)
                    _cycleCache = new Dictionary<string, object>();

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var settings = await db.AnalysisSettings.FirstOrDefaultAsync(stoppingToken) ?? new AnalysisSettings();

                    if (!settings.Enabled)
                    {
                        var nowDisabled = DateTime.UtcNow;
                        if ((nowDisabled - _lastDisabledLogTime) >= _logThrottleInterval)
                        {
                            _logger.LogInformation("[ORCHESTRATOR] AnalysisSettings.Enabled = false - idle (this message repeats every 5 min)");
                            _lastDisabledLogTime = nowDisabled;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    // ✅ DIAGNOSTIC: Log settings
                    if (cycleCount <= 3)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Settings: Enabled={Enabled}, Mode={Mode}, MaxConcurrent={MaxConcurrent}",
                            settings.Enabled, settings.AssignmentMode ?? "Manual", settings.MaxConcurrentPerUser);
                    }

                    var now = DateTime.UtcNow;

                    // Phase 3.1: Adaptive Polling - Count work items to determine intervals
                    // ✅ DIAGNOSTIC: Log before work count queries
                    if (cycleCount <= 5 || cycleCount % 10 == 1)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Starting work count queries...", cycleCount);
                    }

                    var intakeWorkCount = await GetIntakeWorkCountAsync(db, stoppingToken);
                    if (cycleCount <= 5 || cycleCount % 10 == 1)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Intake work count: {Count}", cycleCount, intakeWorkCount);
                    }

                    var assignmentWorkCount = await GetAssignmentWorkCountAsync(db, stoppingToken);
                    if (cycleCount <= 5 || cycleCount % 10 == 1)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Assignment work count: {Count}", cycleCount, assignmentWorkCount);
                    }

                    var submissionWorkCount = await GetSubmissionWorkCountAsync(db, stoppingToken);
                    if (cycleCount <= 5 || cycleCount % 10 == 1)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Submission work count: {Count}", cycleCount, submissionWorkCount);
                    }

                    // ✅ DIAGNOSTIC: Log that work count queries completed
                    if (cycleCount <= 5 || cycleCount % 10 == 1)
                    {
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Work count queries completed, checking adaptive polling...", cycleCount);
                    }

                    // Execute workflows with adaptive intervals
                    // ✅ ARCHITECTURAL FIX: Run workflows in parallel to prevent blocking
                    // High-priority workflows (like Assignment) should not be blocked by long-running workflows (like Intake)
                    // Each workflow gets its own DbContext scope for thread safety
                    var workflowTasks = new List<Task>();

                    // Intake: adaptive based on work count
                    // ✅ FIX: First cycle awaits Intake so Assignment has groups; subsequent cycles fire-and-forget
                    if (_adaptivePolling.ShouldExecute(_lastIntakeRun, intakeWorkCount, now))
                    {
                        if (cycleCount <= 5 || cycleCount % 10 == 1)
                        {
                            _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Intake workflow should execute (last run: {LastRun})",
                                cycleCount, _lastIntakeRun == DateTime.MinValue ? "Never" : _lastIntakeRun.ToString("HH:mm:ss"));
                        }
                        _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Starting intake workflow (work count: {Count})...", cycleCount, intakeWorkCount);
                        var intakeStartTime = DateTime.UtcNow;
                        _lastIntakeRun = now;
                        _lastIntakeError = null;

                        async Task RunIntakeWithTrackingAsync(CancellationToken token)
                        {
                            using var intakeScope = _scopeFactory.CreateScope();
                            var intakeDb = intakeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            try
                            {
                                await _healthMonitor.MeasureExecutionAsync(
                                    "ImageAnalysisOrchestrator",
                                    "Intake",
                                    async () => await RunIntakeWorkflowAsync(intakeDb, token),
                                    () => intakeWorkCount);
                                var intakeDuration = DateTime.UtcNow - intakeStartTime;
                                _hasCompletedInitialIntake = true;
                                _logger.LogInformation("[ORCHESTRATOR] Intake workflow completed in {Duration:F1}s", intakeDuration.TotalSeconds);
                            }
                            catch (Exception ex)
                            {
                                _lastIntakeError = $"{ex.GetType().Name}: {ex.Message}";
                                _logger.LogError(ex, "[ORCHESTRATOR] Error in intake workflow");
                            }
                        }

                        if (!_hasCompletedInitialIntake)
                        {
                            // First run: await Intake (max 2 min) so Assignment has groups
                            _logger.LogInformation("[ORCHESTRATOR] Awaiting initial Intake before Assignment (first cycle)");
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            cts.CancelAfter(TimeSpan.FromMinutes(2));
                            try
                            {
                                await RunIntakeWithTrackingAsync(cts.Token);
                            }
                            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                            {
                                _logger.LogWarning("[ORCHESTRATOR] Initial Intake timed out after 2 min - allowing Assignment to proceed");
                                _hasCompletedInitialIntake = true;
                            }
                        }
                        else
                        {
                            // Fire-and-forget but log unobserved failures.
                            // RunIntakeWithTrackingAsync has its own try/catch, so this continuation is
                            // a defense-in-depth net for future refactors that might let exceptions escape.
                            _ = Task.Run(() => RunIntakeWithTrackingAsync(stoppingToken), stoppingToken)
                                .ContinueWith(t =>
                                {
                                    if (t.IsFaulted && t.Exception is { } aex)
                                    {
                                        _logger.LogError(aex, "[ORCHESTRATOR] Background Intake task faulted (unobserved)");
                                    }
                                }, TaskScheduler.Default);
                        }
                    }
                    else
                    {
                        if (cycleCount <= 5 || cycleCount % 10 == 1)
                        {
                            _logger.LogInformation("[ORCHESTRATOR] Cycle #{Cycle}: Intake workflow skipped (not enough time elapsed, last run: {LastRun})",
                                cycleCount, _lastIntakeRun == DateTime.MinValue ? "Never" : _lastIntakeRun.ToString("HH:mm:ss"));
                        }
                    }

                    // Decision Agent: auto-decide safe/risky cargo before Assignment picks them up
                    if (_hasCompletedInitialIntake)
                    {
                        try
                        {
                            var agentSettings = await db.DecisionAgentSettings.FirstOrDefaultAsync(stoppingToken);
                            if (agentSettings?.Enabled == true)
                            {
                                var agentConditions = await db.DecisionAgentConditions
                                    .Where(c => c.Enabled)
                                    .OrderBy(c => c.SortOrder)
                                    .ToListAsync(stoppingToken);

                                if (agentConditions.Any())
                                {
                                    workflowTasks.Add(Task.Run(async () =>
                                    {
                                        using var agentScope = _scopeFactory.CreateScope();
                                        var agentDb = agentScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                        var agentIcumDb = agentScope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
                                        try
                                        {
                                            await DecisionAgent.DecisionAgentWorker.RunDecisionAgentWorkflowAsync(
                                                agentDb, agentIcumDb, agentSettings, agentConditions, _logger, stoppingToken);
                                            _lastDecisionAgentRun = now;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "[DECISION-AGENT] Error in decision agent workflow");
                                        }
                                    }, stoppingToken));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[DECISION-AGENT] Error loading agent settings");
                        }
                    }

                    // Assignment: adaptive based on work count
                    // ✅ FIX: Skip Assignment until Intake has completed at least once (avoids empty runs for 14+ min)
                    var shouldExecuteAssignment = _hasCompletedInitialIntake &&
                        (_lastAssignmentRun == DateTime.MinValue || _adaptivePolling.ShouldExecute(_lastAssignmentRun, assignmentWorkCount, now));
                    if (!_hasCompletedInitialIntake && cycleCount <= 3)
                    {
                        _logger.LogInformation("[ASSIGNMENT-POLLING] Skipping Assignment - waiting for initial Intake to complete");
                    }
                    var timeSinceLastAssignment = _lastAssignmentRun == DateTime.MinValue ? TimeSpan.Zero : (now - _lastAssignmentRun);

                    _logger.LogDebug("[ASSIGNMENT-POLLING] Work count: {Count}, Last run: {LastRun} ({SecondsAgo:F1}s ago), Should execute: {ShouldExecute}",
                        assignmentWorkCount, _lastAssignmentRun == DateTime.MinValue ? "Never" : _lastAssignmentRun.ToString("HH:mm:ss"),
                        timeSinceLastAssignment.TotalSeconds, shouldExecuteAssignment);

                    if (shouldExecuteAssignment)
                    {
                        workflowTasks.Add(Task.Run(async () =>
                        {
                            using var assignmentScope = _scopeFactory.CreateScope();
                            var assignmentDb = assignmentScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var assignmentSettings = await assignmentDb.AnalysisSettings.FirstOrDefaultAsync(stoppingToken) ?? new AnalysisSettings();
                            try
                            {
                                await _healthMonitor.MeasureExecutionAsync(
                                    "ImageAnalysisOrchestrator",
                                    "Assignment",
                                    async () => await RunAssignmentWorkflowAsync(assignmentDb, assignmentSettings, now, stoppingToken),
                                    () => assignmentWorkCount);
                                _lastAssignmentRun = now;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "[ASSIGNMENT] Error in assignment workflow");
                            }
                        }, stoppingToken));
                    }
                    else
                    {
                        _logger.LogDebug("[ASSIGNMENT-POLLING] Skipping assignment workflow - not enough time elapsed (interval: {Interval}s)",
                            _adaptivePolling.CalculateInterval(assignmentWorkCount).TotalSeconds);
                    }

                    // Submission: run if main work exists OR if it's been at least 2 min since last run
                    // (the second condition ensures Outbox retry fires even when no new AuditCompleted groups
                    //  exist — otherwise payload files generated previously sit unsent forever).
                    var shouldRunSubmission = _adaptivePolling.ShouldExecute(_lastSubmissionRun, submissionWorkCount, now)
                                              || (now - _lastSubmissionRun).TotalMinutes >= 2;
                    if (shouldRunSubmission)
                    {
                        workflowTasks.Add(Task.Run(async () =>
                        {
                            using var submissionScope = _scopeFactory.CreateScope();
                            var submissionDb = submissionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            try
                            {
                                await _healthMonitor.MeasureExecutionAsync(
                                    "ImageAnalysisOrchestrator",
                                    "Submission",
                                    async () => await RunSubmissionWorkflowAsync(submissionDb, stoppingToken),
                                    () => submissionWorkCount);
                                _lastSubmissionRun = now;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "[ORCHESTRATOR] Error in submission workflow");
                            }
                        }, stoppingToken));
                    }

                    // Wait for all workflows to complete (they run in parallel with separate DbContext scopes)
                    if (workflowTasks.Count > 0)
                    {
                        await Task.WhenAll(workflowTasks);
                    }

                    // Split detection: every 60 seconds, populate split fields on AnalysisRecords
                    // for containers that are part of 2-container scans
                    if ((now - _lastSplitDetectionRun).TotalSeconds >= 60)
                    {
                        try
                        {
                            using var splitScope = _scopeFactory.CreateScope();
                            var splitDb = splitScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var httpClientFactory = splitScope.ServiceProvider.GetService<IHttpClientFactory>();
                            if (httpClientFactory != null)
                            {
                                await RunSplitDetectionAsync(splitDb, httpClientFactory, stoppingToken);
                            }
                            _lastSplitDetectionRun = now;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[SPLIT-DETECTION] Error in split detection workflow");
                        }
                    }

                    // Housekeeping: every 2 minutes (low priority, doesn't need adaptive polling)
                    // Uses its own scoped DbContext to ensure SaveChangesAsync persists correctly
                    if ((now - _lastHousekeepingRun).TotalMinutes >= 2)
                    {
                        using var housekeepingScope = _scopeFactory.CreateScope();
                        var housekeepingDb = housekeepingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        await _healthMonitor.MeasureExecutionAsync(
                            "ImageAnalysisOrchestrator",
                            "Housekeeping",
                            async () => await RunHousekeepingWorkflowAsync(housekeepingDb, now, stoppingToken));
                        _lastHousekeepingRun = now;
                    }

                    // Update service metrics
                    var memoryUsage = _healthMonitor.GetCurrentMemoryUsage();
                    _healthMonitor.RecordServiceMetrics("ImageAnalysisOrchestrator", memoryUsage);

                    // ✅ MEMORY FIX: Cleanup old metrics every hour to prevent unbounded growth
                    if ((now - _lastMetricsCleanup).TotalHours >= 1)
                    {
                        _healthMonitor.CleanupOldMetrics(TimeSpan.FromHours(24));
                        _lastMetricsCleanup = now;
                        if (cycleCount % 10 == 1 || cycleCount <= 3)
                        {
                            _logger.LogInformation("[ORCHESTRATOR] Cleaned up old health monitor metrics");
                        }
                    }

                    // Calculate adaptive delay based on maximum work count
                    // ✅ FIX: Use MAX instead of MIN to ensure high-priority work (like assignments) gets frequent checks
                    // If any workflow has HIGH work (5s interval), we should check frequently, not wait 5 minutes
                    var maxWorkCount = Math.Max(Math.Max(intakeWorkCount, assignmentWorkCount), submissionWorkCount);
                    var delay = _adaptivePolling.CalculateInterval(maxWorkCount);

                    // ✅ DIAGNOSTIC: Log delay (every 10 cycles to avoid spam)
                    if (cycleCount % 10 == 1 || cycleCount <= 3)
                    {
                        _logger.LogDebug("[ORCHESTRATOR] Cycle #{Cycle} completed - waiting {DelayMs}ms before next cycle (max work: {MaxWork})",
                            cycleCount, delay.TotalMilliseconds, maxWorkCount);
                    }

                    // Audit 8.13 (Sprint 5G2): per-iteration heartbeat. processed
                    // here is the maximum-of-three workflow work counts from this
                    // cycle (best proxy for "stuff this cycle saw"); skipped /
                    // failed are 0 in steady-state and surface only when the catch
                    // arms below increment them.
                    _logger.LogIterationSummary(
                        nameof(ImageAnalysisOrchestratorService),
                        cycleCount,
                        DateTime.UtcNow - _cycleStartedAt,
                        itemsProcessed: maxWorkCount,
                        itemsSkipped: 0,
                        itemsFailed: 0);

                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _logger.LogWarning(ex, "[IMAGE-ANALYSIS-ORCHESTRATOR] Database connectivity issue. Retrying in 30 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex, "[IMAGE-ANALYSIS-ORCHESTRATOR] Error in orchestration cycle. Retrying in 5 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }

            _logger.LogInformation("[IMAGE-ANALYSIS-ORCHESTRATOR] Image Analysis Orchestrator Service stopped");
        }

        #region Bootstrapper Workflow

        [Obsolete]
        private async Task RunBootstrapperAsync(CancellationToken cancellationToken)
        {
            if (_lastBootstrapperRun != DateTime.MinValue)
                return; // Only run once

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (!await db.Database.CanConnectAsync(cancellationToken))
                {
                    _logger.LogWarning("[BOOTSTRAPPER] Database not available - will retry on next cycle");
                    return;
                }

                // Ensure AnalysisSettings row
                var settings = await db.AnalysisSettings.AsTracking().FirstOrDefaultAsync(cancellationToken);
                if (settings == null)
                {
                    settings = new AnalysisSettings
                    {
                        Enabled = true,
                        AssignmentMode = "Manual",
                        AutoAssignStrategy = "RoundRobin",
                        AutoAssign = false,
                        LeaseMinutes = 15,
                        MaxConcurrentPerUser = 5,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    db.AnalysisSettings.Add(settings);
                }
                else
                {
                    if (string.IsNullOrEmpty(settings.AssignmentMode))
                    {
                        settings.AssignmentMode = settings.AutoAssign ? "Auto" : "Manual";
                        settings.AutoAssignStrategy = "RoundRobin";
                        settings.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }

                // Ensure roles: Analyst, Audit, Lead
                var requiredRoles = new[] { "Analyst", "Audit", "Lead" };
                var parameters = requiredRoles.Cast<object>().ToArray();
                var placeholders = string.Join(",", requiredRoles.Select((_, i) => $"{{{i}}}"));
                var existingRoles = await db.Roles
                    .FromSqlRaw($"SELECT * FROM Roles WHERE Name IN ({placeholders})", parameters)
                    .Select(r => r.Name)
                    .ToListAsync(cancellationToken);

                foreach (var roleName in requiredRoles.Except(existingRoles))
                {
                    db.Roles.Add(new Role
                    {
                        Name = roleName,
                        DisplayName = roleName,
                        Description = $"Role for {roleName} operations",
                        IsSystemRole = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                _lastBootstrapperRun = DateTime.UtcNow;
                _logger.LogInformation("[BOOTSTRAPPER] Image Analysis system initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BOOTSTRAPPER] Failed to initialize Image Analysis system");
            }
        }

        #endregion

        #region Intake Workflow

        private async Task RunIntakeWorkflowAsync(ApplicationDbContext db, CancellationToken stoppingToken)
        {
            try
            {
                // Ensure AnalysisSettings exists
                var settings = await db.AnalysisSettings.FirstOrDefaultAsync(stoppingToken);
                if (settings == null)
                {
                    settings = new AnalysisSettings();
                    db.AnalysisSettings.Add(settings);
                    await db.SaveChangesAsync(stoppingToken);
                }

                if (!settings.Enabled)
                    return;

                // 1.16.0 — record-anchored intake pass. Runs FIRST so record-driven groups
                // get created before the legacy container-grouping pass. Creates
                // AnalysisGroup rows directly from RecordCompletenessStatus records where
                // Status=Ready (all containers ready) or Status=PartiallyReady with enough
                // ready containers to meet WaveMinBatchSize. The legacy container-grouping
                // pass below acts as a fallback for containers whose records don't exist
                // yet in RecordCompletenessStatus.
                try
                {
                    await RunRecordAnchoredIntakeAsync(db, settings, stoppingToken);
                }
                catch (Exception recEx)
                {
                    _logger.LogError(recEx, "[INTAKE-RECORD] Record-anchored intake pass failed");
                }

                // LEGACY CONTAINER-GROUPING INTAKE — RETIRED
                // All group creation now goes through RunRecordAnchoredIntakeAsync + wave processing.
                // Event-driven record building (RecordBuildingService) ensures records are ready
                // before intake runs. The legacy path is preserved below as dead code for rollback safety.
                // To re-enable: uncomment the block below and remove this comment.

                /*
                // ✅ OPTIMIZATION: Add time limit to prevent blocking assignment checks
                var startTime = DateTime.UtcNow;
                var maxExecutionTime = TimeSpan.FromMinutes(_configuration.GetValue<int>("ImageAnalysis:MaxExecutionTimeMinutes", 2));

                // ✅ OPTIMIZATION: Batch processing configuration
                var maxGroupsPerCycle = _configuration.GetValue<int>("ImageAnalysis:MaxGroupsPerCycle", 500);
                var maxCompletenessRowsPerCycle = _configuration.GetValue<int>("ImageAnalysis:MaxCompletenessRowsPerCycle", 5000);

                // ✅ FIX LATE-ARRIVING CONTAINERS: Track existing (GroupIdentifier, ScannerType) pairs but
                // do NOT exclude them from processing. Late-arriving containers must be allowed to flow
                // through to the add-containers path so missing AnalysisRecords get created. Anti-reprocessing
                // is enforced by containernumber-level dedup at the AnalysisRecord insert step.
                var existingGroupKeys = await db.AnalysisGroups
                    .Where(g => !string.IsNullOrEmpty(g.GroupIdentifier) && !string.IsNullOrEmpty(g.ScannerType))
                    .Select(g => new { GroupIdentifier = g.GroupIdentifier ?? "", ScannerType = g.ScannerType ?? "" })
                    .ToListAsync(stoppingToken);

                var existingGroupSet = new HashSet<string>(
                    existingGroupKeys.Select(x => $"{x.GroupIdentifier}|{x.ScannerType}".ToUpperInvariant()),
                    StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("[INTAKE] Found {Count} existing AnalysisGroups - will check for late-arriving containers", existingGroupSet.Count);

                // Create or update AnalysisGroups from completeness rows that are fully complete
                // ✅ MEMORY FIX: Add limit to prevent loading all completeness rows into memory
                // ✅ FIX: Include ImageAnalysis - ContainerCompletenessService sets ImageAnalysis when Complete, BEFORE any group exists.
                // The existingGroupSet filter below excludes GroupIdentifiers that already have groups.
                // ✅ MinYearForIntake: When set, only process records with ScanDate.Year >= that value (e.g. 2026)
                // Null/0 = no filter (handles pre-migration or missing DB column)
                var minYear = settings.MinYearForIntake;
                var minDate = (minYear.HasValue && minYear.Value > 0)
                    ? new DateTime(minYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : (DateTime?)null;
                if (minDate.HasValue)
                {
                    _logger.LogInformation("[INTAKE] MinYearForIntake={Year} - only processing records with ScanDate >= {MinDate:yyyy-MM-dd}", minYear?.ToString() ?? "null", minDate!.Value);
                }
                var allMatchingRows = await db.ContainerCompletenessStatuses
                    .AsTracking()
                    .Where(s => s.Status.StartsWith("Complete")
                        && (string.IsNullOrEmpty(s.WorkflowStage) || s.WorkflowStage == "Pending" || s.WorkflowStage == "ImageAnalysis")
                        && !string.IsNullOrEmpty(s.GroupIdentifier)
                        && (minDate == null || s.ScanDate >= minDate.Value))
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(maxCompletenessRowsPerCycle * 2)
                    .ToListAsync(stoppingToken);

                // ✅ FIX LATE-ARRIVING CONTAINERS: Do NOT filter by existing groups. Both new and existing
                // groups must flow through so the add-containers path can create AnalysisRecords for
                // late-arriving containers. The transaction body distinguishes new vs existing via
                // MergeAnalysisGroupIfNotExists + per-container dedup.
                var completenessRows = allMatchingRows
                    .Where(s => !string.IsNullOrEmpty(s.GroupIdentifier) && !string.IsNullOrEmpty(s.ScannerType))
                    .Take(maxCompletenessRowsPerCycle)
                    .ToList();

                var existingGroupRowCount = completenessRows
                    .Count(s => existingGroupSet.Contains($"{s.GroupIdentifier}|{s.ScannerType}".ToUpperInvariant()));

                // ✅ DIAGNOSTIC: Log completeness rows found
                _logger.LogInformation("[INTAKE] Found {Count} completeness rows matching criteria ({Existing} for existing groups will be checked for late-arriving containers, from {Total} matching rows)",
                    completenessRows.Count, existingGroupRowCount, allMatchingRows.Count);

                if (!completenessRows.Any())
                {
                    _logger.LogInformation("[INTAKE] No completeness rows found - exiting early");
                    return;
                }

                var lookup = await BuildCompletenessLookupAsync(db, completenessRows, stoppingToken);

                // ✅ DIAGNOSTIC: Log lookup built
                _logger.LogInformation("[INTAKE] Built completeness lookup");

                var validRows = new List<ContainerCompletenessStatus>();
                var rowsProcessedCount = 0;
                var lastProgressLog = DateTime.UtcNow;

                foreach (var status in completenessRows)
                {
                    rowsProcessedCount++;

                    // ✅ OPTIMIZATION: Check time limit during processing
                    var rowsElapsed = DateTime.UtcNow - startTime;
                    if (rowsElapsed > maxExecutionTime)
                    {
                        _logger.LogWarning("[INTAKE] Time limit reached ({MaxMinutes} min), stopping intake workflow to allow assignment checks. Processed {Processed}/{Total} completeness rows",
                            maxExecutionTime.TotalMinutes, rowsProcessedCount, completenessRows.Count);
                        break;
                    }

                    // ✅ DIAGNOSTIC: Log progress every 1000 rows or every 10 seconds
                    var timeSinceLastLog = DateTime.UtcNow - lastProgressLog;
                    if (rowsProcessedCount % 1000 == 0 || timeSinceLastLog.TotalSeconds >= 10)
                    {
                        _logger.LogInformation("[INTAKE] Progress: Processing completeness rows {Processed}/{Total} ({Percent:F1}%) - Elapsed: {Elapsed:F1}s",
                            rowsProcessedCount, completenessRows.Count, (rowsProcessedCount * 100.0 / completenessRows.Count), rowsElapsed.TotalSeconds);
                        lastProgressLog = DateTime.UtcNow;
                    }

                    var ensured = await EnsureCompletenessFlagsAsync(db, status, lookup, stoppingToken);
                    if (ensured)
                    {
                        validRows.Add(status);
                    }
                }

                // ✅ DIAGNOSTIC: Log valid rows found
                _logger.LogInformation("[INTAKE] Found {Count} valid rows after ensuring completeness flags", validRows.Count);

                if (!validRows.Any())
                {
                    _logger.LogInformation("[INTAKE] No valid rows found - exiting early");
                    return;
                }

                var readyGroups = validRows
                    .Where(s => !string.IsNullOrWhiteSpace(s.GroupIdentifier))
                    .GroupBy(s => new { s.GroupIdentifier, s.ScannerType })
                    .Select(g => new { g.Key.GroupIdentifier, g.Key.ScannerType })
                    .ToList();

                // ✅ DIAGNOSTIC: Log ready groups found
                _logger.LogInformation("[INTAKE] Found {Count} ready groups", readyGroups.Count);

                // ✅ OPTIMIZATION: Limit groups processed per cycle
                var groupsToProcess = readyGroups.Take(maxGroupsPerCycle).ToList();
                var groupsSkipped = readyGroups.Count - groupsToProcess.Count;

                // ✅ DIAGNOSTIC: Always log batch processing (even if groupsSkipped == 0)
                _logger.LogInformation("[INTAKE] Processing {Processed}/{Total} groups this cycle (batch limit: {MaxGroups}). Remaining {Remaining} will be processed in next cycle",
                    groupsToProcess.Count, readyGroups.Count, maxGroupsPerCycle, groupsSkipped);

                var processedCount = 0;
                var createdCount = 0;
                var updatedCount = 0;
                var localProcessedCount = 0;
                var localCreatedCount = 0;
                var localUpdatedCount = 0;

                foreach (var g in groupsToProcess)
                {
                    // ✅ OPTIMIZATION: Check time limit before processing each group
                    if (DateTime.UtcNow - startTime > maxExecutionTime)
                    {
                        _logger.LogWarning("[INTAKE] Time limit reached ({MaxMinutes} min), stopping intake workflow. Processed {Processed}/{Total} groups in this batch",
                            maxExecutionTime.TotalMinutes, processedCount, groupsToProcess.Count);
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(g.GroupIdentifier)) continue;

                    if (IsCompositeContainerPairIdentifier(g.GroupIdentifier))
                    {
                        _logger.LogWarning(
                            "[INTAKE] Skipping composite scan-pair identifier {GroupIdentifier}. Split jobs may use container pairs, but AnalysisGroup identifiers must be cargo/record keys.",
                            g.GroupIdentifier);
                        continue;
                    }

                    var containersInGroup = validRows
                        .Where(s => s.GroupIdentifier == g.GroupIdentifier)
                        .ToList();

                    if (!containersInGroup.Any()) continue;

                    var allContainersInGroup = await db.ContainerCompletenessStatuses
                        .Where(s => s.GroupIdentifier == g.GroupIdentifier)
                        .ToListAsync(stoppingToken);

                    var totalContainers = allContainersInGroup.Count;
                    var readyContainersWithImages = allContainersInGroup.Where(c => c.HasImageData).ToList();
                    var pendingContainersNoImages = allContainersInGroup.Where(c => !c.HasImageData).ToList();

                    // ✅ WAVE PROCESSING: When enabled, create partial waves for groups with mixed image availability
                    if (settings.EnableWaveProcessing && pendingContainersNoImages.Count > 0 && readyContainersWithImages.Count > 0)
                    {
                        await ProcessWaveIntakeAsync(db, g.GroupIdentifier!, g.ScannerType, allContainersInGroup,
                            readyContainersWithImages, pendingContainersNoImages, stoppingToken);
                        Interlocked.Increment(ref localCreatedCount);
                        Interlocked.Increment(ref localProcessedCount);
                        continue; // Wave logic handled group creation; skip normal path
                    }

                    var imageAnalysisCount = allContainersInGroup.Count(c => c.WorkflowStage == "ImageAnalysis");
                    var auditCount = allContainersInGroup.Count(c => c.WorkflowStage == "Audit");
                    var completedCount = allContainersInGroup.Count(c => c.WorkflowStage == "PendingSubmission" || c.WorkflowStage == "Submitted" || c.WorkflowStage == "Completed");
                    var pendingCount = allContainersInGroup.Count(c => string.IsNullOrEmpty(c.WorkflowStage) || c.WorkflowStage == "Pending");

                    var initialStatus = WorkflowStageStatusHelper.ComputeStatusFromWorkflowStage(
                        totalContainers, imageAnalysisCount, auditCount, completedCount, pendingCount);
                    if (initialStatus == null)
                    {
                        _logger.LogWarning(
                            "[INTAKE] Skipping group {GroupIdentifier} - unexpected WorkflowStage distribution",
                            g.GroupIdentifier);
                        continue;
                    }

                    // ✅ DIAGNOSTIC: Log WorkflowStage distribution before processing
                    _logger.LogDebug(
                        "[INTAKE] Processing group {GroupIdentifier}: Total={Total}, ImageAnalysis={ImageAnalysis}, Pending={Pending}, Audit={Audit}, Completed={Completed}, InitialStatus={Status}",
                        g.GroupIdentifier, totalContainers, imageAnalysisCount, pendingCount, auditCount, completedCount, initialStatus);

                    var strategy = db.Database.CreateExecutionStrategy();
                    try
                    {
                        await strategy.ExecuteAsync(async () =>
                        {
                            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                            try
                            {
                                var oldestContainerDate = allContainersInGroup
                                    .Where(c => c.UpdatedAt != default(DateTime))
                                    .Select(c => c.UpdatedAt)
                                    .DefaultIfEmpty(DateTime.UtcNow)
                                    .Min();
                                var ageInHours = (DateTime.UtcNow - oldestContainerDate).TotalHours;
                                var containerCount = allContainersInGroup.Count;
                                var calculatedPriority = Math.Min(100, (int)(ageInHours * 2) + containerCount);

                                var wasInserted = await MergeAnalysisGroupIfNotExistsAsync(
                                    db, g.GroupIdentifier!, g.ScannerType, initialStatus, calculatedPriority, stoppingToken);
                                if (wasInserted)
                                {
                                    Interlocked.Increment(ref localCreatedCount);
                                    _logger.LogInformation(
                                        "[INTAKE] Created new AnalysisGroup {GroupIdentifier} with Status={Status} (TotalContainers={Total}, ImageAnalysis={ImageAnalysis}, Pending={Pending})",
                                        g.GroupIdentifier, initialStatus, totalContainers, imageAnalysisCount, pendingCount);
                                }
                                else
                                {
                                    _logger.LogInformation(
                                        "[INTAKE] AnalysisGroup {GroupIdentifier} already exists - checking for late-arriving containers to add",
                                        g.GroupIdentifier);
                                }

                                var group = await db.AnalysisGroups
                                    .AsTracking()
                                    .FirstOrDefaultAsync(x => x.GroupIdentifier == g.GroupIdentifier && x.ScannerType == g.ScannerType, stoppingToken);

                                if (group == null)
                                {
                                    _logger.LogError("[INTAKE] MERGE succeeded but group not found for {GroupIdentifier}|{ScannerType}", g.GroupIdentifier, g.ScannerType);
                                    await transaction.RollbackAsync(stoppingToken);
                                    return;
                                }

                                // 1.15.0 — link to the matching RecordCompletenessStatus so the record view
                                // can navigate from a group to its declaration. Best-effort; no-op for Pattern A
                                // and date-split groups that don't match a declaration number directly.
                                await TryLinkGroupToRecordAsync(db, group.Id, group.GroupIdentifier, group.NormalizedGroupIdentifier, stoppingToken);

                                // ✅ TERMINAL-STATE GUARD: Never reopen a group that has progressed past analyst review.
                                // Late-arriving containers will get AnalysisRecords inserted below, but the parent
                                // group's status is left alone — manual review handles reopening case-by-case.
                                var terminalStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    AnalysisStatuses.AnalystCompleted,
                                    AnalysisStatuses.AuditAssigned,
                                    AnalysisStatuses.AuditCompleted,
                                    AnalysisStatuses.Submitted,
                                    AnalysisStatuses.Completed,
                                    AnalysisStatuses.PartiallyCompleted
                                };
                                var groupIsTerminal = terminalStatuses.Contains(group.Status);

                                if (!wasInserted && !groupIsTerminal && group.Status != initialStatus)
                                {
                                    // Sprint 5G2 / B1: routed through facade. The previous local
                                    // transition table is now redundant — AnalysisStatusValidator
                                    // covers the same edges (Ready/AnalystAssigned/AnalystCompleted/
                                    // AuditAssigned/AuditCompleted → Completed) and the facade's
                                    // ValidateTransition throws if the edge isn't legal.
                                    _logger.LogInformation(
                                        "[INTAKE] Updating group {GroupIdentifier} from {OldStatus} to {NewStatus}",
                                        g.GroupIdentifier, group.Status, initialStatus);
                                    try
                                    {
                                        await AnalysisGroupStateMachine.TransitionAsync(
                                            db, group, initialStatus,
                                            triggerName: "IntakeWaveProgression",
                                            actor: "ORCHESTRATOR-INTAKE",
                                            reason: $"Wave-progression reconciliation: existing group reobserved during intake; container completeness implies {initialStatus}.",
                                            correlationId: null,
                                            ct: stoppingToken);
                                        Interlocked.Increment(ref localUpdatedCount);
                                    }
                                    catch (InvalidOperationException ex)
                                    {
                                        // Illegal transition (e.g. terminal Cancelled/Archived to
                                        // anything). Log + skip rather than crash the intake loop.
                                        _logger.LogWarning(
                                            ex,
                                            "[INTAKE] Skipping illegal transition {From}→{To} for group {GroupIdentifier}",
                                            group.Status, initialStatus, g.GroupIdentifier);
                                    }
                                }

                                Interlocked.Increment(ref localProcessedCount);

                                var containerNumbers = await db.ContainerCompletenessStatuses
                                    .Where(s => s.GroupIdentifier == g.GroupIdentifier)
                                    .Select(s => s.ContainerNumber)
                                    .Where(c => !string.IsNullOrWhiteSpace(c))
                                    .Select(c => c!.Trim().ToUpperInvariant())
                                    .Distinct()
                                    .ToListAsync(stoppingToken);

                                if (containerNumbers.Count == 0)
                                {
                                    await transaction.CommitAsync(stoppingToken);
                                    return;
                                }

                                var existingNumbers = await db.AnalysisRecords
                                    .Where(r => r.GroupId == group.Id)
                                    .Select(r => r.ContainerNumber)
                                    .ToListAsync(stoppingToken);
                                var existingSet = new HashSet<string>(
                                    existingNumbers
                                        .Where(c => !string.IsNullOrWhiteSpace(c))
                                        .Select(c => c!.Trim().ToUpperInvariant()),
                                    StringComparer.OrdinalIgnoreCase);

                                var newContainerNumbers = containerNumbers
                                    .Where(c => !existingSet.Contains(c))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                                if (newContainerNumbers.Count > 0)
                                {
                                    foreach (var containerNumber in newContainerNumbers)
                                    {
                                        db.AnalysisRecords.Add(new AnalysisRecord
                                        {
                                            GroupId = group.Id,
                                            ContainerNumber = containerNumber,
                                            ScannerType = g.ScannerType,
                                            Status = "Ready",
                                            CreatedAtUtc = DateTime.UtcNow
                                        });
                                    }

                                    try
                                    {
                                        await db.SaveChangesAsync(stoppingToken);
                                        if (wasInserted)
                                        {
                                            _logger.LogInformation(
                                                "[INTAKE] Created {Count} new analysis records for new group {Group}: {Containers}",
                                                newContainerNumbers.Count, group.GroupIdentifier, string.Join(", ", newContainerNumbers));
                                        }
                                        else if (groupIsTerminal)
                                        {
                                            _logger.LogWarning(
                                                "[INTAKE] Added {Count} late-arriving analysis records to TERMINAL group {Group} (Status={Status}): {Containers}. Group status left unchanged - manual review required to reopen.",
                                                newContainerNumbers.Count, group.GroupIdentifier, group.Status, string.Join(", ", newContainerNumbers));
                                        }
                                        else
                                        {
                                            _logger.LogInformation(
                                                "[INTAKE] Added {Count} late-arriving analysis records to existing group {Group} (Status={Status}): {Containers}",
                                                newContainerNumbers.Count, group.GroupIdentifier, group.Status, string.Join(", ", newContainerNumbers));
                                        }
                                    }
                                    catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                                    {
                                        await transaction.RollbackAsync(stoppingToken);
                                        db.ChangeTracker.Clear();
                                        _logger.LogDebug(ex, "[INTAKE] Duplicate analysis record detected for group {Group}", group.GroupIdentifier);
                                        return;
                                    }
                                }

                                var containersToUpdate = await db.ContainerCompletenessStatuses
                                    .AsTracking()
                                    .Where(s => s.GroupIdentifier == g.GroupIdentifier
                                        && (string.IsNullOrEmpty(s.WorkflowStage) || s.WorkflowStage == "Pending"))
                                    .ToListAsync(stoppingToken);

                                if (containersToUpdate.Count > 0)
                                {
                                    foreach (var container in containersToUpdate)
                                    {
                                        container.WorkflowStage = "ImageAnalysis";
                                        container.UpdatedAt = DateTime.UtcNow;
                                    }

                                    await db.SaveChangesAsync(stoppingToken);
                                    _logger.LogDebug(
                                        "[INTAKE] Updated WorkflowStage to 'ImageAnalysis' for {Count} containers in group {Group}",
                                        containersToUpdate.Count, g.GroupIdentifier);
                                }

                                await transaction.CommitAsync(stoppingToken);
                            }
                            catch (Exception)
                            {
                                await transaction.RollbackAsync(stoppingToken);
                                db.ChangeTracker.Clear();
                                throw;
                            }
                            finally
                            {
                                db.ChangeTracker.Clear();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[INTAKE] Failed to process analysis group {GroupIdentifier}", g.GroupIdentifier);
                    }
                }

                processedCount = localProcessedCount;
                createdCount = localCreatedCount;
                updatedCount = localUpdatedCount;

                var elapsed = DateTime.UtcNow - startTime;
                var totalGroups = readyGroups.Count;
                var remainingGroups = totalGroups - processedCount;

                if (groupsSkipped > 0 || processedCount < groupsToProcess.Count)
                {
                    _logger.LogInformation(
                        "[INTAKE] Intake workflow completed (partial batch): Processed {Processed}/{Total} groups ({Created} created, {Updated} updated) in {ElapsedMs:F0}ms. " +
                        "Skipped {Skipped} groups (batch limit). {Remaining} remaining will be processed in next cycle",
                        processedCount, totalGroups, createdCount, updatedCount, elapsed.TotalMilliseconds, groupsSkipped, remainingGroups);
                }
                else
                {
                    _logger.LogInformation(
                        "[INTAKE] Intake workflow completed: Processed {Processed} groups ({Created} created, {Updated} updated) in {ElapsedMs:F0}ms",
                        processedCount, createdCount, updatedCount, elapsed.TotalMilliseconds);
                }
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INTAKE] Error in intake workflow");
            }
        }

        #endregion

        #region Assignment Workflow

        private async Task RunAssignmentWorkflowAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            DateTime now,
            CancellationToken stoppingToken)
        {
            var assignmentMode = string.IsNullOrEmpty(settings.AssignmentMode) ? "Manual" : settings.AssignmentMode;
            _logger.LogDebug("[ASSIGNMENT] Starting assignment workflow (Mode={Mode}, MaxConcurrent={MaxConcurrent})",
                assignmentMode, settings.MaxConcurrentPerUser);

            try
            {
                // Always reclaim expired leases (method logs count internally)
                await ReclaimExpiredAssignmentsAsync(db, now, stoppingToken);

                // Clean up expired user readiness
                await CleanupExpiredUserReadinessAsync(db, now, stoppingToken);

                // Validate assignments periodically (every 30 seconds)
                var timeSinceLastValidation = (now - _lastValidationTime).TotalSeconds;
                if (timeSinceLastValidation >= 30)
                {
                    await ValidateAssignmentsAsync(db, now, stoppingToken);
                    _lastValidationTime = now;
                }

                // Handle assignment based on mode
                if (assignmentMode == "Auto")
                {
                    await AutoAssignGroupsAsync(db, settings, now, stoppingToken);
                }
                else
                {
                    _logger.LogDebug("[ASSIGNMENT] Mode is '{Mode}' - skipping auto-assignment", assignmentMode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASSIGNMENT] Error in assignment workflow");
            }
            finally
            {
                _logger.LogDebug("[ASSIGNMENT] Assignment workflow completed");
            }
        }

        private async Task AutoAssignGroupsAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            DateTime now,
            CancellationToken stoppingToken)
        {
            _logger.LogDebug("[ASSIGNMENT] AutoAssignGroupsAsync - processing Analyst and Audit roles");
            try
            {
                await AutoAssignByRoleAsync(
                    db, settings, roleName: "Analyst",
                    eligibleStatus: AnalysisStatuses.Ready,
                    assignedStatus: AnalysisStatuses.AnalystAssigned,
                    now, stoppingToken);

                // Get available auditors
                await AutoAssignByRoleAsync(
                    db, settings, roleName: "Audit",
                    eligibleStatus: AnalysisStatuses.AnalystCompleted,
                    assignedStatus: AnalysisStatuses.AuditAssigned,
                    now, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASSIGNMENT] Error in AutoAssignGroupsAsync");
            }
        }

        private async Task AutoAssignByRoleAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            string roleName,
            string eligibleStatus,
            string assignedStatus,
            DateTime now,
            CancellationToken stoppingToken)
        {
            var readyGroups = await _readyGroupsCache.GetReadyGroupsForRoleAsync(roleName, eligibleStatus, stoppingToken);

            if (!readyGroups.Any())
            {
                // Throttled Information log so Audit-role polling is visible even
                // when there are zero AnalystCompleted groups (otherwise this branch
                // is silent at Information level and operators can't tell whether
                // the orchestrator is polling Audit at all).
                var nowFlag = DateTime.UtcNow;
                var shouldLog = !_lastNoGroupsLogByRole.TryGetValue(roleName, out var lastLog)
                    || (nowFlag - lastLog) >= _logThrottleInterval;
                if (shouldLog)
                {
                    _logger.LogInformation(
                        "[ASSIGNMENT] No ready groups for {Role} role (eligibleStatus={EligibleStatus}) - polling alive but eligible pool is empty (this message repeats every 5 min)",
                        roleName, eligibleStatus);
                    _lastNoGroupsLogByRole[roleName] = nowFlag;
                }
                return;
            }

            _logger.LogInformation("[ASSIGNMENT] Found {Count} ready groups for {Role} role", readyGroups.Count, roleName);

            // Get ready users for this role
            _logger.LogDebug("[ASSIGNMENT] Querying ready users for {Role} role", roleName);
            var readyUsers = await GetReadyUsersForRoleAsync(db, roleName, stoppingToken);

            if (readyUsers.Any())
            {
                _logger.LogInformation("[ASSIGNMENT] Found {Count} ready users for {Role} role: {Users}",
                    readyUsers.Count, roleName, string.Join(", ", readyUsers));
            }

            if (!readyUsers.Any())
            {
                var now2 = DateTime.UtcNow;
                var shouldLog = !_lastNoUsersLogByRole.TryGetValue(roleName, out var lastLog)
                    || (now2 - lastLog) >= _logThrottleInterval;
                if (shouldLog)
                {
                    _logger.LogInformation("[ASSIGNMENT] No ready users for role {Role} - assignments paused (this message repeats every 5 min)", roleName);
                    _lastNoUsersLogByRole[roleName] = now2;
                }
                return;
            }

            var maxConcurrent = settings.MaxConcurrentPerUser > 0 ? settings.MaxConcurrentPerUser : 5;
            var leaseMinutes = settings.LeaseMinutes > 0 ? settings.LeaseMinutes : 15;
            var leaseUntil = now.AddMinutes(leaseMinutes);

            _logger.LogInformation("[ASSIGNMENT] Assignment parameters: MaxConcurrent={MaxConcurrent}, LeaseMinutes={LeaseMinutes}, Strategy={Strategy}",
                maxConcurrent, leaseMinutes, settings.AutoAssignStrategy ?? "RoundRobin");

            var groupsProcessed = 0;
            var groupsSkipped = 0;
            var groupsAssigned = 0;
            var skipReasons = new Dictionary<string, int>();

            foreach (var group in readyGroups)
            {
                if (stoppingToken.IsCancellationRequested) break;
                groupsProcessed++;

                // Check if group already has active assignment
                var hasActiveAssignment = await db.AnalysisAssignments
                    .AnyAsync(a => a.GroupId == group.Id && a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);

                if (hasActiveAssignment)
                {
                    groupsSkipped++;
                    skipReasons["AlreadyAssigned"] = skipReasons.GetValueOrDefault("AlreadyAssigned", 0) + 1;
                    _logger.LogDebug("[ASSIGNMENT] Group {GroupId} ({GroupIdentifier}) already has active assignment - skipping",
                        group.Id, group.GroupIdentifier);
                    continue;
                }

                // Find available user (not at max concurrent)
                string? assignedUser = null;
                var strategy = settings.AutoAssignStrategy ?? "RoundRobin";

                if (strategy == "RoundRobin")
                {
                    assignedUser = GetNextUserRoundRobin(readyUsers, roleName);
                }
                else
                {
                    // LeastBusy strategy
                    assignedUser = await GetLeastBusyUserAsync(db, readyUsers, maxConcurrent, now, stoppingToken);
                }

                if (string.IsNullOrEmpty(assignedUser))
                {
                    groupsSkipped++;
                    skipReasons["NoUserSelected"] = skipReasons.GetValueOrDefault("NoUserSelected", 0) + 1;
                    _logger.LogWarning("[ASSIGNMENT] Group {GroupId} ({GroupIdentifier}) - No user selected (strategy: {Strategy}, users: {UserCount})",
                        group.Id, group.GroupIdentifier, strategy, readyUsers.Count);
                    continue;
                }

                // Check user's current assignments
                var userAssignments = await db.AnalysisAssignments
                    .CountAsync(a => a.AssignedTo == assignedUser && a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);

                if (userAssignments >= maxConcurrent)
                {
                    groupsSkipped++;
                    skipReasons["UserAtCapacity"] = skipReasons.GetValueOrDefault("UserAtCapacity", 0) + 1;
                    _logger.LogDebug("[ASSIGNMENT] Group {GroupId} ({GroupIdentifier}) - User {User} is at capacity ({Current}/{Max}) - skipping",
                        group.Id, group.GroupIdentifier, assignedUser, userAssignments, maxConcurrent);
                    continue;
                }

                try
                {
                    var assignStrategy = db.Database.CreateExecutionStrategy();
                    var wasAssigned = false;
                    AnalysisAssignment? assignment = null;

                    await assignStrategy.ExecuteAsync(async () =>
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                        var activeAssignmentsForGroup = await db.AnalysisAssignments
                            .AsTracking()
                            .Where(a => a.GroupId == group.Id && a.State == "Active")
                            .ToListAsync(stoppingToken);

                        var hasCurrentActiveAssignment = activeAssignmentsForGroup
                            .Any(a => a.LeaseUntilUtc == null || a.LeaseUntilUtc > now);

                        if (hasCurrentActiveAssignment)
                        {
                            await tx.RollbackAsync(stoppingToken);
                            return;
                        }

                        foreach (var staleAssignment in activeAssignmentsForGroup)
                        {
                            staleAssignment.State = "Expired";
                            staleAssignment.UpdatedAtUtc = now;
                        }

                        if (activeAssignmentsForGroup.Count > 0)
                        {
                            _logger.LogInformation(
                                "[ASSIGNMENT] Reclaimed {Count} stale active assignment(s) for group {GroupId} during assignment guard",
                                activeAssignmentsForGroup.Count,
                                group.Id);

                            // Clear the partial unique index on active group assignments before
                            // inserting the replacement row in the same transaction.
                            await db.SaveChangesAsync(stoppingToken);
                        }

                        assignment = new AnalysisAssignment
                        {
                            GroupId = group.Id,
                            AssignedTo = assignedUser,
                            Role = roleName,
                            LeaseUntilUtc = leaseUntil,
                            State = "Active",
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now
                        };

                        db.AnalysisAssignments.Add(assignment);

                        var trackedGroup = await db.AnalysisGroups
                            .AsTracking()
                            .FirstOrDefaultAsync(g => g.Id == group.Id, stoppingToken);
                        if (trackedGroup != null)
                        {
                            // Sprint 5G2 / B1: routed through facade. The facade's SaveChangesAsync
                            // commits the AnalysisAssignment.Add above too — both writes share the
                            // tracked context and the outer transaction (tx.CommitAsync below).
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, trackedGroup, assignedStatus,
                                triggerName: $"AssignmentTo{roleName}",
                                actor: "ORCHESTRATOR-ASSIGNMENT",
                                reason: $"Auto-assigned to {assignedUser} (role={roleName}, lease={leaseUntil:O}).",
                                correlationId: null,
                                ct: stoppingToken);
                        }
                        else
                        {
                            // Group disappeared between selection and assignment — persist the
                            // AnalysisAssignment row for forensic audit (the assigned user and
                            // lease still represent intent), then commit. This matches the
                            // pre-refactor behaviour where the SaveChangesAsync ran regardless.
                            await db.SaveChangesAsync(stoppingToken);
                        }
                        await tx.CommitAsync(stoppingToken);
                        wasAssigned = true;
                    });

                    if (wasAssigned && assignment != null)
                    {
                        groupsAssigned++;
                        _logger.LogInformation(
                            "[ASSIGNMENT-EVENT] Created | AssignmentId={AssignmentId} | GroupId={GroupId} | GroupIdentifier={GroupIdentifier} | User={User} | Role={Role} | LeaseUntil={LeaseUntil} | Reason=AutoAssign",
                            assignment.Id, group.Id, group.GroupIdentifier, assignedUser, roleName, leaseUntil);

                        await _readyGroupsCache.InvalidateCacheAsync(roleName, eligibleStatus, stoppingToken);

                        // 2026-05-05 (Sprint 2C, audit 4.02): materialize the queue entry
                        // immediately after the assignment commit. Without this the user's
                        // workbench has to wait for the next reconciliation pass (1-2 min)
                        // before the just-created assignment shows up. Failures here are
                        // best-effort — the periodic reconciler picks up misses.
                        try
                        {
                            await _readyGroupsCache.UpsertQueueEntryAsync(db, assignment.Id, stoppingToken);
                        }
                        catch (Exception upsertEx)
                        {
                            _logger.LogWarning(upsertEx,
                                "[ASSIGNMENT] UpsertQueueEntryAsync failed for AssignmentId={AssignmentId} (Group={GroupId}, User={User}); reconciler will catch up next cycle",
                                assignment.Id, group.Id, assignedUser);
                        }
                    }
                    else
                    {
                        groupsSkipped++;
                        skipReasons["AlreadyAssigned"] = skipReasons.GetValueOrDefault("AlreadyAssigned", 0) + 1;
                    }
                }
                catch (Exception ex)
                {
                    groupsSkipped++;
                    skipReasons["SaveError"] = skipReasons.GetValueOrDefault("SaveError", 0) + 1;
                    _logger.LogWarning(ex, "[ASSIGNMENT] Failed to assign group {GroupId} ({GroupIdentifier}) to {User}: {Error}",
                        group.Id, group.GroupIdentifier, assignedUser, ex.Message);
                }
            }

            // ✅ DIAGNOSTIC: Log summary
            _logger.LogInformation("[ASSIGNMENT] AutoAssignByRoleAsync summary for {Role}: Processed={Processed}, Assigned={Assigned}, Skipped={Skipped}",
                roleName, groupsProcessed, groupsAssigned, groupsSkipped);

            if (skipReasons.Any())
            {
                var reasons = string.Join(", ", skipReasons.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                _logger.LogInformation("[ASSIGNMENT] Skip reasons for {Role}: {Reasons}", roleName, reasons);
            }
        }

        private string? GetNextUserRoundRobin(List<string> users, string roleName)
        {
            if (!users.Any()) return null;

            lock (_lastAssignedUserByRole)
            {
                if (!_lastAssignedUserByRole.TryGetValue(roleName, out var lastUser) || !users.Contains(lastUser))
                {
                    lastUser = users[0];
                }
                else
                {
                    var currentIndex = users.IndexOf(lastUser);
                    var nextIndex = (currentIndex + 1) % users.Count;
                    lastUser = users[nextIndex];
                }

                _lastAssignedUserByRole[roleName] = lastUser;
                return lastUser;
            }
        }

        private async Task<string?> GetLeastBusyUserAsync(
            ApplicationDbContext db,
            List<string> users,
            int maxConcurrent,
            DateTime now,
            CancellationToken stoppingToken)
        {
            var userLoads = new Dictionary<string, int>();

            foreach (var user in users)
            {
                var count = await db.AnalysisAssignments
                    .CountAsync(a => a.AssignedTo == user && a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);
                userLoads[user] = count;
            }

            var availableUsers = userLoads
                .Where(u => u.Value < maxConcurrent)
                .OrderBy(u => u.Value)
                .ThenBy(u => u.Key)
                .Select(u => u.Key)
                .ToList();

            return availableUsers.FirstOrDefault();
        }

        private async Task<List<string>> GetReadyUsersForRoleAsync(
            ApplicationDbContext db,
            string roleName,
            CancellationToken stoppingToken)
        {
            var roleNameUpper = roleName.ToUpperInvariant();
            // ✅ MODIFIED: Increased heartbeat window from 2 to 60 minutes to rely more on database
            // Users with IsReady=true and heartbeat within 60 minutes are considered ready
            var maxIdleMinutes = _configuration.GetValue<int>("ImageAnalysis:MaxIdleMinutesForReadiness", 60);
            var maxIdleTime = TimeSpan.FromMinutes(maxIdleMinutes);
            var dbMaxIdle = DateTime.UtcNow.AddMinutes(-maxIdleMinutes);

            // ✅ MODIFIED: Database is now PRIMARY source for user readiness
            // This allows assignments even when users aren't actively connected via SignalR
            var allReadinessRecords = await db.UserReadiness
                .Where(r => r.Role.ToUpper() == roleNameUpper)
                .Select(r => new { r.Username, r.IsReady, r.LastHeartbeat })
                .ToListAsync(stoppingToken);

            _logger.LogInformation("[ASSIGNMENT] Total UserReadiness records for role '{Role}': {TotalCount}",
                roleName, allReadinessRecords.Count);

            // Log details about each record for diagnosis
            foreach (var record in allReadinessRecords)
            {
                var timeSinceHeartbeat = DateTime.UtcNow - record.LastHeartbeat;
                var isExpired = record.LastHeartbeat < dbMaxIdle;
                var status = !record.IsReady ? "NOT READY" : isExpired ? "HEARTBEAT EXPIRED" : "READY";
                _logger.LogDebug("[ASSIGNMENT] UserReadiness: {Username} - {Status} (IsReady: {IsReady}, Heartbeat: {Heartbeat}, Age: {AgeMinutes:F1} min)",
                    record.Username, status, record.IsReady, record.LastHeartbeat, timeSinceHeartbeat.TotalMinutes);
            }

            // ✅ MODIFIED: Database users are PRIMARY - include users with IsReady=true and heartbeat within 60 minutes
            var dbReadyUsers = allReadinessRecords
                .Where(r => r.IsReady && r.LastHeartbeat >= dbMaxIdle)
                .Select(r => r.Username)
                .Distinct()
                .ToList();

            _logger.LogDebug("[ASSIGNMENT] DB ready users for '{Role}': {Count} (idle timeout: {MaxIdleMinutes} min)",
                roleName, dbReadyUsers.Count, maxIdleMinutes);

            // ✅ MODIFIED: SignalR is now optional fallback (for users who might not be in database)
            // SignalR connections indicate active connection, but we don't require it
            var signalRReadyUsers = UserReadinessStateProvider.GetReadyUsers(roleName, maxIdleTime);
            _logger.LogDebug("[ASSIGNMENT] Found {Count} ready users from SignalR for role '{Role}' (optional fallback)",
                signalRReadyUsers.Count, roleName);

            // ✅ MODIFIED: Prioritize database users, add SignalR users as fallback (to avoid duplicates)
            var combinedReadyUsers = dbReadyUsers
                .Concat(signalRReadyUsers.Where(u => !dbReadyUsers.Contains(u, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogDebug("[ASSIGNMENT] Combined ready users for '{Role}': {Count} (DB: {DbCount}, SignalR: {SignalRCount})",
                roleName, combinedReadyUsers.Count, dbReadyUsers.Count, signalRReadyUsers.Count);

            // ✅ MODIFIED: Database is primary source - assignments can be created for users with IsReady=true
            // even if they don't have active SignalR connections

            // Verify these users exist in database and have correct role
            if (!combinedReadyUsers.Any())
            {
                return new List<string>();
            }

            // ✅ FIX: Load all active users first, then filter in memory to avoid Contains() CTE generation
            var validUsers = new List<string>();

            // Load roles first
            var matchingRoles = await db.Roles
                .Where(r => r.IsActive && r.Name.ToUpper() == roleNameUpper)
                .Select(r => r.Id)
                .ToListAsync(stoppingToken);

            if (combinedReadyUsers.Count > 0 && matchingRoles.Any())
            {
                // ✅ FIX: Load all active users first, then filter in memory (avoids Contains() CTE generation)
                var allActiveUsers = await db.Users
                    .AsNoTracking()
                    .Where(u => u.IsActive && u.RoleId != null)
                    .Select(u => new { u.Username, u.RoleId })
                    .ToListAsync(stoppingToken);

                // Filter in memory
                var matchingRoleSet = new HashSet<int>(matchingRoles);
                var combinedReadyUsersSet = new HashSet<string>(combinedReadyUsers, StringComparer.OrdinalIgnoreCase);

                validUsers = allActiveUsers
                    .Where(u => combinedReadyUsersSet.Contains(u.Username)
                        && u.RoleId.HasValue
                        && matchingRoleSet.Contains(u.RoleId.Value))
                    .Select(u => u.Username)
                    .Distinct()
                    .ToList();
            }

            _logger.LogDebug("[ASSIGNMENT] Verified {Count} users with correct role for '{Role}'",
                validUsers.Count, roleName);

            return validUsers;
        }

        private async Task<int> ReclaimExpiredAssignmentsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken stoppingToken)
        {
            // ✅ FIX: Don't expire assignments that were accessed recently (active work session)
            // If LastAccessedAtUtc is within last 30 minutes, consider it an active work session and don't expire
            var activeWorkThreshold = now.AddMinutes(-30);

            List<AnalysisAssignment> expired;
            try
            {
                // Try query with LastAccessedAtUtc (requires migration: AddLastAccessedAtUtcToAnalysisAssignments)
                expired = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.State == "Active"
                        && a.LeaseUntilUtc != null
                        && a.LeaseUntilUtc < now
                        && (a.LastAccessedAtUtc == null || a.LastAccessedAtUtc < activeWorkThreshold))
                    .ToListAsync(stoppingToken);
            }
            catch (Exception ex) when (ex.Message.Contains("LastAccessedAtUtc") || ex.Message.Contains("Invalid column name"))
            {
                _logger.LogWarning("LastAccessedAtUtc column not found. Using fallback query without active work session protection. Please run migration: AddLastAccessedAtUtcToAnalysisAssignments");
                expired = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.State == "Active" && a.LeaseUntilUtc != null && a.LeaseUntilUtc < now)
                    .ToListAsync(stoppingToken);
            }

            if (expired.Any())
            {
                // 2026-05-05 (Sprint 2C, audit 4.03): ported from the now-deleted
                // AssignmentWorker.cs. Walk every expired assignment and transition
                // its parent AG back to Ready / AnalystCompleted — UNLESS the AG is
                // an orphan (no boedocumentid + no active CBR), in which case it
                // moves to Cancelled to break the lease re-issue cycle. Pre-2.16.1
                // these orphans bounced through AnalystAssigned → Ready every
                // 10 min, surfacing phantom assignments on the analyst workbench.
                foreach (var assignment in expired)
                {
                    assignment.State = "Expired";
                    assignment.UpdatedAtUtc = now;
                    _logger.LogDebug("[ASSIGNMENT] Expiring assignment {AssignmentId} for user {User} (LeaseUntil: {LeaseUntil}, LastAccessed: {LastAccessed})",
                        assignment.Id, assignment.AssignedTo, assignment.LeaseUntilUtc, assignment.LastAccessedAtUtc);

                    var groupToUpdate = await db.AnalysisGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(g => g.Id == assignment.GroupId, stoppingToken);
                    if (groupToUpdate != null &&
                        (groupToUpdate.Status == AnalysisStatuses.AnalystAssigned ||
                         groupToUpdate.Status == AnalysisStatuses.AuditAssigned))
                    {
                        if (await IsOrphanAnalysisGroupAsync(db, groupToUpdate.Id, stoppingToken))
                        {
                            // Sprint 5G2 / B1: route through the state-machine facade. AnalystAssigned/AuditAssigned → Cancelled
                            // are in the legal table.
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, groupToUpdate, AnalysisStatuses.Cancelled,
                                triggerName: "ExpiredLeaseOrphanCancellation",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: $"Orphan AG with expired {assignment.Role} assignment: no boedocumentid, no active CBR.",
                                correlationId: null,
                                ct: stoppingToken);
                            _logger.LogInformation(
                                "[CLEANUP] Orphan AG {GroupId} ({GroupIdentifier}) → Cancelled (no boedocumentid, no active CBR; lease sweeper would otherwise re-issue assignment)",
                                groupToUpdate.Id, groupToUpdate.GroupIdentifier);
                        }
                        else
                        {
                            var revertTo = assignment.Role == "Audit"
                                ? AnalysisStatuses.AnalystCompleted
                                : AnalysisStatuses.Ready;
                            // Sprint 5G2 / B1: route through the state-machine facade. AuditAssigned → AnalystCompleted
                            // and AnalystAssigned → Ready are in the legal table.
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, groupToUpdate, revertTo,
                                triggerName: "ExpiredLeaseRevert",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: $"Lease expired for {assignment.Role} assignment {assignment.Id} (user={assignment.AssignedTo}); reverting to {revertTo}.",
                                correlationId: null,
                                ct: stoppingToken);
                        }
                        groupToUpdate.UpdatedAtUtc = now;
                    }
                }

                await db.SaveChangesAsync(stoppingToken);

                // Remove expired assignments from the materialized ready-groups queue
                // so dashboards stop showing them.
                foreach (var assignment in expired)
                {
                    try { await _readyGroupsCache.RemoveQueueEntryAsync(db, assignment.Id, stoppingToken); }
                    catch { /* reconciliation catches misses */ }
                }

                foreach (var cacheTarget in expired
                    .Where(a => a.Role == "Analyst" || a.Role == "Audit")
                    .Select(a => new
                    {
                        Role = a.Role,
                        Status = a.Role == "Audit"
                            ? AnalysisStatuses.AnalystCompleted
                            : AnalysisStatuses.Ready
                    })
                    .Distinct())
                {
                    await _readyGroupsCache.InvalidateCacheAsync(
                        cacheTarget.Role,
                        cacheTarget.Status,
                        stoppingToken);
                }

                _logger.LogInformation("[ASSIGNMENT] Reclaimed {Count} expired assignments", expired.Count);
            }

            return expired.Count;
        }

        /// <summary>
        /// 2026-05-05 (Sprint 2C, audit 4.03): orphan-AG predicate ported from the
        /// now-deleted AssignmentWorker.cs. An AG is "orphan" iff every container
        /// in it has NULL <c>BOEDocumentId</c> on its CCS row AND zero active
        /// <c>ContainerBOERelations</c>. These AGs have no actionable match data;
        /// the lease sweeper should not keep cycling them through analyst assignment.
        ///
        /// Mirrors the inline predicate already in
        /// <see cref="ReadyGroupsCacheService.GetReadyGroupsForRoleAsync"/> — keep
        /// the two in sync. Pushed down to SQL via EF Core (single round-trip).
        /// </summary>
        private static async Task<bool> IsOrphanAnalysisGroupAsync(
            ApplicationDbContext db,
            Guid groupId,
            CancellationToken ct)
        {
            var hasMatch = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Id == groupId && (
                    db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                        db.ContainerCompletenessStatuses.Any(c =>
                            c.ContainerNumber == r.ContainerNumber && c.BOEDocumentId != null))
                    ||
                    db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                        db.ContainerBOERelations.Any(cbr =>
                            cbr.ContainerNumber == r.ContainerNumber && cbr.IsActive))
                ))
                .AnyAsync(ct);
            return !hasMatch;
        }

        private async Task CleanupExpiredUserReadinessAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken stoppingToken)
        {
            var maxIdleMinutes = _configuration.GetValue<int>("ImageAnalysis:MaxIdleMinutesForReadiness", 60);
            var cutoff = now.AddMinutes(-maxIdleMinutes);

            var expired = await db.UserReadiness
                .AsTracking()
                .Where(r => r.IsReady && r.LastHeartbeat < cutoff)
                .ToListAsync(stoppingToken);

            if (expired.Any())
            {
                foreach (var readiness in expired)
                {
                    readiness.IsReady = false;
                    readiness.LastChangedAt = now;
                }

                await db.SaveChangesAsync(stoppingToken);
            }
        }

        private async Task ValidateAssignmentsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken stoppingToken)
        {
            // Validate assignments are still valid
            var invalidAssignments = await db.AnalysisAssignments
                .AsTracking()
                .Where(a => a.State == "Active" && !db.AnalysisGroups.Any(g => g.Id == a.GroupId))
                .ToListAsync(stoppingToken);

            if (invalidAssignments.Any())
            {
                // 2026-05-05 (Sprint 2C, audit 4.03): mirror of ReclaimExpiredAssignmentsAsync —
                // walk parent AGs of each invalid assignment and transition them to Cancelled
                // (orphan) or Ready/AnalystCompleted. Most invalid assignments here have no
                // matching AG (the predicate filters those whose group was deleted) so the
                // group lookup returns null and the loop is a no-op for them, but the few
                // that DO have a still-existing group are handled correctly.
                foreach (var assignment in invalidAssignments)
                {
                    assignment.State = "Released";
                    assignment.UpdatedAtUtc = now;

                    var groupToUpdate = await db.AnalysisGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(g => g.Id == assignment.GroupId, stoppingToken);
                    if (groupToUpdate != null &&
                        (groupToUpdate.Status == AnalysisStatuses.AnalystAssigned ||
                         groupToUpdate.Status == AnalysisStatuses.AuditAssigned))
                    {
                        if (await IsOrphanAnalysisGroupAsync(db, groupToUpdate.Id, stoppingToken))
                        {
                            // Sprint 5G2 / B1: route through the state-machine facade. AnalystAssigned/AuditAssigned → Cancelled
                            // are in the legal table.
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, groupToUpdate, AnalysisStatuses.Cancelled,
                                triggerName: "InvalidAssignmentOrphanCancellation",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: $"Orphan AG with invalid {assignment.Role} assignment: no boedocumentid, no active CBR.",
                                correlationId: null,
                                ct: stoppingToken);
                            _logger.LogInformation(
                                "[VALIDATION] Orphan AG {GroupId} ({GroupIdentifier}) → Cancelled (no boedocumentid, no active CBR)",
                                groupToUpdate.Id, groupToUpdate.GroupIdentifier);
                        }
                        else
                        {
                            var revertTo = assignment.Role == "Audit"
                                ? AnalysisStatuses.AnalystCompleted
                                : AnalysisStatuses.Ready;
                            // Sprint 5G2 / B1: route through the state-machine facade.
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, groupToUpdate, revertTo,
                                triggerName: "InvalidAssignmentRevert",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: $"Invalid {assignment.Role} assignment {assignment.Id} found during validation; reverting to {revertTo}.",
                                correlationId: null,
                                ct: stoppingToken);
                        }
                        groupToUpdate.UpdatedAtUtc = now;
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("[ASSIGNMENT] Validated and fixed {Count} invalid assignments", invalidAssignments.Count);
            }
        }

        #endregion

        #region Submission Workflow

        private async Task RunSubmissionWorkflowAsync(ApplicationDbContext db, CancellationToken stoppingToken)
        {
            try
            {
                // Find groups approved by audit but not yet submitted
                var candidates = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.Status == AnalysisStatuses.AuditCompleted || g.Status == "AuditCompleted")
                    .OrderBy(g => g.CreatedAtUtc)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var g in candidates)
                {
                    try
                    {
                        var oldStatus = g.Status;
                        var newStatus = AnalysisStatuses.Completed;
                        // Sprint 5G2 / B1: keep the explicit guard so we can `continue` cleanly without
                        // raising the facade's exception inside the transaction. AuditCompleted → Completed
                        // is in the legal table; any other oldStatus is a real bug we should surface.
                        if (!AnalysisStatusValidator.IsValidTransition(oldStatus, newStatus))
                        {
                            _logger.LogWarning(
                                "[SUBMISSION] Invalid status transition: {OldStatus} → {NewStatus} for group {GroupId}",
                                oldStatus, newStatus, g.Id);
                            continue;
                        }

                        var submissionStrategy = db.Database.CreateExecutionStrategy();
                        string? fullPath = null;
                        await submissionStrategy.ExecuteAsync(async () =>
                        {
                            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                            try
                            {
                                // FIX A: Use both raw and normalized GroupIdentifier to match decisions
                                // (ported from obsolete SubmissionWorker which had this safeguard)
                                var normalizedId = !string.IsNullOrEmpty(g.NormalizedGroupIdentifier)
                                    ? g.NormalizedGroupIdentifier
                                    : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) ?? g.GroupIdentifier);
                                var decisionGroupIds = new[] { g.GroupIdentifier, normalizedId }
                                    .Where(x => !string.IsNullOrEmpty(x))
                                    .Distinct()
                                    .ToList();

                                var analyst = await db.ImageAnalysisDecisions
                                    .Where(d => decisionGroupIds.Contains(d.GroupIdentifier ?? ""))
                                    .ToListAsync(stoppingToken);
                                var audits = await db.AuditDecisions
                                    .Where(a => decisionGroupIds.Contains(a.GroupIdentifier ?? ""))
                                    .ToListAsync(stoppingToken);

                                // FIX B: Guard against empty submissions — don't mark Completed with no decision data
                                if (!analyst.Any() && !audits.Any())
                                {
                                    _logger.LogWarning(
                                        "[SUBMISSION] Skipping group {GroupId} ({GroupIdentifier}) — no analyst or audit decisions found (searched: {SearchIds}). Will retry next cycle.",
                                        g.Id, g.GroupIdentifier, string.Join(", ", decisionGroupIds));
                                    await transaction.RollbackAsync(stoppingToken);
                                    return;
                                }

                                var idempotencyKey = $"{g.Id}-{analyst.OrderBy(a => a.Id).FirstOrDefault()?.Id}-{audits.OrderBy(a => a.Id).FirstOrDefault()?.Id}";

                                var payload = new
                                {
                                    idempotencyKey,
                                    group = new { g.Id, g.GroupIdentifier, g.GroupType, g.ScannerType },
                                    analystDecisions = analyst.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.Tags, a.Comments, a.ReviewedBy, a.ReviewedAt }),
                                    auditDecisions = audits.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.AuditNotes, a.AuditedBy, a.AuditedAt })
                                };

                                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                                var outputFolder = _configuration["ICUMS:Submission:OutputFolder"]
                                    ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                                    ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
                                Directory.CreateDirectory(outputFolder);
                                var fileName = $"{g.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{idempotencyKey}.json";
                                fullPath = Path.Combine(outputFolder, fileName);
                                await System.IO.File.WriteAllTextAsync(fullPath, json, Encoding.UTF8, stoppingToken);
                                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

                                db.AnalysisSubmissions.Add(new AnalysisSubmission
                                {
                                    GroupId = g.Id,
                                    PayloadPath = fullPath,
                                    PayloadHash = hash,
                                    Status = "TestSaved",
                                    CreatedAtUtc = DateTime.UtcNow,
                                    SubmittedAtUtc = DateTime.UtcNow
                                });

                                // Build per-container ICUMS scanData payloads (real format for ICUMS submission)
                                await BuildAndWriteIcumsPayloadsAsync(db, g, analyst, audits, outputFolder, stoppingToken);

                                // Self-healing CCS sync: update by both group identity and the
                                // group's AnalysisRecords containers. Some production CCS rows
                                // still carry the container number as GroupIdentifier while the
                                // AnalysisGroup uses the BOE/declaration number.
                                var submissionContainers = analyst
                                    .Select(a => a.ContainerNumber)
                                    .Union(audits.Select(a => a.ContainerNumber))
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();
                                var ccsUpdated = await SubmissionWorkflowStageSync.MarkPendingSubmissionAsync(
                                    db, g, submissionContainers, stoppingToken);
                                if (ccsUpdated > 0)
                                    _logger.LogInformation("[SUBMISSION] Set {Count} CCS record(s) to WorkflowStage=PendingSubmission for group {GroupId} ({GroupIdentifier})",
                                        ccsUpdated, g.Id, g.GroupIdentifier);

                                // Sprint 5G2 / B1: route through the state-machine facade. AuditCompleted → Completed
                                // is in the legal table. The facade's SaveChangesAsync auto-enlists in the ambient
                                // transaction, so the audit row is committed/rolled-back atomically with the submission.
                                await AnalysisGroupStateMachine.TransitionAsync(
                                    db, g, AnalysisStatuses.Completed,
                                    triggerName: "ICUMSSubmissionCompleted",
                                    actor: "ORCHESTRATOR-SUBMISSION",
                                    reason: $"ICUMS payload generated and saved (group={g.GroupIdentifier}, analyst={analyst.Count}, audit={audits.Count}).",
                                    correlationId: null,
                                    ct: stoppingToken);
                                g.UpdatedAtUtc = DateTime.UtcNow;
                                await transaction.CommitAsync(stoppingToken);

                                _logger.LogInformation("[SUBMISSION] Submission completed for group {Group} ({Identifier}) — {AnalystCount} analyst + {AuditCount} audit decisions",
                                    g.Id, g.GroupIdentifier, analyst.Count, audits.Count);
                            }
                            catch
                            {
                                if (!string.IsNullOrEmpty(fullPath) && System.IO.File.Exists(fullPath))
                                {
                                    try { System.IO.File.Delete(fullPath); } catch { /* best effort */ }
                                }
                                await transaction.RollbackAsync(stoppingToken);
                                throw;
                            }
                        });
                    }
                    catch (Exception exGroup)
                    {
                        _logger.LogWarning(exGroup, "[SUBMISSION] Submission failed for group {Group}", g.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SUBMISSION] Error in submission workflow");
            }

            // Retry any pending ICUMS payload files that were generated but not yet submitted
            await RetryPendingIcumsSubmissionsAsync(db, stoppingToken);
            await ReconcileAcknowledgedIcumsSubmissionsAsync(db, stoppingToken);
        }

        /// <summary>
        /// Scans the ICUMS Outbox folder for payload files that haven't been acknowledged yet
        /// and attempts to submit them. Handles payloads generated while LiveSubmitEnabled was off,
        /// or ones that previously failed.
        /// </summary>
        private async Task RetryPendingIcumsSubmissionsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var liveSubmitEnabled = await IsLiveSubmitEnabledAsync(db);
            if (!liveSubmitEnabled) return;

            var outputFolder = _configuration["ICUMS:Submission:OutputFolder"]
                ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
            var icumsFolder = Path.Combine(outputFolder, "ICUMS");

            if (!Directory.Exists(icumsFolder)) return;

            var pendingFiles = Directory.GetFiles(icumsFolder, "ICUMS_*.json");
            if (pendingFiles.Length == 0) return;

            _logger.LogInformation("[ICUMS-RETRY] Found {Count} pending payload file(s) to submit", pendingFiles.Length);

            var submitUrl = _configuration["ICUMS:SubmitResultUrl"];
            var interfaceKey = _configuration["ICUMS:SubmitResultKey"] ?? "IF_P01_NSCUNI_02";
            var authKey = _configuration["ICUMS:AuthKey"]
                ?? Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY")
                ?? "";

            if (string.IsNullOrEmpty(submitUrl) || string.IsNullOrEmpty(authKey))
            {
                _logger.LogWarning("[ICUMS-RETRY] SubmitResultUrl or AuthKey not configured — skipping");
                return;
            }

            IHttpClientFactory? httpFactory = null;
            try
            {
                using var httpScope = _scopeFactory.CreateScope();
                httpFactory = httpScope.ServiceProvider.GetService<IHttpClientFactory>();
            }
            catch { /* fall through */ }

            if (httpFactory == null)
            {
                _logger.LogWarning("[ICUMS-RETRY] IHttpClientFactory not available — skipping");
                return;
            }

            var submitted = 0;
            var failed = 0;

            foreach (var payloadFile in pendingFiles)
            {
                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(payloadFile, ct);
                    var payloadFileName = Path.GetFileName(payloadFile);
                    var containerNumber = ExtractContainerFromFileName(payloadFileName);
                    var groupId = ExtractGroupIdFromFileName(payloadFileName);

                    using var httpClient = httpFactory.CreateClient();
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("ESB_IF_ID", interfaceKey);
                    httpClient.DefaultRequestHeaders.Add("ESB_AUTH_KEY", authKey);
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                    httpClient.Timeout = TimeSpan.FromSeconds(30);

                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(submitUrl, content, ct);
                    var responseBody = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation(
                            "[ICUMS-RETRY] SUCCESS for {Container} — HTTP {Status}, Response: {Response}",
                            containerNumber, (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

                        // 2026-04-27: ARCHIVE-FIRST, MARK-SECOND.
                        // Pre-fix order was: DB UPDATE → File.Move. A crash between the two
                        // left WorkflowStage='Submitted' but the payload still in the Outbox,
                        // so the next start re-submitted to ICUMS — duplicate inserts on the
                        // customs side. By moving the file first and marking the row second,
                        // the worst-case is a one-time orphan in /Acknowledged with no DB
                        // update (next pass treats the row as still PendingSubmission, but
                        // the file is gone so RetryPending won't re-fire — the orphan can
                        // be reconciled by a separate sweeper). No more duplicate submissions.
                        var ackDir = Path.Combine(icumsFolder, "Acknowledged");
                        Directory.CreateDirectory(ackDir);
                        var ackPath = Path.Combine(ackDir, Path.GetFileName(payloadFile));
                        System.IO.File.Move(payloadFile, ackPath, overwrite: true);

                        if (!string.IsNullOrEmpty(containerNumber))
                        {
                            try
                            {
                                var group = groupId.HasValue
                                    ? await db.AnalysisGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId.Value, ct)
                                    : null;
                                var acked = await SubmissionWorkflowStageSync.MarkContainerSubmittedAsync(
                                    db, containerNumber, group, ct);
                                if (acked > 0)
                                    _logger.LogInformation("[ICUMS-RETRY] Set WorkflowStage=Submitted for {Container} ({Count} record(s))", containerNumber, acked);
                            }
                            catch (Exception dbEx)
                            {
                                // File is already in /Acknowledged so we won't re-submit on
                                // restart. The DB update will be retried next cycle when the
                                // row is still PendingSubmission. Log loudly so ops can
                                // reconcile if needed.
                                _logger.LogError(dbEx,
                                    "[ICUMS-RETRY] File archived to {AckPath} but DB update FAILED for {Container} — manual reconciliation may be required.",
                                    ackPath, containerNumber);
                            }
                        }
                        submitted++;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[ICUMS-RETRY] FAILED for {Container} — HTTP {Status}: {Response}",
                            containerNumber, (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ICUMS-RETRY] Exception submitting {File}", Path.GetFileName(payloadFile));
                    failed++;
                }
            }

            if (submitted > 0 || failed > 0)
                _logger.LogInformation("[ICUMS-RETRY] Complete: Submitted={Submitted}, Failed={Failed}", submitted, failed);
        }

        /// <summary>
        /// Reconciles acknowledged ICUMS payload files back into CCS state without
        /// resubmitting them. This heals crashes and old group-id drift where the
        /// file reached /Acknowledged but CCS stayed in ImageAnalysis/Audit/Completed.
        /// </summary>
        private async Task ReconcileAcknowledgedIcumsSubmissionsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (now - _lastIcumsAckReconciliationRun < TimeSpan.FromMinutes(15))
                return;

            _lastIcumsAckReconciliationRun = now;

            var outputFolder = _configuration["ICUMS:Submission:OutputFolder"]
                ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
            var ackDir = Path.Combine(outputFolder, "ICUMS", "Acknowledged");

            if (!Directory.Exists(ackDir))
                return;

            var acknowledgedFiles = Directory.GetFiles(ackDir, "ICUMS_*.json")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(2000)
                .ToList();

            if (acknowledgedFiles.Count == 0)
                return;

            var reconciled = 0;
            var skipped = 0;

            foreach (var file in acknowledgedFiles)
            {
                ct.ThrowIfCancellationRequested();

                var containerNumber = ExtractContainerFromFileName(file.Name);
                if (string.IsNullOrWhiteSpace(containerNumber))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var groupId = ExtractGroupIdFromFileName(file.Name);
                    var group = groupId.HasValue
                        ? await db.AnalysisGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId.Value, ct)
                        : null;

                    var updated = await SubmissionWorkflowStageSync.MarkContainerSubmittedAsync(
                        db, containerNumber, group, ct);
                    reconciled += updated;
                }
                catch (Exception ex)
                {
                    skipped++;
                    _logger.LogWarning(ex,
                        "[ICUMS-ACK-RECONCILE] Failed to reconcile acknowledged payload {File}",
                        file.Name);
                }
            }

            if (reconciled > 0 || skipped > 0)
            {
                _logger.LogInformation(
                    "[ICUMS-ACK-RECONCILE] Complete: Files={Files}, ReconciledRows={Reconciled}, Skipped={Skipped}",
                    acknowledgedFiles.Count,
                    reconciled,
                    skipped);
            }
        }

        /// <summary>
        /// Builds per-container ICUMS scanData payloads in the format expected by the ICUMS Submit Results API
        /// (POST /api/rm/scan/result, IF_P01_NSCUNI_02). Files are written to an "ICUMS" subfolder for verification.
        /// Actual HTTP submission is gated by LiveSubmitEnabled (DB systemsettings, fallback appsettings.json).
        ///
        /// Data is anchored to the specific CCS record (via InspectionId + ScannerType) to ensure the correct
        /// scanner record, image, and metadata are used — not a loose container-number lookup that could match
        /// the wrong scan.
        /// </summary>
        private async Task BuildAndWriteIcumsPayloadsAsync(
            ApplicationDbContext db,
            AnalysisGroup group,
            List<ImageAnalysisDecision> analystDecisions,
            List<AuditDecision> auditDecisions,
            string baseOutputFolder,
            CancellationToken ct)
        {
            var icumsFolder = Path.Combine(baseOutputFolder, "ICUMS");
            Directory.CreateDirectory(icumsFolder);

            // Resolve ASE image converter once for this method (converts raw scanner → JPEG)
            using var converterScope = _scopeFactory.CreateScope();
            var aseConverter = converterScope.ServiceProvider.GetService<ImageProcessing.ASE.IASEImageConverterService>();

            var containerNumbers = analystDecisions
                .Select(a => a.ContainerNumber)
                .Union(auditDecisions.Select(a => a.ContainerNumber))
                .Distinct()
                .ToList();

            if (!containerNumbers.Any())
            {
                _logger.LogWarning("[ICUMS-PAYLOAD] No containers found for group {GroupId} — skipping ICUMS payload generation", group.Id);
                return;
            }

            // Load CCS records for this group's containers — these carry InspectionId (the link to the exact scan)
            var normalizedId = !string.IsNullOrEmpty(group.NormalizedGroupIdentifier)
                ? group.NormalizedGroupIdentifier
                : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(group.GroupIdentifier) ?? group.GroupIdentifier);
            var groupIds = new[] { group.GroupIdentifier, normalizedId }
                .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

            var ccsRecords = await db.ContainerCompletenessStatuses
                .Where(c => containerNumbers.Contains(c.ContainerNumber) && groupIds.Contains(c.GroupIdentifier))
                .ToListAsync(ct);

            // Fallback: if no CCS match by group, try by container number alone
            if (!ccsRecords.Any())
            {
                ccsRecords = await db.ContainerCompletenessStatuses
                    .Where(c => containerNumbers.Contains(c.ContainerNumber))
                    .ToListAsync(ct);
            }

            var ccsByContainer = ccsRecords
                .GroupBy(c => c.ContainerNumber)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.ScanDate).First());

            // Resolve BOE data from the ICUMS downloads database
            Dictionary<string, Core.Models.BOEDocument> boeByContainer;
            try
            {
                using var icumsScope = _scopeFactory.CreateScope();
                var icumsDb = icumsScope.ServiceProvider.GetRequiredService<Infrastructure.Data.IcumDownloadsDbContext>();
                var boeDocuments = await icumsDb.BOEDocuments
                    .Where(b => containerNumbers.Contains(b.ContainerNumber))
                    .ToListAsync(ct);
                boeByContainer = boeDocuments
                    .GroupBy(b => b.ContainerNumber)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.CreatedAt).First());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ICUMS-PAYLOAD] Could not load BOE data — payloads will use fallback values");
                boeByContainer = new Dictionary<string, Core.Models.BOEDocument>();
            }

            var payloadCount = 0;

            foreach (var containerNumber in containerNumbers)
            {
                try
                {
                    var analystDecision = analystDecisions
                        .Where(a => a.ContainerNumber == containerNumber)
                        .OrderByDescending(a => a.ReviewedAt)
                        .FirstOrDefault();
                    var auditDecision = auditDecisions
                        .Where(a => a.ContainerNumber == containerNumber)
                        .OrderByDescending(a => a.AuditedAt)
                        .FirstOrDefault();

                    boeByContainer.TryGetValue(containerNumber, out var boe);
                    ccsByContainer.TryGetValue(containerNumber, out var ccs);

                    var scannerType = ccs?.ScannerType
                        ?? analystDecision?.ScannerType
                        ?? auditDecision?.ScannerType
                        ?? group.ScannerType ?? "";
                    var inspectionId = ccs?.InspectionId;

                    DateTime? scanDate = ccs?.ScanDate;
                    string? truckPlate = null;
                    string imageBase64 = "";

                    // Use InspectionId to find the EXACT scanner record that was analyzed
                    if (scannerType.Equals("ASE", StringComparison.OrdinalIgnoreCase))
                    {
                        Core.Entities.ASE.AseScan? aseScan = null;

                        // Primary: match by InspectionId. Split ASE queue rows use
                        // suffixed IDs such as "84830-a"; trim to the source
                        // inspection id so payload generation can find the original
                        // scan image.
                        if (TryParseBaseAseInspectionId(inspectionId, out var aseInspId))
                            aseScan = await db.AseScans.FirstOrDefaultAsync(s => s.InspectionId == aseInspId, ct);

                        // Fallback: latest scan for this container. This must be
                        // token-aware because ASE preserves dual-container source
                        // labels like "C1, C2" in AseScans.ContainerNumber.
                        aseScan ??= await ResolveLatestAseScanForContainerAsync(
                            db,
                            containerNumber,
                            requireImage: false,
                            ct);

                        if (aseScan != null)
                        {
                            scanDate ??= aseScan.ScanTime;
                            truckPlate = aseScan.TruckPlate;

                            // Convert raw ASE scanner image to JPEG before base64 encoding
                            if (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0)
                            {
                                if (aseConverter != null)
                                {
                                    try
                                    {
                                        var convResult = await aseConverter.ConvertAseImageToJpegAsync(aseScan.ScanImage);
                                        if (convResult.Success && convResult.ImageData != null && convResult.ImageData.Length > 0)
                                            imageBase64 = Convert.ToBase64String(convResult.ImageData);
                                        else
                                            _logger.LogWarning("[ICUMS-PAYLOAD] ASE image conversion failed for {Container}: {Error}", containerNumber, convResult.ErrorMessage);
                                    }
                                    catch (Exception convEx)
                                    {
                                        _logger.LogWarning(convEx, "[ICUMS-PAYLOAD] ASE image conversion exception for {Container}", containerNumber);
                                    }
                                }
                                if (string.IsNullOrEmpty(imageBase64))
                                    imageBase64 = Convert.ToBase64String(aseScan.ScanImage);
                            }
                        }
                    }
                    else if (scannerType.Equals("FS6000", StringComparison.OrdinalIgnoreCase))
                    {
                        Core.Entities.FS6000.FS6000Scan? fs6000Scan = null;

                        // Primary: match by InspectionId (= FS6000Scan.Id as string)
                        if (!string.IsNullOrEmpty(inspectionId) && Guid.TryParse(inspectionId, out var fsScanId))
                            fs6000Scan = await db.FS6000Scans.Include(s => s.Images).FirstOrDefaultAsync(s => s.Id == fsScanId, ct);

                        // Fallback: latest scan for this container
                        fs6000Scan ??= await db.FS6000Scans.Include(s => s.Images)
                            .Where(s => s.ContainerNumber == containerNumber)
                            .OrderByDescending(s => s.ScanTime)
                            .FirstOrDefaultAsync(ct);

                        if (fs6000Scan != null)
                        {
                            scanDate ??= fs6000Scan.ScanTime;
                            // FS6000 stores truck/vehicle plate in VesselName (operators enter it there)
                            // Also check TruckPlate field (populated by ingestion for newer records)
                            truckPlate = !string.IsNullOrWhiteSpace(fs6000Scan.TruckPlate)
                                ? fs6000Scan.TruckPlate
                                : fs6000Scan.VesselName;

                            // Image from the exact FS6000 scan record (prefer "Main" type)
                            var mainImage = fs6000Scan.Images
                                .Where(i => i.ImageData != null && i.ImageData.Length > 0)
                                .OrderByDescending(i => i.ImageType == "Main")
                                .ThenByDescending(i => i.CreatedAt)
                                .FirstOrDefault();
                            if (mainImage?.ImageData != null)
                                imageBase64 = Convert.ToBase64String(mainImage.ImageData);
                        }
                    }

                    // If we still have no image, try the ImageCache as a last resort
                    if (string.IsNullOrEmpty(imageBase64))
                    {
                        try
                        {
                            var cached = await db.ImageCaches
                                .Where(c => c.ContainerNumber == containerNumber
                                    && (string.IsNullOrEmpty(scannerType) || c.ScannerType == scannerType))
                                .OrderByDescending(c => c.CachedAt)
                                .FirstOrDefaultAsync(ct);
                            if (cached?.ImageData != null && cached.ImageData.Length > 0)
                            {
                                // Check if raw scanner format (not JPEG/PNG) and convert
                                if (aseConverter != null && cached.ImageData.Length > 2
                                    && cached.ImageData[0] != 0xFF && cached.ImageData[0] != 0x89)
                                {
                                    try
                                    {
                                        var convResult = await aseConverter.ConvertAseImageToJpegAsync(cached.ImageData);
                                        if (convResult.Success && convResult.ImageData != null && convResult.ImageData.Length > 0)
                                            imageBase64 = Convert.ToBase64String(convResult.ImageData);
                                    }
                                    catch { /* fall through to raw */ }
                                }
                                if (string.IsNullOrEmpty(imageBase64))
                                    imageBase64 = Convert.ToBase64String(cached.ImageData);
                            }
                        }
                        catch (Exception cacheEx)
                        {
                            _logger.LogDebug(cacheEx, "[ICUMS-PAYLOAD] ImageCache lookup failed for {Container}", containerNumber);
                        }
                    }

                    if (string.IsNullOrEmpty(imageBase64))
                        _logger.LogWarning("[ICUMS-PAYLOAD] No image found for container {Container} (InspectionId={InspId}, Scanner={Scanner})", containerNumber, inspectionId ?? "N/A", scannerType);

                    var effectiveScanDate = scanDate ?? DateTime.UtcNow;

                    // Map analyst verdict to ICUMS verdict
                    var verdict = "clear";
                    if (analystDecision != null)
                    {
                        verdict = analystDecision.Decision switch
                        {
                            "Abnormal" => "suspicious",
                            "Normal" => "clear",
                            _ => analystDecision.Decision?.ToLowerInvariant() ?? "clear"
                        };
                    }

                    var findings = analystDecision?.Comments;
                    if (string.IsNullOrEmpty(findings))
                        findings = auditDecision?.AuditNotes;
                    if (string.IsNullOrEmpty(findings))
                        findings = verdict == "clear" ? "No suspicious items found" : "Suspicious items detected";

                    // Deterministic scan reference for idempotency
                    var scanRef = $"{group.Id:N}"[..10] + $"{containerNumber}".Replace(" ", "");
                    if (scanRef.Length > 20) scanRef = scanRef[..20];

                    var scanData = new
                    {
                        scanData = new
                        {
                            DeclarationNumber = boe?.DeclarationNumber ?? "",
                            VersionNumber = boe?.DeclarationVersion ?? 1,
                            RotationNumber = boe?.RotationNumber ?? "",
                            BlNumber = boe?.BlNumber ?? "",
                            HouseBl = boe?.HouseBl ?? "",
                            ContainerNumber = containerNumber,
                            ScanReferenceNumber = scanRef,
                            ScanDate = effectiveScanDate.ToString("dd-MM-yy"),
                            ScanStartDate = effectiveScanDate.ToString("yyyyMMddHHmmss"),
                            ScanEndDate = effectiveScanDate.AddMinutes(10).ToString("yyyyMMddHHmmss"),
                            ScanAnalysisStartDate = (analystDecision?.CreatedAt ?? effectiveScanDate).ToString("yyyyMMddHHmmss"),
                            ScanAnalysisEndDate = (auditDecision?.AuditedAt ?? analystDecision?.ReviewedAt ?? effectiveScanDate).ToString("yyyyMMddHHmmss"),
                            TruckPlateNumber = truckPlate,
                            Verdict = verdict,
                            FindingsDescription = findings,
                            ImageAnalystName = analystDecision?.ReviewedBy ?? "Unknown",
                            CustomOfficerName = auditDecision?.AuditedBy ?? "Unknown",
                            ImageDocument = imageBase64
                        }
                    };

                    var icumsJson = JsonSerializer.Serialize(scanData, new JsonSerializerOptions { WriteIndented = true });
                    var icumsFileName = $"ICUMS_{containerNumber}_{group.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                    var icumsPath = Path.Combine(icumsFolder, icumsFileName);
                    await System.IO.File.WriteAllTextAsync(icumsPath, icumsJson, Encoding.UTF8, ct);
                    payloadCount++;

                    _logger.LogInformation(
                        "[ICUMS-PAYLOAD] Generated for {Container} — Scanner={Scanner}, InspectionId={InspId}, Verdict={Verdict}, Analyst={Analyst}, Auditor={Auditor}, BOE={HasBoe}, Image={HasImage}, TruckPlate={Plate}",
                        containerNumber, scannerType, inspectionId ?? "N/A", verdict,
                        analystDecision?.ReviewedBy ?? "N/A",
                        auditDecision?.AuditedBy ?? "N/A",
                        boe != null ? "Yes" : "No",
                        !string.IsNullOrEmpty(imageBase64) ? "Yes" : "No",
                        !string.IsNullOrEmpty(truckPlate) ? truckPlate : "N/A");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ICUMS-PAYLOAD] Failed to build payload for container {Container} in group {GroupId}", containerNumber, group.Id);
                }
            }

            _logger.LogInformation("[ICUMS-PAYLOAD] Generated {Count}/{Total} ICUMS payloads for group {GroupId} ({GroupIdentifier})",
                payloadCount, containerNumbers.Count, group.Id, group.GroupIdentifier);

            var liveSubmitEnabled = await IsLiveSubmitEnabledAsync(db);
            if (liveSubmitEnabled && payloadCount > 0)
            {
                await SubmitPayloadsToIcumsAsync(db, icumsFolder, group, containerNumbers, ct);
            }
        }

        /// <summary>
        /// Checks DB systemsettings first for LiveSubmitEnabled, falls back to appsettings.json.
        /// </summary>
        private async Task<bool> IsLiveSubmitEnabledAsync(ApplicationDbContext db)
        {
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT settingvalue FROM systemsettings WHERE settingkey = 'Submission.LiveSubmitEnabled' AND isactive = true LIMIT 1";
                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    return result.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ICUMS] Could not read LiveSubmitEnabled from DB, falling back to config");
            }

            return _configuration.GetValue<bool>("ICUMS:Submission:LiveSubmitEnabled", false);
        }

        /// <summary>
        /// POST each generated ICUMS payload file to the external ICUMS API.
        /// On success response, update CCS WorkflowStage from PendingSubmission → Submitted.
        /// </summary>
        private async Task SubmitPayloadsToIcumsAsync(
            ApplicationDbContext db,
            string icumsFolder,
            AnalysisGroup group,
            List<string> containerNumbers,
            CancellationToken ct)
        {
            var submitUrl = _configuration["ICUMS:SubmitResultUrl"];
            var interfaceKey = _configuration["ICUMS:SubmitResultKey"] ?? "IF_P01_NSCUNI_02";
            var authKey = _configuration["ICUMS:AuthKey"]
                ?? Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY")
                ?? "";

            if (string.IsNullOrEmpty(submitUrl) || string.IsNullOrEmpty(authKey))
            {
                _logger.LogWarning("[ICUMS-SUBMIT] LiveSubmitEnabled=true but SubmitResultUrl or AuthKey not configured — skipping HTTP submission");
                return;
            }

            IHttpClientFactory? httpFactory = null;
            // 2026-05-05 (audit 3.01, P0): LAYER 5 — submission-time port + fyco
            // re-validation. Resolve the validator from a single scope shared with
            // the http factory so each payload re-runs the cardinal port rule and
            // the fyco-direction rule one last time before the HTTP POST. An
            // upstream regression in queue / mapper / cascade can no longer slip
            // through to ICUMS unchecked. Validator is feature-flag-aware (same
            // flags as the queue and mapper layers); flags off → no-op pass.
            //
            // Composes ON TOP of 4ded1fe (Sprint 2D D1) which moved the file move
            // BEFORE the DB update on success. Layer 5 sits BEFORE the HTTP POST
            // — its skip path leaves the file in the live Outbox so the next
            // submission sweep (or a re-validation after the underlying anomaly
            // is resolved) can pick it up cleanly. CHANGELOG 2.16.0 line 155 has
            // claimed this gate existed for 3+ days; this commit makes the claim
            // true.
            IContainerValidationService? submissionGate = null;
            IServiceScope? gateScope = null;
            try
            {
                gateScope = _scopeFactory.CreateScope();
                httpFactory = gateScope.ServiceProvider.GetService<IHttpClientFactory>();
                submissionGate = gateScope.ServiceProvider.GetService<IContainerValidationService>();
            }
            catch { /* fall through */ }

            try
            {

            if (httpFactory == null)
            {
                _logger.LogWarning("[ICUMS-SUBMIT] IHttpClientFactory not available — skipping HTTP submission");
                return;
            }

            var payloadFiles = Directory.GetFiles(icumsFolder, $"ICUMS_*_{group.Id}_*.json");
            var submitted = 0;
            var failed = 0;
            var blockedByLayer5 = 0;

            foreach (var payloadFile in payloadFiles)
            {
                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(payloadFile, ct);
                    var containerNumber = ExtractContainerFromFileName(Path.GetFileName(payloadFile));

                    // ── LAYER 5 GATE (audit 3.01, P0, 2026-05-05) ──────────────────
                    // Re-run port-match + fyco-direction rules right before the HTTP
                    // POST. On disagreement: skip the submission, write a Critical
                    // MatchQualityFlag, log a Warning, leave the payload file in the
                    // live Outbox so it can be picked up after the upstream anomaly
                    // is resolved (or a manual unmatch / rematch from the admin tool
                    // brings the data back into agreement). Validator failure itself
                    // is treated as "no opinion" and falls through to submit (logged
                    // by the validator) — better to under-block than to wrongly
                    // block.
                    if (submissionGate != null && !string.IsNullOrEmpty(containerNumber))
                    {
                        BusinessRuleValidationResult? gateResult = null;
                        try
                        {
                            gateResult = await submissionGate.ValidateSubmissionGateAsync(containerNumber);
                        }
                        catch (Exception gateEx)
                        {
                            _logger.LogWarning(gateEx,
                                "[ICUMS-SUBMIT-GATE] Submission gate threw for {Container}; falling through to submit",
                                containerNumber);
                        }

                        if (gateResult != null && !gateResult.IsValid && gateResult.FailedRules.Count > 0)
                        {
                            var portFailures = gateResult.FailedRules
                                .Where(r => r.StartsWith("Port mismatch", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            var fycoFailures = gateResult.FailedRules
                                .Where(r => r.StartsWith("Fyco", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            var description = string.Join(" | ", gateResult.FailedRules);
                            if (description.Length > 1000) description = description[..1000];

                            _logger.LogWarning(
                                "[ICUMS-SUBMIT-GATE] BLOCKED submission for {Container} — {FailureCount} rule failure(s): {Failures}. Payload file left in Outbox: {File}",
                                containerNumber, gateResult.FailedRules.Count, description, Path.GetFileName(payloadFile));

                            // Best-effort flag persistence. Upsert to avoid flooding the
                            // table on repeat-validation cycles. Mirrors the contract used
                            // by ContainerCompletenessService.WriteMatchQualityFlagAsync
                            // (per-(container,flagtype) idempotency) but inlined here
                            // because the helper is private to that service.
                            try
                            {
                                if (portFailures.Count > 0)
                                {
                                    await UpsertMatchQualityFlagInlineAsync(
                                        db, containerNumber, "PortMismatch", "Critical",
                                        string.Join(" | ", portFailures), ct);
                                }
                                if (fycoFailures.Count > 0)
                                {
                                    await UpsertMatchQualityFlagInlineAsync(
                                        db, containerNumber, "FycoMismatch", "Critical",
                                        string.Join(" | ", fycoFailures), ct);
                                }
                                // If somehow neither bucket caught the failure (defensive),
                                // fall back to a generic SubmissionGate flag so the admin
                                // page surfaces it.
                                if (portFailures.Count == 0 && fycoFailures.Count == 0)
                                {
                                    await UpsertMatchQualityFlagInlineAsync(
                                        db, containerNumber, "SubmissionGate", "Critical",
                                        description, ct);
                                }
                                await db.SaveChangesAsync(ct);
                            }
                            catch (Exception flagEx)
                            {
                                _logger.LogWarning(flagEx,
                                    "[ICUMS-SUBMIT-GATE] Failed to persist MatchQualityFlag for {Container} — continuing (payload still skipped)",
                                    containerNumber);
                            }

                            blockedByLayer5++;
                            continue; // LEAVE FILE IN /ICUMS — do NOT POST, do NOT move to /Acknowledged.
                        }
                    }
                    // ── END LAYER 5 GATE ───────────────────────────────────────────

                    using var httpClient = httpFactory.CreateClient();
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("ESB_IF_ID", interfaceKey);
                    httpClient.DefaultRequestHeaders.Add("ESB_AUTH_KEY", authKey);
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                    httpClient.Timeout = TimeSpan.FromSeconds(30);

                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(submitUrl, content, ct);
                    var responseBody = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation(
                            "[ICUMS-SUBMIT] SUCCESS for {Container} — HTTP {Status}, Response: {Response}",
                            containerNumber, (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

                        // 2026-05-05 (audit 5.03, P1): ARCHIVE-FIRST, MARK-SECOND.
                        // Pre-fix order on this primary submission path was: DB UPDATE
                        // → File.Move. A crash between the two left WorkflowStage='Submitted'
                        // but the payload still in the live Outbox, so the next start
                        // re-submitted to ICUMS — duplicate inserts on the customs side.
                        // The retry path (RetryPendingIcumsSubmissionsAsync) already had
                        // the correct order from f4ec289; this brings the primary path
                        // into alignment. By moving the file first and marking the row
                        // second, the worst-case is a one-time orphan in /Acknowledged
                        // with no DB update (next pass treats the row as still
                        // PendingSubmission, but the file is gone so RetryPending won't
                        // re-fire — the orphan can be reconciled by a separate sweeper).
                        // No more duplicate submissions.
                        var ackDir = Path.Combine(icumsFolder, "Acknowledged");
                        Directory.CreateDirectory(ackDir);
                        var ackPath = Path.Combine(ackDir, Path.GetFileName(payloadFile));
                        System.IO.File.Move(payloadFile, ackPath, overwrite: true);

                        if (!string.IsNullOrEmpty(containerNumber))
                        {
                            try
                            {
                                var acked = await SubmissionWorkflowStageSync.MarkContainerSubmittedAsync(
                                    db, containerNumber, group, ct);
                                if (acked > 0)
                                    _logger.LogInformation("[ICUMS-SUBMIT] Set WorkflowStage=Submitted for {Container} ({Count} record(s))", containerNumber, acked);
                            }
                            catch (Exception dbEx)
                            {
                                // File is already in /Acknowledged so we won't re-submit on
                                // restart. The DB update will be retried next cycle when the
                                // row is still PendingSubmission. Log loudly so ops can
                                // reconcile if needed.
                                _logger.LogError(dbEx,
                                    "[ICUMS-SUBMIT] File archived to {AckPath} but DB update FAILED for {Container} — manual reconciliation may be required.",
                                    ackPath, containerNumber);
                            }
                        }
                        submitted++;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[ICUMS-SUBMIT] FAILED for {Container} — HTTP {Status}: {Response}",
                            containerNumber, (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ICUMS-SUBMIT] Exception submitting payload {File}", Path.GetFileName(payloadFile));
                    failed++;
                }
            }

            _logger.LogInformation("[ICUMS-SUBMIT] Group {GroupId}: Submitted={Submitted}, Failed={Failed}, BlockedByLayer5={Blocked}",
                group.Id, submitted, failed, blockedByLayer5);

            }
            finally
            {
                gateScope?.Dispose();
            }
        }

        /// <summary>
        /// Layer-5 inline upsert helper (audit 3.01, 2026-05-05). Mirrors the
        /// (container, flagtype) idempotency contract of
        /// ContainerCompletenessService.WriteMatchQualityFlagAsync (which is
        /// private to that service). Marks the flag unresolved + Critical and
        /// refreshes the description on repeat. Caller is responsible for the
        /// SaveChangesAsync (we batch all flag writes for a given payload).
        /// </summary>
        private async Task UpsertMatchQualityFlagInlineAsync(
            ApplicationDbContext db,
            string containerNumber,
            string flagType,
            string severity,
            string description,
            CancellationToken ct)
        {
            if (description.Length > 1000) description = description[..1000];

            var existing = await db.MatchQualityFlags
                .Where(f => f.ContainerNumber == containerNumber
                            && f.FlagType == flagType
                            && !f.IsResolved)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                existing.Description = description;
                existing.Severity = severity;
                return;
            }

            db.MatchQualityFlags.Add(new MatchQualityFlag
            {
                ContainerNumber = containerNumber,
                FlagType = flagType,
                Severity = severity,
                Description = description,
                IsResolved = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        private static string ExtractContainerFromFileName(string fileName)
        {
            // Format: ICUMS_{containerNumber}_{groupId}_{timestamp}.json
            if (fileName.StartsWith("ICUMS_"))
            {
                var parts = fileName["ICUMS_".Length..].Split('_');
                if (parts.Length >= 1) return parts[0];
            }
            return "";
        }

        private static Guid? ExtractGroupIdFromFileName(string fileName)
        {
            // Format: ICUMS_{containerNumber}_{groupId}_{timestamp}.json
            if (!fileName.StartsWith("ICUMS_"))
                return null;

            var parts = fileName["ICUMS_".Length..].Split('_');
            return parts.Length >= 2 && Guid.TryParse(parts[1], out var groupId)
                ? groupId
                : null;
        }

        /// <summary>
        /// Self-healing sweep: finds groups stuck in Ready/AnalystAssigned that already have
        /// decisions, and applies the centralized side effects to unstick them.
        /// Also recovers groups stuck in AgentProcessing (agent crash recovery).
        /// Runs every housekeeping cycle as a safety net.
        /// </summary>
        private async Task SweepStuckDecidedGroupsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            try
            {
                // 1. Fix groups with decisions still in analyst phase
                var stuckGroups = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => (g.Status == "Ready" || g.Status == "AnalystAssigned")
                        && db.ImageAnalysisDecisions.Any(d =>
                            d.GroupIdentifier == g.GroupIdentifier
                            && (d.Decision == "Normal" || d.Decision == "Abnormal")))
                    .ToListAsync(ct);

                if (stuckGroups.Any())
                {
                    _logger.LogWarning("[HOUSEKEEPING-SWEEP] Found {Count} candidate group(s) with decisions in analyst phase — validating completeness",
                        stuckGroups.Count);

                    var sideEffects = new DecisionSideEffectsService(_logger, _readyGroupsCache);
                    int fixedCount = 0, skipped = 0;

                    foreach (var group in stuckGroups)
                    {
                        // GUARD: Verify ALL containers with Ready AnalysisRecords have actual decisions.
                        // Without this, the sweep can auto-decide containers that were never reviewed.
                        var readyContainers = await db.AnalysisRecords
                            .Where(r => r.GroupId == group.Id && r.Status == "Ready")
                            .Select(r => r.ContainerNumber)
                            .Distinct()
                            .ToListAsync(ct);

                        if (readyContainers.Count > 0)
                        {
                            var decisionsForReady = await db.ImageAnalysisDecisions
                                .Where(d => readyContainers.Contains(d.ContainerNumber)
                                    && (d.GroupIdentifier == group.GroupIdentifier
                                        || d.GroupIdentifier == group.NormalizedGroupIdentifier))
                                .Select(d => d.ContainerNumber)
                                .Distinct()
                                .ToListAsync(ct);

                            var undecidedContainers = readyContainers
                                .Where(c => !decisionsForReady.Contains(c, StringComparer.OrdinalIgnoreCase))
                                .ToList();

                            if (undecidedContainers.Count > 0)
                            {
                                _logger.LogWarning(
                                    "[HOUSEKEEPING-SWEEP] Group {Group} has {Count} container(s) with no decisions: {Containers} — skipping ApplyForGroupAsync",
                                    group.GroupIdentifier, undecidedContainers.Count, string.Join(", ", undecidedContainers));
                                skipped++;
                                continue;
                            }
                        }

                        await sideEffects.ApplyForGroupAsync(db, group.GroupIdentifier, group.ScannerType, ct);
                        fixedCount++;
                    }

                    _logger.LogInformation("[HOUSEKEEPING-SWEEP] Fixed {Fixed} stuck group(s), skipped {Skipped} partial group(s)",
                        fixedCount, skipped);
                }

                // 1b. Fix groups stuck in AuditAssigned where ALL containers already have audit decisions
                var stuckAuditGroups = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.Status == AnalysisStatuses.AuditAssigned)
                    .ToListAsync(ct);

                if (stuckAuditGroups.Any())
                {
                    int auditFixed = 0;

                    foreach (var group in stuckAuditGroups)
                    {
                        // Get all containers in this group
                        var recordContainers = await db.AnalysisRecords
                            .Where(r => r.GroupId == group.Id)
                            .Select(r => r.ContainerNumber)
                            .Distinct()
                            .ToListAsync(ct);

                        if (!recordContainers.Any()) continue;

                        // Get containers with audit decisions
                        var decisionGroupIds = new[] { group.GroupIdentifier, group.NormalizedGroupIdentifier }
                            .Where(x => !string.IsNullOrEmpty(x))
                            .Distinct()
                            .ToList();

                        var auditedContainers = await db.AuditDecisions
                            .Where(ad => decisionGroupIds.Contains(ad.GroupIdentifier))
                            .Select(ad => ad.ContainerNumber)
                            .Distinct()
                            .ToListAsync(ct);

                        var undecided = recordContainers
                            .Where(c => !auditedContainers.Contains(c, StringComparer.OrdinalIgnoreCase))
                            .ToList();

                        if (!undecided.Any())
                        {
                            // All containers audited — transition to AuditCompleted
                            // Sprint 5G2 / B1: route through the state-machine facade. AuditAssigned → AuditCompleted
                            // is in the legal table.
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, group, AnalysisStatuses.AuditCompleted,
                                triggerName: "HousekeepingStuckAuditFix",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: $"All {auditedContainers.Count}/{recordContainers.Count} containers audited but group stuck in AuditAssigned.",
                                correlationId: null,
                                ct: ct);
                            group.UpdatedAtUtc = DateTime.UtcNow;

                            // Mark audit decisions as completed
                            foreach (var gid in decisionGroupIds)
                            {
                                await db.Database.ExecuteSqlRawAsync(
                                    "UPDATE AuditDecisions SET IsCompleted = true, CompletedAt = now() AT TIME ZONE 'UTC', UpdatedAt = now() AT TIME ZONE 'UTC' WHERE GroupIdentifier = {0} AND IsCompleted = false",
                                    gid);
                            }

                            // Release active audit assignments
                            await db.Database.ExecuteSqlRawAsync(
                                "UPDATE AnalysisAssignments SET State = 'Released', UpdatedAtUtc = now() AT TIME ZONE 'UTC' WHERE GroupId = {0} AND Role = 'Audit' AND State = 'Active'",
                                group.Id);

                            auditFixed++;
                            _logger.LogWarning(
                                "[HOUSEKEEPING-SWEEP] Auto-completed stuck audit group {Group} ({Id}): {AuditedCount}/{TotalCount} containers audited → AuditCompleted",
                                group.GroupIdentifier, group.Id, auditedContainers.Count, recordContainers.Count);
                        }
                    }

                    if (auditFixed > 0)
                    {
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("[HOUSEKEEPING-SWEEP] Fixed {Count} stuck AuditAssigned group(s) → AuditCompleted (will be picked up by submission workflow)",
                            auditFixed);
                    }
                }

                // 2. Recover groups stuck in AgentProcessing for >5 minutes (agent crash recovery)
                var agentStuckCutoff = DateTime.UtcNow.AddMinutes(-5);
                var agentStuck = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.Status == AnalysisStatuses.AgentProcessing
                        && g.UpdatedAtUtc < agentStuckCutoff)
                    .ToListAsync(ct);

                if (agentStuck.Any())
                {
                    foreach (var group in agentStuck)
                    {
                        // Sprint 5G2 / B1: route through the state-machine facade. AgentProcessing → Ready
                        // is in the legal table.
                        await AnalysisGroupStateMachine.TransitionAsync(
                            db, group, AnalysisStatuses.Ready,
                            triggerName: "HousekeepingAgentCrashRecovery",
                            actor: "ORCHESTRATOR-HOUSEKEEPING",
                            reason: $"Group stuck in AgentProcessing for >5 minutes (UpdatedAtUtc={group.UpdatedAtUtc:o}) — agent likely crashed; reverting to Ready.",
                            correlationId: null,
                            ct: ct);
                        group.UpdatedAtUtc = DateTime.UtcNow;
                    }
                    _logger.LogWarning("[HOUSEKEEPING-SWEEP] Recovered {Count} group(s) stuck in AgentProcessing (>5 min) back to Ready",
                        agentStuck.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HOUSEKEEPING-SWEEP] Error in stuck groups sweep");
            }
        }

        #endregion

        #region Wave Processing

        /// <summary>
        /// Creates Wave 1 AnalysisGroup for a group that has some containers with images and some without.
        /// Creates AnalysisParentGroup, tracks pending containers in WavePendingContainers, and creates
        /// an AnalysisGroup containing only the ready (with images) containers.
        /// </summary>
        private async Task ProcessWaveIntakeAsync(
            ApplicationDbContext db,
            string groupIdentifier,
            string? scannerType,
            List<ContainerCompletenessStatus> allContainers,
            List<ContainerCompletenessStatus> readyContainers,
            List<ContainerCompletenessStatus> pendingContainers,
            CancellationToken ct)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    // 1. Create or find AnalysisParentGroup
                    var parentGroup = await db.AnalysisParentGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(p => p.GroupIdentifier == groupIdentifier
                            && p.ScannerType == scannerType, ct);

                    if (parentGroup == null)
                    {
                        parentGroup = new AnalysisParentGroup
                        {
                            GroupIdentifier = groupIdentifier,
                            ScannerType = scannerType,
                            TotalExpectedContainers = allContainers.Count,
                            Status = "Active"
                        };
                        db.AnalysisParentGroups.Add(parentGroup);
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "[WAVE-INTAKE] Created AnalysisParentGroup for {GroupIdentifier}: {Ready} ready, {Pending} pending of {Total} total",
                            groupIdentifier, readyContainers.Count, pendingContainers.Count, allContainers.Count);
                    }
                    else
                    {
                        parentGroup.TotalExpectedContainers = allContainers.Count;
                        await db.SaveChangesAsync(ct);
                    }

                    // 2. Add pending containers to WavePendingContainers (skip duplicates)
                    var existingPending = await db.WavePendingContainers
                        .Where(w => w.ParentGroupId == parentGroup.Id)
                        .Select(w => w.ContainerNumber)
                        .ToListAsync(ct);
                    var existingPendingSet = new HashSet<string>(existingPending, StringComparer.OrdinalIgnoreCase);

                    foreach (var pending in pendingContainers)
                    {
                        var containerNum = pending.ContainerNumber?.Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(containerNum) || existingPendingSet.Contains(containerNum))
                            continue;

                        db.WavePendingContainers.Add(new WavePendingContainer
                        {
                            ParentGroupId = parentGroup.Id,
                            ContainerNumber = containerNum,
                            ScannerType = scannerType,
                            Status = "Pending"
                        });
                        existingPendingSet.Add(containerNum);
                    }
                    await db.SaveChangesAsync(ct);

                    // 3. Create Wave 1 AnalysisGroup with only ready containers
                    var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
                    var waveGroupIdentifier = groupIdentifier; // Keep same identifier for consistency

                    // Check if an AnalysisGroup already exists for this identifier
                    var existingGroup = await db.AnalysisGroups
                        .FirstOrDefaultAsync(x => x.GroupIdentifier == waveGroupIdentifier && x.ScannerType == scannerType, ct);

                    if (existingGroup != null)
                    {
                        // Already exists — just link to parent if not already
                        if (existingGroup.ParentGroupId == null)
                        {
                            existingGroup.ParentGroupId = parentGroup.Id;
                            existingGroup.WaveNumber = 1;
                            existingGroup.WaveCreatedReason = "InitialBatch";
                            existingGroup.UpdatedAtUtc = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);
                        }
                        await transaction.CommitAsync(ct);
                        return;
                    }

                    var readyContainerNumbers = readyContainers
                        .Where(c => !string.IsNullOrWhiteSpace(c.ContainerNumber))
                        .Select(c => c.ContainerNumber!.Trim().ToUpperInvariant())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var imageAnalysisCount = readyContainers.Count(c => c.WorkflowStage == "ImageAnalysis");
                    var auditCount = readyContainers.Count(c => c.WorkflowStage == "Audit");
                    var completedCount = readyContainers.Count(c => c.WorkflowStage == "PendingSubmission" || c.WorkflowStage == "Submitted" || c.WorkflowStage == "Completed");
                    var pendingWorkflowCount = readyContainers.Count(c => string.IsNullOrEmpty(c.WorkflowStage) || c.WorkflowStage == "Pending");

                    var initialStatus = WorkflowStageStatusHelper.ComputeStatusFromWorkflowStage(
                        readyContainers.Count, imageAnalysisCount, auditCount, completedCount, pendingWorkflowCount) ?? "Ready";

                    // 1.15.0 — find the matching RecordCompletenessStatus so the new group gets linked on creation
                    int? recordIdForWave = null;
                    string? recordClearanceTypeForWave = null;
                    try
                    {
                        var recordForWave = await db.RecordCompletenessStatuses
                            .Where(r => r.DeclarationNumber == normalized || r.DeclarationNumber == waveGroupIdentifier)
                            .Select(r => new { r.Id, r.ClearanceType })
                            .FirstOrDefaultAsync(ct);
                        recordIdForWave = recordForWave?.Id;
                        recordClearanceTypeForWave = recordForWave?.ClearanceType;
                    }
                    catch { /* best-effort */ }

                    // Sprint 5G2 / B1 lock-the-door: Status setter is internal; the object
                    // initializer can't write it from outside Infrastructure. Create with default
                    // "Ready", then transition to the computed initialStatus via the facade so the
                    // initial state lands an audit row when it diverges from default. If
                    // initialStatus == "Ready" the facade's same-state idempotent path returns
                    // without writing.
                    var waveGroup = new AnalysisGroup
                    {
                        GroupIdentifier = waveGroupIdentifier,
                        NormalizedGroupIdentifier = normalized,
                        GroupType = GetRecordBackedGroupType(recordClearanceTypeForWave),
                        ScannerType = scannerType,
                        ParentGroupId = parentGroup.Id,
                        WaveNumber = 1,
                        WaveCreatedReason = "InitialBatch",
                        RecordCompletenessStatusId = recordIdForWave
                    };
                    db.AnalysisGroups.Add(waveGroup);
                    await db.SaveChangesAsync(ct);
                    if (!string.Equals(initialStatus, AnalysisStatuses.Ready, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, waveGroup, initialStatus,
                                triggerName: "WaveIntakeInitialStatus",
                                actor: "ORCHESTRATOR-INTAKE",
                                reason: $"New wave-group created during intake; container completeness implies initial status {initialStatus} (Ready→{initialStatus}).",
                                correlationId: null,
                                ct: ct);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogWarning(ex, "[INTAKE] Could not apply initial status {Status} to wave-group {GroupId}; group remains Ready", initialStatus, waveGroup.Id);
                        }
                    }

                    // 4. Create AnalysisRecords for ready containers only
                    foreach (var containerNumber in readyContainerNumbers)
                    {
                        db.AnalysisRecords.Add(new AnalysisRecord
                        {
                            GroupId = waveGroup.Id,
                            ContainerNumber = containerNumber,
                            ScannerType = scannerType,
                            Status = "Ready",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync(ct);

                    // 5. Update WorkflowStage for ready containers
                    var readyToUpdate = await db.ContainerCompletenessStatuses
                        .AsTracking()
                        .Where(s => s.GroupIdentifier == groupIdentifier
                            && s.HasImageData
                            && (string.IsNullOrEmpty(s.WorkflowStage) || s.WorkflowStage == "Pending"))
                        .ToListAsync(ct);

                    foreach (var container in readyToUpdate)
                    {
                        container.WorkflowStage = "ImageAnalysis";
                        container.UpdatedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "[WAVE-INTAKE] Created Wave 1 for {GroupIdentifier}: {ReadyCount} containers with images (of {Total} total). {PendingCount} containers awaiting images",
                        groupIdentifier, readyContainerNumbers.Count, allContainers.Count, pendingContainers.Count);

                    await transaction.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    _logger.LogDebug(ex, "[WAVE-INTAKE] Duplicate detected for group {GroupIdentifier} - already processed", groupIdentifier);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    throw;
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            });
        }

        /// <summary>
        /// Periodic scan of Active parent groups to check if pending containers have received images.
        /// Creates new waves when enough containers become ready (MinBatchSize) or timeout expires.
        /// </summary>
        private async Task RunPartialWaveScanAsync(ApplicationDbContext db, AnalysisSettings settings, CancellationToken ct)
        {
            if (!settings.EnableWaveProcessing)
                return;

            try
            {
                var activeParents = await db.AnalysisParentGroups
                    .AsTracking()
                    .Where(p => p.Status == "Active")
                    .ToListAsync(ct);

                if (!activeParents.Any())
                    return;

                _logger.LogDebug("[WAVE-SCAN] Checking {Count} active parent groups for newly ready containers", activeParents.Count);

                foreach (var parent in activeParents)
                {
                    var pendingContainers = await db.WavePendingContainers
                        .AsTracking()
                        .Where(w => w.ParentGroupId == parent.Id && (w.Status == "Pending" || w.Status == "Ready"))
                        .ToListAsync(ct);

                    if (!pendingContainers.Any())
                    {
                        // No more pending — mark parent as complete
                        parent.Status = "Complete";
                        parent.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("[WAVE-SCAN] Parent group {GroupIdentifier} completed — no remaining pending containers", parent.GroupIdentifier);
                        continue;
                    }

                    // Check which AwaitingImages containers now have images
                    var awaitingContainers = pendingContainers.Where(w => w.Status == "Pending").ToList();
                    if (awaitingContainers.Any())
                    {
                        var containerNumbers = awaitingContainers.Select(w => w.ContainerNumber).ToList();
                        var nowReady = await db.ContainerCompletenessStatuses
                            .Where(s => containerNumbers.Contains(s.ContainerNumber)
                                && s.GroupIdentifier == parent.GroupIdentifier
                                && s.HasImageData)
                            .Select(s => s.ContainerNumber)
                            .ToListAsync(ct);

                        var nowReadySet = new HashSet<string>(nowReady, StringComparer.OrdinalIgnoreCase);
                        foreach (var wpc in awaitingContainers)
                        {
                            if (nowReadySet.Contains(wpc.ContainerNumber))
                            {
                                wpc.Status = "Ready";
                                wpc.BecameReadyUtc = DateTime.UtcNow;
                            }
                        }
                        if (nowReadySet.Count > 0)
                        {
                            await db.SaveChangesAsync(ct);
                            _logger.LogInformation("[WAVE-SCAN] {Count} containers became ready for parent {GroupIdentifier}",
                                nowReadySet.Count, parent.GroupIdentifier);

                            // 1.16.0 — mirror the promotion to the new RecordExpectedContainer table
                            // so the record view reflects the same state the wave system sees.
                            try
                            {
                                var recordId = await db.RecordCompletenessStatuses
                                    .Where(r => r.DeclarationNumber == parent.GroupIdentifier)
                                    .Select(r => (int?)r.Id)
                                    .FirstOrDefaultAsync(ct);
                                if (recordId.HasValue)
                                {
                                    var toPromote = await db.RecordExpectedContainers
                                        .AsTracking()
                                        .Where(e => e.RecordId == recordId.Value
                                                 && nowReady.Contains(e.ContainerNumber)
                                                 && (e.Status == "AwaitingScan" || e.Status == "Pending"))
                                        .ToListAsync(ct);
                                    var nowUtc = DateTime.UtcNow;
                                    foreach (var rec in toPromote)
                                    {
                                        rec.Status = "Ready";
                                        rec.BecameReadyUtc = nowUtc;
                                        if (rec.ScannedAtUtc == null) rec.ScannedAtUtc = nowUtc;
                                    }
                                    if (toPromote.Count > 0)
                                    {
                                        await db.SaveChangesAsync(ct);
                                    }
                                }
                            }
                            catch (Exception recEx)
                            {
                                _logger.LogWarning(recEx, "[WAVE-SCAN] Record-side mirror failed for parent {GroupIdentifier} (non-fatal)", parent.GroupIdentifier);
                            }
                        }
                    }

                    // ─── Wave #4 (1.11.0): per-container Pending-without-images timeout ───
                    // Previously the only escape from a permanently-Pending container
                    // was AutoCloseExpiredWaveParentsAsync, which fires once the WHOLE
                    // PARENT GROUP is older than WaveAutoCloseDays (default 30 days).
                    // That left a hole: a container that joined a young parent group
                    // and never received images would sit Pending for the whole 30
                    // days. Now we mark individual Pending containers as
                    // NoImageAvailable after PendingContainerStuckHours (constant
                    // below — currently 72 hours / 3 days). Tunable in source if
                    // operational reality changes.
                    var pendingStuckCutoff = DateTime.UtcNow.AddHours(-PendingContainerStuckHours);
                    var stillPending = pendingContainers.Where(w => w.Status == "Pending").ToList();
                    var stuckPending = stillPending.Where(w => w.FirstSeenUtc < pendingStuckCutoff).ToList();
                    if (stuckPending.Count > 0)
                    {
                        foreach (var wpc in stuckPending)
                        {
                            wpc.Status = "NoImageAvailable";
                        }
                        await db.SaveChangesAsync(ct);
                        _logger.LogWarning(
                            "[WAVE-SCAN] Marked {Count} pending container(s) as NoImageAvailable for parent {GroupIdentifier} " +
                            "(stuck > {Hours}h with no images). Containers: {Containers}",
                            stuckPending.Count, parent.GroupIdentifier, PendingContainerStuckHours,
                            string.Join(", ", stuckPending.Select(w => w.ContainerNumber)));
                    }

                    // Check if we should create a new wave
                    var readyForWave = pendingContainers.Where(w => w.Status == "Ready").ToList();
                    if (readyForWave.Count == 0)
                        continue;

                    var oldestReady = readyForWave.Min(w => w.BecameReadyUtc ?? DateTime.UtcNow);
                    var hoursWaiting = (DateTime.UtcNow - oldestReady).TotalHours;

                    var shouldCreateWave = readyForWave.Count >= settings.WaveMinBatchSize
                        || hoursWaiting >= settings.WaveTimeoutHours;

                    if (!shouldCreateWave)
                        continue;

                    var reason = readyForWave.Count >= settings.WaveMinBatchSize ? "NewImages" : "Timeout";
                    await CreateNewWaveAsync(db, parent, readyForWave, reason, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WAVE-SCAN] Error in partial wave scan");
            }
        }

        /// <summary>
        /// Creates a new wave AnalysisGroup from ready pending containers.
        /// </summary>
        private async Task CreateNewWaveAsync(
            ApplicationDbContext db,
            AnalysisParentGroup parent,
            List<WavePendingContainer> readyContainers,
            string reason,
            CancellationToken ct)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    // ─── Wave #5 (1.11.0): serialize wave creation per parent ───
                    // Two housekeeping workers running RunPartialWaveScanAsync
                    // simultaneously could both compute the same nextWaveNumber
                    // here, race to the INSERT, and one of them would lose.
                    // The existing catch on IsUniqueConstraintViolation papered
                    // over the race but logged at debug level (silent to
                    // operators). Fix: take a Postgres transaction-scoped
                    // advisory lock keyed by the parent id BEFORE computing
                    // the next wave number. The lock is released automatically
                    // on COMMIT/ROLLBACK. Workers contending for the same
                    // parent serialize cleanly; workers on different parents
                    // run in parallel as before.
                    //
                    // Lock key derivation: pg_advisory_xact_lock takes a single
                    // bigint, so we hash the parent's Guid into one. Hash
                    // collisions across DIFFERENT parents are tolerable here
                    // because the worst case is two unrelated wave-creation
                    // operations briefly serializing — incorrect serialization
                    // costs latency, not correctness.
                    var advisoryKey = (long)BitConverter.ToInt64(parent.Id.ToByteArray(), 0);
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0})",
                        new object[] { advisoryKey },
                        ct);

                    // Get next wave number — EF Core 8+ can't translate DefaultIfEmpty().Max(),
                    // so project to nullable and use ?? 0 fallback for empty result.
                    var maxWaveNumber = await db.AnalysisGroups
                        .Where(g => g.ParentGroupId == parent.Id)
                        .MaxAsync(g => (int?)g.WaveNumber, ct) ?? 0;
                    var nextWaveNumber = maxWaveNumber + 1;

                    var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(parent.GroupIdentifier) ?? parent.GroupIdentifier;

                    // Wave groups need a unique GroupIdentifier — append wave suffix
                    var waveGroupIdentifier = $"{parent.GroupIdentifier}_W{nextWaveNumber}";

                    // Resolve the RCS link so cargo summary lookups by NormalizedGroupIdentifier
                    // can hit RCS-keyed data. InitialBatch waves get this set from the orchestrator;
                    // Timeout/AutoClose waves used to leave it null which broke /api/cargogroup
                    // resolution for the resulting "{BL}_W{N}" identifiers (Goods/ICUMS empty).
                    //
                    // 2026-05-07: pull rcs.ScannerType in the same query as a fallback for when
                    // parent.ScannerType is null (Timeout-reason waves under parent groups whose
                    // scannertype was never resolved upstream — 26 stranded waves in prod). The
                    // workbench's Scanner column rendered empty for those.
                    var rcsForWave = await db.RecordCompletenessStatuses
                        .AsNoTracking()
                        .Where(r => r.DeclarationNumber == parent.GroupIdentifier
                                 || r.BlNumber == parent.GroupIdentifier
                                 || r.ContainerGroupKey == parent.GroupIdentifier)
                        .Where(r => parent.ScannerType == null || r.ScannerType == null || r.ScannerType == parent.ScannerType)
                        .OrderByDescending(r => r.UpdatedAtUtc)
                        .Select(r => new { r.Id, r.ScannerType, r.ClearanceType })
                        .FirstOrDefaultAsync(ct);

                    // Third-tier fallback: containercompletenessstatuses keyed by the
                    // container numbers we're about to enrol. Catches cases where neither
                    // parent nor the RCS row itself has scannertype populated. In prod 26
                    // Timeout-reason waves landed with NULL scanner via the parent+RCS path;
                    // CCS has it for all 26 (see probe wavescannerlookup).
                    string? scannerFromCcs = null;
                    if (parent.ScannerType == null && rcsForWave?.ScannerType == null && readyContainers.Count > 0)
                    {
                        var rcContainerNumbers = readyContainers.Select(w => w.ContainerNumber).ToList();
                        scannerFromCcs = await db.ContainerCompletenessStatuses
                            .AsNoTracking()
                            .Where(s => rcContainerNumbers.Contains(s.ContainerNumber)
                                     && s.ScannerType != null)
                            .Select(s => s.ScannerType)
                            .FirstOrDefaultAsync(ct);
                    }

                    var resolvedScannerType = parent.ScannerType ?? rcsForWave?.ScannerType ?? scannerFromCcs;

                    // Sprint 5G2 / B1 lock-the-door: redundant Status="Ready" removed (default value).
                    var waveGroup = new AnalysisGroup
                    {
                        GroupIdentifier = waveGroupIdentifier,
                        NormalizedGroupIdentifier = normalized,
                        GroupType = GetRecordBackedGroupType(rcsForWave?.ClearanceType),
                        ScannerType = resolvedScannerType,
                        ParentGroupId = parent.Id,
                        WaveNumber = nextWaveNumber,
                        WaveCreatedReason = reason,
                        RecordCompletenessStatusId = rcsForWave?.Id
                    };
                    db.AnalysisGroups.Add(waveGroup);
                    await db.SaveChangesAsync(ct);

                    // Create AnalysisRecords for these containers
                    foreach (var wpc in readyContainers)
                    {
                        db.AnalysisRecords.Add(new AnalysisRecord
                        {
                            GroupId = waveGroup.Id,
                            ContainerNumber = wpc.ContainerNumber,
                            ScannerType = resolvedScannerType,
                            Status = "Ready",
                            CreatedAtUtc = DateTime.UtcNow
                        });

                        wpc.Status = "Processed";
                    }
                    await db.SaveChangesAsync(ct);

                    // Update WorkflowStage for these containers
                    var containerNumbers = readyContainers.Select(w => w.ContainerNumber).ToList();
                    var completenessRows = await db.ContainerCompletenessStatuses
                        .AsTracking()
                        .Where(s => containerNumbers.Contains(s.ContainerNumber)
                            && s.GroupIdentifier == parent.GroupIdentifier
                            && (string.IsNullOrEmpty(s.WorkflowStage) || s.WorkflowStage == "Pending"))
                        .ToListAsync(ct);

                    foreach (var row in completenessRows)
                    {
                        row.WorkflowStage = "ImageAnalysis";
                        row.UpdatedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync(ct);

                    // Check if parent is now complete
                    var remainingAwaiting = await db.WavePendingContainers
                        .CountAsync(w => w.ParentGroupId == parent.Id && w.Status == "Pending", ct);
                    if (remainingAwaiting == 0)
                    {
                        parent.Status = "Complete";
                        parent.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }

                    _logger.LogInformation(
                        "[WAVE-SCAN] Created Wave {WaveNumber} for {GroupIdentifier} (reason: {Reason}): {ContainerCount} containers. {Remaining} still awaiting images",
                        nextWaveNumber, parent.GroupIdentifier, reason, readyContainers.Count, remainingAwaiting);

                    await transaction.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Wave #5 (1.11.0): With the advisory lock above, this
                    // catch should never fire in practice — it's a safety net
                    // for the case where the lock somehow gets released early
                    // (e.g. nested transaction edge case) or the unique key
                    // collision comes from a different process not using the
                    // orchestrator path. Promoted from LogDebug to LogWarning
                    // so operators see if the lock isn't holding.
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    _logger.LogWarning(ex,
                        "[WAVE-SCAN] Duplicate wave detected for parent {GroupIdentifier} despite advisory lock " +
                        "— another process raced past the lock or the lock is misconfigured. Investigate.",
                        parent.GroupIdentifier);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    throw;
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            });
        }

        /// <summary>
        /// Auto-closes parent groups that have been active for longer than WaveAutoCloseDays.
        /// <summary>
        /// Detects containers that are Ready in RecordExpectedContainers but have no
        /// AnalysisRecord in any wave group. Feeds them into WavePendingContainers so
        /// RunPartialWaveScanAsync can create Wave N on the next cycle.
        /// This handles late-arriving containers for groups that are already terminal.
        /// </summary>
        private async Task CheckForLateArrivalsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            try
            {
                // Find Ready RecordExpectedContainers that don't have an AnalysisRecord yet
                var readyOrphans = await db.RecordExpectedContainers
                    .AsNoTracking()
                    .Where(e => e.Status == "Ready"
                        && !db.AnalysisRecords.Any(ar =>
                            ar.ContainerNumber == e.ContainerNumber
                            && db.AnalysisGroups.Any(ag =>
                                ag.Id == ar.GroupId
                                && ag.NormalizedGroupIdentifier == db.RecordCompletenessStatuses
                                    .Where(r => r.Id == e.RecordId)
                                    .Select(r => r.DeclarationNumber)
                                    .FirstOrDefault())))
                    .Take(200)
                    .ToListAsync(ct);

                if (readyOrphans.Count == 0) return;

                // Group by parent record
                var byRecord = readyOrphans.GroupBy(e => e.RecordId).ToList();
                int addedToWave = 0;

                // ✅ PERF FIX: Batch pre-fetch records, parent groups, and existing groups
                // instead of N+1 queries per record group
                var recordIds = byRecord.Select(g => g.Key).Distinct().ToList();
                var recordsMap = await db.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => recordIds.Contains(r.Id))
                    .ToDictionaryAsync(r => r.Id, ct);

                var declarationNumbers = recordsMap.Values
                    .Select(r => r.DeclarationNumber)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .ToList();

                var parentGroupsMap = (await db.AnalysisParentGroups
                    .AsTracking()
                    .Where(p => declarationNumbers.Contains(p.GroupIdentifier))
                    .ToListAsync(ct))
                    .GroupBy(p => p.GroupIdentifier)
                    .ToDictionary(g => g.Key, g => g.First());

                var existingGroupsMap = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => declarationNumbers.Contains(g.GroupIdentifier)
                             || declarationNumbers.Contains(g.NormalizedGroupIdentifier))
                    .ToListAsync(ct);

                foreach (var recordGroup in byRecord)
                {
                    if (!recordsMap.TryGetValue(recordGroup.Key, out var record)) continue;

                    // Find or create AnalysisParentGroup
                    parentGroupsMap.TryGetValue(record.DeclarationNumber, out var parentGroup);

                    if (parentGroup == null)
                    {
                        _logger.LogWarning("[WAVE-LATE] No AnalysisParentGroup for {Decl} — creating one", record.DeclarationNumber);
                        parentGroup = new AnalysisParentGroup
                        {
                            GroupIdentifier = record.DeclarationNumber,
                            ScannerType = record.ScannerType,
                            TotalExpectedContainers = record.TotalExpectedContainers,
                            Status = "Active"
                        };
                        db.AnalysisParentGroups.Add(parentGroup);
                        await db.SaveChangesAsync(ct);
                        parentGroupsMap[record.DeclarationNumber] = parentGroup;

                        // Link existing AnalysisGroup as Wave 1 if it exists
                        var existingGroup = existingGroupsMap.FirstOrDefault(g =>
                            g.GroupIdentifier == record.DeclarationNumber
                            || g.NormalizedGroupIdentifier == record.DeclarationNumber);
                        if (existingGroup != null && existingGroup.ParentGroupId == null)
                        {
                            existingGroup.ParentGroupId = parentGroup.Id;
                            existingGroup.WaveNumber = 1;
                            existingGroup.WaveCreatedReason ??= "InitialBatch";
                        }
                    }
                }

                // ✅ PERF FIX: Batch pre-fetch all pending containers across all parent groups
                // instead of AnyAsync per orphan container
                var parentGroupIds = parentGroupsMap.Values.Select(p => p.Id).Distinct().ToList();
                var existingPendingContainers = await db.WavePendingContainers
                    .Where(w => parentGroupIds.Contains(w.ParentGroupId))
                    .Select(w => new { w.ParentGroupId, w.ContainerNumber })
                    .ToListAsync(ct);
                var pendingLookup = new HashSet<string>(
                    existingPendingContainers.Select(e => $"{e.ParentGroupId}:{e.ContainerNumber}"),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var recordGroup in byRecord)
                {
                    if (!recordsMap.TryGetValue(recordGroup.Key, out var record)) continue;
                    if (!parentGroupsMap.TryGetValue(record.DeclarationNumber, out var parentGroup)) continue;

                    foreach (var orphan in recordGroup)
                    {
                        if (pendingLookup.Contains($"{parentGroup.Id}:{orphan.ContainerNumber}"))
                            continue;

                        db.WavePendingContainers.Add(new WavePendingContainer
                        {
                            ParentGroupId = parentGroup.Id,
                            ContainerNumber = orphan.ContainerNumber,
                            ScannerType = orphan.ScannerType ?? record.ScannerType,
                            Status = "Ready",
                            BecameReadyUtc = orphan.BecameReadyUtc ?? DateTime.UtcNow
                        });
                        addedToWave++;
                    }
                }

                if (addedToWave > 0)
                {
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("[WAVE-LATE] Added {Count} late-arriving container(s) to WavePendingContainers across {Groups} record(s)",
                        addedToWave, byRecord.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WAVE-LATE] Error checking for late arrivals");
            }
        }

        /// <summary>
        /// Auto-close wave parent groups whose pending containers have been stuck for too long.
        /// Creates a final wave with any remaining containers and marks the rest as NoImageAvailable.
        /// </summary>
        private async Task AutoCloseExpiredWaveParentsAsync(ApplicationDbContext db, AnalysisSettings settings, CancellationToken ct)
        {
            if (!settings.EnableWaveProcessing)
                return;

            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-settings.WaveAutoCloseDays);
                var expiredParents = await db.AnalysisParentGroups
                    .AsTracking()
                    .Where(p => p.Status == "Active" && p.CreatedAtUtc < cutoff)
                    .ToListAsync(ct);

                foreach (var parent in expiredParents)
                {
                    var remaining = await db.WavePendingContainers
                        .AsTracking()
                        .Where(w => w.ParentGroupId == parent.Id && (w.Status == "Pending" || w.Status == "Ready"))
                        .ToListAsync(ct);

                    // Separate ready vs still awaiting
                    var readyContainers = remaining.Where(w => w.Status == "Ready").ToList();
                    var awaitingContainers = remaining.Where(w => w.Status == "Pending").ToList();

                    // Create final wave with any ready containers
                    if (readyContainers.Any())
                    {
                        await CreateNewWaveAsync(db, parent, readyContainers, "AutoClose", ct);
                    }

                    // Mark remaining awaiting containers as NoImageAvailable
                    foreach (var wpc in awaitingContainers)
                    {
                        wpc.Status = "NoImageAvailable";
                    }

                    parent.Status = "AutoClosed";
                    parent.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "[WAVE-AUTOCLOSE] Auto-closed parent group {GroupIdentifier} after {Days} days. " +
                        "Final wave: {ReadyCount} containers. {AwaitingCount} containers marked NoImageAvailable",
                        parent.GroupIdentifier, settings.WaveAutoCloseDays, readyContainers.Count, awaitingContainers.Count);

                    // 1.16.0 — mirror the auto-close to the new record table
                    try
                    {
                        var record = await db.RecordCompletenessStatuses
                            .AsTracking()
                            .FirstOrDefaultAsync(r => r.DeclarationNumber == parent.GroupIdentifier, ct);
                        if (record != null && record.ArchivedAtUtc == null)
                        {
                            var nowUtc = DateTime.UtcNow;
                            record.ArchivedAtUtc = nowUtc;
                            record.ArchivalReason = "WaveAutoClosed";
                            record.Status = "Archived";
                            record.UpdatedAtUtc = nowUtc;

                            var orphanedChildren = await db.RecordExpectedContainers
                                .AsTracking()
                                .Where(e => e.RecordId == record.Id
                                         && (e.Status == "AwaitingScan" || e.Status == "Pending"))
                                .ToListAsync(ct);
                            foreach (var c in orphanedChildren)
                            {
                                c.Status = c.Status == "Pending" ? "NoImageAvailable" : "NoScanReceived";
                            }
                            await db.SaveChangesAsync(ct);
                            _logger.LogInformation(
                                "[WAVE-AUTOCLOSE] Record {RecordId} ({Decl}) archived in parallel with legacy parent close",
                                record.Id, record.DeclarationNumber);
                        }
                    }
                    catch (Exception recEx)
                    {
                        _logger.LogWarning(recEx, "[WAVE-AUTOCLOSE] Record-side archive mirror failed for {GroupIdentifier} (non-fatal)", parent.GroupIdentifier);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WAVE-AUTOCLOSE] Error in wave auto-close");
            }
        }

        #endregion

        #region Housekeeping Workflow

        private async Task RunHousekeepingWorkflowAsync(ApplicationDbContext db, DateTime now, CancellationToken stoppingToken)
        {
            try
            {
                // Remove orphaned assignments
                var orphaned = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => !db.AnalysisGroups.Any(g => g.Id == a.GroupId))
                    .ToListAsync(stoppingToken);
                if (orphaned.Count > 0)
                {
                    db.AnalysisAssignments.RemoveRange(orphaned);
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("[HOUSEKEEPING] Removed {Count} orphaned assignments", orphaned.Count);
                }

                await ExpireStaleActiveAssignmentsAsync(db, now, stoppingToken);

                // Fix stuck groups
                var stuckGroups = await db.AnalysisGroups
                    .AsTracking()
                    .Where(g => (g.Status == AnalysisStatuses.AnalystAssigned || g.Status == AnalysisStatuses.AuditAssigned || g.Status == "Assigned")
                        && !string.IsNullOrEmpty(g.GroupIdentifier))
                    .ToListAsync(stoppingToken);

                var groupsToFix = new List<AnalysisGroup>();
                foreach (var g in stuckGroups)
                {
                    var active = await db.AnalysisAssignments
                        .AnyAsync(a => a.GroupId == g.Id && a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);
                    if (!active)
                    {
                        var normalizedForLookup = !string.IsNullOrEmpty(g.NormalizedGroupIdentifier) ? g.NormalizedGroupIdentifier : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) ?? g.GroupIdentifier);
                        var containers = await db.ContainerCompletenessStatuses
                            .Where(c => c.GroupIdentifier == normalizedForLookup)
                            .ToListAsync(stoppingToken);

                        if (containers.Any())
                        {
                            var totalContainers = containers.Count;
                            var auditCount = containers.Count(c => c.WorkflowStage == "Audit");
                            var completedCount = containers.Count(c => c.WorkflowStage == "PendingSubmission" || c.WorkflowStage == "Submitted" || c.WorkflowStage == "Completed");
                            var imageAnalysisCount = containers.Count(c => c.WorkflowStage == "ImageAnalysis");

                            var correctStatus = WorkflowStageStatusHelper.ComputeCorrectStatusForStuckGroup(
                                totalContainers, imageAnalysisCount, auditCount, completedCount, g.Status);

                            if (correctStatus != null && g.Status != correctStatus)
                            {
                                _logger.LogInformation(
                                    "[HOUSEKEEPING] Fixing stuck group {GroupId} ({GroupIdentifier}): {OldStatus} → {NewStatus}",
                                    g.Id, g.GroupIdentifier, g.Status, correctStatus);
                                // Sprint 5G2 / B1: route through the state-machine facade. Validator now tracks the
                                // edges this sweep can produce (AnalystAssigned/AuditAssigned → AnalystCompleted/Ready/Completed).
                                // The legacy "Assigned" legacy status (filter at 3524) is NOT in the validator — if a real
                                // legacy row appears we'll surface a logged exception and skip rather than silently violate
                                // the legal table; in practice no live writer produces "Assigned" for AnalysisGroup.
                                try
                                {
                                    await AnalysisGroupStateMachine.TransitionAsync(
                                        db, g, correctStatus,
                                        triggerName: "HousekeepingStuckGroupResync",
                                        actor: "ORCHESTRATOR-HOUSEKEEPING",
                                        reason: $"Stuck group with no active assignment; WorkflowStage indicates correct status is {correctStatus} (computed from CCS distribution).",
                                        correlationId: null,
                                        ct: stoppingToken);
                                    g.UpdatedAtUtc = now;
                                    groupsToFix.Add(g);
                                }
                                catch (InvalidOperationException ex)
                                {
                                    _logger.LogError(ex, "[HOUSEKEEPING] Skipping stuck group {GroupId} {OldStatus}→{NewStatus} — transition not in validator legal table",
                                        g.Id, g.Status, correctStatus);
                                }
                            }
                        }
                        else
                        {
                            if (g.Status != AnalysisStatuses.Ready)
                            {
                                _logger.LogWarning(
                                    "[HOUSEKEEPING] Fixing stuck group {GroupId} ({GroupIdentifier}): {OldStatus} → Ready (no containers found)",
                                    g.Id, g.GroupIdentifier, g.Status);
                                // Sprint 5G2 / B1: route through the state-machine facade. AnalystAssigned → Ready
                                // is in the legal table; AuditAssigned → Ready is too (added for housekeeping above).
                                try
                                {
                                    await AnalysisGroupStateMachine.TransitionAsync(
                                        db, g, AnalysisStatuses.Ready,
                                        triggerName: "HousekeepingStuckGroupNoContainers",
                                        actor: "ORCHESTRATOR-HOUSEKEEPING",
                                        reason: $"Stuck group with no active assignment AND no containers found in CCS; reverting to Ready for re-intake.",
                                        correlationId: null,
                                        ct: stoppingToken);
                                    g.UpdatedAtUtc = now;
                                    groupsToFix.Add(g);
                                }
                                catch (InvalidOperationException ex)
                                {
                                    _logger.LogError(ex, "[HOUSEKEEPING] Skipping stuck group {GroupId} {OldStatus}→Ready — transition not in validator legal table",
                                        g.Id, g.Status);
                                }
                            }
                        }
                    }
                }

                if (groupsToFix.Any())
                {
                    _logger.LogInformation("[HOUSEKEEPING] Fixed {Count} stuck groups", groupsToFix.Count);
                }

                // Split jobs may legitimately carry a two-container identifier, but
                // AnalysisGroups must not. Quarantine any legacy rows before the
                // assignment reconciler can re-materialize them.
                await QuarantineCompositeContainerPairGroupsAsync(db, now, stoppingToken);

                // Synchronize status with WorkflowStage
                await SynchronizeStatusWithWorkflowStageAsync(db, now, stoppingToken);

                // Sweep: fix any groups stuck with decisions but still in analyst phase
                await SweepStuckDecidedGroupsAsync(db, stoppingToken);

                // Wave Processing: Check for newly ready containers and create waves
                var settings = await db.AnalysisSettings.FirstOrDefaultAsync(stoppingToken) ?? new AnalysisSettings();
                await RunPartialWaveScanAsync(db, settings, stoppingToken);
                await CheckForLateArrivalsAsync(db, stoppingToken);
                await AutoCloseExpiredWaveParentsAsync(db, settings, stoppingToken);

                // Detect & close stale groups (all containers decided, or empty+old)
                await CloseStaleDecidedGroupsAsync(db, stoppingToken);

                // Reconcile materialized assignment queue (safety net)
                await _readyGroupsCache.ReconcileQueueAsync(db, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HOUSEKEEPING] Error in housekeeping workflow");
            }
        }

        /// <summary>
        /// Expires active assignment rows whose lease is already past due. This
        /// clears the partial unique index on active group assignments and removes
        /// stale materialized queue rows, allowing Ready groups to be reclaimed by
        /// the next assignment cycle.
        /// </summary>
        private async Task ExpireStaleActiveAssignmentsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            var staleAssignmentIds = await db.AnalysisAssignments
                .AsNoTracking()
                .Where(a => a.State == "Active"
                    && a.LeaseUntilUtc.HasValue
                    && a.LeaseUntilUtc <= now)
                .OrderBy(a => a.LeaseUntilUtc)
                .Select(a => a.Id)
                .Take(1000)
                .ToListAsync(ct);

            if (staleAssignmentIds.Count == 0)
                return;

            var expired = await db.AnalysisAssignments
                .Where(a => staleAssignmentIds.Contains(a.Id) && a.State == "Active")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.State, "Expired")
                    .SetProperty(a => a.UpdatedAtUtc, now),
                    ct);

            var removedQueueRows = await db.AnalysisQueueEntries
                .Where(e => staleAssignmentIds.Contains(e.AssignmentId))
                .ExecuteDeleteAsync(ct);

            _logger.LogInformation(
                "[ASSIGNMENT-JANITOR] Expired {ExpiredCount} stale active assignment(s), removed {QueueCount} queue row(s)",
                expired,
                removedQueueRows);
        }

        /// <summary>
        /// Releases legacy AnalysisGroups whose GroupIdentifier is a scan-pair
        /// container list such as "ABCD1234567,EFGH7654321". The split subsystem
        /// owns that pair key; image analysis assignment keys must stay anchored
        /// to declaration/BL/CMR operational records.
        /// </summary>
        private async Task QuarantineCompositeContainerPairGroupsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            var candidateGroups = await db.AnalysisGroups
                .AsTracking()
                .Where(g => !string.IsNullOrEmpty(g.GroupIdentifier)
                    && (g.Status == AnalysisStatuses.Ready
                        || g.Status == AnalysisStatuses.AnalystAssigned
                        || g.Status == AnalysisStatuses.AuditAssigned
                        || g.Status == "Assigned"))
                .OrderByDescending(g => g.UpdatedAtUtc ?? g.CreatedAtUtc)
                .Take(500)
                .ToListAsync(ct);

            var compositeGroups = candidateGroups
                .Where(g => IsCompositeContainerPairIdentifier(g.GroupIdentifier))
                .Take(100)
                .ToList();

            if (compositeGroups.Count == 0)
                return;

            var compositeGroupIds = compositeGroups.Select(g => g.Id).ToList();

            var activeAssignments = await db.AnalysisAssignments
                .AsTracking()
                .Where(a => compositeGroupIds.Contains(a.GroupId) && a.State == "Active")
                .ToListAsync(ct);

            foreach (var assignment in activeAssignments)
            {
                assignment.State = "Expired";
                assignment.UpdatedAtUtc = now;
            }

            var removedQueueRows = await db.AnalysisQueueEntries
                .Where(e => compositeGroupIds.Contains(e.GroupId))
                .ExecuteDeleteAsync(ct);

            if (activeAssignments.Count > 0)
                await db.SaveChangesAsync(ct);

            var transitioned = 0;
            foreach (var group in compositeGroups)
            {
                if (group.Status == AnalysisStatuses.Ready
                    || group.Status == AnalysisStatuses.AnalystAssigned
                    || group.Status == AnalysisStatuses.AuditAssigned)
                {
                    await AnalysisGroupStateMachine.TransitionAsync(
                        db,
                        group,
                        AnalysisStatuses.Cancelled,
                        triggerName: "CompositeScanPairQuarantine",
                        actor: "ORCHESTRATOR-HOUSEKEEPING",
                        reason: "AnalysisGroup identifier is a split scan-pair container list; terminal quarantine lets the record-backed intake path own the real cargo groups.",
                        correlationId: null,
                        ct: ct);
                    transitioned++;
                }
            }

            if (transitioned > 0)
                await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[COMPOSITE-SCAN-GUARD] Quarantined {GroupCount} scan-pair AnalysisGroup(s); expired {AssignmentCount} assignment(s), removed {QueueCount} queue row(s). Groups: {Groups}",
                compositeGroups.Count,
                activeAssignments.Count,
                removedQueueRows,
                string.Join(", ", compositeGroups.Select(g => $"{g.Id}:{g.GroupIdentifier}").Take(10)));
        }

        /// <summary>
        /// Detects and closes stale AnalysisGroups that are still in active states but shouldn't be.
        /// Three patterns handled (discovered 2026-04-14 cleanup — found 291 stale groups):
        ///
        /// 1. ALL containers in the group have a decision (in this group OR any other group).
        ///    Root cause: pre-wave architecture created per-container groups that never got closed
        ///    when the container was later analyzed under its declaration-level group. Also covers
        ///    declaration-named groups where the container was split across multiple groups.
        ///
        /// 2. Group has zero records and is older than 7 days.
        ///    Root cause: group created as a shell but never populated with records; likely from
        ///    an aborted intake or a race condition.
        ///
        /// Runs every housekeeping cycle (~30-60s). All actions logged at Warning so they surface
        /// on the dashboard RecentErrors table.
        /// </summary>
        private async Task CloseStaleDecidedGroupsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            try
            {
                var now = DateTime.UtcNow;
                // B2′-A (2026-05-09): excluded AnalystCompleted + AuditAssigned from the stale-sweep
                // list. Reason: when no audit user has IsReady=true, the orchestrator's
                // AutoAssignByRoleAsync(Audit) returns silently and AGs accumulate in AnalystCompleted.
                // The previous version of this sweep treated "all containers decided" as equivalent
                // to "all done" and auto-completed those AGs with SYSTEM-HOUSEKEEPING synthetic audit
                // rows, bypassing the audit stage entirely. AnalystCompleted with all-decided
                // containers is the *normal* pre-audit state; AuditAssigned with all-decided is the
                // *normal* in-audit state. Neither is an orphan. The sweep stays focused on actual
                // orphan states where DA/analyst crashed mid-flow:
                //   - Ready / AnalystAssigned: containers got decisions out-of-band (DA bypass,
                //     side-effects); AG never advanced.
                //   - AnalystReady / AuditReady: legacy intermediate states from older flows.
                // Surfacing AnalystCompleted backlog goes to Phase B / B3 (queue-depth view +
                // dashboard card). See journal entry 2026-05-09 audit-queue-investigation.
                var activeStates = new[]
                {
                    AnalysisStatuses.Ready,
                    AnalysisStatuses.AnalystAssigned,
                    "AnalystReady",
                    "AuditReady"
                };

                // ── Pattern 1: All containers in group have decisions somewhere ─────
                // Detected as: group has records, and for each record's container number
                // there exists an ImageAnalysisDecisions row (in any group).
                //
                // EF Core 9 NOTE: we cannot project `Containers = subquery.ToList()` — that
                // throws "The query contains a projection '... DbSet<AnalysisRecord>() ...'"
                // and corrupts the Npgsql connection pool. Split into two queries instead.
                var activeGroupIds = await db.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => activeStates.Contains(g.Status))
                    .Select(g => g.Id)
                    .ToListAsync(ct);

                var groupMeta = await db.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => activeGroupIds.Contains(g.Id))
                    .Select(g => new { g.Id, g.GroupIdentifier, g.ScannerType })
                    .ToDictionaryAsync(g => g.Id, ct);

                var recordPairs = await db.AnalysisRecords
                    .AsNoTracking()
                    .Where(r => activeGroupIds.Contains(r.GroupId))
                    .Select(r => new { r.GroupId, r.ContainerNumber })
                    .ToListAsync(ct);

                var groupsWithRecords = recordPairs
                    .GroupBy(x => x.GroupId)
                    .Select(g => new
                    {
                        Id = g.Key,
                        GroupIdentifier = "",  // not needed for the decision-match check below
                        Containers = g.Select(x => x.ContainerNumber).Distinct().ToList()
                    })
                    .ToList();

                if (groupsWithRecords.Count > 0)
                {
                    // Collect all unique container numbers across candidates
                    var allContainers = groupsWithRecords
                        .SelectMany(x => x.Containers)
                        .Distinct()
                        .ToList();

                    // Find which have decisions (expanded to include IAD details for synthetic audit rows)
                    var decidedIads = await db.ImageAnalysisDecisions
                        .AsNoTracking()
                        .Where(d => allContainers.Contains(d.ContainerNumber))
                        .Select(d => new { d.Id, d.ContainerNumber, d.Decision, d.GroupIdentifier })
                        .ToListAsync(ct);

                    var decidedSet = new HashSet<string>(
                        decidedIads.Select(d => d.ContainerNumber),
                        StringComparer.OrdinalIgnoreCase);

                    var iadByContainer = decidedIads
                        .GroupBy(d => d.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Id).First(),
                                      StringComparer.OrdinalIgnoreCase);

                    // A group is stale if it has containers AND ALL of them are in decidedSet
                    // The .Count > 0 guard prevents vacuous-truth: LINQ All() returns true on empty
                    // sequences, which would prematurely complete wave-child AGs before any analyst
                    // decision exists (containers not yet linked at sweep time).
                    var staleIds = groupsWithRecords
                        .Where(x => x.Containers.Count > 0 && x.Containers.All(c => decidedSet.Contains(c)))
                        .Select(x => x.Id)
                        .ToList();

                    if (staleIds.Count > 0)
                    {
                        // Write synthetic AuditDecision rows for legitimately-swept AGs before bulk-completing.
                        // These AGs had all containers decided but audit never happened — the housekeeping
                        // sweep completes them, so we record the action in the audit ledger.
                        foreach (var grp in groupsWithRecords.Where(x => staleIds.Contains(x.Id)))
                        {
                            var meta = groupMeta.GetValueOrDefault(grp.Id);
                            foreach (var containerNum in grp.Containers)
                            {
                                if (!iadByContainer.TryGetValue(containerNum, out var iad)) continue;
                                db.AuditDecisions.Add(new AuditDecision
                                {
                                    ContainerNumber         = containerNum,
                                    GroupIdentifier         = iad.GroupIdentifier ?? meta?.GroupIdentifier ?? grp.Id.ToString(),
                                    ScannerType             = meta?.ScannerType ?? "Unknown",
                                    ImageAnalysisDecisionId = iad.Id,
                                    Decision                = iad.Decision ?? "Approved",
                                    AuditNotes              = "Auto-completed by housekeeping sweep — all containers decided, audit stage not completed",
                                    AuditedBy               = "SYSTEM-HOUSEKEEPING",
                                    AuditedAt               = now,
                                    IsCompleted             = true,
                                    CompletedAt             = now,
                                    OverallGroupDecision    = "Approved",
                                    CreatedAt               = now
                                });
                            }
                        }
                        await db.SaveChangesAsync(ct);

                        var releasedCount = await db.AnalysisAssignments
                            .Where(a => staleIds.Contains(a.GroupId) && a.State == "Active")
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(a => a.State, "Released")
                                .SetProperty(a => a.UpdatedAtUtc, now), ct);

                        // Delete queue entries for the released assignments
                        await db.AnalysisQueueEntries
                            .Where(q => staleIds.Contains(q.GroupId))
                            .ExecuteDeleteAsync(ct);

                        var completedCount = await db.AnalysisGroups
                            .Where(g => staleIds.Contains(g.Id))
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(g => g.Status, AnalysisStatuses.Completed)
                                .SetProperty(g => g.UpdatedAtUtc, now), ct);

                        _logger.LogWarning(
                            "[HOUSEKEEPING] Closed {GroupCount} stale decided group(s); released {AsstCount} stranded assignment(s)",
                            completedCount, releasedCount);
                    }
                }

                // ── Pattern 2: Empty Ready groups older than 7 days ─────────────────
                var cutoff = now.AddDays(-7);
                var emptyOldIds = await db.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.Status == AnalysisStatuses.Ready
                        && g.CreatedAtUtc < cutoff
                        && !db.AnalysisRecords.Any(r => r.GroupId == g.Id))
                    .Select(g => g.Id)
                    .ToListAsync(ct);

                if (emptyOldIds.Count > 0)
                {
                    var archivedCount = await db.AnalysisGroups
                        .Where(g => emptyOldIds.Contains(g.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(g => g.Status, "Archived")
                            .SetProperty(g => g.UpdatedAtUtc, now), ct);

                    _logger.LogWarning(
                        "[HOUSEKEEPING] Archived {Count} empty Ready group(s) older than 7 days",
                        archivedCount);
                }

                // ── Pattern 3: Audit-stage backlog visibility (B2′-A, 2026-05-09) ────
                // Surface (don't act on) AnalystCompleted + AuditAssigned AGs that are
                // older than 30 minutes. If the orchestrator's AutoAssignByRoleAsync(Audit)
                // can't find a Ready auditor, AGs pile up here. The B2′-A change above
                // intentionally stops the auto-complete bypass; this log tells operators
                // *why* the audit page has work showing up (or not) and how stale it is.
                // Throttled by piggy-backing on the housekeeping cadence (2-minute loop).
                var auditBacklogStaleCutoff = now.AddMinutes(-30);
                var auditBacklog = await db.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => (g.Status == AnalysisStatuses.AnalystCompleted
                              || g.Status == AnalysisStatuses.AuditAssigned)
                        && g.UpdatedAtUtc < auditBacklogStaleCutoff)
                    .GroupBy(g => g.Status)
                    .Select(grp => new { Status = grp.Key, Count = grp.Count(), Oldest = grp.Min(g => g.UpdatedAtUtc) })
                    .ToListAsync(ct);

                var totalAuditBacklog = auditBacklog.Sum(b => b.Count);
                DateTime? oldestBacklogTimestamp = auditBacklog
                    .Select(b => b.Oldest)
                    .Where(d => d.HasValue)
                    .DefaultIfEmpty()
                    .Min();

                foreach (var bucket in auditBacklog)
                {
                    var ageMinutes = bucket.Oldest.HasValue
                        ? (now - bucket.Oldest.Value).TotalMinutes
                        : 0;
                    _logger.LogWarning(
                        "[HOUSEKEEPING] Audit-stage backlog: {Count} AG(s) in {Status} older than 30 min (oldest {AgeMinutes:F0} min). " +
                        "Check userreadiness for Role='Audit' — if all IsReady=False or stale, no auditor is online to pick this up.",
                        bucket.Count, bucket.Status, ageMinutes);
                }

                // ── Resilience item 3 (2026-05-09): AuditPoolEmpty dead-mans-switch ──
                // Promote the passive [HOUSEKEEPING] log above to an active Critical
                // dashboard alert when ALL of:
                //   (a) totalAuditBacklog > 0 — there's work waiting in AnalystCompleted
                //       or AuditAssigned older than 30 min (the same predicate the
                //       backlog log uses).
                //   (b) zero auditors are operationally Ready — userreadiness has no
                //       row with IsReady=true AND LastHeartbeat >= now-60min for
                //       Role='Audit' (60-min idle window matches the heartbeat cleanup
                //       in CleanupExpiredUserReadinessAsync via MaxIdleMinutesForReadiness).
                //   (c) the (a)+(b) combination has held for >30 min — guards against
                //       brief lunch-break / sign-off gaps. Tracked via
                //       _firstAuditPoolEmptyAt; reset to MinValue on each recovery.
                //
                // Idempotency: re-fire suppressed for 60 min via _lastAuditPoolEmptyAlertAt
                // (the IDashboardAlertService dedupe path also collapses on (Type, Title)
                // within 30 min and on any unacknowledged row, so this is belt+braces).
                //
                // Reasoning trace (test-equivalent):
                //   T+0    backlog appears, no auditors. _firstAuditPoolEmptyAt = T+0.
                //          totalBacklog>0 + readyAuditors=0 + dwell=0 → no alert.
                //   T+30   condition still holds. dwell=30 → alert fires; _lastAlertAt=T+30.
                //   T+45   condition still holds. dwell=45 but cooldown unexpired → no alert.
                //   T+60   auditor goes Ready. readyAuditors≥1 → reset _firstAuditPoolEmptyAt=MinValue.
                //   T+90   pool empties again. _firstAuditPoolEmptyAt=T+90; dwell timer restarts.
                //   T+120  alerted again. Cooldown=60 min from T+30 has expired (T+90 > T+30+60).
                //
                // No DB schema change — uses existing dashboardalerts via IDashboardAlertService.
                try
                {
                    var readyAuditorCutoff = now - _auditPoolReadinessFreshness;
                    var readyAuditorCount = await db.UserReadiness
                        .AsNoTracking()
                        .CountAsync(r => r.Role == "Audit"
                            && r.IsReady
                            && r.LastHeartbeat >= readyAuditorCutoff, ct);

                    var poolEmpty = totalAuditBacklog > 0 && readyAuditorCount == 0;

                    if (poolEmpty)
                    {
                        if (_firstAuditPoolEmptyAt == DateTime.MinValue)
                        {
                            _firstAuditPoolEmptyAt = now;
                        }

                        var dwell = now - _firstAuditPoolEmptyAt;
                        var sinceLastAlert = now - _lastAuditPoolEmptyAlertAt;

                        if (dwell >= _auditPoolEmptyAlertDwell
                            && sinceLastAlert >= _auditPoolEmptyAlertCooldown)
                        {
                            var oldestAge = oldestBacklogTimestamp.HasValue
                                ? (now - oldestBacklogTimestamp.Value).TotalMinutes
                                : 0;

                            var bucketSummary = string.Join(", ",
                                auditBacklog.Select(b => $"{b.Count}×{b.Status}"));

                            using var scope = _scopeFactory.CreateScope();
                            var alerts = scope.ServiceProvider.GetRequiredService<IDashboardAlertService>();
                            await alerts.RaiseAsync(
                                type: "AuditPoolEmpty",
                                severity: "Critical",
                                title: "Audit pool empty — no auditor Ready while backlog grows",
                                description:
                                    $"{totalAuditBacklog} AG(s) older than 30 min ({bucketSummary}); " +
                                    $"oldest pending {oldestAge:F0} min; zero userreadiness rows with " +
                                    $"Role='Audit', IsReady=true, and heartbeat in the last 60 min. " +
                                    $"Dwell {dwell.TotalMinutes:F0} min. Operator action: log in an auditor and toggle Ready.",
                                source: nameof(ImageAnalysisOrchestratorService) + " (CloseStaleDecidedGroupsAsync)",
                                ct: ct);

                            _lastAuditPoolEmptyAlertAt = now;

                            _logger.LogWarning(
                                "[HOUSEKEEPING] AuditPoolEmpty Critical alert raised: backlog={Backlog} ({Buckets}), " +
                                "oldest={OldestAge:F0}min, dwell={Dwell:F0}min, readyAuditors=0",
                                totalAuditBacklog, bucketSummary, oldestAge, dwell.TotalMinutes);
                        }
                    }
                    else
                    {
                        // Recovery: backlog drained OR an auditor came online. Reset
                        // the dwell tracker so the next pool-empty event starts a
                        // fresh 30-min window. Cooldown timer is *not* reset — it
                        // self-expires after 60 min.
                        if (_firstAuditPoolEmptyAt != DateTime.MinValue)
                        {
                            _logger.LogInformation(
                                "[HOUSEKEEPING] Audit pool recovered: backlog={Backlog}, readyAuditors={ReadyAuditors}; " +
                                "dwell tracker reset.",
                                totalAuditBacklog, readyAuditorCount);
                            _firstAuditPoolEmptyAt = DateTime.MinValue;
                        }
                    }
                }
                catch (Exception alertEx)
                {
                    _logger.LogError(alertEx,
                        "[HOUSEKEEPING] AuditPoolEmpty alert path failed; continuing housekeeping. " +
                        "totalAuditBacklog={Backlog}", totalAuditBacklog);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HOUSEKEEPING] Error closing stale decided groups");
            }
        }

        private async Task SynchronizeStatusWithWorkflowStageAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            try
            {
                var readyGroups = await db.AnalysisGroups
                    .Where(g => g.Status == AnalysisStatuses.Ready && !string.IsNullOrEmpty(g.GroupIdentifier))
                    .Select(g => new { g.GroupIdentifier, g.NormalizedGroupIdentifier })
                    .Distinct()
                    .ToListAsync(ct);

                if (!readyGroups.Any()) return;

                // ✅ Use NormalizedGroupIdentifier for joins with ContainerCompletenessStatus (single source of truth)
                var normalizedForCompleteness = readyGroups
                    .Select(g => !string.IsNullOrEmpty(g.NormalizedGroupIdentifier) ? g.NormalizedGroupIdentifier : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) ?? g.GroupIdentifier))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .ToList();

                // Batch process to avoid CTE generation
                var allWorkflowStats = new List<(string GroupIdentifier, int TotalContainers, int ImageAnalysisContainers, int AuditContainers, int CompletedContainers)>();
                const int batchSize = 100;

                for (int i = 0; i < normalizedForCompleteness.Count; i += batchSize)
                {
                    var batch = normalizedForCompleteness.Skip(i).Take(batchSize).ToList();
                    var placeholders = string.Join(",", batch.Select((_, idx) => $"'{batch[idx].Replace("'", "''")}'"));

                    var batchContainers = await db.ContainerCompletenessStatuses
                        .FromSql($"SELECT * FROM ContainerCompletenessStatuses WHERE GroupIdentifier IN ({placeholders})")
                        .AsNoTracking()
                        .ToListAsync(ct);

                    var batchStats = batchContainers
                        .GroupBy(c => c.GroupIdentifier)
                        .Select(g => new
                        {
                            GroupIdentifier = g.Key,
                            TotalContainers = g.Count(),
                            ImageAnalysisContainers = g.Count(c => c.WorkflowStage == "ImageAnalysis"),
                            AuditContainers = g.Count(c => c.WorkflowStage == "Audit"),
                            CompletedContainers = g.Count(c => c.WorkflowStage == "PendingSubmission" || c.WorkflowStage == "Submitted" || c.WorkflowStage == "Completed")
                        })
                        .ToList();

                    allWorkflowStats.AddRange(batchStats.Select(s => (
                        s.GroupIdentifier ?? string.Empty,
                        s.TotalContainers,
                        s.ImageAnalysisContainers,
                        s.AuditContainers,
                        s.CompletedContainers
                    )));
                }

                var groupsToFix = allWorkflowStats
                    .Where(w => w.TotalContainers > 0 && w.ImageAnalysisContainers == 0)
                    .ToList();

                if (!groupsToFix.Any()) return;

                var groupsToSync = new List<(AnalysisGroup Group, string NewStatus, string Reason)>();

                // ✅ groupsToFix has base id from Completeness; match on NormalizedGroupIdentifier
                var groupIdentifiersToFix = readyGroups
                    .Where(r => groupsToFix.Any(gtf =>
                    {
                        var norm = !string.IsNullOrEmpty(r.NormalizedGroupIdentifier) ? r.NormalizedGroupIdentifier : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(r.GroupIdentifier) ?? r.GroupIdentifier);
                        return norm == gtf.GroupIdentifier;
                    }))
                    .Select(r => r.GroupIdentifier)
                    .Distinct()
                    .ToList();
                var analysisGroups = new List<AnalysisGroup>();
                const int groupBatchSize = 1000;

                if (groupIdentifiersToFix.Count > 0)
                {
                    for (int i = 0; i < groupIdentifiersToFix.Count; i += groupBatchSize)
                    {
                        var batch = groupIdentifiersToFix.Skip(i).Take(groupBatchSize).ToList();
                        var placeholders = string.Join(",", batch.Select((_, idx) => $"'{batch[idx].Replace("'", "''")}'"));
                        var batchGroups = await db.AnalysisGroups
                            .FromSql($"SELECT * FROM AnalysisGroups WHERE GroupIdentifier IN ({placeholders}) AND Status = 'Ready'")
                            .AsTracking()
                            .ToListAsync(ct);
                        analysisGroups.AddRange(batchGroups);
                    }
                }

                foreach (var group in analysisGroups)
                {
                    var groupNormalized = !string.IsNullOrEmpty(group.NormalizedGroupIdentifier) ? group.NormalizedGroupIdentifier : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(group.GroupIdentifier) ?? group.GroupIdentifier);
                    var stats = groupsToFix.FirstOrDefault(s => s.GroupIdentifier == groupNormalized);
                    if (stats.GroupIdentifier == null || stats.TotalContainers == 0) continue;

                    var pendingCount = stats.TotalContainers - stats.ImageAnalysisContainers - stats.AuditContainers - stats.CompletedContainers;
                    var newStatus = WorkflowStageStatusHelper.ComputeStatusFromWorkflowStage(
                        stats.TotalContainers, stats.ImageAnalysisContainers, stats.AuditContainers, stats.CompletedContainers, Math.Max(0, pendingCount));
                    if (newStatus != null)
                        groupsToSync.Add((group, newStatus, $"WorkflowStage sync: {stats.ImageAnalysisContainers} ImageAnalysis, {stats.AuditContainers} Audit, {stats.CompletedContainers} Completed"));
                }

                if (groupsToSync.Any())
                {
                    // Sprint 5G2 / B1: routed through facade per group. Each transition gets its
                    // own audit row + SaveChangesAsync — N round-trips instead of one batch save,
                    // but every change is auditable. Illegal transitions (e.g. legacy "Assigned"
                    // status that's not in the validator's table) log + skip rather than break
                    // the sweep.
                    var syncedCount = 0;
                    foreach (var (group, newStatus, reason) in groupsToSync)
                    {
                        var oldStatus = group.Status;
                        _logger.LogInformation(
                            "[HOUSEKEEPING] Syncing group {GroupId} ({GroupIdentifier}) from {OldStatus} to {NewStatus}. Reason: {Reason}",
                            group.Id, group.GroupIdentifier, oldStatus, newStatus, reason);

                        try
                        {
                            await AnalysisGroupStateMachine.TransitionAsync(
                                db, group, newStatus,
                                triggerName: "WorkflowStageDriftSync",
                                actor: "ORCHESTRATOR-HOUSEKEEPING",
                                reason: reason,
                                correlationId: null,
                                ct: ct);
                            syncedCount++;
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "[HOUSEKEEPING] Skipping illegal drift-sync transition {From}→{To} for group {GroupId} ({GroupIdentifier})",
                                oldStatus, newStatus, group.Id, group.GroupIdentifier);
                        }
                    }

                    _logger.LogInformation("[HOUSEKEEPING] Synchronized {Count} groups based on WorkflowStage mismatch", syncedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HOUSEKEEPING] Error synchronizing AnalysisGroup.Status with WorkflowStage");
            }
        }

        #endregion

        #region Work Count Helpers (Phase 3.1: Adaptive Polling)

        /// <summary>
        /// Count pending work items for intake workflow
        /// </summary>
        private async Task<int> GetIntakeWorkCountAsync(ApplicationDbContext db, CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("intakeWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                var minBatchSize = await db.AnalysisSettings
                    .Select(s => s.WaveMinBatchSize)
                    .FirstOrDefaultAsync(stoppingToken);
                minBatchSize = Math.Max(1, minBatchSize);

                // Intake is now record-anchored. The old container-completeness
                // counter included rows already sitting in ImageAnalysis, so the
                // orchestrator kept reporting hundreds of "work" items even after
                // the real record intake backlog had drained.
                var count = await db.RecordCompletenessStatuses
                    .Where(r => (r.Status == "Ready"
                            || (r.Status == "PartiallyReady" && r.ContainersReady >= minBatchSize))
                        && r.ArchivedAtUtc == null
                        && !db.AnalysisGroups.Any(g => g.RecordCompletenessStatusId == r.Id))
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["intakeWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for assignment workflow
        /// </summary>
        private async Task<int> GetAssignmentWorkCountAsync(ApplicationDbContext db, CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("assignmentWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    _logger.LogDebug("[ASSIGNMENT-POLLING] Using cached work count: {Count}", cachedCount);
                    return cachedCount;
                }

                var readyCount = await db.AnalysisGroups
                    .Where(g => g.Status == AnalysisStatuses.Ready || g.Status == "Ready")
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["assignmentWorkCount"] = readyCount;
                }

                _logger.LogDebug("[ASSIGNMENT-POLLING] Calculated work count from database: {Count} Ready groups", readyCount);
                return readyCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ASSIGNMENT-POLLING] Error calculating work count, returning 0");
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for submission workflow
        /// </summary>
        private async Task<int> GetSubmissionWorkCountAsync(ApplicationDbContext db, CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("submissionWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                var count = await db.AnalysisGroups
                    .Where(g => g.Status == AnalysisStatuses.AuditCompleted || g.Status == "AuditCompleted")
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["submissionWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region Helper Methods

        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is PostgresException sqlEx)
            {
                return sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase);
            }

            var errorMessage = ex.Message;
            return errorMessage.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase) ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx)
            {
                return pgEx.SqlState == "23505"; // unique_violation
            }
            return false;
        }

        private static bool TryParseBaseAseInspectionId(string? inspectionId, out int aseInspectionId)
        {
            aseInspectionId = 0;
            if (string.IsNullOrWhiteSpace(inspectionId))
                return false;

            var trimmed = inspectionId.Trim();
            var suffixIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
            var baseInspectionId = suffixIndex > 0
                ? trimmed[..suffixIndex]
                : trimmed;

            return int.TryParse(baseInspectionId, out aseInspectionId);
        }

        private static async Task<Core.Entities.ASE.AseScan?> ResolveLatestAseScanForContainerAsync(
            ApplicationDbContext db,
            string containerNumber,
            bool requireImage,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return null;

            var exactQuery = db.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber == containerNumber);

            if (requireImage)
                exactQuery = exactQuery.Where(s => s.ScanImage != null && s.ScanImage.Length > 0);

            var exact = await exactQuery
                .OrderByDescending(s => s.ScanTime)
                .FirstOrDefaultAsync(cancellationToken);

            if (exact != null)
                return exact;

            var normalizedContainer = ContainerNumberListMatcher.Normalize(containerNumber);
            if (string.IsNullOrWhiteSpace(normalizedContainer))
                return null;

            var tokenizedQuery = db.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber != null
                    && s.ContainerNumber.ToUpper().Contains(normalizedContainer));

            if (requireImage)
                tokenizedQuery = tokenizedQuery.Where(s => s.ScanImage != null && s.ScanImage.Length > 0);

            var candidates = await tokenizedQuery
                .OrderByDescending(s => s.ScanTime)
                .Take(25)
                .ToListAsync(cancellationToken);

            return candidates.FirstOrDefault(scan =>
                ContainerNumberListMatcher.ContainsContainer(scan.ContainerNumber, normalizedContainer));
        }

        private static bool IsCompositeContainerPairIdentifier(string? identifier)
        {
            var parts = (identifier ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts.Count >= 2 && parts.All(IsLikelyIsoContainerNumber);
        }

        private static bool IsLikelyIsoContainerNumber(string value)
        {
            var token = value.Trim();
            return token.Length == 11
                && token.Take(4).All(char.IsLetter)
                && token.Skip(4).All(char.IsDigit);
        }

        /// <summary>
        /// Idempotent: MERGE ensures AnalysisGroup exists. Returns true if inserted, false if already existed.
        /// Eliminates race condition vs check-then-act + catch.
        /// </summary>
        private static async Task<bool> MergeAnalysisGroupIfNotExistsAsync(
            ApplicationDbContext db,
            string groupIdentifier,
            string? scannerType,
            string initialStatus,
            int priority,
            CancellationToken ct)
        {
            var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
            const string sql = @"
INSERT INTO AnalysisGroups (Id, GroupIdentifier, NormalizedGroupIdentifier, GroupType, ScannerType, Status, Priority, CreatedAtUtc)
VALUES (gen_random_uuid(), {0}, {1}, 'BL', {2}, {3}, {4}, now() AT TIME ZONE 'UTC')
ON CONFLICT (GroupIdentifier, COALESCE(ScannerType, ''))
DO UPDATE SET UpdatedAtUtc = now() AT TIME ZONE 'UTC'
RETURNING (xmax = 0)::int;";

            var results = await db.Database
                .SqlQueryRaw<int>(sql, groupIdentifier, normalized, scannerType ?? (object)DBNull.Value, initialStatus, priority)
                .ToListAsync(ct);
            var wasInserted = results.FirstOrDefault();
            return wasInserted == 1;
        }

        /// <summary>
        /// 1.16.0 — Record-anchored intake pass.
        ///
        /// Queries RecordCompletenessStatus rows that are ready-ish (Status=Ready, or
        /// PartiallyReady with at least WaveMinBatchSize containers ready) and have no
        /// linked AnalysisGroup yet, then creates AnalysisGroup + AnalysisRecord rows
        /// directly from them. This is the "record-first" flow: instead of waiting for
        /// the container-grouping pass to discover a quorum, we build the analyst work
        /// unit from the canonical ICUMS declaration as soon as enough containers are
        /// ready.
        ///
        /// Deliberately narrow:
        /// - Only touches records that DON'T already have a linked group
        ///   (prevents double-creation)
        /// - Only creates groups for Ready / PartiallyReady records
        ///   (leaves Pending for the reconciliation worker to promote first)
        /// - Uses declarationnumber as the group identifier so downstream paths
        ///   (match-corrections, analyst assignments) find the group via the
        ///   same key they've always used
        /// - Populates RecordCompletenessStatusId on the new group so 1.15.0's
        ///   decision rollup dual-write picks it up
        /// - Best-effort: individual record failures don't block the pass
        /// </summary>
        private async Task RunRecordAnchoredIntakeAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            CancellationToken ct)
        {
            var minBatchSize = Math.Max(1, settings.WaveMinBatchSize);

            // Pull eligible records: Ready (all containers ready) OR PartiallyReady with
            // at least minBatchSize ready. Skip records that already have a group linked.
            var eligibleRecords = await db.RecordCompletenessStatuses
                .AsNoTracking()
                .Where(r => (r.Status == "Ready" || (r.Status == "PartiallyReady" && r.ContainersReady >= minBatchSize))
                         && r.ArchivedAtUtc == null
                         && !db.AnalysisGroups.Any(g => g.RecordCompletenessStatusId == r.Id))
                .OrderByDescending(r => r.LastNewContainerAtUtc ?? r.CreatedAtUtc)
                .Take(100)
                .ToListAsync(ct);

            if (eligibleRecords.Count == 0)
            {
                _logger.LogDebug("[INTAKE-RECORD] No eligible records to convert");
                return;
            }

            _logger.LogInformation("[INTAKE-RECORD] Found {Count} eligible records to promote to AnalysisGroups", eligibleRecords.Count);

            int groupsCreated = 0;
            foreach (var record in eligibleRecords)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    // Load the ready children for this record
                    var readyChildren = await db.RecordExpectedContainers
                        .AsNoTracking()
                        .Where(e => e.RecordId == record.Id && e.Status == "Ready")
                        .ToListAsync(ct);

                    if (readyChildren.Count == 0) continue;

                    // Sanity: skip if a group already exists for this declaration+scanner
                    // (the legacy path may have created one before our linkage fired)
                    var scannerType = record.ScannerType ?? readyChildren.First().ScannerType;
                    // 2026-05-05 operator-reported "Scanner column blank" fix: when both
                    // record-side sources are NULL (post-April-2026 record-anchored intake
                    // regression), fall back to CCS where the scanner data actually lives.
                    // Single-scanner case covers 75/75 of NULL-scannertype AGs in production.
                    if (string.IsNullOrEmpty(scannerType))
                    {
                        var firstContainer = readyChildren.First().ContainerNumber;
                        if (!string.IsNullOrEmpty(firstContainer))
                        {
                            scannerType = await db.ContainerCompletenessStatuses
                                .AsNoTracking()
                                .Where(c => c.ContainerNumber == firstContainer && c.ScannerType != null)
                                .OrderByDescending(c => c.UpdatedAt)
                                .Select(c => c.ScannerType)
                                .FirstOrDefaultAsync(ct);
                        }
                    }
                    if (!IsAssignmentIntakeEnabled(scannerType))
                    {
                        _logger.LogInformation(
                            "[INTAKE-RECORD] Skipping record {RecordId} ({Decl}) because scanner workflow assignment intake is disabled for {ScannerType}",
                            record.Id, record.DeclarationNumber, scannerType ?? "Unknown");
                        continue;
                    }
                    var existing = await db.AnalysisGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(g => g.GroupIdentifier == record.DeclarationNumber
                                               && g.ScannerType == scannerType, ct);
                    if (existing != null)
                    {
                        // Already created by the legacy path — just link the FK if missing
                        if (!existing.RecordCompletenessStatusId.HasValue)
                        {
                            existing.RecordCompletenessStatusId = record.Id;
                            existing.UpdatedAtUtc = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);
                        }

                        var existingContainerNumbersToStamp = readyChildren
                            .Select(c => c.ContainerNumber)
                            .ToArray();
                        var existingStamped = await db.Database.ExecuteSqlInterpolatedAsync($@"
                            UPDATE containercompletenessstatuses
                            SET groupidentifier = {record.DeclarationNumber},
                                updatedat = now() AT TIME ZONE 'UTC'
                            WHERE containernumber = ANY({existingContainerNumbersToStamp})
                              AND (groupidentifier IS NULL OR btrim(groupidentifier) = '')", ct);
                        if (existingStamped > 0)
                        {
                            _logger.LogInformation(
                                "[INTAKE-RECORD] Back-stamped groupidentifier={Decl} on {Count} completeness row(s) for existing record group {RecordId}",
                                record.DeclarationNumber, existingStamped, record.Id);
                        }
                        continue;
                    }

                    var existingCmrGroup = await FindExistingCmrCompositeGroupForRealRecordAsync(
                        db,
                        record,
                        readyChildren,
                        scannerType,
                        ct);
                    if (existingCmrGroup != null)
                    {
                        _logger.LogInformation(
                            "[INTAKE-RECORD] Skipping duplicate real declaration group for record {RecordId} ({Decl}); existing CMR group {GroupId} ({GroupIdentifier}) already covers the same container/rotation/BL",
                            record.Id,
                            record.DeclarationNumber,
                            existingCmrGroup.Id,
                            existingCmrGroup.GroupIdentifier);
                        continue;
                    }

                    // Create the group
                    var strategy = db.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(ct);
                        try
                        {
                            // Sprint 5G2 / B1 lock-the-door: redundant Status="Ready" removed (default value).
                            var group = new AnalysisGroup
                            {
                                GroupIdentifier = record.DeclarationNumber,
                                NormalizedGroupIdentifier = record.DeclarationNumber,
                                GroupType = GetRecordBackedGroupType(record.ClearanceType),
                                ScannerType = scannerType,
                                Priority = 0,
                                RecordCompletenessStatusId = record.Id,
                            };
                            db.AnalysisGroups.Add(group);
                            await db.SaveChangesAsync(ct);

                            foreach (var child in readyChildren)
                            {
                                db.AnalysisRecords.Add(new AnalysisRecord
                                {
                                    GroupId = group.Id,
                                    ContainerNumber = child.ContainerNumber,
                                    ScannerType = child.ScannerType ?? scannerType,
                                    Status = "Ready",
                                    CreatedAtUtc = DateTime.UtcNow
                                });
                            }
                            await db.SaveChangesAsync(ct);

                            // Back-stamp containercompletenessstatuses.groupidentifier for the
                            // containers we just attached. Completeness rows can be created
                            // before any group exists (CMR-clearance arrival, pre-BOE scan)
                            // with groupidentifier=NULL. Without this stamp,
                            // ReadyGroupsCacheService.GetReadyGroupsForRoleAsync joins on
                            // groupidentifier and sees zero containers, so its Analyst-role
                            // filter excludes the group and analyst auto-assignment never
                            // fires — group sits in Ready forever. Only stamps NULL rows so
                            // we never overwrite a competing/consolidated group's identifier.
                            var containerNumbersToStamp = readyChildren
                                .Select(c => c.ContainerNumber)
                                .ToArray();
                            var stamped = await db.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE containercompletenessstatuses
                                SET groupidentifier = {record.DeclarationNumber},
                                    updatedat = now() AT TIME ZONE 'UTC'
                                WHERE containernumber = ANY({containerNumbersToStamp})
                                  AND (groupidentifier IS NULL OR btrim(groupidentifier) = '')", ct);
                            if (stamped > 0)
                            {
                                _logger.LogInformation(
                                    "[INTAKE-RECORD] Back-stamped groupidentifier={Decl} on {Count} completeness row(s) for record {RecordId}",
                                    record.DeclarationNumber, stamped, record.Id);
                            }

                            // ALWAYS create wave tracking (AnalysisParentGroup) — even for "Ready"
                            // records. This ensures that if new containers arrive later (BOE amendment,
                            // late scan), the wave mechanism can create Wave N for them.
                            var parentGroup = new AnalysisParentGroup
                            {
                                GroupIdentifier = record.DeclarationNumber,
                                ScannerType = scannerType,
                                TotalExpectedContainers = record.TotalExpectedContainers,
                                Status = "Active"
                            };
                            db.AnalysisParentGroups.Add(parentGroup);
                            await db.SaveChangesAsync(ct);

                            group.ParentGroupId = parentGroup.Id;
                            group.WaveNumber = 1;
                            group.WaveCreatedReason = record.Status == "PartiallyReady" ? "InitialBatch" : "FullBatch";
                            await db.SaveChangesAsync(ct);

                            // For PartiallyReady records, track outstanding containers for wave processing
                            if (record.Status == "PartiallyReady")
                            {
                                var outstandingChildren = await db.RecordExpectedContainers
                                    .AsNoTracking()
                                    .Where(e => e.RecordId == record.Id
                                             && (e.Status == "AwaitingScan" || e.Status == "Pending"))
                                    .ToListAsync(ct);

                                foreach (var outstanding in outstandingChildren)
                                {
                                    db.WavePendingContainers.Add(new WavePendingContainer
                                    {
                                        ParentGroupId = parentGroup.Id,
                                        ContainerNumber = outstanding.ContainerNumber,
                                        ScannerType = outstanding.ScannerType ?? scannerType,
                                        Status = "Pending"
                                    });
                                }
                                if (outstandingChildren.Count > 0)
                                    await db.SaveChangesAsync(ct);
                            }

                            await tx.CommitAsync(ct);
                            groupsCreated++;

                            _logger.LogInformation(
                                "[INTAKE-RECORD] Created AnalysisGroup from record {RecordId} ({Decl}): {Ready} ready, {Total} total, status={RecordStatus}",
                                record.Id, record.DeclarationNumber, readyChildren.Count, record.TotalExpectedContainers, record.Status);
                        }
                        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                        {
                            await tx.RollbackAsync(ct);
                            db.ChangeTracker.Clear();
                            _logger.LogDebug(ex, "[INTAKE-RECORD] Duplicate group for record {RecordId} ({Decl}) - already created", record.Id, record.DeclarationNumber);
                        }
                        catch
                        {
                            await tx.RollbackAsync(ct);
                            db.ChangeTracker.Clear();
                            throw;
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[INTAKE-RECORD] Failed to create group for record {RecordId} ({Decl}) - continuing", record.Id, record.DeclarationNumber);
                }
            }

            if (groupsCreated > 0)
            {
                _logger.LogInformation("[INTAKE-RECORD] Created {Count} AnalysisGroups from records this cycle", groupsCreated);
            }
        }

        private async Task<AnalysisGroup?> FindExistingCmrCompositeGroupForRealRecordAsync(
            ApplicationDbContext db,
            RecordCompletenessStatus record,
            IReadOnlyList<RecordExpectedContainer> readyChildren,
            string? scannerType,
            CancellationToken ct)
        {
            if (!_configuration.GetValue<bool>("CmrCompositeProgression:Enabled", false)
                || string.Equals(record.ClearanceType, "CMR", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(record.RotationNumber)
                || string.IsNullOrWhiteSpace(record.BlNumber))
            {
                return null;
            }

            foreach (var child in readyChildren)
            {
                if (!CmrCompositeKeyHelper.TryCreate(
                        record.RotationNumber,
                        child.ContainerNumber,
                        record.BlNumber,
                        out var cmrKey))
                {
                    continue;
                }

                var cmrRecordId = await db.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => r.ClearanceType == "CMR"
                             && (r.DeclarationNumber == cmrKey.OperationalKey
                              || (r.RotationNumber == cmrKey.RotationNumber
                                  && r.BlNumber == cmrKey.BlNumber
                                  && r.ExpectedContainers.Any(e => e.ContainerNumber == cmrKey.ContainerNumber))))
                    .Select(r => (int?)r.Id)
                    .FirstOrDefaultAsync(ct);

                if (!cmrRecordId.HasValue)
                    continue;

                var query = db.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.RecordCompletenessStatusId == cmrRecordId.Value
                             || g.GroupIdentifier == cmrKey.OperationalKey
                             || g.NormalizedGroupIdentifier == cmrKey.OperationalKey);

                if (!string.IsNullOrWhiteSpace(scannerType))
                {
                    query = query.Where(g => g.ScannerType == null || g.ScannerType == scannerType);
                }

                var existingCmrGroup = await query
                    .OrderByDescending(g => g.UpdatedAtUtc ?? g.CreatedAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (existingCmrGroup != null)
                    return existingCmrGroup;
            }

            return null;
        }

        private static string GetRecordBackedGroupType(string? clearanceType)
            => string.Equals(clearanceType, "CMR", StringComparison.OrdinalIgnoreCase) ? "CMR" : "BL";

        private bool IsAssignmentIntakeEnabled(string? scannerType)
        {
            var normalized = NormalizeScannerType(scannerType);
            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            if (normalized == "EAGLEA25")
            {
                return _configuration.GetValue<bool>("ScannerWorkflow:EagleA25:AssignmentIntakeEnabled", false);
            }

            var disabled = _configuration
                .GetSection("ScannerWorkflow:DisabledAssignmentIntakeScannerTypes")
                .Get<string[]>() ?? Array.Empty<string>();

            return !disabled.Any(s => NormalizeScannerType(s) == normalized);
        }

        private static string NormalizeScannerType(string? scannerType)
            => string.IsNullOrWhiteSpace(scannerType)
                ? string.Empty
                : scannerType.Trim()
                    .Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .ToUpperInvariant();

        /// <summary>
        /// 1.15.0 — Look up the matching RecordCompletenessStatus for an AnalysisGroup
        /// and populate the new RecordCompletenessStatusId FK if not already set.
        ///
        /// The match is: group.GroupIdentifier OR group.NormalizedGroupIdentifier
        /// = record.DeclarationNumber. This catches all non-consolidated cases and
        /// most consolidated ones. Pattern A groups (keyed by container number) and
        /// date-split groups (identifier ends in _YYYYMMDD_YYYYMMDD) will not match
        /// and stay unlinked — that's expected, they're legacy grain and will be
        /// cleaned up in a later release.
        ///
        /// Idempotent: if the FK is already set, this is a no-op.
        /// </summary>
        private async Task TryLinkGroupToRecordAsync(
            ApplicationDbContext db,
            Guid groupId,
            string groupIdentifier,
            string? normalizedGroupIdentifier,
            CancellationToken ct)
        {
            try
            {
                var group = await db.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.Id == groupId, ct);

                if (group == null || group.RecordCompletenessStatusId.HasValue)
                    return; // already linked or gone

                var candidateId = normalizedGroupIdentifier ?? groupIdentifier;
                if (string.IsNullOrWhiteSpace(candidateId))
                    return;

                var recordId = await db.RecordCompletenessStatuses
                    .Where(r => r.DeclarationNumber == candidateId
                             || r.DeclarationNumber == groupIdentifier)
                    .Select(r => (int?)r.Id)
                    .FirstOrDefaultAsync(ct);

                if (recordId.HasValue)
                {
                    group.RecordCompletenessStatusId = recordId;
                    group.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    _logger.LogDebug(
                        "[INTAKE] Linked AnalysisGroup {GroupId} ({GroupIdentifier}) to RecordCompletenessStatus {RecordId}",
                        groupId, groupIdentifier, recordId.Value);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — linking is best-effort, the group still works without the FK
                _logger.LogWarning(ex, "[INTAKE] Failed to link group {GroupId} to record", groupId);
            }
        }

        // Intake helper methods (from IntakeWorker)
        private async Task<CompletenessLookup> BuildCompletenessLookupAsync(
            ApplicationDbContext db,
            IReadOnlyCollection<ContainerCompletenessStatus> statuses,
            CancellationToken ct)
        {
            var lookupStartTime = DateTime.UtcNow;
            var comparer = StringComparer.OrdinalIgnoreCase;

            if (statuses.Count == 0)
            {
                return CompletenessLookup.Empty(comparer);
            }

            // ✅ DIAGNOSTIC: Log lookup build start
            _logger.LogInformation("[INTAKE] Building completeness lookup for {Count} statuses...", statuses.Count);

            var fsCandidates = statuses
                .Where(s => string.Equals(s.ScannerType, "FS6000", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(s.ContainerNumber) &&
                    (!s.HasScannerData || !s.HasImageData))
                .Select(s => s.ContainerNumber!)
                .Distinct(comparer)
                .ToList();

            // ✅ DIAGNOSTIC: Log FS6000 candidates
            _logger.LogInformation("[INTAKE] Found {Count} FS6000 candidates for lookup", fsCandidates.Count);

            var aseCandidates = statuses
                .Where(s => string.Equals(s.ScannerType, "ASE", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(s.ContainerNumber) &&
                    (!s.HasScannerData || !s.HasImageData))
                .Select(s => s.ContainerNumber!)
                .Distinct(comparer)
                .ToList();

            var icumCandidates = statuses
                .Where(s => !s.HasICUMSData && !string.IsNullOrWhiteSpace(s.ContainerNumber))
                .Select(s => s.ContainerNumber!)
                .Distinct(comparer)
                .ToList();

            var fsLookup = new Dictionary<string, ScannerDataStatus>(comparer);
            if (fsCandidates.Count > 0)
            {
                try
                {
                    const int batchSize = 100;
                    var allFsData = new List<(string ContainerNumber, bool HasImage)>();
                    var totalFsBatches = (int)Math.Ceiling((double)fsCandidates.Count / batchSize);

                    _logger.LogInformation("[INTAKE] Processing {Count} FS6000 candidates in {Batches} batches...", fsCandidates.Count, totalFsBatches);

                    for (int i = 0; i < fsCandidates.Count; i += batchSize)
                    {
                        var batchNum = (i / batchSize) + 1;
                        var batch = fsCandidates.Skip(i).Take(batchSize).ToList();
                        var parameters = batch.Cast<object>().ToArray();
                        var placeholders = string.Join(",", batch.Select((_, idx) => $"{{{idx}}}"));
                        // Hoist out of $"..." to silence EF1002 — placeholders is a static
                        // template ({0},{1},...), not user input; parameters carries the values.
                        var fsSql = "SELECT * FROM FS6000Scans WHERE ContainerNumber IN (" + placeholders + ")";
                        var fsScans = await db.FS6000Scans
                            .FromSqlRaw(fsSql, parameters)
                            .AsNoTracking()
                            .ToListAsync(ct);
                        allFsData.AddRange(fsScans.Select(s => (s.ContainerNumber ?? string.Empty, s.HasImage)));

                        if (batchNum % 10 == 0 || batchNum == totalFsBatches)
                        {
                            _logger.LogInformation("[INTAKE] FS6000 lookup progress: {Batch}/{Total} batches processed", batchNum, totalFsBatches);
                        }
                    }

                    fsLookup = allFsData
                        .Where(x => !string.IsNullOrWhiteSpace(x.ContainerNumber))
                        .GroupBy(x => x.ContainerNumber, comparer)
                        .ToDictionary(
                            g => g.Key,
                            g => new ScannerDataStatus(true, g.Any(x => x.HasImage)),
                            comparer);
                }
                catch (PostgresException ex) when (ex.SqlState == "00000")
                {
                    _logger.LogWarning(ex, "[INTAKE] FS6000Scans table not available");
                }
            }

            var aseLookup = new Dictionary<string, ScannerDataStatus>(comparer);
            if (aseCandidates.Count > 0)
            {
                try
                {
                    const int batchSize = 100;
                    var allAseData = new List<(string ContainerNumber, bool HasImage)>();
                    var totalAseBatches = (int)Math.Ceiling((double)aseCandidates.Count / batchSize);

                    _logger.LogInformation("[INTAKE] Processing {Count} ASE candidates in {Batches} batches...", aseCandidates.Count, totalAseBatches);

                    for (int i = 0; i < aseCandidates.Count; i += batchSize)
                    {
                        var batchNum = (i / batchSize) + 1;
                        var batch = aseCandidates.Skip(i).Take(batchSize).ToList();
                        var parameters = batch.Cast<object>().ToArray();
                        var placeholders = string.Join(",", batch.Select((_, idx) => $"{{{idx}}}"));
                        var aseSql = "SELECT * FROM AseScans WHERE ContainerNumber IN (" + placeholders + ")";
                        var aseScans = await db.AseScans
                            .FromSqlRaw(aseSql, parameters)
                            .AsNoTracking()
                            .ToListAsync(ct);

                        var exactLookup = aseScans
                            .Where(s => !string.IsNullOrWhiteSpace(s.ContainerNumber))
                            .GroupBy(s => s.ContainerNumber!, comparer)
                            .ToDictionary(
                                g => g.Key,
                                g => g.Any(s => !string.IsNullOrEmpty(s.ImageDisplayName)),
                                comparer);

                        foreach (var container in batch)
                        {
                            if (exactLookup.TryGetValue(container, out var hasExactImage))
                            {
                                allAseData.Add((container, hasExactImage));
                                continue;
                            }

                            var tokenizedScan = await ResolveLatestAseScanForContainerAsync(
                                db,
                                container,
                                requireImage: false,
                                ct);

                            if (tokenizedScan != null)
                            {
                                allAseData.Add((container, !string.IsNullOrEmpty(tokenizedScan.ImageDisplayName)));
                            }
                        }

                        if (batchNum % 10 == 0 || batchNum == totalAseBatches)
                        {
                            _logger.LogInformation("[INTAKE] ASE lookup progress: {Batch}/{Total} batches processed", batchNum, totalAseBatches);
                        }
                    }

                    aseLookup = allAseData
                        .Where(x => !string.IsNullOrWhiteSpace(x.ContainerNumber))
                        .GroupBy(x => x.ContainerNumber, comparer)
                        .ToDictionary(
                            g => g.Key,
                            g => new ScannerDataStatus(true, g.Any(x => x.HasImage)),
                            comparer);
                }
                catch (PostgresException ex) when (ex.SqlState == "00000")
                {
                    _logger.LogWarning(ex, "[INTAKE] AseScans table not available");
                }
            }

            var icumSet = new HashSet<string>(
                statuses
                    .Where(s => s.HasICUMSData && !string.IsNullOrWhiteSpace(s.ContainerNumber))
                    .Select(s => s.ContainerNumber!),
                comparer);

            if (icumCandidates.Count > 0)
            {
                // ✅ FIX: ApplicationDbContext.IcumContainerData points at the empty
                // nickscan_production.icumcontainerdata table. The real ICUMS BOE data
                // lives in nickscan_downloads.boedocuments via IcumDownloadsDbContext,
                // which is what the mapper and UI also use. Redirect the lookup there
                // so completeness results are non-empty.
                try
                {
                    using var icumScope = _scopeFactory.CreateScope();
                    var icumDownloadsDb = icumScope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                    const int batchSize = 100;
                    var allIcumData = new List<string>();
                    var totalIcumBatches = (int)Math.Ceiling((double)icumCandidates.Count / batchSize);

                    _logger.LogInformation("[INTAKE] Processing {Count} ICUMS candidates in {Batches} batches...", icumCandidates.Count, totalIcumBatches);

                    for (int i = 0; i < icumCandidates.Count; i += batchSize)
                    {
                        var batchNum = (i / batchSize) + 1;
                        var batch = icumCandidates.Skip(i).Take(batchSize).ToList();

                        var matches = await icumDownloadsDb.BOEDocuments
                            .AsNoTracking()
                            .Where(b => batch.Contains(b.ContainerNumber))
                            .Select(b => b.ContainerNumber)
                            .ToListAsync(ct);

                        allIcumData.AddRange(matches
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(comparer));

                        if (batchNum % 10 == 0 || batchNum == totalIcumBatches)
                        {
                            _logger.LogInformation("[INTAKE] ICUMS lookup progress: {Batch}/{Total} batches processed", batchNum, totalIcumBatches);
                        }
                    }

                    foreach (var container in allIcumData.Where(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        icumSet.Add(container!);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[INTAKE] ICUMS BOE document lookup failed; continuing without ICUMS completeness data.");
                    return new CompletenessLookup(fsLookup, aseLookup, icumSet, hasIcumLookup: false);
                }
            }

            var lookupElapsed = DateTime.UtcNow - lookupStartTime;
            _logger.LogInformation("[INTAKE] Completeness lookup built in {Elapsed:F1}s (FS: {FsCount}, ASE: {AseCount}, ICUMS: {IcumCount})",
                lookupElapsed.TotalSeconds, fsLookup.Count, aseLookup.Count, icumSet.Count);

            return new CompletenessLookup(fsLookup, aseLookup, icumSet, hasIcumLookup: true);
        }

        private async Task<bool> EnsureCompletenessFlagsAsync(
            ApplicationDbContext db,
            ContainerCompletenessStatus status,
            CompletenessLookup lookup,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(status.ContainerNumber))
            {
                return false;
            }

            var containerNumber = status.ContainerNumber;
            var hasScannerData = status.HasScannerData;
            var hasImageData = status.HasImageData;
            var hasIcumData = status.HasICUMSData;

            switch (status.ScannerType?.ToUpperInvariant())
            {
                case "FS6000":
                    if (lookup.Fs6000.TryGetValue(containerNumber, out var fsStatus))
                    {
                        if (!hasScannerData && fsStatus.HasScannerData)
                            hasScannerData = true;
                        if (!hasImageData && fsStatus.HasImageData)
                            hasImageData = true;
                    }
                    break;
                case "ASE":
                    if (lookup.Ase.TryGetValue(containerNumber, out var aseStatus))
                    {
                        if (!hasScannerData && aseStatus.HasScannerData)
                            hasScannerData = true;
                        if (!hasImageData && aseStatus.HasImageData)
                            hasImageData = true;
                    }
                    break;
            }

            if (!hasIcumData && lookup.HasIcumLookup && lookup.Icum.Contains(containerNumber))
            {
                // Do not flip ICUMS flag for Export-Pending records (export data pipeline not yet available)
                if (status.Status != "Export-Pending")
                    hasIcumData = true;
            }

            if (hasScannerData && hasImageData && hasIcumData)
            {
                if (!status.HasScannerData || !status.HasImageData || !status.HasICUMSData)
                {
                    status.HasScannerData = hasScannerData;
                    status.HasImageData = hasImageData;
                    status.HasICUMSData = hasIcumData;
                    status.OverallCompleteness = 100;
                    status.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                return true;
            }

            return false;
        }

        private readonly record struct ScannerDataStatus(bool HasScannerData, bool HasImageData);

        private sealed class CompletenessLookup
        {
            public static CompletenessLookup Empty(IEqualityComparer<string> comparer) =>
                new CompletenessLookup(
                    new Dictionary<string, ScannerDataStatus>(comparer),
                    new Dictionary<string, ScannerDataStatus>(comparer),
                    new HashSet<string>(comparer),
                    false);

            public CompletenessLookup(
                IReadOnlyDictionary<string, ScannerDataStatus> fs6000,
                IReadOnlyDictionary<string, ScannerDataStatus> ase,
                ISet<string> icum,
                bool hasIcumLookup = true)
            {
                Fs6000 = fs6000;
                Ase = ase;
                Icum = icum;
                HasIcumLookup = hasIcumLookup;
            }

            public IReadOnlyDictionary<string, ScannerDataStatus> Fs6000 { get; }
            public IReadOnlyDictionary<string, ScannerDataStatus> Ase { get; }
            public ISet<string> Icum { get; }
            public bool HasIcumLookup { get; }
        }

        #endregion

        #region Split Detection

        /// <summary>
        /// Proactively detects AnalysisRecords for containers that are part of 2-container
        /// scans (OriginalScanRecord.DerivedRecordCount == 2) and populates the split fields.
        /// Also checks the splitter service for existing completed jobs and links them.
        /// Runs every 60 seconds; processes up to 50 records per cycle.
        /// </summary>
        private async Task RunSplitDetectionAsync(ApplicationDbContext db, IHttpClientFactory httpClientFactory, CancellationToken ct)
        {
            // Two queries:
            // 1. Records not yet checked (IsMultiContainerScan=false, SplitStatus=null) — detect new multi-container scans
            // 2. Records already flagged but not linked (IsMultiContainerScan=true, SplitStatus=null, SplitJobId=null) — link to splitter jobs
            // ApplicationDbContext defaults to NoTracking — without AsTracking(), property
            // mutations below would be silently dropped by SaveChangesAsync. Pre-2026-05-06
            // this caused only ~10 records to ever get IsMultiContainerScan set across the
            // entire history (those 10 were probably the ones tracked via a different path).
            var uncheckedRecords = await db.AnalysisRecords
                .AsTracking()
                .Where(r => !r.IsMultiContainerScan && r.SplitStatus == null)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(50)
                .ToListAsync(ct);

            var unlinkdRecords = await db.AnalysisRecords
                .AsTracking()
                .Where(r => r.IsMultiContainerScan && r.SplitStatus == null && r.SplitJobId == null)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(50)
                .ToListAsync(ct);

            // Merge both lists (unlinked records already have position set, just need job linking)
            var allRecords = uncheckedRecords.Concat(unlinkdRecords).ToList();
            if (allRecords.Count == 0) return;

            // For the unlinked records, we skip the OriginalScanRecord lookup (already flagged)
            var uncheckedRecordIds = new HashSet<int>(uncheckedRecords.Select(r => r.Id));

            // Batch lookup: get all OriginalScanRecords with DerivedRecordCount == 2
            var containerNumbers = uncheckedRecords.Select(r => r.ContainerNumber).ToList();

            var multiContainerScans = await db.Set<NickScanCentralImagingPortal.Core.Entities.OriginalScanRecord>()
                .Where(o => o.DerivedRecordCount == 2)
                .ToListAsync(ct);

            // Build a lookup: containerNumber → OriginalScanRecord
            var containerToScan = new Dictionary<string, NickScanCentralImagingPortal.Core.Entities.OriginalScanRecord>();
            foreach (var scan in multiContainerScans)
            {
                var parts = scan.OriginalContainerNumbers
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .ToList();
                foreach (var cn in parts)
                {
                    if (!containerToScan.ContainsKey(cn))
                        containerToScan[cn] = scan;
                }
            }

            var updated = 0;
            var client = httpClientFactory.CreateClient("RawImageEngine");

            foreach (var record in allRecords)
            {
                // For unchecked records, verify they're actually multi-container
                if (uncheckedRecordIds.Contains(record.Id))
                {
                    if (!containerToScan.TryGetValue(record.ContainerNumber, out var scan))
                    {
                        // Not a multi-container scan — mark checked so the record leaves
                        // the unchecked queue. Without this, single-container records
                        // recur in `Take(50)` forever and starve the older multi-container
                        // backlog (queue grew to 3000+ before this fix).
                        record.SplitStatus = "NotApplicable";
                        updated++;
                        continue;
                    }

                    var containers = scan.OriginalContainerNumbers
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.Trim())
                        .ToList();

                    if (containers.Count < 2)
                    {
                        record.SplitStatus = "NotApplicable";
                        updated++;
                        continue;
                    }

                    var position = containers[0] == record.ContainerNumber ? "left" : "right";
                    record.IsMultiContainerScan = true;
                    record.SplitPosition = position;
                }
                // For already-flagged records, position is already set — just need job linking

                // Resolve the OriginalScanRecord for this container (needed for splitter job lookup)
                if (!containerToScan.TryGetValue(record.ContainerNumber, out var scanForSearch))
                {
                    // Defensive: shouldn't happen for IsMultiContainerScan=true records, but
                    // mark as Skipped rather than continue so it leaves the unlinked queue.
                    record.SplitStatus = "Skipped";
                    updated++;
                    continue;
                }

                // Check if a split job exists in the splitter for these containers
                try
                {
                    var normalized = scanForSearch.OriginalContainerNumbers.Replace(" ", "");
                    var searchResponse = await client.GetAsync(
                        $"/api/split/search?container_numbers={Uri.EscapeDataString(normalized)}", ct);

                    if (searchResponse.IsSuccessStatusCode)
                    {
                        var json = await searchResponse.Content.ReadAsStringAsync(ct);
                        var jobData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

                        SplitJobStatus? splitJobStatus = null;

                        if (jobData.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            splitJobStatus = TryReadSplitJobStatus(jobData);
                        }
                        else if (jobData.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var j in jobData.EnumerateArray())
                            {
                                var jCn = j.TryGetProperty("container_numbers", out var cnp) ? cnp.GetString()?.Replace(" ", "") : "";
                                if (jCn == normalized)
                                {
                                    splitJobStatus = TryReadSplitJobStatus(j);
                                    break;
                                }
                            }
                        }

                        if (splitJobStatus != null)
                        {
                            record.SplitJobId = splitJobStatus.JobId;
                            var explicitNonChoice = SplitAnalysisStatus.TryMapNonChoiceOutcome(new[]
                            {
                                splitJobStatus.SplitOutcome,
                                splitJobStatus.Status,
                                splitJobStatus.BestStrategy,
                                splitJobStatus.ErrorMessage
                            });
                            var shouldFetchCandidates = explicitNonChoice == null
                                && SplitAnalysisStatus.IsCompletedJobStatus(splitJobStatus.Status);

                            var candidateOutcomes = Array.Empty<string?>();
                            var fetchedCandidateCount = 0;

                            if (shouldFetchCandidates)
                            {
                                // Fetch top 2 results
                                try
                                {
                                    var resultsResp = await client.GetAsync($"/api/split/{splitJobStatus.JobId}/results", ct);
                                    if (resultsResp.IsSuccessStatusCode)
                                    {
                                        var resultsJson = await resultsResp.Content.ReadAsStringAsync(ct);
                                        var results = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(resultsJson);
                                        if (results.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            var sorted = results.EnumerateArray()
                                                .Select(r => new
                                                {
                                                    Id = r.TryGetProperty("id", out var id) ? id.GetString() : null,
                                                    Conf = r.TryGetProperty("confidence", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number ? c.GetDouble() : 0.0,
                                                    Outcome = TryGetOutcome(r)
                                                })
                                                .Where(r => r.Id != null)
                                                .OrderByDescending(r => r.Conf)
                                                .Take(2)
                                                .ToList();

                                            fetchedCandidateCount = sorted.Count;
                                            candidateOutcomes = sorted.Select(r => r.Outcome).ToArray();
                                            if (sorted.Count >= 1)
                                                record.SplitOptionA_ResultId = Guid.Parse(sorted[0].Id!);
                                            if (sorted.Count >= 2)
                                                record.SplitOptionB_ResultId = Guid.Parse(sorted[1].Id!);
                                        }
                                    }
                                }
                                catch { /* Non-fatal: candidates can be populated later by the API */ }
                            }

                            record.SplitStatus = SplitAnalysisStatus.ResolveForAnalysisRecord(
                                splitJobStatus,
                                fetchedCandidateCount,
                                shouldFetchCandidates,
                                candidateOutcomes);

                            if (!string.Equals(record.SplitStatus, SplitAnalysisStatus.Ready, StringComparison.OrdinalIgnoreCase))
                            {
                                record.SplitOptionA_ResultId = null;
                                record.SplitOptionB_ResultId = null;
                            }
                        }
                        else
                        {
                            // No split job exists — submit one now (durable wiring; previously
                            // this branch silently set Pending and waited for a manual
                            // submit_backlog.py run that stopped happening 2026-04-10).
                            // FS6000 only for now; ASE images need a JWT-authed fetch via
                            // /api/ContainerDetails/image/ase/full which the orchestrator
                            // doesn't have credentials for.
                            record.SplitStatus = "Pending";
                            if (string.Equals(scanForSearch.ScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var pair = scanForSearch.OriginalContainerNumbers
                                        .Replace(";", ",")
                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                                        .Where(s => s.Length >= 4 && !s.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
                                        .Take(2)
                                        .ToArray();
                                    if (pair.Length >= 2)
                                    {
                                        var imgBytes = await db.Set<NickScanCentralImagingPortal.Core.Entities.FS6000.FS6000Image>()
                                            .AsNoTracking()
                                            .Where(i => i.ImageType == "Main" && i.ImageData != null
                                                && db.Set<NickScanCentralImagingPortal.Core.Entities.FS6000.FS6000Scan>()
                                                    .Any(s => s.Id == i.ScanId &&
                                                              (s.ContainerNumber == pair[0] || s.ContainerNumber == pair[1])))
                                            .OrderBy(i => i.CreatedAt)
                                            .Select(i => i.ImageData)
                                            .FirstOrDefaultAsync(ct);

                                        if (imgBytes != null && imgBytes.Length > 0)
                                        {
                                            using var content = new System.Net.Http.MultipartFormDataContent();
                                            var img = new System.Net.Http.ByteArrayContent(imgBytes);
                                            img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                                            content.Add(img, "file", "scan.jpg");
                                            content.Add(new System.Net.Http.StringContent($"{pair[0]},{pair[1]}"), "container_numbers");
                                            content.Add(new System.Net.Http.StringContent("FS6000"), "scanner_type");

                                            var submitResp = await client.PostAsync("/api/split/upload", content, ct);
                                            if (submitResp.IsSuccessStatusCode)
                                            {
                                                var submitJson = await submitResp.Content.ReadAsStringAsync(ct);
                                                using var submitDoc = System.Text.Json.JsonDocument.Parse(submitJson);
                                                if (submitDoc.RootElement.TryGetProperty("id", out var newIdProp))
                                                {
                                                    var newId = newIdProp.GetString();
                                                    if (Guid.TryParse(newId, out var parsedId))
                                                    {
                                                        record.SplitJobId = parsedId;
                                                        _logger.LogInformation(
                                                            "[SPLIT-DETECTION] Enqueued split job {JobId} for {C1}+{C2}",
                                                            parsedId, pair[0], pair[1]);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                _logger.LogWarning(
                                                    "[SPLIT-DETECTION] /api/split/upload returned {Status} for {C1}+{C2}",
                                                    submitResp.StatusCode, pair[0], pair[1]);
                                            }
                                        }
                                    }
                                }
                                catch (Exception submitEx)
                                {
                                    _logger.LogDebug(submitEx,
                                        "[SPLIT-DETECTION] Auto-submit failed for {Container} (will retry next cycle)",
                                        record.ContainerNumber);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Splitter not reachable — just mark the multi-container flag
                        record.SplitStatus = "Pending";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SPLIT-DETECTION] Splitter check failed for {Container}", record.ContainerNumber);
                    record.SplitStatus = "Pending";
                }

                updated++;
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("[SPLIT-DETECTION] Populated split fields for {Count} AnalysisRecords", updated);
            }
        }

        private static SplitJobStatus? TryReadSplitJobStatus(JsonElement job)
        {
            var jobId = TryGetGuid(job, "id") ?? TryGetGuid(job, "job_id");
            if (!jobId.HasValue)
                return null;

            return new SplitJobStatus(
                jobId.Value,
                TryGetString(job, "status") ?? "unknown",
                TryGetString(job, "best_strategy"),
                TryGetDouble(job, "best_confidence") ?? TryGetDouble(job, "best_score"),
                TryGetInt(job, "split_x"),
                TryGetInt(job, "result_count") ?? 0,
                TryGetOutcome(job),
                TryGetString(job, "error_message"));
        }

        private static Guid? TryGetGuid(JsonElement element, string propertyName)
        {
            var value = TryGetString(element, propertyName);
            return Guid.TryParse(value, out var guid) ? guid : null;
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;

            return prop.TryGetInt32(out var value) ? value : null;
        }

        private static double? TryGetDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;

            return prop.TryGetDouble(out var value) ? value : null;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
                return null;

            return prop.GetString();
        }

        private static string? TryGetOutcome(JsonElement element)
        {
            foreach (var propertyName in new[]
            {
                "split_outcome",
                "splitOutcome",
                "outcome",
                "visual_outcome",
                "visualOutcome",
                "classification",
                "resolution"
            })
            {
                var value = TryGetString(element, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            foreach (var propertyName in new[]
            {
                "not_applicable",
                "notApplicable",
                "visual_single",
                "visualSingle",
                "single_container",
                "singleContainer",
                "uncertain"
            })
            {
                if (element.TryGetProperty(propertyName, out var prop)
                    && prop.ValueKind == JsonValueKind.True)
                {
                    return propertyName;
                }
            }

            if (element.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                return TryGetOutcome(metadata);

            return null;
        }

        #endregion
    }
}

