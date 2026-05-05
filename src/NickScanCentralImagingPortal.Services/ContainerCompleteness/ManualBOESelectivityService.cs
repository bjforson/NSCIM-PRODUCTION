using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.IcumApi;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Background service that processes manual BOE selectivity requests to ICUMS
    /// </summary>
    public class ManualBOESelectivityService : BackgroundService, IManualBOESelectivityService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ManualBOESelectivityService> _logger;
        private readonly int _maxConcurrentRequests = 5;
        private const string SERVICE_ID = "[MANUAL-BOE-SELECTIVITY]";
        private int _consecutiveDatabaseUnavailableCount = 0;
        private const int MAX_WARNING_LOGS = 3; // Only log warnings for first 3 attempts
        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. ExecuteAsync
        // owns these for the per-iteration summary line.
        private int _cycleCount = 0;

        public ManualBOESelectivityService(
            IServiceProvider serviceProvider,
            ILogger<ManualBOESelectivityService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} ManualBOESelectivityService started - processing at configured interval from settings", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(ManualBOESelectivityService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                int _failedThisCycle = 0;
                bool _skippedCycle = false;
                try
                {
                    // Check if we can access databases before processing
                    if (await CanAccessDatabasesAsync())
                    {
                        // Reset counter when database is accessible
                        if (_consecutiveDatabaseUnavailableCount > 0)
                        {
                            _logger.LogInformation("{ServiceId} Database connectivity restored after {Count} attempts",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                            _consecutiveDatabaseUnavailableCount = 0;
                        }

                        // ✅ EXISTING: Process manual BOE requests from users
                        await ProcessPendingBOERequestsAsync();

                        // 🆕 NEW: Auto-discovery - Queue containers with missing ICUMS data
                        await AutoQueueMissingICUMSContainersAsync();

                        _logger.LogDebug("{ServiceId} BOE request processing cycle completed (manual + auto-discovery)", SERVICE_ID);
                    }
                    else
                    {
                        _consecutiveDatabaseUnavailableCount++;
                        _skippedCycle = true;
                        // Only log warnings for first few attempts, then use Debug to reduce noise
                        if (_consecutiveDatabaseUnavailableCount <= MAX_WARNING_LOGS)
                        {
                            _logger.LogWarning("{ServiceId} Databases not accessible, skipping BOE request processing cycle (attempt {Count}/{Max})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount, MAX_WARNING_LOGS);
                        }
                        else
                        {
                            _logger.LogDebug("{ServiceId} Databases not accessible, skipping BOE request processing cycle (attempt {Count})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Check if it's a database connectivity error
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _consecutiveDatabaseUnavailableCount++;
                        if (_consecutiveDatabaseUnavailableCount <= MAX_WARNING_LOGS)
                        {
                            _logger.LogWarning(ex, "{ServiceId} Database connectivity error during BOE processing (attempt {Count}/{Max})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount, MAX_WARNING_LOGS);
                        }
                        else
                        {
                            _logger.LogDebug(ex, "{ServiceId} Database connectivity error during BOE processing (attempt {Count})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                        }
                        _failedThisCycle = 1;
                    }
                    else
                    {
                        // Actual error (not connectivity) - always log
                        _failedThisCycle = 1;
                        _logger.LogError(ex, "{ServiceId} Error during manual BOE request processing cycle", SERVICE_ID);
                    }
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // Per-request granularity is owned by the called methods; this
                // line confirms liveness + cycle outcome (skipped due to DB
                // unavailable / failed / clean run).
                _logger.LogIterationSummary(
                    "MANUAL-BOE-SELECTIVITY",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _failedThisCycle == 0 && !_skippedCycle ? 1 : 0,
                    itemsSkipped: _skippedCycle ? 1 : 0,
                    itemsFailed: _failedThisCycle);

                // Wait for configured interval (read from database settings)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    var processingIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "ManualBOESelectivityService.ProcessIntervalMinutes", 2);
                    _logger.LogDebug("⏰ Next manual BOE processing in {Interval} minutes (from settings)", processingIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(processingIntervalMinutes), stoppingToken);
                }
            }

            _logger.LogInformation("{ServiceId} ManualBOESelectivityService stopped", SERVICE_ID);
        }

        private async Task<bool> CanAccessDatabasesAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                var appDbAccessible = await dbContext.Database.CanConnectAsync();
                var icumDbAccessible = await icumDbContext.Database.CanConnectAsync();

                return appDbAccessible && icumDbAccessible;
            }
            catch (Exception ex)
            {
                // Don't log here - let the caller handle logging based on consecutive count
                return false;
            }
        }

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

            var message = ex.Message.ToLowerInvariant();
            return message.Contains("could not open a connection") ||
                   message.Contains("connection refused") ||
                   message.Contains("timeout") ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        public async Task<int> ProcessPendingBOERequestsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumApiService = scope.ServiceProvider.GetRequiredService<IIcumApiService>();

            _logger.LogInformation("{ServiceId} Starting manual BOE request processing", SERVICE_ID);

            var processedCount = 0;

            try
            {
                // Get pending requests ready for processing
                var pendingRequests = await GetPendingBOERequestsAsync(_maxConcurrentRequests);
                _logger.LogInformation("{ServiceId} Found {Count} pending BOE requests to process", SERVICE_ID, pendingRequests.Count);

                if (pendingRequests.Count == 0)
                {
                    _logger.LogDebug("No pending BOE requests to process");
                    return 0;
                }

                // Process requests in parallel (limited by _maxConcurrentRequests)
                var semaphore = new SemaphoreSlim(_maxConcurrentRequests, _maxConcurrentRequests);
                var tasks = pendingRequests.Select(async request =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ProcessBOERequestAsync(request);
                        Interlocked.Increment(ref processedCount);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                _logger.LogInformation("Manual BOE request processing completed - processed {Count} requests", processedCount);
                return processedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual BOE request processing");
                throw;
            }
        }

        public async Task<ManualBOERequest> CreateManualBOERequestAsync(string containerNumber, string requestedBy = "System")
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                // Check if a request already exists for this container
                var existingRequest = await dbContext.ManualBOERequests
                    .FirstOrDefaultAsync(r => r.ContainerNumber == containerNumber && r.Status != "Completed");

                if (existingRequest != null)
                {
                    _logger.LogDebug("BOE request already exists for container {ContainerNumber}", containerNumber);
                    return existingRequest;
                }

                // Create new request
                var request = new ManualBOERequest
                {
                    ContainerNumber = containerNumber,
                    RequestDate = DateTime.UtcNow,
                    Status = "Pending",
                    RequestedBy = requestedBy,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                dbContext.ManualBOERequests.Add(request);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("Created manual BOE request for container {ContainerNumber}", containerNumber);
                return request;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating manual BOE request for container {ContainerNumber}", containerNumber);
                throw;
            }
        }

        public async Task<ManualBOERequest> ProcessBOERequestAsync(ManualBOERequest request)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();

            try
            {
                _logger.LogDebug("{ServiceId} Processing BOE request for container {ContainerNumber} (queueing for download)",
                    SERVICE_ID, request.ContainerNumber);

                // ✅ FIX: Use proper queueService that checks BOTH queue status AND BOE data existence
                var queueService = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueService>();

                // Check if we should queue this container (checks BOE data + queue status)
                var shouldQueue = await queueService.ShouldEnqueueAsync(request.ContainerNumber);

                if (shouldQueue)
                {
                    // Add to centralized download queue
                    var queued = await queueService.EnqueueContainerAsync(
                        request.ContainerNumber,
                        priority: 1, // High priority
                        requestSource: "ManualBOE-AutoDiscovery",
                        requestedBy: request.RequestedBy ?? "ManualBOESelectivityService");

                    if (queued)
                    {
                        _logger.LogInformation("{ServiceId} ✅ Queued container {ContainerNumber} for ICUMS download",
                            SERVICE_ID, request.ContainerNumber);

                        // Update request status to "Queued"
                        await UpdateBOERequestStatusAsync(
                            request.Id,
                            "Queued",
                            "Added to ICUMSDownloadQueue for processing",
                            null);
                    }
                    else
                    {
                        _logger.LogDebug("{ServiceId} Container {ContainerNumber} was not queued (likely already has data or in queue)",
                            SERVICE_ID, request.ContainerNumber);

                        await UpdateBOERequestStatusAsync(
                            request.Id,
                            "Completed",
                            "Container already has BOE data",
                            null);
                    }
                }
                else
                {
                    var hasData = await icumsDownloadsDbContext.BOEDocuments
                        .AnyAsync(b => b.ContainerNumber == request.ContainerNumber);

                    if (hasData)
                    {
                        _logger.LogDebug("{ServiceId} Container {ContainerNumber} already has BOE data - marking completed",
                            SERVICE_ID, request.ContainerNumber);
                        await UpdateBOERequestStatusAsync(request.Id, "Completed", "Container already has BOE data", null);
                    }
                    else
                    {
                        _logger.LogDebug("{ServiceId} Container {ContainerNumber} already in download queue",
                            SERVICE_ID, request.ContainerNumber);
                        await UpdateBOERequestStatusAsync(request.Id, "Queued", "Container already in download queue", null);
                    }
                }

                // ❌ REMOVED: No longer calling ICUMS API directly
                // var icumsResponse = await icumApiService.FetchContainerDataAsync(request.ContainerNumber);

                // ✅ ICUMSDownloadBackgroundService will handle the actual API call

                var updatedRequest = await dbContext.ManualBOERequests.FindAsync(request.Id);
                return updatedRequest ?? request;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error queueing BOE request for container {ContainerNumber}",
                    SERVICE_ID, request.ContainerNumber);

                // Update status to Failed with error message
                await UpdateBOERequestStatusAsync(request.Id, "Failed", ex.Message, null);

                var failedRequest = await dbContext.ManualBOERequests.FindAsync(request.Id);
                return failedRequest ?? request;
            }
        }

        public async Task<List<ManualBOERequest>> GetPendingBOERequestsAsync(int limit = 50)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var requests = await dbContext.ManualBOERequests
                    .Where(r => r.Status == "Pending" &&
                               (r.NextRetryAt == null || r.NextRetryAt <= DateTime.UtcNow))
                    .OrderBy(r => r.RequestDate)
                    .Take(limit)
                    .ToListAsync();

                return requests;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending BOE requests");
                throw;
            }
        }

        public async Task<ManualBOERequest> UpdateBOERequestStatusAsync(
            int requestId,
            string status,
            string? errorMessage = null,
            string? icuMSResponseId = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var request = await dbContext.ManualBOERequests.FindAsync(requestId);
                if (request == null)
                {
                    throw new ArgumentException($"BOE request with ID {requestId} not found");
                }

                request.Status = status;
                request.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    request.ErrorMessage = errorMessage;
                    request.RetryCount++;

                    // Set next retry time based on retry count (exponential backoff)
                    var retryDelay = TimeSpan.FromMinutes(Math.Pow(2, request.RetryCount));
                    request.NextRetryAt = DateTime.UtcNow.Add(retryDelay);
                }

                if (!string.IsNullOrEmpty(icuMSResponseId))
                {
                    request.ICUMSResponseId = icuMSResponseId;
                }

                if (status == "Completed")
                {
                    request.CompletedAt = DateTime.UtcNow;
                    request.NextRetryAt = null;
                }

                dbContext.ManualBOERequests.Update(request);
                await dbContext.SaveChangesAsync();

                return request;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating BOE request status for ID {RequestId}", requestId);
                throw;
            }
        }

        public async Task<List<ManualBOERequest>> GetFailedBOERequestsForRetryAsync(int maxRetryCount = 3)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var requests = await dbContext.ManualBOERequests
                    .Where(r => r.Status == "Failed" &&
                               r.RetryCount < maxRetryCount &&
                               r.NextRetryAt <= DateTime.UtcNow)
                    .OrderBy(r => r.NextRetryAt)
                    .Take(20) // Limit retry batch size
                    .ToListAsync();

                return requests;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving failed BOE requests for retry");
                throw;
            }
        }

        public async Task<BOERequestStatistics> GetBOERequestStatisticsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var stats = new BOERequestStatistics
                {
                    TotalRequests = await dbContext.ManualBOERequests.CountAsync(),
                    PendingRequests = await dbContext.ManualBOERequests.CountAsync(r => r.Status == "Pending"),
                    ProcessingRequests = await dbContext.ManualBOERequests.CountAsync(r => r.Status == "Processing"),
                    CompletedRequests = await dbContext.ManualBOERequests.CountAsync(r => r.Status == "Completed"),
                    FailedRequests = await dbContext.ManualBOERequests.CountAsync(r => r.Status == "Failed"),
                    RetryRequests = await dbContext.ManualBOERequests.CountAsync(r => r.Status == "Failed" && r.RetryCount > 0)
                };

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving BOE request statistics");
                throw;
            }
        }

        /// <summary>
        /// 🆕 AUTO-DISCOVERY: Automatically queue containers with missing ICUMS data for download
        /// This bridges the gap between ContainerCompletenessService (detection) and ICUMSDownloadBackgroundService (download)
        /// </summary>
        public async Task<int> AutoQueueMissingICUMSContainersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();
            var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            var queuedCount = 0;
            var fixedCount = 0;

            try
            {
                _logger.LogInformation("{ServiceId} 🔍 Auto-Discovery: Checking for containers with missing ICUMS data", SERVICE_ID);

                var missingContainers = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.Status == "Missing" &&
                               !c.HasICUMSData &&
                               c.RetryCount < 3 &&
                               !string.IsNullOrEmpty(c.ContainerNumber))
                    .OrderBy(c => c.CreatedAt)
                    .Take(200)
                    .ToListAsync();

                if (missingContainers.Count == 0)
                {
                    _logger.LogDebug("{ServiceId} Auto-Discovery: No containers with missing ICUMS data found", SERVICE_ID);
                    return 0;
                }

                _logger.LogInformation("{ServiceId} Auto-Discovery: Found {Count} containers with missing ICUMS data",
                    SERVICE_ID, missingContainers.Count);

                var queueService = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueService>();

                foreach (var container in missingContainers)
                {
                    try
                    {
                        var containerNumbers = container.ContainerNumber
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c) && c != "Unknown")
                            .Distinct()
                            .ToList();

                        if (containerNumbers.Count > 1)
                        {
                            _logger.LogInformation("{ServiceId} Auto-Discovery: Found multi-container record with {Count} containers: {Containers}",
                                SERVICE_ID, containerNumbers.Count, string.Join(", ", containerNumbers));
                        }

                        // Check if BOE data has already arrived (e.g. via batch download)
                        bool allHaveBoeData = true;
                        foreach (var containerNum in containerNumbers)
                        {
                            var hasBoe = await icumsDownloadsDbContext.BOEDocuments
                                .AnyAsync(b => b.ContainerNumber == containerNum);
                            if (!hasBoe) { allHaveBoeData = false; break; }
                        }

                        if (allHaveBoeData)
                        {
                            // BOE data already exists — fix the completeness record directly
                            container.HasICUMSData = true;
                            container.ICUMSDataCompleteness = 100;
                            container.ICUMSDataDate = DateTime.UtcNow;
                            var hasImages = container.HasImageData;
                            container.OverallCompleteness = (100 + 100 + (hasImages ? 100 : 0)) / 3;
                            var allComplete = container.HasScannerData && container.HasImageData;
                            container.Status = allComplete ? "Complete" : "Missing";
                            if (allComplete && container.WorkflowStage == "Pending")
                                container.WorkflowStage = "ImageAnalysis";
                            container.UpdatedAt = DateTime.UtcNow;
                            container.LastCheckedAt = DateTime.UtcNow;
                            dbContext.ContainerCompletenessStatuses.Update(container);
                            fixedCount++;
                            _logger.LogInformation("{ServiceId} 🔧 Auto-Discovery: BOE data already exists for {Container} — fixed completeness (Status: {Status}, Overall: {Overall}%)",
                                SERVICE_ID, container.ContainerNumber, container.Status, container.OverallCompleteness);
                            continue;
                        }

                        foreach (var containerNum in containerNumbers)
                        {
                            var shouldQueue = await queueService.ShouldEnqueueAsync(containerNum);

                            if (!shouldQueue)
                            {
                                _logger.LogDebug("{ServiceId} Auto-Discovery: {ContainerNumber} already has data or in queue, skipping",
                                    SERVICE_ID, containerNum);
                                continue;
                            }

                            var queued = await queueService.EnqueueContainerAsync(
                                containerNum,
                                priority: 1,
                                requestSource: "Auto-Discovery",
                                requestedBy: $"ContainerCompletenessService-AutoDiscovery|Source:{container.ContainerNumber}");

                            if (queued)
                            {
                                queuedCount++;
                                _logger.LogInformation("{ServiceId} ✅ Auto-queued {ContainerNumber} ({ScannerType}) for ICUMS download",
                                    SERVICE_ID, containerNum, container.ScannerType);
                            }
                        }

                        container.RetryCount++;
                        container.UpdatedAt = DateTime.UtcNow;
                        container.LastCheckedAt = DateTime.UtcNow;
                        dbContext.ContainerCompletenessStatuses.Update(container);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{ServiceId} Error auto-queuing container {ContainerNumber}",
                            SERVICE_ID, container.ContainerNumber);
                    }
                }

                if (queuedCount > 0 || fixedCount > 0)
                {
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("{ServiceId} 🎯 Auto-Discovery: Queued {Queued}, Fixed {Fixed} containers",
                        SERVICE_ID, queuedCount, fixedCount);
                }

                return queuedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error during auto-discovery of missing ICUMS containers", SERVICE_ID);
                return queuedCount;
            }
        }
    }
}
