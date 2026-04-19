using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Logging;
using NickScanCentralImagingPortal.Services.Monitoring;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Orchestrates all Container Completeness workflows in a single coordinated service
    /// Consolidates: ContainerCompletenessService, ContainerDataMapperService, 
    ///               ManualBOESelectivityService, PostICUMSValidationService
    /// Benefits: Reduced memory (~150MB → ~40MB), single DbContext scope, shared state
    /// </summary>
    public class ContainerCompletenessOrchestratorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ContainerCompletenessOrchestratorService> _logger;
        private readonly AdaptivePollingHelper _adaptivePolling;
        private readonly ServiceHealthMonitor _healthMonitor;
        private const string SERVICE_ID = "CONTAINER-COMPLETENESS-ORCHESTRATOR";

        // Track last execution times for each workflow
        private DateTime _lastCompletenessCheckRun = DateTime.MinValue;
        private DateTime _lastDataMappingRun = DateTime.MinValue;
        private DateTime _lastBOESelectivityRun = DateTime.MinValue;
        private DateTime _lastPostICUMSValidationRun = DateTime.MinValue;

        // Query result cache within cycle (Phase 3.3 optimization)
        private Dictionary<string, object>? _cycleCache;

        // Cycle counter for logging
        private int cycleCount = 0;

        public ContainerCompletenessOrchestratorService(
            IServiceProvider serviceProvider,
            ILogger<ContainerCompletenessOrchestratorService> logger,
            AdaptivePollingHelper? adaptivePolling = null,
            ServiceHealthMonitor? healthMonitor = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _adaptivePolling = adaptivePolling ?? new AdaptivePollingHelper(logger);
            // Create health monitor with proper logger type
            if (healthMonitor == null)
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
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
            // Random startup delay to prevent all services from starting simultaneously
            var randomDelay = Random.Shared.Next(1000, 5000);
            await Task.Delay(randomDelay, stoppingToken);

            _logger.LogInformation("[{ServiceId}] Container Completeness Orchestrator Service started", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    cycleCount++;

                    // Clear cycle cache at start of each cycle (Phase 3.3 optimization)
                    _cycleCache = new Dictionary<string, object>();

                    var now = DateTime.UtcNow;

                    // Phase 3.1: Adaptive Polling - Count work items to determine intervals
                    var completenessWorkCount = await GetCompletenessWorkCountAsync(stoppingToken);
                    var dataMappingWorkCount = await GetDataMappingWorkCountAsync(stoppingToken);
                    var boeSelectivityWorkCount = await GetBOESelectivityWorkCountAsync(stoppingToken);
                    var postICUMSValidationWorkCount = await GetPostICUMSValidationWorkCountAsync(stoppingToken);

                    // Execute workflows with adaptive intervals
                    // Completeness Check: adaptive based on work count
                    var shouldExecute = _adaptivePolling.ShouldExecute(_lastCompletenessCheckRun, completenessWorkCount, now);
                    var timeSinceLastRun = _lastCompletenessCheckRun == DateTime.MinValue ? TimeSpan.Zero : (now - _lastCompletenessCheckRun);

                    // ✅ CRITICAL FIX: Force execution if there's work and it's been more than 2 minutes since last run
                    // This ensures pending items are processed even if adaptive polling says to wait longer
                    if (completenessWorkCount > 0 && timeSinceLastRun.TotalMinutes >= 2)
                    {
                        shouldExecute = true;
                        _logger.LogInformation("[COMPLETENESS-POLLING] ⚠️ Forcing execution: {Count} pending items, last run was {Minutes:F1} minutes ago",
                            completenessWorkCount, timeSinceLastRun.TotalMinutes);
                    }

                    // ✅ ENHANCED LOGGING: Log polling decision for debugging
                    if (completenessWorkCount > 0 || cycleCount % 10 == 1 || cycleCount <= 3)
                    {
                        _logger.LogInformation("[COMPLETENESS-POLLING] Work count: {Count}, Last run: {LastRun} ({SecondsAgo:F1}s ago), Should execute: {ShouldExecute}, Interval: {Interval}s",
                            completenessWorkCount,
                            _lastCompletenessCheckRun == DateTime.MinValue ? "Never" : _lastCompletenessCheckRun.ToString("HH:mm:ss"),
                            timeSinceLastRun.TotalSeconds,
                            shouldExecute,
                            _adaptivePolling.CalculateInterval(completenessWorkCount).TotalSeconds);
                    }

                    if (shouldExecute)
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "CompletenessCheck",
                            async () => await RunCompletenessCheckWorkflowAsync(stoppingToken),
                            () => completenessWorkCount);
                        _lastCompletenessCheckRun = now;
                    }

                    // Data Mapping: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastDataMappingRun, dataMappingWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "DataMapping",
                            async () => await RunDataMappingWorkflowAsync(stoppingToken),
                            () => dataMappingWorkCount);
                        _lastDataMappingRun = now;
                    }

                    // BOE Selectivity: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastBOESelectivityRun, boeSelectivityWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "BOESelectivity",
                            async () => await RunBOESelectivityWorkflowAsync(stoppingToken),
                            () => boeSelectivityWorkCount);
                        _lastBOESelectivityRun = now;
                    }

                    // Post-ICUMS Validation: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastPostICUMSValidationRun, postICUMSValidationWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "PostICUMSValidation",
                            async () => await RunPostICUMSValidationWorkflowAsync(stoppingToken),
                            () => postICUMSValidationWorkCount);
                        _lastPostICUMSValidationRun = now;
                    }

                    // Update service metrics
                    var memoryUsage = _healthMonitor.GetCurrentMemoryUsage();
                    _healthMonitor.RecordServiceMetrics(SERVICE_ID, memoryUsage);

                    // Calculate adaptive delay based on minimum work count
                    var minWorkCount = Math.Min(Math.Min(completenessWorkCount, dataMappingWorkCount),
                        Math.Min(boeSelectivityWorkCount, postICUMSValidationWorkCount));
                    var delay = _adaptivePolling.CalculateInterval(minWorkCount);
                    // Ensure minimum delay of 1 minute for container completeness
                    if (delay.TotalSeconds < 60)
                        delay = TimeSpan.FromMinutes(1);
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
                        _logger.LogWarning(ex, "[{ServiceId}] Database connectivity issue. Retrying in 30 seconds...", SERVICE_ID);
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex, "[{ServiceId}] Error in orchestration cycle. Retrying in 5 seconds...", SERVICE_ID);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }

            _logger.LogInformation("[{ServiceId}] Container Completeness Orchestrator Service stopped", SERVICE_ID);
        }

        #region Completeness Check Workflow

        private async Task RunCompletenessCheckWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
                var completenessService = scope.ServiceProvider.GetRequiredService<IContainerCompletenessService>();

                // Call the existing completeness check method
                await completenessService.CheckContainerCompletenessAsync(stoppingToken);

                _logger.LogDebug("[COMPLETENESS-CHECK] Completeness check workflow completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COMPLETENESS-CHECK] Error in completeness check workflow");
            }
        }

        #endregion

        #region Data Mapping Workflow

        private async Task RunDataMappingWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dataMapperService = scope.ServiceProvider.GetRequiredService<IContainerDataMapperService>();

                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumsContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                if (!await dbContext.Database.CanConnectAsync(stoppingToken) ||
                    !await icumsContext.Database.CanConnectAsync(stoppingToken))
                {
                    _logger.LogDebug("[DATA-MAPPING] Databases not accessible, skipping mapping cycle");
                    return;
                }

                await dataMapperService.ProcessPendingMappingsAsync(stoppingToken);
                _logger.LogDebug("[DATA-MAPPING] Data mapping workflow completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATA-MAPPING] Error in data mapping workflow");
            }
        }

        #endregion

        #region BOE Selectivity Workflow

        private async Task RunBOESelectivityWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var boeService = scope.ServiceProvider.GetRequiredService<IManualBOESelectivityService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumsContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                if (!await dbContext.Database.CanConnectAsync(stoppingToken) ||
                    !await icumsContext.Database.CanConnectAsync(stoppingToken))
                {
                    _logger.LogDebug("[BOE-SELECTIVITY] Databases not accessible, skipping BOE cycle");
                    return;
                }

                var processed = await boeService.ProcessPendingBOERequestsAsync();
                var autoQueued = await boeService.AutoQueueMissingICUMSContainersAsync();

                if (processed > 0 || autoQueued > 0)
                {
                    _logger.LogInformation("[BOE-SELECTIVITY] Processed {Processed} manual requests, auto-queued {AutoQueued} containers",
                        processed, autoQueued);
                }
                else
                {
                    _logger.LogDebug("[BOE-SELECTIVITY] BOE selectivity workflow completed (no work)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BOE-SELECTIVITY] Error in BOE selectivity workflow");
            }
        }

        #endregion

        #region Post-ICUMS Validation Workflow

        private async Task RunPostICUMSValidationWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumsContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
                var validationService = scope.ServiceProvider.GetRequiredService<MultiContainerValidationService>();

                // ✅ FIX: Query OriginalScanRecords (scanner-agnostic) instead of ContainerCompletenessStatuses
                // Every scanner (FS6000, ASE, Heimann) writes OriginalScanRecords on ingest.
                // DerivedRecordCount >= 2 means multiple containers in one scan.
                //
                // 1.20.0 — filter by LastValidatedAtUtc so the worker advances instead of
                // looping on the same oldest 100 rows forever. Rows are eligible if:
                //   - LastValidatedAtUtc IS NULL (never processed), or
                //   - LastValidatedAtUtc < now() - 1 hour (stale, retry for late BOE data)
                // 1-hour cooldown lets the worker catch up on the 318-row backlog quickly
                // while still re-visiting "pending BOE" rows when new ICUMS data arrives.
                var staleCutoff = DateTime.UtcNow.AddHours(-1);
                var multiContainerOriginals = await dbContext.OriginalScanRecords
                    .Where(r => r.DerivedRecordCount >= 2
                                && (r.LastValidatedAtUtc == null || r.LastValidatedAtUtc < staleCutoff))
                    .OrderBy(r => r.LastValidatedAtUtc ?? DateTime.MinValue)
                    .ThenBy(r => r.IngestedAt)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                if (!multiContainerOriginals.Any())
                {
                    _logger.LogDebug("[POST-ICUMS-VALIDATION] No multi-container scans pending validation");
                    return;
                }

                _logger.LogInformation("[POST-ICUMS-VALIDATION] Found {Count} multi-container original scans to validate",
                    multiContainerOriginals.Count);

                var validatedCount = 0;
                var crossRecordCount = 0;
                var sameRecordCount = 0;
                var skippedCount = 0;
                var pendingBOECount = 0;

                foreach (var original in multiContainerOriginals)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        // Normalize container numbers from the original record
                        var containerNumbers = original.OriginalContainerNumbers
                            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(c => c.Trim())
                            .Where(c => c.Length >= 4)
                            .Distinct()
                            .ToList();

                        if (containerNumbers.Count < 2)
                        {
                            _logger.LogDebug("[POST-ICUMS-VALIDATION] Skipping original {Id} — only {Count} valid container(s)", original.Id, containerNumbers.Count);
                            continue;
                        }

                        var c1 = containerNumbers[0];
                        var c2 = containerNumbers[1];

                        // Check if already validated (order-insensitive dedup)
                        var alreadyCrossRecord = await dbContext.CrossRecordScans
                            .AnyAsync(cr => (cr.Container1 == c1 && cr.Container2 == c2) ||
                                           (cr.Container1 == c2 && cr.Container2 == c1), stoppingToken);

                        // 1.19.0 — Removed the `settledCount >= containerNumbers.Count` check
                        // that used to short-circuit this loop. That check assumed "if all
                        // containers are individually Complete, we've already handled them".
                        // That assumption broke after 1.18.0's ASE comma-split fix, which
                        // made each container go through completeness independently and
                        // reach Complete status quickly. As a result, every multi-container
                        // scan was being skipped before cross-record validation could run,
                        // and CrossRecordScan entries stopped being created. 243 of 291
                        // multi-container ASE scans (83%) had no CRS entry at release time.
                        //
                        // Correct behaviour: skip ONLY if we already have a CRS row for this
                        // pair (idempotency dedup). Individual container completeness status
                        // is irrelevant — we want to run the cross-record check whenever BOE
                        // data is available for both sides, regardless of their CCS state.
                        if (alreadyCrossRecord)
                        {
                            skippedCount++;
                            continue;
                        }

                        // Build combined string for the validation service
                        var combinedContainerString = string.Join(",", containerNumbers);

                        var validationResult = await validationService.ValidateMultiContainerScanAsync(
                            combinedContainerString,
                            dbContext,
                            icumsContext);

                        if (validationResult.PendingBOEData)
                        {
                            // Update individual completeness statuses
                            var pendingStatuses = await dbContext.ContainerCompletenessStatuses
                                .Where(c => containerNumbers.Contains(c.ContainerNumber) && c.Status != "Complete-CrossRecord")
                                .ToListAsync(stoppingToken);
                            foreach (var s in pendingStatuses)
                            {
                                s.Status = "Pending-Validation";
                                s.UpdatedAt = DateTime.UtcNow;
                            }
                            pendingBOECount++;
                            _logger.LogDebug("[POST-ICUMS-VALIDATION] BOE data pending for {Containers}, will retry on next cycle",
                                original.OriginalContainerNumbers);
                            continue;
                        }

                        if (!validationResult.IsSameRecord && validationResult.RequiresSpecialTracking)
                        {
                            // Get scanner record ID via OriginalScanRecordId FK
                            var scannerRecordId = await GetScannerRecordIdFromOriginal(
                                original.Id, original.ScannerType, dbContext, stoppingToken);

                            if (scannerRecordId != Guid.Empty)
                            {
                                await validationService.CreateCrossRecordTrackingAsync(
                                    combinedContainerString,
                                    scannerRecordId,
                                    original.ScannerType,
                                    original.ScanTime,
                                    validationResult,
                                    dbContext,
                                    icumsContext);

                                // Update individual completeness statuses
                                var crossStatuses = await dbContext.ContainerCompletenessStatuses
                                    .Where(c => containerNumbers.Contains(c.ContainerNumber))
                                    .ToListAsync(stoppingToken);
                                foreach (var s in crossStatuses)
                                {
                                    s.Status = "Complete-CrossRecord";
                                    s.HasICUMSData = true;
                                    s.UpdatedAt = DateTime.UtcNow;
                                }
                                crossRecordCount++;
                            }
                        }
                        else
                        {
                            var sameStatuses = await dbContext.ContainerCompletenessStatuses
                                .Where(c => containerNumbers.Contains(c.ContainerNumber) && c.Status != "Complete")
                                .ToListAsync(stoppingToken);
                            foreach (var s in sameStatuses)
                            {
                                s.Status = "Complete";
                                s.HasICUMSData = true;
                                s.UpdatedAt = DateTime.UtcNow;
                            }
                            sameRecordCount++;
                        }

                        validatedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[POST-ICUMS-VALIDATION] Failed to validate original scan {Id}: {Containers}",
                            original.Id, original.OriginalContainerNumbers);
                    }
                    finally
                    {
                        // 1.20.0 — stamp the cursor even on exceptions / skips so the
                        // worker advances past this row on the next tick.
                        var tracked = await dbContext.OriginalScanRecords
                            .AsTracking()
                            .FirstOrDefaultAsync(r => r.Id == original.Id, stoppingToken);
                        if (tracked != null)
                        {
                            tracked.LastValidatedAtUtc = DateTime.UtcNow;
                        }
                    }
                }

                if (validatedCount > 0 || skippedCount > 0 || pendingBOECount > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("[POST-ICUMS-VALIDATION] Processed {Total} originals: {Validated} validated ({Cross} cross-record, {Same} same-record), {Skipped} already settled, {Pending} pending BOE data",
                        multiContainerOriginals.Count, validatedCount, crossRecordCount, sameRecordCount, skippedCount, pendingBOECount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[POST-ICUMS-VALIDATION] Error in post-ICUMS validation workflow");
            }
        }

        /// <summary>
        /// Finds the scanner-specific record ID using the OriginalScanRecordId FK.
        /// Scanner-agnostic: works for FS6000, ASE, and any future scanner type.
        /// </summary>
        private async Task<Guid> GetScannerRecordIdFromOriginal(
            int originalScanRecordId,
            string scannerType,
            ApplicationDbContext dbContext,
            CancellationToken stoppingToken)
        {
            try
            {
                switch (scannerType.ToUpper())
                {
                    case "FS6000":
                        var fs6000Scan = await dbContext.FS6000Scans
                            .FirstOrDefaultAsync(s => s.OriginalScanRecordId == originalScanRecordId, stoppingToken);
                        return fs6000Scan?.Id ?? Guid.Empty;

                    case "ASE":
                        var aseScan = await dbContext.AseScans
                            .FirstOrDefaultAsync(s => s.OriginalScanRecordId == originalScanRecordId, stoppingToken);
                        return aseScan?.Id ?? Guid.Empty;

                    default:
                        _logger.LogWarning("[POST-ICUMS-VALIDATION] Unknown scanner type: {Type}", scannerType);
                        return Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[POST-ICUMS-VALIDATION] Error getting scanner record ID for original {Id}",
                    originalScanRecordId);
                return Guid.Empty;
            }
        }

        #endregion

        #region Work Count Helpers (Phase 3.1: Adaptive Polling)

        /// <summary>
        /// Count pending work items for completeness check workflow
        /// </summary>
        private async Task<int> GetCompletenessWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("completenessWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // ✅ CRITICAL FIX: Count PENDING queue items, not completeness statuses
                // The service processes items from ContainerScanQueue, so work count should reflect queue items
                // This ensures the service continues processing when new items are added to the queue
                var count = await dbContext.ContainerScanQueues
                    .Where(q => q.Status == ContainerScanQueueStatus.Pending && q.RetryCount < q.MaxRetries)
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["completenessWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for data mapping workflow
        /// </summary>
        private async Task<int> GetDataMappingWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("dataMappingWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Count containers that need data mapping
                // (containers with all data but may need linking)
                var count = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.HasScannerData && c.HasImageData && c.HasICUMSData &&
                        (c.BOEDocumentId == null || string.IsNullOrEmpty(c.GroupIdentifier)))
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["dataMappingWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for BOE selectivity workflow
        /// </summary>
        private async Task<int> GetBOESelectivityWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("boeSelectivityWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Count containers that need BOE selectivity processing
                // (containers with scanner data but missing ICUMS data)
                var count = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.HasScannerData && !c.HasICUMSData && c.Status == "Missing")
                    .CountAsync(stoppingToken);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["boeSelectivityWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for post-ICUMS validation workflow
        /// </summary>
        private async Task<int> GetPostICUMSValidationWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("postICUMSValidationWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Count OriginalScanRecords with DerivedRecordCount >= 2 minus already-resolved cross-records
                var totalMulti = await dbContext.OriginalScanRecords
                    .Where(r => r.DerivedRecordCount >= 2)
                    .CountAsync(stoppingToken);
                var resolvedCross = await dbContext.CrossRecordScans
                    .CountAsync(stoppingToken);
                var count = Math.Max(0, totalMulti - resolvedCross);

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["postICUMSValidationWorkCount"] = count;
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
            if (ex is NpgsqlException or PostgresException)
            {
                var msg = ex.Message;
                return msg.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("broken pipe", StringComparison.OrdinalIgnoreCase);
            }

            var errorMessage = ex.Message;
            return errorMessage.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        #endregion
    }
}

