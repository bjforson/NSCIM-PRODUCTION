using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class FileSyncService : IFileSyncService, IDisposable
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FileSyncService> _logger;
        private readonly FileSyncConfiguration _config;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task? _syncTask;
        private bool _disposed = false;

        private readonly int _goLiveYear;
        private readonly int _goLiveMonthDay;

        // Track the last processed folder to avoid re-scanning everything
        private string _lastProcessedFolder = "";

        public FileSyncService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FileSyncService> logger,
            IOptions<FileSyncConfiguration> config,
            IOptions<GoLiveOptions> goLiveOptions)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _config = config.Value;
            _cancellationTokenSource = new CancellationTokenSource();

            var goLiveDate = goLiveOptions?.Value?.EffectiveGoLiveDate ?? DateTime.MinValue;
            if (goLiveDate > DateTime.MinValue)
            {
                _goLiveYear = goLiveDate.Year;
                _goLiveMonthDay = goLiveDate.Month * 100 + goLiveDate.Day;
            }
            else
            {
                _goLiveYear = _config.MinimumYear;
                _goLiveMonthDay = _config.MinimumMonthDay;
            }
        }

        public async Task StartSyncAsync()
        {
            _logger.LogInformation("🚀 Starting File Sync Service for FS6000");

            try
            {
                // Validate configuration
                _config.Validate(_logger);

                // Validate source directory (Z:\)
                if (!await ValidateSourceDirectoryAsync())
                {
                    throw new InvalidOperationException($"Source directory is not accessible: {_config.SourcePath}");
                }

                // Create and validate destination directories
                CreateAndValidateDirectories();

                // Initialize the last processed folder from database
                await InitializeLastProcessedFolderAsync();

                // Start the background sync task
                _syncTask = Task.Run(async () => await SyncLoopAsync(_cancellationTokenSource.Token));

                _logger.LogInformation("✅ File Sync Service started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to start File Sync Service");
                throw;
            }
        }

        private async Task InitializeLastProcessedFolderAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Get the most recently processed folder from the database
                var lastProcessed = await context.FS6000SyncLogs
                    .Where(s => s.SyncStatus == "Completed")
                    .OrderByDescending(s => s.CompletedAt)
                    .Select(s => s.DestinationPath)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrEmpty(lastProcessed))
                {
                    // Extract the relative path from the full destination path
                    // Handle both old path (C:\Temp\FS6000\Processed\) and new path (C:\tadi_mirror\)
                    var relativePath = lastProcessed;

                    // Try to extract relative path by looking for the year folder pattern (2025/...)
                    var yearMatch = System.Text.RegularExpressions.Regex.Match(lastProcessed, @"(20\d{2}[/\\]\d{4}[/\\]\d{4})");
                    if (yearMatch.Success)
                    {
                        relativePath = yearMatch.Groups[1].Value.Replace('\\', '/');
                    }
                    else
                    {
                        // Fallback: try standard replacement
                        relativePath = lastProcessed.Replace(_config.DestinationPath, "").TrimStart('\\', '/').Replace('\\', '/');
                    }

                    _lastProcessedFolder = relativePath;
                    _logger.LogInformation("Initialized last processed folder: {LastProcessedFolder} (extracted from: {FullPath})", _lastProcessedFolder, lastProcessed);
                }
                else
                {
                    _lastProcessedFolder = $"{_goLiveYear}/{_goLiveMonthDay:D4}/0000";
                    _logger.LogInformation("No previous sync history found, starting from go-live cutoff: {LastProcessedFolder}", _lastProcessedFolder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing last processed folder, starting from go-live cutoff");
                _lastProcessedFolder = $"{_goLiveYear}/{_goLiveMonthDay:D4}/0000";
            }
        }

        public async Task StopSyncAsync()
        {
            _logger.LogInformation("Stopping File Sync Service");
            _cancellationTokenSource.Cancel();

            if (_syncTask != null)
            {
                await _syncTask;
            }

            _logger.LogInformation("File Sync Service stopped");
        }

        private async Task SyncLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File sync loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting intelligent file sync cycle - only checking for new folders since: {LastProcessedFolder}", _lastProcessedFolder);
                    await SyncFilesAsync();
                    _logger.LogDebug("File sync cycle completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during file sync cycle");
                }

                // Wait for the next sync interval (read from database settings)
                try
                {
                    // Read sync interval from database settings (default: 5 minutes)
                    TimeSpan syncInterval;
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                        var syncIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "FS6000.SyncIntervalMinutes", 5);
                        syncInterval = TimeSpan.FromMinutes(syncIntervalMinutes);

                        _logger.LogDebug("⏰ Next sync in {Interval} minutes (from settings)", syncIntervalMinutes);
                    }
                    await Task.Delay(syncInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("File sync loop ended");
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Check if source directory is accessible
                if (!Directory.Exists(_config.SourcePath))
                {
                    _logger.LogWarning("Source directory does not exist: {SourcePath}", _config.SourcePath);
                    return false;
                }

                // Check if destination directory is accessible
                if (!Directory.Exists(_config.DestinationPath))
                {
                    _logger.LogWarning("Destination directory does not exist: {DestinationPath}", _config.DestinationPath);
                    return false;
                }

                // Check database connectivity
                await context.Database.CanConnectAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed for File Sync Service");
                return false;
            }
        }

        public async Task<int> GetPendingSyncCountAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return await context.FS6000SyncLogs
                .CountAsync(s => s.SyncStatus == "Pending" || s.SyncStatus == "Processing");
        }

        public async Task<int> GetFailedSyncCountAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return await context.FS6000SyncLogs
                .CountAsync(s => s.SyncStatus == "Failed");
        }

        private async Task SyncFilesAsync()
        {
            try
            {
                _logger.LogInformation("Starting intelligent file sync - only checking for new folders since: {LastProcessedFolder}", _lastProcessedFolder);

                // Parse the last processed folder to get year, month-day, and serial
                var lastProcessedParts = _lastProcessedFolder.Split('/');
                if (lastProcessedParts.Length != 3)
                {
                    _logger.LogWarning("Invalid last processed folder format: {LastProcessedFolder}, resetting to go-live cutoff {MinYear}/{MinMonthDay}/0000",
                        _lastProcessedFolder, _goLiveYear, _goLiveMonthDay.ToString("D4"));
                    _lastProcessedFolder = $"{_goLiveYear}/{_goLiveMonthDay:D4}/0000";
                    lastProcessedParts = _lastProcessedFolder.Split('/');
                }

                var lastYear = int.Parse(lastProcessedParts[0]);
                var lastMonthDay = int.Parse(lastProcessedParts[1]);
                var lastSerial = int.Parse(lastProcessedParts[2]);

                var newFoldersFound = 0;
                var foldersProcessed = 0;

                // Get year folders from go-live year onwards
                var yearFolders = Directory.GetDirectories(_config.SourcePath)
                    .Where(d =>
                    {
                        var yearName = Path.GetFileName(d);
                        return yearName.Length == 4 &&
                               int.TryParse(yearName, out var year) &&
                               year >= _goLiveYear;
                    })
                    .OrderBy(d => d)
                    .ToList();

                _logger.LogDebug("Found {YearCount} year folders ({MinYear}+): {Years}",
                    yearFolders.Count, _goLiveYear, string.Join(", ", yearFolders.Select(f => Path.GetFileName(f))));

                foreach (var yearFolder in yearFolders)
                {
                    var year = Path.GetFileName(yearFolder);
                    var yearInt = int.Parse(year);

                    // Skip years before our last processed year
                    if (yearInt < lastYear)
                    {
                        _logger.LogDebug("Skipping year {Year} (before last processed year {LastYear})", year, lastYear);
                        continue;
                    }

                    _logger.LogDebug("Processing year folder: {Year}", year);

                    // Get month-day folders — for the go-live year, only from go-live day onwards
                    var monthDayFolders = Directory.GetDirectories(yearFolder)
                        .Where(d =>
                        {
                            var monthDay = Path.GetFileName(d);
                            if (monthDay.Length != 4 || !int.TryParse(monthDay, out var monthDayInt))
                                return false;

                            if (yearInt == _goLiveYear)
                            {
                                return monthDayInt >= _goLiveMonthDay;
                            }

                            return true;
                        })
                        .OrderBy(d => d)
                        .ToList();

                    foreach (var monthDayFolder in monthDayFolders)
                    {
                        var monthDay = Path.GetFileName(monthDayFolder);
                        var monthDayInt = int.Parse(monthDay);

                        // Skip month-day folders before our last processed one (for the same year)
                        if (yearInt == lastYear && monthDayInt < lastMonthDay)
                        {
                            _logger.LogDebug("Skipping month-day {MonthDay} (before last processed {LastMonthDay})", monthDay, lastMonthDay);
                            continue;
                        }

                        _logger.LogDebug("Processing month-day folder: {MonthDay}", monthDay);

                        // Get all serial number folders (0001, 0002, etc.)
                        var serialFolders = Directory.GetDirectories(monthDayFolder)
                            .Where(d => Path.GetFileName(d).Length == 4 && int.TryParse(Path.GetFileName(d), out _))
                            .OrderBy(d => d)
                            .ToList();

                        _logger.LogDebug("Found {SerialCount} serial folders in {Year}/{MonthDay}", serialFolders.Count, year, monthDay);

                        foreach (var serialFolder in serialFolders)
                        {
                            var serial = Path.GetFileName(serialFolder);
                            var serialInt = int.Parse(serial);

                            // Skip serial folders before our last processed one (for the same year and month-day)
                            if (yearInt == lastYear && monthDayInt == lastMonthDay && serialInt <= lastSerial)
                            {
                                _logger.LogDebug("Skipping serial {Serial} (already processed or before last processed {LastSerial})", serial, lastSerial);
                                continue;
                            }

                            newFoldersFound++;
                            _logger.LogDebug("Processing serial folder: {Year}/{MonthDay}/{Serial}", year, monthDay, serial);

                            var success = await ProcessSerialFolderAsync(serialFolder, year, monthDay, serial);
                            if (success)
                            {
                                foldersProcessed++;
                                // Update our last processed folder
                                _lastProcessedFolder = $"{year}/{monthDay}/{serial}";
                            }
                        }
                    }
                }

                if (newFoldersFound == 0)
                {
                    _logger.LogInformation("No new folders found since last sync. Last processed: {LastProcessedFolder}", _lastProcessedFolder);
                }
                else
                {
                    _logger.LogInformation("Found {NewFoldersFound} new folders, successfully processed {FoldersProcessed}. Last processed: {LastProcessedFolder}",
                        newFoldersFound, foldersProcessed, _lastProcessedFolder);
                }

                _logger.LogDebug("Intelligent file sync cycle completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during intelligent file sync cycle");
            }
        }

        private async Task<bool> ProcessSerialFolderAsync(string sourceFolder, string year, string monthDay, string serial)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var relativePath = $"{year}/{monthDay}/{serial}";
                var destinationFolder = Path.Combine(_config.DestinationPath, relativePath);
                var processedFolder = Path.Combine(_config.ProcessedPath, relativePath);

                _logger.LogDebug("Processing folder: {SourceFolder} -> {DestinationFolder}", sourceFolder, destinationFolder);

                // Check if already processed
                if (Directory.Exists(processedFolder))
                {
                    _logger.LogDebug("Folder already processed: {RelativePath}", relativePath);
                    return true; // Consider this successful since it's already done
                }

                // Check if already synced but not yet processed
                // FIXED: Check if folder has files, not just if it exists
                if (Directory.Exists(destinationFolder))
                {
                    var existingFiles = Directory.GetFiles(destinationFolder);
                    if (existingFiles.Length > 0)
                    {
                        _logger.LogDebug("Folder already synced with {FileCount} files: {RelativePath}", existingFiles.Length, relativePath);
                        return true; // Consider this successful since it's already synced with files
                    }
                    else
                    {
                        _logger.LogDebug("Folder exists but is empty, will re-sync: {RelativePath}", relativePath);
                        // Continue to sync files
                    }
                }

                // Create destination directory
                Directory.CreateDirectory(destinationFolder);
                _logger.LogDebug("Created destination directory: {DestinationFolder}", destinationFolder);

                // Find XML, JPEG, and raw .img files
                var xmlFiles = Directory.GetFiles(sourceFolder, "*.xml");
                var jpegFiles = Directory.GetFiles(sourceFolder, "*.jpg");
                var imgFiles = Directory.GetFiles(sourceFolder, "*.img");

                if (xmlFiles.Length == 0)
                {
                    _logger.LogWarning("No XML file found in folder: {SourceFolder}", sourceFolder);
                    return false;
                }

                if (jpegFiles.Length == 0)
                {
                    _logger.LogWarning("No JPEG file found in folder: {SourceFolder}", sourceFolder);
                    return false;
                }

                if (imgFiles.Length == 0)
                {
                    _logger.LogWarning("No .img files found in folder: {SourceFolder} — raw channel data will not be available", sourceFolder);
                }

                // Find the largest JPEG file
                var largestJpeg = jpegFiles
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .First();

                var xmlFile = xmlFiles[0]; // Take the first XML file

                _logger.LogDebug("Found XML: {XmlFile}, JPEG: {JpegFile}, .img files: {ImgCount}", xmlFile, largestJpeg, imgFiles.Length);

                // ✅ IMPROVEMENT 1: Wait for file stability before copying
                // The scanner may still be writing files, so wait for them to stabilize
                _logger.LogDebug("Waiting for file stability before copying...");
                var xmlStable = await WaitForFileStabilityAsync(xmlFile, maxWaitMs: 5000);
                var jpegStable = await WaitForFileStabilityAsync(largestJpeg, maxWaitMs: 5000);

                if (!xmlStable)
                {
                    _logger.LogWarning("XML file may still be writing: {XmlFile}", xmlFile);
                }
                if (!jpegStable)
                {
                    _logger.LogWarning("JPEG file may still be writing: {JpegFile}", largestJpeg);
                }

                // Wait for .img file stability
                foreach (var imgFile in imgFiles)
                {
                    var imgStable = await WaitForFileStabilityAsync(imgFile, maxWaitMs: 5000);
                    if (!imgStable)
                        _logger.LogWarning(".img file may still be writing: {ImgFile}", imgFile);
                }

                // Copy XML file with retry (now uses FileShare.ReadWrite)
                var xmlFileName = Path.GetFileName(xmlFile);
                var xmlDestination = Path.Combine(destinationFolder, xmlFileName);
                await CopyFileWithRetryAsync(xmlFile, xmlDestination);
                _logger.LogDebug("Copied XML file: {XmlFileName}", xmlFileName);

                // Copy JPEG file with retry (now uses FileShare.ReadWrite)
                var jpegFileName = Path.GetFileName(largestJpeg);
                var jpegDestination = Path.Combine(destinationFolder, jpegFileName);
                await CopyFileWithRetryAsync(largestJpeg, jpegDestination);
                _logger.LogDebug("Copied JPEG file: {JpegFileName}", jpegFileName);

                // Copy raw .img channel files (high, low, material)
                foreach (var imgFile in imgFiles)
                {
                    var imgFileName = Path.GetFileName(imgFile);
                    var imgDestination = Path.Combine(destinationFolder, imgFileName);
                    await CopyFileWithRetryAsync(imgFile, imgDestination);
                    _logger.LogDebug("Copied .img file: {ImgFileName}", imgFileName);
                }
                if (imgFiles.Length > 0)
                    _logger.LogInformation("Copied {Count} .img channel file(s) to staging", imgFiles.Length);

                // ✅ IMPROVEMENT 3: Validate copied files exist and have content before marking as Completed
                var copiedFiles = Directory.GetFiles(destinationFolder);
                if (copiedFiles.Length < 2)
                {
                    throw new InvalidOperationException($"File copy incomplete: Expected 2 files (XML + JPG), found {copiedFiles.Length} files in {destinationFolder}");
                }

                // Verify files have content (not empty)
                foreach (var copiedFile in copiedFiles)
                {
                    var fileInfo = new FileInfo(copiedFile);
                    if (fileInfo.Length == 0)
                    {
                        throw new InvalidOperationException($"Copied file is empty: {copiedFile}");
                    }
                }

                _logger.LogInformation("✅ Validated {FileCount} copied files with content", copiedFiles.Length);

                // Log successful sync
                var syncLog = new FS6000SyncLog
                {
                    SourcePath = sourceFolder,
                    DestinationPath = destinationFolder,
                    SyncStatus = "Completed",
                    CompletedAt = DateTime.UtcNow
                };

                context.FS6000SyncLogs.Add(syncLog);
                await context.SaveChangesAsync();

                _logger.LogInformation("Successfully synced folder: {RelativePath}", relativePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing serial folder: {SourceFolder}", sourceFolder);

                // Log failed sync
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var relativePath = $"{year}/{monthDay}/{serial}";
                    var destinationFolder = Path.Combine(_config.DestinationPath, relativePath);
                    var syncLog = new FS6000SyncLog
                    {
                        SourcePath = sourceFolder,
                        DestinationPath = destinationFolder,
                        SyncStatus = "Failed",
                        ErrorMessage = ex.Message,
                        CompletedAt = DateTime.UtcNow
                    };

                    context.FS6000SyncLogs.Add(syncLog);
                    await context.SaveChangesAsync();
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Error logging failed sync for folder: {SourceFolder}", sourceFolder);
                }

                return false;
            }
        }

        /// <summary>
        /// ✅ IMPROVEMENT 1: Check if a file is stable (finished writing) by monitoring file size
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
        /// ✅ IMPROVEMENT 2: Copy file with retry logic using FileShare.ReadWrite to handle concurrent access.
        /// ✅ IMPROVEMENT 3 (2026-04-19): Also retry on transient network errors (UNC share briefly unavailable).
        /// </summary>
        private async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 2)); // Start at 100ms, then 200ms, 400ms
                        _logger.LogDebug("Retrying file copy in {Delay}ms (attempt {Attempt}/{MaxRetries})", delay.TotalMilliseconds, attempt, maxRetries);
                        await Task.Delay(delay);
                    }

                    // ✅ IMPROVEMENT 2: Use FileShare.ReadWrite to allow concurrent access
                    // This allows the scanner to continue writing while we read/copy
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
                    {
                        using (var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                        }
                    }

                    _logger.LogDebug("Successfully copied file: {SourcePath} -> {DestinationPath}", sourcePath, destinationPath);
                    return;
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process") || ex.HResult == -2147024864) // 0x80070020 ERROR_SHARING_VIOLATION
                {
                    _logger.LogWarning("🔒 File is being used by another process (attempt {Attempt}/{MaxRetries}): {SourcePath}", attempt, maxRetries, sourcePath);
                    if (attempt == maxRetries)
                    {
                        throw new IOException($"Failed to copy file after {maxRetries} attempts: {sourcePath}", ex);
                    }
                }
                // NEW: Transient network errors on UNC shares (network path not found, name no longer available, etc.)
                catch (IOException ex) when (IsTransientNetworkError(ex))
                {
                    // Longer backoff for network errors (likely share momentarily unreachable)
                    var netDelay = TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1)); // 2s, 4s, 8s
                    _logger.LogWarning("🌐 Transient network error (attempt {Attempt}/{MaxRetries}) copying {SourcePath}: {Msg}. Waiting {Delay}s before retry",
                        attempt, maxRetries, sourcePath, ex.Message, netDelay.TotalSeconds);
                    if (attempt == maxRetries)
                    {
                        throw new IOException($"Failed to copy file after {maxRetries} network retries: {sourcePath}", ex);
                    }
                    await Task.Delay(netDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error copying file (attempt {Attempt}/{MaxRetries}): {SourcePath}", attempt, maxRetries, sourcePath);
                    throw;
                }
            }
        }

        /// <summary>
        /// Detect transient network errors that are worth retrying against a UNC share.
        /// Common on Windows when \\host\share is briefly unreachable (e.g. scanner reboot, VPN blip).
        /// </summary>
        private static bool IsTransientNetworkError(IOException ex)
        {
            var msg = ex.Message ?? string.Empty;
            // Well-known Win32 / .NET messages for transient network issues
            if (msg.Contains("network path was not found", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("network name is no longer available", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("network name cannot be found", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("did not respond", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("remote system is not available", StringComparison.OrdinalIgnoreCase)) return true;
            // HRESULTs: 0x80070035 (path not found), 0x80070040 (network name deleted), 0x8007003B (unexpected error on network)
            switch (unchecked((uint)ex.HResult))
            {
                case 0x80070035U:
                case 0x80070040U:
                case 0x8007003BU:
                case 0x80070043U: // network name cannot be found
                    return true;
            }
            return ex.InnerException is System.Net.Sockets.SocketException;
        }

        private async Task<bool> ValidateSourceDirectoryAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Validating source directory: {SourcePath} (with 30s timeout)", _config.SourcePath);

                // H12: Use Task.Run with timeout to prevent indefinite hangs on network drives.
                // Was: checkTask.Wait(timeout) + .Result — sync-over-async, blocks a thread-pool
                // thread for up to 30s. Now: await checkTask.WaitAsync(timeout) — non-blocking.
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var checkTask = Task.Run(() =>
                {
                    try
                    {
                        // Check if directory exists
                        if (!Directory.Exists(_config.SourcePath))
                        {
                            _logger.LogError("❌ Source directory does not exist: {SourcePath}", _config.SourcePath);
                            return false;
                        }

                        // Check if we can read from directory
                        try
                        {
                            var testRead = Directory.GetDirectories(_config.SourcePath);
                            _logger.LogInformation("✅ Source directory is accessible with {Count} subdirectories", testRead.Length);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogError(ex, "❌ No read permission for source directory: {SourcePath}", _config.SourcePath);
                            return false;
                        }

                        // Check for expected folder structure
                        var yearFolders = Directory.GetDirectories(_config.SourcePath)
                            .Where(d =>
                            {
                                var yearName = Path.GetFileName(d);
                                return yearName.Length == 4 && int.TryParse(yearName, out var year) && year >= _goLiveYear;
                            })
                            .ToList();

                        if (!yearFolders.Any())
                        {
                            _logger.LogWarning("⚠️ No year folders ({MinYear}+) found in source directory: {SourcePath}", _goLiveYear, _config.SourcePath);
                            _logger.LogWarning("⚠️ Expected folder structure: {SourcePath}\\{MinYear}\\{MinMonthDay}\\0001\\", _config.SourcePath, _goLiveYear, _goLiveMonthDay.ToString("0000"));
                        }
                        else
                        {
                            _logger.LogInformation("✅ Found {Count} year folders: {Years}",
                                yearFolders.Count,
                                string.Join(", ", yearFolders.Select(f => Path.GetFileName(f))));
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error accessing source directory");
                        return false;
                    }
                }, cts.Token);

                // Wait for the task with timeout — async, no thread blocking.
                try
                {
                    return await checkTask.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogError("❌ Source directory check TIMED OUT after 30 seconds");
                    _logger.LogError("❌ Z:\\ drive is too slow or unresponsive");
                    _logger.LogError("❌ This may indicate network latency issues");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("❌ Source directory check was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error validating source directory: {SourcePath}", _config.SourcePath);
                return false;
            }
        }

        private void CreateAndValidateDirectories()
        {
            _logger.LogInformation("📁 Creating and validating directories...");

            // Destination directory
            try
            {
                if (!Directory.Exists(_config.DestinationPath))
                {
                    Directory.CreateDirectory(_config.DestinationPath);
                    _logger.LogInformation("✅ Created destination directory: {DestinationPath}", _config.DestinationPath);
                }
                else
                {
                    _logger.LogInformation("✅ Destination directory exists: {DestinationPath}", _config.DestinationPath);
                }

                // Test write access
                var testFile = Path.Combine(_config.DestinationPath, $"test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                _logger.LogInformation("✅ Destination directory is writable");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create or access destination directory: {DestinationPath}", _config.DestinationPath);
                throw;
            }

            // Processed directory
            try
            {
                if (!Directory.Exists(_config.ProcessedPath))
                {
                    Directory.CreateDirectory(_config.ProcessedPath);
                    _logger.LogInformation("✅ Created processed directory: {ProcessedPath}", _config.ProcessedPath);
                }
                else
                {
                    _logger.LogInformation("✅ Processed directory exists: {ProcessedPath}", _config.ProcessedPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create processed directory: {ProcessedPath}", _config.ProcessedPath);
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}
