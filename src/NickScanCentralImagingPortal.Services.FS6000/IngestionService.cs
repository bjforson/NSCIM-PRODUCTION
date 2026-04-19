using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public partial class IngestionService : IIngestionService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<IngestionService> _logger;
        private readonly FileSyncConfiguration _config;
        private readonly IXmlParsingService _xmlParsingService;
        private readonly GoLiveOptions _goLiveOptions;
        private readonly DataRetentionOptions _dataRetention;
        private bool _isRunning = false;
        private Task? _ingestionTask;
        private readonly CancellationTokenSource _cancellationTokenSource;

        // Phase 1: Caching for duplicate checking
        private HashSet<string> _existingContainersCache = new();
        private DateTime _cacheLastRefreshed = DateTime.MinValue;
        private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(5);

        // Phase 2: FileSystemWatcher for real-time detection
        private FileSystemWatcher? _fileWatcher;
        private readonly object _processingLock = new object();
        private bool _isProcessing = false;

        // Phase 3: Metrics and monitoring
        private int _filesProcessedTotal = 0;
        private int _filesFailedTotal = 0;
        private DateTime _lastProcessedAt = DateTime.MinValue;
        private int _consecutiveFailures = 0;
        private const int CircuitBreakerThreshold = 10;
        private bool _circuitOpen = false;
        private DateTime _circuitOpenedAt = DateTime.MinValue;
        private readonly TimeSpan _circuitResetTimeout = TimeSpan.FromMinutes(5);

        // FIX: File-level parsing cache to prevent duplicate parsing in same cycle
        private readonly HashSet<string> _parsedFilesThisCycle = new();
        private readonly object _parsedFilesLock = new object();

        public IngestionService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<IngestionService> logger,
            IOptions<FileSyncConfiguration> config,
            IXmlParsingService xmlParsingService,
            IOptions<GoLiveOptions> goLiveOptions,
            IOptions<DataRetentionOptions> dataRetention)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _config = config.Value;
            _xmlParsingService = xmlParsingService;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
            _dataRetention = dataRetention?.Value ?? new DataRetentionOptions();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartIngestionAsync()
        {
            if (_isRunning) return;

            _isRunning = true;
            _logger.LogInformation("[FS6000-INGESTION] ✅ Starting ENHANCED FS6000 ingestion service with continuous loop + FileWatcher");

            // PHASE 1: Initialize cache
            await RefreshContainerCacheAsync();

            // PHASE 2: Initialize FileSystemWatcher for real-time detection
            InitializeFileWatcher();

            // PHASE 1 FIX: Start continuous ingestion loop
            _ingestionTask = Task.Run(async () => await ContinuousIngestionLoopAsync(_cancellationTokenSource.Token));

            _logger.LogInformation("[FS6000-INGESTION] ✅ Enhanced ingestion service started successfully");
        }

        public async Task StopIngestionAsync()
        {
            _logger.LogInformation("[FS6000-INGESTION] Stopping FS6000 ingestion service");
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            // Stop FileWatcher
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }

            // Wait for ingestion task to complete
            if (_ingestionTask != null)
            {
                await _ingestionTask;
            }

            _logger.LogInformation("[FS6000-INGESTION] FS6000 ingestion service stopped");
        }

        /// <summary>
        /// PHASE 1 FIX: Continuous ingestion loop - processes files continuously instead of once
        /// </summary>
        private async Task ContinuousIngestionLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[FS6000-INGESTION] 🔄 Continuous ingestion loop started");

            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // PHASE 3: Check circuit breaker
                    if (_circuitOpen)
                    {
                        if (DateTime.UtcNow - _circuitOpenedAt > _circuitResetTimeout)
                        {
                            _logger.LogInformation("[FS6000-INGESTION] 🔄 Circuit breaker auto-reset after timeout");
                            _circuitOpen = false;
                            _consecutiveFailures = 0;
                        }
                        else
                        {
                            _logger.LogWarning("[FS6000-INGESTION] ⚠️ Circuit breaker is OPEN, skipping processing cycle");
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                            continue;
                        }
                    }

                    _logger.LogDebug("[FS6000-INGESTION] Starting processing cycle");

                    // Process files
                    var processed = await ProcessAllFoldersBatchAsync();

                    if (processed > 0)
                    {
                        _filesProcessedTotal += processed;
                        _lastProcessedAt = DateTime.UtcNow;
                        _consecutiveFailures = 0; // Reset on success

                        _logger.LogInformation("[FS6000-INGESTION] ✅ Processed {Count} files. Total: {Total}, Failed: {Failed}",
                            processed, _filesProcessedTotal, _filesFailedTotal);
                    }
                    else
                    {
                        _logger.LogDebug("[FS6000-INGESTION] No new files to process");
                    }

                    // PHASE 1: Refresh cache periodically
                    if (DateTime.UtcNow - _cacheLastRefreshed > _cacheRefreshInterval)
                    {
                        await RefreshContainerCacheAsync();
                    }

                    // Wait before next cycle (read from database settings)
                    // ✅ FIX: Check cancellation before creating scope to prevent ObjectDisposedException
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        using (var scope = _serviceScopeFactory.CreateScope())
                        {
                            var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                            var processingIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "FS6000.ProcessingIntervalMinutes", 1);
                            _logger.LogDebug("⏰ Next processing cycle in {Interval} minutes (from settings)", processingIntervalMinutes);
                            await Task.Delay(TimeSpan.FromMinutes(processingIntervalMinutes), cancellationToken);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogInformation("[FS6000-INGESTION] Service scope disposed, stopping ingestion loop");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[FS6000-INGESTION] Ingestion loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _filesFailedTotal++;
                    _consecutiveFailures++;

                    _logger.LogError(ex, "[FS6000-INGESTION] ❌ Error in ingestion loop (consecutive failures: {Count})", _consecutiveFailures);

                    // PHASE 3: Circuit breaker
                    if (_consecutiveFailures >= CircuitBreakerThreshold)
                    {
                        _circuitOpen = true;
                        _circuitOpenedAt = DateTime.UtcNow;
                        _logger.LogError("[FS6000-INGESTION] 🔴 CIRCUIT BREAKER OPENED after {Count} consecutive failures", _consecutiveFailures);
                    }

                    // PHASE 2: Exponential backoff on error
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(_consecutiveFailures, 5))));
                    _logger.LogWarning("[FS6000-INGESTION] Waiting {Delay} seconds before retry", delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            _logger.LogInformation("[FS6000-INGESTION] Continuous ingestion loop ended");
        }

        /// <summary>
        /// PHASE 2: Initialize FileSystemWatcher for real-time file detection
        /// </summary>
        private void InitializeFileWatcher()
        {
            try
            {
                if (!Directory.Exists(_config.DestinationPath))
                {
                    _logger.LogWarning("[FS6000-INGESTION] Destination path does not exist, skipping FileWatcher: {Path}", _config.DestinationPath);
                    return;
                }

                _fileWatcher = new FileSystemWatcher(_config.DestinationPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    Filter = "*.xml"
                };

                _fileWatcher.Created += OnFileCreated;
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.EnableRaisingEvents = true;

                _logger.LogInformation("[FS6000-INGESTION] 👁️ FileSystemWatcher initialized for real-time detection: {Path}", _config.DestinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-INGESTION] Failed to initialize FileSystemWatcher");
            }
        }

        /// <summary>
        /// PHASE 2: Handle new file creation events
        /// </summary>
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation("[FS6000-INGESTION] 🆕 New XML file detected: {FilePath}", e.FullPath);
            TriggerImmediateProcessing();
        }

        /// <summary>
        /// PHASE 2: Handle file change events
        /// </summary>
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            _logger.LogDebug("[FS6000-INGESTION] File changed: {FilePath}", e.FullPath);
            // Don't trigger on every change to avoid excessive processing
        }

        /// <summary>
        /// PHASE 2: Trigger immediate processing when new files are detected
        /// </summary>
        private void TriggerImmediateProcessing()
        {
            lock (_processingLock)
            {
                if (_isProcessing)
                {
                    _logger.LogDebug("[FS6000-INGESTION] Processing already in progress, skipping trigger");
                    return;
                }

                _isProcessing = true;
            }

            // Process in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)); // Debounce: wait for file to be fully written
                    await ProcessAllFoldersBatchAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[FS6000-INGESTION] Error in immediate processing");
                }
                finally
                {
                    lock (_processingLock)
                    {
                        _isProcessing = false;
                    }
                }
            });
        }

        /// <summary>
        /// PHASE 1: Refresh container cache to avoid loading entire table on every check
        /// </summary>
        private async Task RefreshContainerCacheAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var containerNumbers = await dbContext.FS6000Scans
                    .Select(s => s.ContainerNumber)
                    .ToListAsync();

                _existingContainersCache = containerNumbers.ToHashSet();
                _cacheLastRefreshed = DateTime.UtcNow;

                _logger.LogInformation("[FS6000-INGESTION] 💾 Container cache refreshed: {Count} containers", _existingContainersCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-INGESTION] Error refreshing container cache");
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // ✅ FIX: Handle ObjectDisposedException during shutdown
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await dbContext.Database.CanConnectAsync();
            }
            catch (ObjectDisposedException)
            {
                return false; // Service is shutting down
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> GetPendingIngestionCountAsync()
        {
            try
            {
                // ✅ FIX: Handle ObjectDisposedException during shutdown
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await dbContext.FS6000Scans.CountAsync(s => s.SyncStatus == "Pending");
            }
            catch (ObjectDisposedException)
            {
                return 0; // Service is shutting down
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> GetFailedIngestionCountAsync()
        {
            try
            {
                // ✅ FIX: Handle ObjectDisposedException during shutdown
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await dbContext.FS6000Scans.CountAsync(s => s.SyncStatus == "Failed");
            }
            catch (ObjectDisposedException)
            {
                return 0; // Service is shutting down
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// PHASE 3: Get comprehensive metrics
        /// </summary>
        public FS6000Metrics GetMetrics()
        {
            return new FS6000Metrics
            {
                FilesProcessedTotal = _filesProcessedTotal,
                FilesFailedTotal = _filesFailedTotal,
                LastProcessedAt = _lastProcessedAt,
                IsRunning = _isRunning,
                CircuitBreakerOpen = _circuitOpen,
                ConsecutiveFailures = _consecutiveFailures,
                CachedContainerCount = _existingContainersCache.Count,
                CacheLastRefreshed = _cacheLastRefreshed
            };
        }

        /// <summary>
        /// PHASE 3: Metrics model
        /// </summary>
        public class FS6000Metrics
        {
            public int FilesProcessedTotal { get; set; }
            public int FilesFailedTotal { get; set; }
            public DateTime LastProcessedAt { get; set; }
            public bool IsRunning { get; set; }
            public bool CircuitBreakerOpen { get; set; }
            public int ConsecutiveFailures { get; set; }
            public int CachedContainerCount { get; set; }
            public DateTime CacheLastRefreshed { get; set; }
        }

        // FIXED: Now uses XmlParsingService for proper field extraction
        public async Task<int> ProcessAllFoldersBatchAsync()
        {
            try
            {
                _logger.LogDebug("Starting FIXED batch ingestion cycle with proper XML parsing");
                var dataFolders = GetDataFolders();
                _logger.LogDebug("Found {Count} data folders to process", dataFolders.Count);

                if (!dataFolders.Any())
                {
                    _logger.LogDebug("No data folders found");
                    return 0;
                }

                // ✅ FIX: Handle ObjectDisposedException during shutdown
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var queuePublisher = scope.ServiceProvider.GetService<IContainerScanQueuePublisher>();

                // PHASE 2: Collect all container data with proper XML parsing using parallel processing
                var allContainerData = new System.Collections.Concurrent.ConcurrentBag<(FS6000Scan Scan, string JpegFile, string FolderPath)>();
                var processingErrors = new System.Collections.Concurrent.ConcurrentBag<string>();

                // PHASE 2: Process folders in parallel for better throughput
                await Parallel.ForEachAsync(dataFolders, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
                },
                async (folder, cancellationToken) =>
                {
                    try
                    {
                        var xmlFiles = Directory.GetFiles(folder, "*.xml");
                        var jpegFiles = Directory.GetFiles(folder, "*.jpg")
                            .Concat(Directory.GetFiles(folder, "*.jpeg"))
                            .ToList();

                        if (!xmlFiles.Any() || !jpegFiles.Any())
                        {
                            _logger.LogDebug("Skipping folder {Folder} - missing XML or JPEG files", folder);
                            return;
                        }

                        var xmlFile = xmlFiles.First();

                        // FIX: Check if this file was already parsed in this cycle
                        var normalizedXmlPath = Path.GetFullPath(xmlFile).ToLowerInvariant();
                        lock (_parsedFilesLock)
                        {
                            if (_parsedFilesThisCycle.Contains(normalizedXmlPath))
                            {
                                _logger.LogDebug("[FS6000-INGESTION] ⏭️ Skipping already-parsed file in this cycle: {File}", xmlFile);
                                return;
                            }
                            _parsedFilesThisCycle.Add(normalizedXmlPath);
                        }

                        var jpegFile = GetLargestJpegFile(jpegFiles);

                        if (jpegFile == null)
                        {
                            _logger.LogDebug("Skipping folder {Folder} - no valid JPEG file", jpegFile);
                            return;
                        }

                        // FIXED: Use XmlParsingService to parse ALL fields properly
                        var scans = await _xmlParsingService.ParseXmlFileAsync(xmlFile);

                        if (!scans.Any())
                        {
                            _logger.LogWarning("No scans parsed from XML: {XmlFile}", xmlFile);
                            return;
                        }

                        // Go-live + data retention cutoff: only process scans on or after cutoff (no retrospective backfill)
                        var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
                        var retentionCutoff = _dataRetention.EffectiveCutoffDate;
                        var cutoff = (goLiveDate > DateTime.MinValue && retentionCutoff > DateTime.MinValue)
                            ? (goLiveDate > retentionCutoff ? goLiveDate : retentionCutoff)
                            : (goLiveDate > DateTime.MinValue ? goLiveDate : retentionCutoff);
                        var scansToAdd = cutoff > DateTime.MinValue
                            ? scans.Where(s => s.ScanTime >= cutoff).ToList()
                            : scans;

                        if (scans.Count != scansToAdd.Count)
                        {
                            _logger.LogDebug("Skipped {Skipped} scans before cutoff {Cutoff} from {XmlFile}",
                                scans.Count - scansToAdd.Count, cutoff.ToString("yyyy-MM-dd"), xmlFile);
                        }

                        // Add parsed scans with proper field mapping
                        foreach (var scan in scansToAdd)
                        {
                            scan.FilePath = folder;
                            scan.CreatedAt = DateTime.UtcNow;
                            scan.SyncStatus = "Pending";
                            allContainerData.Add((scan, jpegFile, folder));
                        }

                        _logger.LogDebug("Parsed {Count} scans from {XmlFile}", scansToAdd.Count, xmlFile);
                    }
                    catch (Exception ex)
                    {
                        processingErrors.Add($"{folder}: {ex.Message}");
                        _logger.LogError(ex, "Error processing folder: {Folder}", folder);
                    }
                });

                if (processingErrors.Any())
                {
                    _logger.LogWarning("[FS6000-INGESTION] ⚠️ {Count} folders failed processing", processingErrors.Count);
                }

                if (!allContainerData.Any())
                {
                    _logger.LogDebug("No valid container data found in any folder");
                    return 0;
                }

                // PHASE 1: Use cached container list instead of loading from database
                _logger.LogDebug("Using cached container list: {CachedCount} containers", _existingContainersCache.Count);

                // Filter out existing containers using cache
                var newContainerData = allContainerData
                    .Where(c => !_existingContainersCache.Contains(c.Scan.ContainerNumber))
                    .ToList();

                _logger.LogDebug("Found {NewCount} new containers out of {TotalCount} total containers",
                    newContainerData.Count, allContainerData.Count);

                // ✅ FIX: Process new containers if any exist
                if (newContainerData.Any())
                {
                    _logger.LogDebug("Processing {Count} new containers with FULL field mapping", newContainerData.Count);

                    var scansToAdd = newContainerData.Select(c => c.Scan).ToList();

                    // Create OriginalScanRecord entries grouped by PicNumber (one per original scan event)
                    var scansByPicNumber = newContainerData.GroupBy(c => c.Scan.PicNumber);
                    foreach (var picGroup in scansByPicNumber)
                    {
                        var firstScan = picGroup.First();
                        var allContainersInGroup = picGroup.Select(c => c.Scan.ContainerNumber).Distinct().ToList();

                        string? rawXml = null;
                        try
                        {
                            var xmlFiles = Directory.GetFiles(firstScan.FolderPath, "*.xml");
                            if (xmlFiles.Any())
                                rawXml = await File.ReadAllTextAsync(xmlFiles.First());
                        }
                        catch (Exception xmlEx)
                        {
                            _logger.LogWarning(xmlEx, "[FS6000-INGESTION] Could not read raw XML for audit: {Folder}", firstScan.FolderPath);
                        }

                        var originalRecord = new OriginalScanRecord
                        {
                            ScannerType = "FS6000",
                            OriginalContainerNumbers = string.Join(", ", allContainersInGroup),
                            DerivedRecordCount = allContainersInGroup.Count,
                            PicNumber = picGroup.Key,
                            ScanTime = firstScan.Scan.ScanTime,
                            RawData = rawXml,
                            SourceFilePath = firstScan.FolderPath,
                            IngestedAt = DateTime.UtcNow
                        };

                        dbContext.OriginalScanRecords.Add(originalRecord);
                        await dbContext.SaveChangesAsync();

                        foreach (var item in picGroup)
                        {
                            item.Scan.OriginalScanRecordId = originalRecord.Id;
                        }
                    }

                    dbContext.FS6000Scans.AddRange(scansToAdd);
                    await dbContext.SaveChangesAsync();

                    // ✅ QUEUE ARCHITECTURE: Publish scans to completeness queue for processing
                    // This enables event-driven completeness checking instead of polling scanner tables
                    if (queuePublisher != null)
                    {
                        try
                        {
                            var queueItems = scansToAdd.Select(scan => new ContainerScanInfo
                            {
                                ContainerNumber = scan.ContainerNumber,
                                ScannerType = CommonScannerTypes.FS6000,
                                InspectionId = scan.Id.ToString(),
                                ScanDate = scan.ScanTime,
                                Priority = 0, // Normal priority for new scans
                                Metadata = $"{{ \"FilePath\": \"{scan.FilePath}\", \"PicNumber\": \"{scan.PicNumber}\" }}"
                            }).ToList();

                            var publishedCount = await queuePublisher.PublishScansBatchAsync(queueItems);
                            _logger.LogInformation("[FS6000-INGESTION] 📤 Published {PublishedCount} scans to completeness queue (from {TotalCount} new scans)",
                                publishedCount, scansToAdd.Count);
                        }
                        catch (Exception queueEx)
                        {
                            // ✅ CRITICAL: Queue publishing failures should NOT break scanner ingestion
                            // Log error but continue - scans are saved, queue publishing can retry later
                            _logger.LogWarning(queueEx, "[FS6000-INGESTION] ⚠️ Failed to publish scans to queue (non-critical - scans are saved)");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[FS6000-INGESTION] ⚠️ IContainerScanQueuePublisher not available - scans saved but not queued");
                    }

                    // PHASE 1: Update cache with newly added containers
                    foreach (var scan in scansToAdd)
                    {
                        _existingContainersCache.Add(scan.ContainerNumber);
                    }

                    _logger.LogInformation("[FS6000-INGESTION] ✅ Successfully processed {Count} new containers with FULL field mapping including fyco_present", scansToAdd.Count);

                    // Log sample of processed data to verify field extraction
                    var sampleScan = scansToAdd.First();
                    _logger.LogInformation("Sample processed scan - Container: {Container}, FycoPresent: {FycoPresent}, VesselName: {VesselName}, OperatorId: {OperatorId}, ScanResult: {ScanResult}",
                        sampleScan.ContainerNumber, sampleScan.FycoPresent, sampleScan.VesselName, sampleScan.OperatorId, sampleScan.ScanResult);

                    // NEW: Process and store associated JPEG images
                    await ProcessAndStoreImagesAsync(newContainerData, dbContext);
                }
                else
                {
                    _logger.LogDebug("All containers already exist in database - folders will still be moved to archive");
                }

                // ✅ FIX: Move ALL processed folders to archive, even if all containers already existed
                // This ensures folders don't accumulate in Staging folder
                var allContainerDataList = allContainerData.ToList();
                await MoveProcessedFoldersAsync(allContainerDataList);

                _logger.LogDebug("FIXED batch ingestion cycle completed with proper XML parsing");

                // FIX: Clear the parsed files cache at the end of each cycle
                lock (_parsedFilesLock)
                {
                    _logger.LogDebug("[FS6000-INGESTION] 🧹 Clearing parsed files cache: {Count} files were tracked this cycle", _parsedFilesThisCycle.Count);
                    _parsedFilesThisCycle.Clear();
                }

                // Return count of new containers processed (for metrics)
                return newContainerData?.Count ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FIXED batch ingestion cycle");

                // FIX: Clear cache even on error to prevent stale entries
                lock (_parsedFilesLock)
                {
                    _parsedFilesThisCycle.Clear();
                }

                return 0;
            }
        }

        // Legacy method for backward compatibility - now uses batch processing
        public async Task<int> ProcessAllFoldersAsync()
        {
            return await ProcessAllFoldersBatchAsync();
        }

        public async Task ProcessFolderAsync(string folderPath)
        {
            // This method is now deprecated in favor of batch processing
            // but kept for backward compatibility
            _logger.LogDebug("ProcessFolderAsync called - redirecting to batch processing");
            await ProcessAllFoldersBatchAsync();
        }

        private List<string> GetDataFolders()
        {
            var dataFolders = new List<string>();

            try
            {
                // Create destination path if it doesn't exist
                if (!Directory.Exists(_config.DestinationPath))
                {
                    _logger.LogInformation("Creating destination directory: {Path}", _config.DestinationPath);
                    Directory.CreateDirectory(_config.DestinationPath);
                }

                // Create processed path if it doesn't exist
                if (!Directory.Exists(_config.ProcessedPath))
                {
                    _logger.LogInformation("Creating processed directory: {Path}", _config.ProcessedPath);
                    Directory.CreateDirectory(_config.ProcessedPath);
                }

                var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
                var goLiveYear = goLiveDate > DateTime.MinValue ? goLiveDate.Year : _config.MinimumYear;
                var yearFolders = Directory.GetDirectories(_config.DestinationPath)
                    .Where(d =>
                    {
                        var yearName = Path.GetFileName(d);
                        return yearName.Length == 4 &&
                               int.TryParse(yearName, out var year) &&
                               year >= goLiveYear;
                    })
                    .OrderBy(d => d)
                    .ToList();

                foreach (var yearFolder in yearFolders)
                {
                    var monthDayFolders = Directory.GetDirectories(yearFolder)
                        .OrderBy(d => d)
                        .ToList();

                    foreach (var monthDayFolder in monthDayFolders)
                    {
                        var serialFolders = Directory.GetDirectories(monthDayFolder)
                            .OrderBy(d => d)
                            .ToList();

                        foreach (var serialFolder in serialFolders)
                        {
                            var hasXml = Directory.GetFiles(serialFolder, "*.xml").Any();
                            var hasJpeg = Directory.GetFiles(serialFolder, "*.jpg").Any() || Directory.GetFiles(serialFolder, "*.jpeg").Any();

                            if (hasXml && hasJpeg)
                            {
                                dataFolders.Add(serialFolder);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting data folders from {Path}", _config.DestinationPath);
            }

            return dataFolders;
        }

        private string? GetLargestJpegFile(List<string> jpegFiles)
        {
            if (!jpegFiles.Any())
                return null;

            return jpegFiles
                .OrderByDescending(f => new FileInfo(f).Length)
                .First();
        }

        /// <summary>
        /// Process and store JPEG images as binary data (base64 compatible) in the database
        /// </summary>
        private async Task ProcessAndStoreImagesAsync(List<(FS6000Scan Scan, string JpegFile, string FolderPath)> containerData, ApplicationDbContext dbContext)
        {
            try
            {
                _logger.LogInformation("Starting image processing for {Count} containers", containerData.Count);

                var imagesToAdd = new List<FS6000Image>();
                var processedCount = 0;
                var failedCount = 0;

                foreach (var (scan, jpegFile, folderPath) in containerData)
                {
                    try
                    {
                        // Check if file still exists
                        if (!File.Exists(jpegFile))
                        {
                            _logger.LogWarning("JPEG file not found: {FilePath}", jpegFile);
                            failedCount++;
                            continue;
                        }

                        // CRITICAL FIX: Read image with retry logic and FileShare.ReadWrite
                        // The FS6000 scanner or other processes may still have the file open
                        byte[] imageBytes = await ReadFileWithRetryAsync(jpegFile, scan.ContainerNumber);

                        // Determine image type based on filename
                        var imageType = GetImageTypeFromFileName(Path.GetFileName(jpegFile));

                        // Create FS6000Image entity
                        var image = new FS6000Image
                        {
                            ScanId = scan.Id,
                            ImageType = imageType,
                            FileName = Path.GetFileName(jpegFile),
                            ImageData = imageBytes, // Store as binary data (base64 compatible)
                            FileSizeBytes = imageBytes.Length,
                            CreatedAt = DateTime.UtcNow
                        };

                        imagesToAdd.Add(image);
                        processedCount++;

                        _logger.LogDebug("Prepared image for storage - Container: {Container}, File: {FileName}, Size: {Size} bytes, Type: {ImageType}",
                            scan.ContainerNumber, image.FileName, image.FileSizeBytes, image.ImageType);

                        // Also ingest raw .img channel files (high, low, material) if present
                        var imgChannelFiles = Directory.GetFiles(folderPath, "*.img");
                        foreach (var imgFile in imgChannelFiles)
                        {
                            try
                            {
                                var imgType = GetImageTypeFromFileName(Path.GetFileName(imgFile));
                                if (imgType is not ("HighEnergy" or "LowEnergy" or "Material"))
                                    continue;

                                byte[] imgBytes = await ReadFileWithRetryAsync(imgFile, scan.ContainerNumber);
                                var imgImage = new FS6000Image
                                {
                                    ScanId = scan.Id,
                                    ImageType = imgType,
                                    FileName = Path.GetFileName(imgFile),
                                    ImageData = imgBytes,
                                    FileSizeBytes = imgBytes.Length,
                                    CreatedAt = DateTime.UtcNow
                                };
                                imagesToAdd.Add(imgImage);
                                _logger.LogDebug("Prepared .img for storage - Container: {Container}, File: {FileName}, Size: {Size} bytes, Type: {ImageType}",
                                    scan.ContainerNumber, imgImage.FileName, imgImage.FileSizeBytes, imgImage.ImageType);
                            }
                            catch (Exception imgEx)
                            {
                                _logger.LogError(imgEx, "Error processing .img file for container {Container}: {FilePath}", scan.ContainerNumber, imgFile);
                                // Non-fatal: JPEG ingestion already succeeded
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing image for container {Container}: {FilePath}", scan.ContainerNumber, jpegFile);
                        failedCount++;
                    }
                }

                // Batch insert all images
                if (imagesToAdd.Any())
                {
                    dbContext.FS6000Images.AddRange(imagesToAdd);
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("Successfully stored {Count} images in database. Processed: {ProcessedCount}, Failed: {FailedCount}",
                        imagesToAdd.Count, processedCount, failedCount);

                    // Update scan image completeness flags
                    var scanImageCounts = imagesToAdd.GroupBy(i => i.ScanId)
                        .ToDictionary(g => g.Key, g => g.Count());

                    foreach (var (scan, _, _) in containerData)
                    {
                        if (scanImageCounts.TryGetValue(scan.Id, out var imageCount))
                        {
                            scan.HasImage = true;
                            scan.ImageCount = imageCount;
                            scan.ImageIngestedAt = DateTime.UtcNow;
                            scan.ImageValidationError = null;

                            _logger.LogDebug("[FS6000-IMAGE-VALIDATION] Updated scan {ContainerNumber}: HasImage=true, ImageCount={ImageCount}",
                                scan.ContainerNumber, imageCount);
                        }
                        else
                        {
                            scan.HasImage = false;
                            scan.ImageCount = 0;
                            scan.ImageValidationError = "Image processing failed";

                            _logger.LogWarning("[FS6000-IMAGE-VALIDATION] Scan {ContainerNumber} has no images after processing",
                                scan.ContainerNumber);
                        }
                    }

                    await dbContext.SaveChangesAsync();

                    // Log sample of stored image data
                    var sampleImage = imagesToAdd.First();
                    _logger.LogInformation("Sample stored image - Container: {Container}, File: {FileName}, Size: {Size} bytes, Type: {ImageType}",
                        containerData.First().Scan.ContainerNumber, sampleImage.FileName, sampleImage.FileSizeBytes, sampleImage.ImageType);
                }
                else
                {
                    _logger.LogWarning("No images were processed successfully");

                    // Mark all scans as having no images
                    foreach (var (scan, _, _) in containerData)
                    {
                        scan.HasImage = false;
                        scan.ImageCount = 0;
                        scan.ImageValidationError = "No images found or processing failed";
                    }

                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessAndStoreImagesAsync");
            }
        }

        /// <summary>
        /// Check if a file is stable (finished writing) by monitoring file size
        /// </summary>
        private async Task<bool> WaitForFileStabilityAsync(string filePath, int maxWaitMs = 5000)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                long previousSize = -1;
                int stableCount = 0;
                int checkIntervalMs = 200;
                int totalWaitMs = 0;

                while (totalWaitMs < maxWaitMs)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        long currentSize = fileInfo.Length;

                        if (currentSize == previousSize && currentSize > 0)
                        {
                            stableCount++;
                            // File size hasn't changed for 2 consecutive checks = stable
                            if (stableCount >= 2)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                            previousSize = currentSize;
                        }

                        await Task.Delay(checkIntervalMs);
                        totalWaitMs += checkIntervalMs;
                    }
                    catch (IOException)
                    {
                        // File locked, wait and try again
                        await Task.Delay(checkIntervalMs);
                        totalWaitMs += checkIntervalMs;
                    }
                }

                return false; // Timeout
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Read a file with retry logic to handle file locking issues
        /// </summary>
        private async Task<byte[]> ReadFileWithRetryAsync(string filePath, string containerNumber, int maxRetries = 5)
        {
            // STEP 1: Wait for file to be stable (finished writing)
            _logger.LogDebug("Waiting for file stability for container {Container}: {FilePath}", containerNumber, filePath);
            bool isStable = await WaitForFileStabilityAsync(filePath, maxWaitMs: 5000);

            if (!isStable)
            {
                _logger.LogWarning("File may still be writing for container {Container}: {FilePath}", containerNumber, filePath);
            }

            // STEP 2: Attempt to read with retries
            int retryCount = 0;
            int delayMs = 500; // Start with 500ms delay (increased from 100ms)

            while (retryCount < maxRetries)
            {
                try
                {
                    // Use FileShare.ReadWrite to allow other processes to read/write while we read
                    // This is critical for FS6000 scanner which may still be writing the file
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
                    {
                        var imageBytes = new byte[fileStream.Length];
                        await fileStream.ReadAsync(imageBytes, 0, (int)fileStream.Length);

                        if (retryCount > 0)
                        {
                            _logger.LogInformation("✅ Successfully read file for container {Container} after {RetryCount} retries: {FilePath}",
                                containerNumber, retryCount, filePath);
                        }

                        return imageBytes;
                    }
                }
                catch (IOException ex) when (retryCount < maxRetries - 1)
                {
                    retryCount++;
                    _logger.LogWarning("🔒 File locked for container {Container}, attempt {Attempt}/{MaxRetries}. Waiting {Delay}ms before retry: {FilePath}",
                        containerNumber, retryCount, maxRetries, delayMs, filePath);

                    await Task.Delay(delayMs);

                    // Exponential backoff - increased max delay from 2s to 5s
                    delayMs = Math.Min(delayMs * 2, 5000); // Cap at 5 seconds
                }
                catch (IOException ex) when (retryCount >= maxRetries - 1)
                {
                    // Final attempt failed, log and rethrow
                    _logger.LogError(ex, "❌ Failed to read file for container {Container} after {MaxRetries} attempts. File may still be locked by scanner software: {FilePath}",
                        containerNumber, maxRetries, filePath);
                    throw;
                }
            }

            throw new IOException($"Failed to read file after {maxRetries} attempts: {filePath}");
        }

        /// <summary>
        /// Determine image type based on filename
        /// </summary>
        private string GetImageTypeFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Unknown";

            // FS6000 raw channel files — classified by name suffix before extension
            var lowerName = fileName.ToLowerInvariant();
            if (lowerName.EndsWith("high.img")) return "HighEnergy";
            if (lowerName.EndsWith("low.img")) return "LowEnergy";
            if (lowerName.EndsWith("material.img")) return "Material";

            var extension = Path.GetExtension(lowerName);
            return extension switch
            {
                ".jpg" or ".jpeg" => "Main",
                ".png" => "Icon",
                ".bmp" => "CCR",
                ".tiff" => "LPR",
                ".pdf" => "Manifest",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Move successfully processed folders to archive directory
        /// </summary>
        private async Task MoveProcessedFoldersAsync(List<(FS6000Scan Scan, string JpegFile, string FolderPath)> containerData)
        {
            try
            {
                _logger.LogInformation("[FS6000-ARCHIVE] Moving {Count} processed folders to archive", containerData.Count);

                // Get unique folder paths
                var foldersToMove = containerData
                    .Select(c => c.FolderPath)
                    .Distinct()
                    .ToList();

                var movedCount = 0;
                var failedCount = 0;

                foreach (var sourceFolder in foldersToMove)
                {
                    try
                    {
                        // Extract relative path from source folder
                        // Example: C:\tadi_mirror\2025\1009\0001 -> 2025\1009\0001
                        var relativePath = sourceFolder.Replace(_config.DestinationPath, "").TrimStart('\\', '/');

                        // Build destination path in archive
                        var archivePath = Path.Combine(_config.ProcessedPath, relativePath);

                        // Create parent directory if it doesn't exist
                        var archiveParent = Path.GetDirectoryName(archivePath);
                        if (!string.IsNullOrEmpty(archiveParent))
                        {
                            Directory.CreateDirectory(archiveParent);
                        }

                        // Move the folder with retry logic for file lock issues
                        if (Directory.Exists(sourceFolder))
                        {
                            bool moved = false;
                            int retryCount = 0;
                            const int maxRetries = 3;

                            while (!moved && retryCount < maxRetries)
                            {
                                try
                                {
                                    if (Directory.Exists(archivePath))
                                    {
                                        // If archive folder exists, delete it first
                                        Directory.Delete(archivePath, true);
                                    }

                                    Directory.Move(sourceFolder, archivePath);
                                    moved = true;
                                    movedCount++;

                                    _logger.LogDebug("[FS6000-ARCHIVE] Moved folder: {SourceFolder} -> {ArchivePath}", sourceFolder, archivePath);
                                }
                                catch (IOException ex) when (retryCount < maxRetries - 1)
                                {
                                    retryCount++;
                                    _logger.LogWarning("[FS6000-ARCHIVE] File lock on folder {SourceFolder}, retry {Retry}/{MaxRetries} after delay",
                                        sourceFolder, retryCount, maxRetries);

                                    // Wait for file locks to release (exponential backoff)
                                    await Task.Delay(TimeSpan.FromSeconds(2 * retryCount));
                                }
                            }

                            if (!moved)
                            {
                                throw new IOException($"Failed to move folder after {maxRetries} attempts due to file locks");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[FS6000-ARCHIVE] Source folder not found: {SourceFolder}", sourceFolder);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[FS6000-ARCHIVE] Error moving folder: {SourceFolder}", sourceFolder);
                        failedCount++;
                    }
                }

                _logger.LogInformation("[FS6000-ARCHIVE] Archive completed: {MovedCount} folders moved, {FailedCount} failed", movedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-ARCHIVE] Error in MoveProcessedFoldersAsync");
            }
        }
    }
}
