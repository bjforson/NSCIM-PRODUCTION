using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Logging;
using NickScanCentralImagingPortal.Services.Monitoring;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Orchestrates all ICUMS pipeline workflows in a single coordinated service
    /// Consolidates: IcumBackgroundService, IcumFileScannerService, 
    ///               IcumDataTransferService, ICUMSDownloadBackgroundService
    /// Note: JSON ingestion is handled by standalone IcumJsonIngestionService
    /// Benefits: Reduced memory (~200MB → ~40MB), single DbContext scope, shared state
    /// </summary>
    public class IcumPipelineOrchestratorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IcumPipelineOrchestratorService> _logger;
        private readonly AdaptivePollingHelper _adaptivePolling;
        private readonly ServiceHealthMonitor _healthMonitor;
        private const string SERVICE_ID = "ICUMS-PIPELINE-ORCHESTRATOR";

        // Track last execution times for each workflow
        private DateTime _lastBackgroundServiceRun = DateTime.MinValue;
        private DateTime _lastFileScannerRun = DateTime.MinValue;

        private DateTime _lastDataTransferRun = DateTime.MinValue;
        private DateTime _lastDownloadQueueRun = DateTime.MinValue;

        // Track state for background service (initialized from GoLiveDate in constructor)
        private DateTime _lastFetchTime;
        private int _consecutiveFailures = 0;
        private const int CIRCUIT_BREAKER_THRESHOLD = 10;
        private DateTime _lastCleanupTime = DateTime.UtcNow;
        private const int CLEANUP_INTERVAL_HOURS = 24;
        private DateTime _lastHeartbeat = DateTime.UtcNow;
        private const int HEARTBEAT_INTERVAL_MINUTES = 5;
        private DateTime _serviceStartTime = DateTime.UtcNow;
        private int _totalProcessed = 0;
        private int _totalSuccessful = 0;
        private int _totalFailed = 0;

        // Query result cache within cycle (Phase 3.3 optimization)
        private Dictionary<string, object>? _cycleCache;

        // Audit 8.13 (Sprint 5G2): monotonic iteration counter for the
        // per-iteration heartbeat log. Reset on process restart, which is fine
        // — operators only need it to be monotonic within one process lifetime.
        private int _cycleCount = 0;

        private readonly GoLiveOptions _goLiveOptions;
        private readonly DataRetentionOptions _dataRetention;

        public IcumPipelineOrchestratorService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<IcumPipelineOrchestratorService> logger,
            IOptions<GoLiveOptions> goLiveOptions,
            IOptions<DataRetentionOptions> dataRetention,
            AdaptivePollingHelper? adaptivePolling = null,
            ServiceHealthMonitor? healthMonitor = null)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
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
            _serviceStartTime = DateTime.UtcNow;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
            _dataRetention = dataRetention?.Value ?? new DataRetentionOptions();
            // Go-live + data retention: don't fetch data before cutoff (no retrospective backfill)
            var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
            var retentionCutoff = _dataRetention.EffectiveCutoffDate;
            var cutoff = (goLiveDate > DateTime.MinValue && retentionCutoff > DateTime.MinValue)
                ? (goLiveDate > retentionCutoff ? goLiveDate : retentionCutoff)
                : (goLiveDate > DateTime.MinValue ? goLiveDate : retentionCutoff);
            _lastFetchTime = cutoff > DateTime.MinValue
                ? (cutoff > DateTime.UtcNow.AddHours(-2) ? cutoff : DateTime.UtcNow.AddHours(-2))
                : DateTime.UtcNow.AddHours(-2);
        }

        private DateTime GetEffectiveCutoffDate()
        {
            var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
            var retentionCutoff = _dataRetention.EffectiveCutoffDate;
            if (goLiveDate > DateTime.MinValue && retentionCutoff > DateTime.MinValue)
                return goLiveDate > retentionCutoff ? goLiveDate : retentionCutoff;
            return goLiveDate > DateTime.MinValue ? goLiveDate : retentionCutoff;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[{ServiceId}] ICUMS Pipeline Orchestrator Service started", SERVICE_ID);

            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2): mint per-cycle CorrelationId so every
                // log line emitted during this iteration carries the same key.
                using var _cycleScope = _logger.BeginCycle(nameof(IcumPipelineOrchestratorService));
                // Audit 8.13 (Sprint 5G2): track elapsed time for the heartbeat
                // line emitted at the bottom of the iteration.
                var _cycleStartedAt = DateTime.UtcNow;
                try
                {
                    _cycleCount++;
                    // Clear cycle cache at start of each cycle (Phase 3.3 optimization)
                    _cycleCache = new Dictionary<string, object>();

                    var now = DateTime.UtcNow;

                    // Phase 3.1: Adaptive Polling - Count work items to determine intervals
                    var fileScannerWorkCount = await GetFileScannerWorkCountAsync(stoppingToken);
                    var downloadQueueWorkCount = await GetDownloadQueueWorkCountAsync(stoppingToken);

                    var dataTransferWorkCount = await GetDataTransferWorkCountAsync(stoppingToken);

                    // Execute workflows with adaptive intervals
                    // 1. File Scanner: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastFileScannerRun, fileScannerWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "FileScanner",
                            async () => await RunFileScannerWorkflowAsync(stoppingToken),
                            () => fileScannerWorkCount);
                        _lastFileScannerRun = now;
                    }

                    // 2. Download Queue: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastDownloadQueueRun, downloadQueueWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "DownloadQueue",
                            async () => await RunDownloadQueueWorkflowAsync(stoppingToken),
                            () => downloadQueueWorkCount);
                        _lastDownloadQueueRun = now;
                    }

                    // 3. Data Transfer: adaptive based on work count
                    if (_adaptivePolling.ShouldExecute(_lastDataTransferRun, dataTransferWorkCount, now))
                    {
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "DataTransfer",
                            async () => await RunDataTransferWorkflowAsync(stoppingToken),
                            () => dataTransferWorkCount);
                        _lastDataTransferRun = now;
                    }

                    // 5. Background Service: every 30 minutes (batch downloads - low priority)
                    var minutesSinceLastRun = (now - _lastBackgroundServiceRun).TotalMinutes;
                    if (minutesSinceLastRun >= 30)
                    {
                        _logger.LogInformation("[BACKGROUND-SERVICE] ⏰ Triggering batch download workflow (Last run: {LastRun}, Minutes since: {Minutes:F1})",
                            _lastBackgroundServiceRun == DateTime.MinValue ? "Never" : _lastBackgroundServiceRun.ToString("HH:mm:ss"), minutesSinceLastRun);
                        await _healthMonitor.MeasureExecutionAsync(
                            SERVICE_ID,
                            "BackgroundService",
                            async () => await RunBackgroundServiceWorkflowAsync(stoppingToken));
                        _lastBackgroundServiceRun = now;
                    }
                    else
                    {
                        _logger.LogDebug("[BACKGROUND-SERVICE] ⏸️ Skipping batch download (Last run: {LastRun}, Minutes since: {Minutes:F1}, Next run in: {NextRun:F1} min)",
                            _lastBackgroundServiceRun == DateTime.MinValue ? "Never" : _lastBackgroundServiceRun.ToString("HH:mm:ss"),
                            minutesSinceLastRun, 30 - minutesSinceLastRun);
                    }

                    // Update service metrics
                    var memoryUsage = _healthMonitor.GetCurrentMemoryUsage();
                    _healthMonitor.RecordServiceMetrics(SERVICE_ID, memoryUsage);

                    // Calculate adaptive delay based on minimum work count
                    var minWorkCount = Math.Min(Math.Min(fileScannerWorkCount, downloadQueueWorkCount),
                        dataTransferWorkCount);
                    var delay = _adaptivePolling.CalculateInterval(minWorkCount);
                    // Ensure minimum delay of 30 seconds for ICUMS pipeline
                    if (delay.TotalSeconds < 30)
                        delay = TimeSpan.FromSeconds(30);

                    // Audit 8.13 (Sprint 5G2): per-iteration heartbeat. processed
                    // is the sum of file-scanner / download-queue / data-transfer
                    // work counts seen this cycle.
                    _logger.LogIterationSummary(
                        SERVICE_ID,
                        _cycleCount,
                        DateTime.UtcNow - _cycleStartedAt,
                        itemsProcessed: fileScannerWorkCount + downloadQueueWorkCount + dataTransferWorkCount,
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
                    _logger.LogError(ex, "[{ServiceId}] Error in orchestration cycle", SERVICE_ID);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }

            _logger.LogInformation("[{ServiceId}] ICUMS Pipeline Orchestrator Service stopped", SERVICE_ID);
        }

        #region File Scanner Workflow

        private async Task RunFileScannerWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";

                if (!Directory.Exists(downloadsPath))
                {
                    _logger.LogWarning("[FILE-SCANNER] ICUMS downloads directory does not exist: {Path}", downloadsPath);
                    return;
                }

                // Scan subdirectories for JSON files
                var subdirectories = new[] { "BatchData", "ContainerData", "ScanResults", "StatusChecks" };
                var jsonFiles = new List<string>();

                foreach (var subdir in subdirectories)
                {
                    var subdirPath = Path.Combine(downloadsPath, subdir);
                    if (Directory.Exists(subdirPath))
                    {
                        var filesInSubdir = Directory.GetFiles(subdirPath, "*.json", SearchOption.TopDirectoryOnly);
                        jsonFiles.AddRange(filesInSubdir);
                    }
                }

                if (!jsonFiles.Any())
                {
                    return;
                }

                var newFilesCount = 0;

                foreach (var filePath in jsonFiles)
                {
                    try
                    {
                        // Check if file still exists (may have been deleted between discovery and processing)
                        if (!File.Exists(filePath))
                        {
                            _logger.LogDebug("[FILE-SCANNER] File no longer exists, skipping: {FilePath}", filePath);
                            continue;
                        }

                        var fileInfo = new FileInfo(filePath);
                        var fileName = fileInfo.Name;

                        // Go-live + data retention: skip files created before cutoff (no retrospective processing)
                        var cutoff = GetEffectiveCutoffDate();
                        if (cutoff > DateTime.MinValue && fileInfo.CreationTimeUtc < cutoff)
                        {
                            _logger.LogDebug("[FILE-SCANNER] Skipping file before cutoff {Cutoff}: {FileName}",
                                cutoff.ToString("yyyy-MM-dd"), fileName);
                            continue;
                        }

                        var fileHash = await ComputeFileHashAsync(filePath);

                        var existingFile = await downloadsRepository.GetFileByNameAsync(fileName);
                        if (existingFile != null)
                        {
                            continue;
                        }

                        var duplicateByHash = await downloadsRepository.GetFileByHashAsync(fileHash);
                        if (duplicateByHash != null)
                        {
                            _logger.LogDebug("[FILE-SCANNER] Skipping duplicate file by content hash: {FileName}", fileName);
                            continue;
                        }

                        var downloadedFile = new DownloadedFile
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            FileSize = fileInfo.Length,
                            FileHash = fileHash,
                            DownloadDate = fileInfo.CreationTimeUtc,
                            ProcessingStatus = "Pending"
                        };

                        await downloadsRepository.SaveDownloadedFileAsync(downloadedFile);
                        newFilesCount++;

                        _logger.LogDebug("[FILE-SCANNER] Registered new ICUMS file: {FileName}", fileName);
                    }
                    catch (FileNotFoundException ex)
                    {
                        // File was deleted between discovery and processing - this is normal, just skip it
                        _logger.LogDebug("[FILE-SCANNER] File not found (may have been deleted), skipping: {FilePath}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[FILE-SCANNER] Failed to register file {FilePath}", filePath);
                    }
                }

                if (newFilesCount > 0)
                {
                    _logger.LogInformation("[FILE-SCANNER] Registered {Count} new ICUMS files for processing", newFilesCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FILE-SCANNER] Error in file scanner workflow");
            }
        }

        #endregion

        #region Download Queue Workflow

        private async Task RunDownloadQueueWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Health monitoring: Log heartbeat periodically
                if ((DateTime.UtcNow - _lastHeartbeat).TotalMinutes >= HEARTBEAT_INTERVAL_MINUTES)
                {
                    var uptime = DateTime.UtcNow - _serviceStartTime;
                    _logger.LogInformation("[DOWNLOAD-QUEUE] 💓 Heartbeat - Uptime: {Uptime}, Processed: {Processed} (Success: {Success}, Failed: {Failed})",
                        uptime, _totalProcessed, _totalSuccessful, _totalFailed);
                    _lastHeartbeat = DateTime.UtcNow;
                }

                // Periodic cleanup
                if ((DateTime.UtcNow - _lastCleanupTime).TotalHours >= CLEANUP_INTERVAL_HOURS)
                {
                    await ArchiveOldItemsAsync(stoppingToken);
                    _lastCleanupTime = DateTime.UtcNow;
                }

                await ProcessQueueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "[DOWNLOAD-QUEUE] Error in queue processing (Consecutive Failures: {Failures}/{Threshold})",
                    _consecutiveFailures, CIRCUIT_BREAKER_THRESHOLD);

                if (_consecutiveFailures >= CIRCUIT_BREAKER_THRESHOLD)
                {
                    _logger.LogWarning("[DOWNLOAD-QUEUE] Circuit breaker triggered. Waiting 5 minutes before retry...");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    _consecutiveFailures = 0;
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            List<ICUMSDownloadQueue> batch;
            using (var statsScope = _serviceProvider.CreateScope())
            {
                var queueRepository = statsScope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();

                try
                {
                    if (_consecutiveFailures >= CIRCUIT_BREAKER_THRESHOLD)
                    {
                        return;
                    }

                    var recoveredCount = await queueRepository.RecoverStuckProcessingItemsAsync(30);
                    if (recoveredCount > 0)
                    {
                        _logger.LogInformation("[DOWNLOAD-QUEUE] Recovered {Count} items stuck in Processing status", recoveredCount);
                    }

                    var stats = await queueRepository.GetQueueStatisticsAsync();

                    if (stats.TotalPending == 0)
                    {
                        return;
                    }

                    _logger.LogDebug("[DOWNLOAD-QUEUE] Queue: {Pending} pending, {Processing} processing, {Completed} completed, {Failed} failed",
                        stats.TotalPending, stats.TotalProcessing, stats.TotalCompleted, stats.TotalFailed);

                    batch = await queueRepository.GetNextBatchAsync(50);

                    if (!batch.Any())
                    {
                        return;
                    }

                    _logger.LogInformation("[DOWNLOAD-QUEUE] Processing batch of {Count} containers", batch.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DOWNLOAD-QUEUE] Error getting batch from queue");
                    return;
                }
            }

            var successCount = 0;
            var failureCount = 0;

            foreach (var item in batch)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                using (var itemScope = _serviceProvider.CreateScope())
                {
                    var itemQueueRepository = itemScope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();
                    var itemIcumApiService = itemScope.ServiceProvider.GetRequiredService<IIcumApiService>();
                    var itemIcumDownloadsRepository = itemScope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

                    try
                    {
                        var hasData = await itemIcumDownloadsRepository.ContainerHasICUMSDataAsync(item.ContainerNumber);
                        if (hasData)
                        {
                            _logger.LogDebug("[DOWNLOAD-QUEUE] ⏭️ Skipping {ContainerNumber} - already has ICUMS data", item.ContainerNumber);
                            await itemQueueRepository.MarkAsCompletedAsync(item.Id);
                            successCount++;
                            continue;
                        }

                        await itemQueueRepository.MarkAsProcessingAsync(item.Id);

                        _logger.LogDebug("[DOWNLOAD-QUEUE] Downloading ICUMS data for: {ContainerNumber}", item.ContainerNumber);

                        var icumsResponse = await itemIcumApiService.FetchContainerDataAsync(item.ContainerNumber);

                        if (icumsResponse.Status == "Success" && icumsResponse.Data != null && IsValidBoeScanDocument(icumsResponse.Data))
                        {

                            // Create JSON file
                            var fileName = $"ContainerData_{item.ContainerNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                            var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
                            var containerDataPath = Path.Combine(downloadsPath, "ContainerData");
                            var filePath = Path.Combine(containerDataPath, fileName);

                            try
                            {
                                Directory.CreateDirectory(containerDataPath);

                                var jsonData = new
                                {
                                    DownloadDate = DateTime.UtcNow,
                                    DownloadSource = "ICUMSDownloadBackgroundService-IndividualAPI",
                                    ContainerNumber = item.ContainerNumber,
                                    RecordCount = 1,
                                    BOEScanDocument = new[] { icumsResponse.Data }
                                };

                                var jsonContent = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions
                                {
                                    WriteIndented = true,
                                    PropertyNamingPolicy = null
                                });

                                await File.WriteAllTextAsync(filePath, jsonContent, stoppingToken);

                                var fileSize = new FileInfo(filePath).Length;
                                var fileHash = await ComputeFileHashAsync(filePath);

                                var downloadedFile = new DownloadedFile
                                {
                                    FileName = fileName,
                                    FilePath = filePath,
                                    FileSize = fileSize,
                                    FileHash = fileHash,
                                    DownloadDate = DateTime.UtcNow,
                                    ProcessingStatus = "Pending",
                                    RecordCount = 1
                                };

                                var downloadedFileId = await itemIcumDownloadsRepository.SaveDownloadedFileAsync(downloadedFile);

                                var boeDocument = ConvertICUMSResponseToBOEDocument(icumsResponse.Data, downloadedFileId, item.ContainerNumber);

                                if (boeDocument != null)
                                {
                                    await itemIcumDownloadsRepository.SaveBOEDocumentAsync(boeDocument);
                                    await itemQueueRepository.MarkAsCompletedAsync(item.Id);
                                    successCount++;
                                    _consecutiveFailures = 0;

                                    _logger.LogInformation("[DOWNLOAD-QUEUE] Successfully downloaded: {ContainerNumber}", item.ContainerNumber);
                                }
                                else
                                {
                                    await itemQueueRepository.UpdateRetryInfoAsync(item.Id, "Failed to convert ICUMS response", "CONVERSION_ERROR");
                                    failureCount++;
                                }
                            }
                            catch (Exception fileEx)
                            {
                                _logger.LogError(fileEx, "[DOWNLOAD-QUEUE] Error creating file for {ContainerNumber}", item.ContainerNumber);
                                await itemQueueRepository.UpdateRetryInfoAsync(item.Id, "File creation error", "FILE_ERROR");
                                failureCount++;
                            }
                        }
                        else if (icumsResponse.Status == "Success")
                        {
                            _logger.LogInformation("[DOWNLOAD-QUEUE] ICUMS has no BOE data for {ContainerNumber} (not yet declared)", item.ContainerNumber);
                            await itemQueueRepository.MarkAsCompletedAsync(item.Id);
                            successCount++;
                        }
                        else
                        {
                            var errorMsg = icumsResponse.Error?.ErrorMsg ?? icumsResponse.StatusMsg ?? "No data returned";
                            await itemQueueRepository.UpdateRetryInfoAsync(item.Id, errorMsg, icumsResponse.Status);
                            failureCount++;
                            _consecutiveFailures++;
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.LogWarning("[DOWNLOAD-QUEUE] Timeout downloading {ContainerNumber}: {Message}", item.ContainerNumber, ex.Message);
                        await itemQueueRepository.UpdateRetryInfoAsync(item.Id, "Timeout", "TIMEOUT");
                        failureCount++;
                        _consecutiveFailures++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DOWNLOAD-QUEUE] Error processing queue item: {ContainerNumber}", item.ContainerNumber);
                        await itemQueueRepository.UpdateRetryInfoAsync(item.Id, ex.Message, ex.GetType().Name);
                        failureCount++;
                        _consecutiveFailures++;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }

            _totalProcessed += batch.Count;
            _totalSuccessful += successCount;
            _totalFailed += failureCount;

            _logger.LogInformation("[DOWNLOAD-QUEUE] ✅ Batch completed: {Success} successful, {Failed} failed",
                successCount, failureCount);
        }

        #endregion

        #region Data Transfer Workflow

        private async Task RunDataTransferWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                var icumRepository = scope.ServiceProvider.GetRequiredService<IIcumRepository>();

                var completedDocuments = await downloadsRepository.GetPendingBOEDocumentsAsync();

                if (!completedDocuments.Any())
                {
                    return;
                }

                _logger.LogInformation("[DATA-TRANSFER] Found {Count} completed BOE documents to transfer", completedDocuments.Count);

                var transferredCount = 0;
                var transferredItemsCount = 0;

                foreach (var boeDoc in completedDocuments)
                {
                    try
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        // ✅ FIX REPROCESSING: Double-check status before processing (race condition protection)
                        if (boeDoc.ProcessingStatus == "Transferred" || boeDoc.ProcessingStatus == "TransferFailed")
                        {
                            _logger.LogDebug("[DATA-TRANSFER] ⏭️ Skipping {ContainerNumber} - already transferred (Status: {Status})",
                                boeDoc.ContainerNumber, boeDoc.ProcessingStatus);
                            continue;
                        }

                        using var documentScope = _serviceProvider.CreateScope();
                        var scopedDownloadsRepository = documentScope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                        var scopedIcumRepository = documentScope.ServiceProvider.GetRequiredService<IIcumRepository>();

                        // ✅ FIX REPROCESSING: Check if container already exists in main ICUMS database
                        var alreadyTransferred = await scopedIcumRepository.ContainerDataExistsAsync(boeDoc.ContainerNumber ?? "");
                        if (alreadyTransferred)
                        {
                            _logger.LogDebug("[DATA-TRANSFER] ⏭️ Skipping {ContainerNumber} - already exists in main ICUMS database",
                                boeDoc.ContainerNumber);
                            // Mark as transferred even though we didn't transfer (already there)
                            await scopedDownloadsRepository.UpdateBOEDocumentProcessingStatusAsync(boeDoc.Id, "Transferred");
                            continue;
                        }

                        var itemsForThisDocument = await scopedDownloadsRepository.GetManifestItemsByBOEDocumentIdAsync(boeDoc.Id);

                        var containerData = ConvertToIcumContainerData(boeDoc);
                        var icumManifestItems = itemsForThisDocument.Select(ConvertToIcumManifestItem).ToList();

                        await scopedIcumRepository.SaveContainerDataWithItemsAsync(containerData, icumManifestItems);
                        await scopedDownloadsRepository.UpdateBOEDocumentProcessingStatusAsync(boeDoc.Id, "Transferred");

                        foreach (var item in itemsForThisDocument)
                        {
                            await scopedDownloadsRepository.UpdateManifestItemProcessingStatusAsync(item.Id, "Transferred");
                        }

                        transferredCount++;
                        transferredItemsCount += itemsForThisDocument.Count;

                        _logger.LogDebug("[DATA-TRANSFER] ✓ Transferred container {ContainerNumber} with {ItemCount} manifest items",
                            boeDoc.ContainerNumber, itemsForThisDocument.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DATA-TRANSFER] ✗ Failed to transfer BOE document {DocumentId}",
                            boeDoc.Id);
                        await downloadsRepository.UpdateBOEDocumentProcessingStatusAsync(boeDoc.Id, "TransferFailed", ex.Message);
                    }
                }

                if (transferredCount > 0)
                {
                    _logger.LogInformation("[DATA-TRANSFER] ✓ Completed transfer: {Documents} documents, {Items} manifest items",
                        transferredCount, transferredItemsCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATA-TRANSFER] Error in data transfer workflow");
            }
        }

        #endregion

        #region Background Service Workflow

        /// <summary>
        /// Public method to manually trigger batch download workflow
        /// </summary>
        public async Task<bool> TriggerBatchDownloadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[BACKGROUND-SERVICE] 🚀 Manual batch download triggered via API");
                await RunBackgroundServiceWorkflowAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BACKGROUND-SERVICE] ❌ Error during manual batch download trigger");
                return false;
            }
        }

        private async Task RunBackgroundServiceWorkflowAsync(CancellationToken stoppingToken)
        {
            try
            {
                var isEnabled = _configuration.GetValue<bool>("BackgroundServices:IcumBackgroundService:Enabled", false);

                if (!isEnabled)
                {
                    _logger.LogWarning("[BACKGROUND-SERVICE] ⚠️ Service disabled in configuration - batch downloads will not run");
                    return;
                }

                _logger.LogInformation("[BACKGROUND-SERVICE] ✅ Service enabled - starting batch download workflow");

                using var scope = _serviceProvider.CreateScope();
                var icumApiService = scope.ServiceProvider.GetRequiredService<IIcumApiService>();
                var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();

                var endDate = DateTime.UtcNow;
                var startDate = _lastFetchTime;

                // Go-live + data retention: never fetch before cutoff
                var cutoff = GetEffectiveCutoffDate();
                if (cutoff > DateTime.MinValue && startDate < cutoff)
                    startDate = cutoff;

                var maxCatchUpHours = 2;
                if ((endDate - startDate).TotalHours > maxCatchUpHours)
                {
                    startDate = endDate.AddHours(-maxCatchUpHours);
                }

                if ((endDate - startDate).TotalDays > 1)
                {
                    startDate = endDate.AddDays(-1);
                }

                _logger.LogInformation("[BACKGROUND-SERVICE] Fetching ICUMS batch data from {StartDate} to {EndDate}", startDate, endDate);

                var allRecords = new List<BoeScanDocument>();
                var currentStart = startDate;
                var chunkSize = TimeSpan.FromHours(2);
                var chunkCount = 0;

                while (currentStart < endDate)
                {
                    var currentEnd = currentStart.Add(chunkSize);
                    if (currentEnd > endDate)
                        currentEnd = endDate;

                    chunkCount++;
                    var batchResponse = await icumApiService.FetchBatchDataAsync(currentStart, currentEnd);

                    if (batchResponse.Status == "Success" && batchResponse.Data?.BoeScanDocuments != null)
                    {
                        allRecords.AddRange(batchResponse.Data.BoeScanDocuments);
                    }

                    currentStart = currentEnd;

                    if (currentStart < endDate)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    }
                }

                if (allRecords.Count > 0)
                {
                    var newRecords = new List<BoeScanDocument>();
                    var skippedExisting = 0;
                    var skippedInQueue = 0;

                    foreach (var record in allRecords)
                    {
                        var containerNumber = record.ContainerDetails?.ContainerNumber;
                        if (!string.IsNullOrEmpty(containerNumber))
                        {
                            var hasData = await downloadsRepository.ContainerHasICUMSDataAsync(containerNumber);
                            if (hasData)
                            {
                                skippedExisting++;
                                continue;
                            }

                            var inQueue = await queueRepository.IsInQueueAsync(containerNumber);
                            if (inQueue)
                            {
                                skippedInQueue++;
                                continue;
                            }

                            newRecords.Add(record);
                        }
                    }

                    if (newRecords.Count > 0)
                    {
                        var fileName = $"BatchData_{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:HHmmss}.json";
                        var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
                        var batchDataPath = Path.Combine(downloadsPath, "BatchData");
                        var filePath = Path.Combine(batchDataPath, fileName);

                        Directory.CreateDirectory(batchDataPath);

                        var jsonData = new
                        {
                            DownloadDate = DateTime.UtcNow,
                            DownloadSource = "IcumBackgroundService-BatchAPI",
                            StartDate = startDate,
                            EndDate = endDate,
                            RecordCount = newRecords.Count,
                            BOEScanDocument = newRecords
                        };

                        var jsonContent = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = null
                        });

                        await File.WriteAllTextAsync(filePath, jsonContent);
                        var fileSize = new FileInfo(filePath).Length;
                        var fileHash = await ComputeFileHashAsync(filePath);

                        var downloadedFile = new DownloadedFile
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            FileSize = fileSize,
                            FileHash = fileHash,
                            DownloadDate = DateTime.UtcNow,
                            ProcessingStatus = "Pending",
                            RecordCount = newRecords.Count
                        };

                        var fileId = await downloadsRepository.SaveDownloadedFileAsync(downloadedFile);

                        _logger.LogInformation("[BACKGROUND-SERVICE] ✅ Saved and registered batch file: {FileName} ({Count} records)",
                            fileName, newRecords.Count);
                    }
                }

                _lastFetchTime = endDate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BACKGROUND-SERVICE] Error in background service workflow");
            }
        }

        #endregion

        #region Helper Methods

        private async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var fileStream = File.OpenRead(filePath);
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            return Convert.ToHexString(hashBytes);
        }

        private bool IsValidBoeScanDocument(BoeScanDocument? data)
        {
            if (data == null)
                return false;

            bool hasContainerDetails = data.ContainerDetails != null &&
                                      !string.IsNullOrWhiteSpace(data.ContainerDetails.ContainerNumber);

            bool hasHeader = data.Header != null &&
                            (!string.IsNullOrWhiteSpace(data.Header.DeclarationNumber) ||
                             !string.IsNullOrWhiteSpace(data.Header.ClearanceType));

            bool hasManifestDetails = data.ManifestDetails != null &&
                                     (!string.IsNullOrWhiteSpace(data.ManifestDetails.RotationNumber) ||
                                      !string.IsNullOrWhiteSpace(data.ManifestDetails.MasterBlNumber));

            return hasContainerDetails || hasHeader || hasManifestDetails;
        }

        private BOEDocument? ConvertICUMSResponseToBOEDocument(BoeScanDocument data, int downloadedFileId, string containerNumber)
        {
            try
            {
                var boeDocument = new BOEDocument
                {
                    DownloadedFileId = downloadedFileId,
                    DocumentIndex = 0,
                    ContainerNumber = containerNumber,
                    ProcessingStatus = "Completed"
                };

                if (data.ContainerDetails != null)
                {
                    boeDocument.ContainerDescription = data.ContainerDetails.ContainerType;
                    boeDocument.ContainerISO = data.ContainerDetails.ContainerISO;
                    boeDocument.ContainerSize = data.ContainerDetails.ContainerSize;
                    // 2026-05-05 (audit 5.02, P0): the previous `ContainerQuantity = 1`
                    // hardcode silently overrode whatever the JSON carried. The strongly-
                    // typed IcumApiContainerDetails model does not expose ContainerQuantity
                    // today, so removing the assignment leaves the value null on this path
                    // — which is honest about the gap and aligned with the JSON-file
                    // ingestion path (IcumJsonIngestionService:1190 reads it from JSON).
                    boeDocument.ContainerWeight = data.ContainerDetails.ContainerWeight;
                    boeDocument.SealNumber = data.ContainerDetails.SealNumber;
                    boeDocument.TruckPlateNumber = data.ContainerDetails.TruckPlateNumber;
                    boeDocument.DriverName = data.ContainerDetails.DriverName;
                    boeDocument.DriverLicense = data.ContainerDetails.DriverLicense;
                    boeDocument.ContainerStatus = data.ContainerDetails.Status;
                    boeDocument.ContainerRemarks = data.ContainerDetails.Remarks;
                }

                if (data.Header != null)
                {
                    boeDocument.ImpName = data.Header.ImpName;
                    boeDocument.TotalDutyPaid = data.Header.TotalDutyPaid;
                    boeDocument.CrmsLevel = data.Header.CrmsLevel;
                    boeDocument.ExpAddress = data.Header.ExpAddress;
                    boeDocument.DeclarationNumber = data.Header.DeclarationNumber;
                    boeDocument.RegimeCode = data.Header.RegimeCode;
                    boeDocument.NoOfContainers = data.Header.NoofContainers;
                    boeDocument.CompOffRemarks = data.Header.CompOffRemarks;
                    boeDocument.DeclarantName = data.Header.DeclarantName;
                    boeDocument.ExpName = data.Header.ExpName;
                    boeDocument.ImpAddress = data.Header.ImpAddress;
                    boeDocument.ImpExpName = data.Header.ImpExpName;
                    boeDocument.CcvrIntelRemarks = data.Header.CcvrIntelRemarks;
                    boeDocument.DeclarationVersion = data.Header.DeclarationVersion;
                    boeDocument.ImpExpAddress = data.Header.ImpExpAddress;
                    boeDocument.DeclarationDate = data.Header.DeclarationDate;
                    boeDocument.ClearanceType = data.Header.ClearanceType;
                    boeDocument.DeclarantAddress = data.Header.DeclarantAddress;
                }

                if (data.ManifestDetails != null)
                {
                    boeDocument.RotationNumber = data.ManifestDetails.RotationNumber;
                    boeDocument.ConsigneeName = data.ManifestDetails.ConsigneeName;
                    boeDocument.CountryOfOrigin = data.ManifestDetails.CountryofOrigin;
                    boeDocument.MarksNumbers = data.ManifestDetails.MarksNumbers;
                    boeDocument.ShipperName = data.ManifestDetails.ShipperName;
                    boeDocument.ShipperAddress = data.ManifestDetails.ShipperAddress;
                    // 2026-05-05 (audit 5.02, P0): persist MasterBlNumber to its proper
                    // column. Pre-fix this path copied MasterBlNumber → BlNumber and never
                    // set MasterBlNumber, leaving every on-demand-fetched IsConsolidated
                    // row with NULL MasterBlNumber. Now: master goes to MasterBlNumber, and
                    // BlNumber is only filled from master as a fallback when no other
                    // source supplied it (the strongly-typed model has only MasterBlNumber
                    // at the manifest level today, but a future ingest path might split
                    // master vs document BL — the fallback preserves the historic shape).
                    boeDocument.MasterBlNumber = data.ManifestDetails.MasterBlNumber;
                    if (string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                    {
                        boeDocument.BlNumber = data.ManifestDetails.MasterBlNumber;
                    }
                    boeDocument.DeliveryPlace = data.ManifestDetails.DeliveryPlace;
                    boeDocument.HouseBl = data.ManifestDetails.HouseBl;
                    boeDocument.ConsigneeAddress = data.ManifestDetails.ConsigneeAddress;
                    boeDocument.GoodsDescription = data.ManifestDetails.GoodsDescription;
                    boeDocument.IsConsolidated = !string.IsNullOrWhiteSpace(data.ManifestDetails.HouseBl) &&
                                                  data.ManifestDetails.HouseBl != data.ManifestDetails.MasterBlNumber;
                }

                return boeDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DOWNLOAD-QUEUE] Error converting ICUMS response for {ContainerNumber}", containerNumber);
                return null;
            }
        }

        private IcumContainerData ConvertToIcumContainerData(BOEDocument boeDoc)
        {
            return new IcumContainerData
            {
                ContainerNumber = boeDoc.ContainerNumber,
                BoeData = JsonSerializer.Serialize(boeDoc, new JsonSerializerOptions { WriteIndented = true }),
                ContainerWeight = boeDoc.ContainerWeight,
                ContainerQuantity = boeDoc.ContainerQuantity,
                ContainerISO = boeDoc.ContainerISO ?? string.Empty,
                TotalDutyPaid = boeDoc.TotalDutyPaid ?? 0m,
                CrmsLevel = boeDoc.CrmsLevel ?? string.Empty,
                ClearanceType = boeDoc.ClearanceType ?? string.Empty,
                DeclarationNumber = boeDoc.DeclarationNumber ?? string.Empty,
                MasterBlNumber = boeDoc.BlNumber ?? string.Empty,
                HouseBl = boeDoc.HouseBl ?? string.Empty,
                RotationNumber = boeDoc.RotationNumber ?? string.Empty,
                ConsigneeName = boeDoc.ConsigneeName ?? string.Empty,
                ShipperName = boeDoc.ShipperName ?? string.Empty,
                CountryOfOrigin = boeDoc.CountryOfOrigin ?? string.Empty,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private IcumManifestItem ConvertToIcumManifestItem(DownloadedManifestItem item)
        {
            return new IcumManifestItem
            {
                HsCode = item.HsCode ?? string.Empty,
                Description = item.Description ?? string.Empty,
                Quantity = item.Quantity ?? 0m,
                Unit = item.Unit ?? string.Empty,
                Weight = item.Weight ?? 0m,
                ItemFob = item.ItemFob ?? 0m,
                ItemDutyPaid = item.ItemDutyPaid ?? 0m,
                FobCurrency = item.FobCurrency ?? string.Empty,
                CountryOfOrigin = item.CountryOfOrigin ?? string.Empty,
                ItemNo = item.ItemNo ?? 0,
                Cpc = item.Cpc ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private async Task ArchiveOldItemsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();

                var archivedCount = await queueRepository.CleanupOldItemsAsync(daysToKeep: 7);

                if (archivedCount > 0)
                {
                    _logger.LogInformation("[DOWNLOAD-QUEUE] ✅ Archived {Count} old queue items", archivedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DOWNLOAD-QUEUE] Error archiving old queue items");
            }
        }

        #endregion

        #region Work Count Helpers (Phase 3.1: Adaptive Polling)

        /// <summary>
        /// Count pending work items for file scanner workflow
        /// </summary>
        private Task<int> GetFileScannerWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("fileScannerWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return Task.FromResult(cachedCount);
                }

                var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
                if (!Directory.Exists(downloadsPath))
                    return Task.FromResult(0);

                var subdirectories = new[] { "BatchData", "ContainerData", "ScanResults", "StatusChecks" };
                var totalFiles = 0;

                foreach (var subdir in subdirectories)
                {
                    var subdirPath = Path.Combine(downloadsPath, subdir);
                    if (Directory.Exists(subdirPath))
                    {
                        var files = Directory.GetFiles(subdirPath, "*.json", SearchOption.TopDirectoryOnly);
                        totalFiles += files.Length;
                    }
                }

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["fileScannerWorkCount"] = totalFiles;
                }
                return Task.FromResult(totalFiles);
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Count pending work items for download queue workflow
        /// </summary>
        private async Task<int> GetDownloadQueueWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("downloadQueueWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();
                var stats = await queueRepository.GetQueueStatisticsAsync();

                var count = stats.TotalPending;

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["downloadQueueWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Count pending work items for data transfer workflow
        /// </summary>
        private async Task<int> GetDataTransferWorkCountAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check cache first (Phase 3.3 optimization)
                if (_cycleCache?.TryGetValue("dataTransferWorkCount", out var cached) == true && cached is int cachedCount)
                {
                    return cachedCount;
                }

                using var scope = _serviceProvider.CreateScope();
                var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                var completedDocuments = await downloadsRepository.GetPendingBOEDocumentsAsync();

                var count = completedDocuments?.Count ?? 0;

                // Cache result for this cycle
                if (_cycleCache != null)
                {
                    _cycleCache["dataTransferWorkCount"] = count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        #endregion
    }
}

