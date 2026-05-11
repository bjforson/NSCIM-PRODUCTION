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
                var yearFolders = (await GetDirectoriesWithRetryAsync(_config.SourcePath, "source year folders"))
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
                    var monthDayFolders = (await GetDirectoriesWithRetryAsync(yearFolder, "source month-day folders"))
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
                        var serialFolders = (await GetDirectoriesWithRetryAsync(monthDayFolder, "source serial folders"))
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

                // Round-1 audit H-10: path-traversal guard. The {year}/{monthDay}/{serial}
                // segments come from FS6000 scanner XML and could in theory contain
                // ".." or absolute-path tokens that escape DestinationPath/ProcessedPath.
                // Reject any segment that is empty, contains a separator, or contains "..".
                if (!IsSafePathSegment(year) || !IsSafePathSegment(monthDay) || !IsSafePathSegment(serial))
                {
                    _logger.LogWarning(
                        "Rejecting unsafe FS6000 path segments year={Year} monthDay={MonthDay} serial={Serial}",
                        year, monthDay, serial);
                    return false;
                }

                var relativePath = $"{year}/{monthDay}/{serial}";
                var destinationFolder = Path.Combine(_config.DestinationPath, relativePath);
                var processedFolder = Path.Combine(_config.ProcessedPath, relativePath);

                // Belt-and-suspenders: confirm the resolved absolute paths still sit
                // inside the configured roots. Path.GetFullPath canonicalises ".."
                // sequences; if either resolved path escapes its base we abort.
                var destRoot = EnsureTrailingDirectorySeparator(_config.DestinationPath);
                var procRoot = EnsureTrailingDirectorySeparator(_config.ProcessedPath);
                var destResolved = Path.GetFullPath(destinationFolder);
                var procResolved = Path.GetFullPath(processedFolder);
                if (!IsPathInsideRoot(destResolved, destRoot)
                    || !IsPathInsideRoot(procResolved, procRoot))
                {
                    _logger.LogWarning(
                        "Rejecting FS6000 path that escapes its root: dest={Dest} proc={Proc}",
                        destResolved, procResolved);
                    return false;
                }

                _logger.LogDebug("Processing folder: {SourceFolder} -> {DestinationFolder}", sourceFolder, destinationFolder);

                // Check if already processed
                if (Directory.Exists(processedFolder))
                {
                    _logger.LogDebug("Folder already processed: {RelativePath}", relativePath);
                    return true; // Consider this successful since it's already done
                }

                // Find XML, JPEG, and raw .img files
                var xmlFiles = await GetFilesWithRetryAsync(sourceFolder, "*.xml", "source XML files");
                var jpegFiles = await GetFilesWithRetryAsync(sourceFolder, "*.jpg", "source JPEG files");
                var imgFiles = await GetFilesWithRetryAsync(sourceFolder, "*.img", "source raw channel files");

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
                var expectedCopies = new List<(string SourcePath, string DestinationPath)>
                {
                    (xmlFile, Path.Combine(destinationFolder, Path.GetFileName(xmlFile))),
                    (largestJpeg, Path.Combine(destinationFolder, Path.GetFileName(largestJpeg)))
                };

                expectedCopies.AddRange(imgFiles.Select(imgFile =>
                    (imgFile, Path.Combine(destinationFolder, Path.GetFileName(imgFile)))));

                _logger.LogDebug("Found XML: {XmlFile}, JPEG: {JpegFile}, .img files: {ImgCount}", xmlFile, largestJpeg, imgFiles.Length);

                _logger.LogDebug("Waiting for file stability before copying...");
                var unstableFiles = new List<string>();

                foreach (var (sourcePath, _) in expectedCopies)
                {
                    if (!await WaitForFileStabilityAsync(sourcePath, maxWaitMs: 15000))
                    {
                        unstableFiles.Add(sourcePath);
                    }
                }

                if (unstableFiles.Count > 0)
                {
                    _logger.LogInformation(
                        "Deferring FS6000 sync for {RelativePath}; {Count} source file(s) were not stable after bounded wait: {Files}",
                        relativePath,
                        unstableFiles.Count,
                        string.Join(", ", unstableFiles.Select(Path.GetFileName)));
                    return false;
                }

                if (Directory.Exists(destinationFolder))
                {
                    if (ExpectedCopiesAlreadyPresent(expectedCopies))
                    {
                        _logger.LogDebug("Folder already synced with complete files: {RelativePath}", relativePath);
                        return true;
                    }

                    _logger.LogWarning("Folder exists but staged files are incomplete or stale, will re-sync: {RelativePath}", relativePath);
                }

                Directory.CreateDirectory(destinationFolder);
                _logger.LogDebug("Ensured destination directory exists: {DestinationFolder}", destinationFolder);

                // Copy XML file with retry
                var xmlFileName = Path.GetFileName(xmlFile);
                await CopyFileWithRetryAsync(xmlFile, Path.Combine(destinationFolder, xmlFileName));
                _logger.LogDebug("Copied XML file: {XmlFileName}", xmlFileName);

                // Copy JPEG file with retry
                var jpegFileName = Path.GetFileName(largestJpeg);
                await CopyFileWithRetryAsync(largestJpeg, Path.Combine(destinationFolder, jpegFileName));
                _logger.LogDebug("Copied JPEG file: {JpegFileName}", jpegFileName);

                // Copy raw .img channel files (high, low, material)
                foreach (var imgFile in imgFiles)
                {
                    var imgFileName = Path.GetFileName(imgFile);
                    await CopyFileWithRetryAsync(imgFile, Path.Combine(destinationFolder, imgFileName));
                    _logger.LogDebug("Copied .img file: {ImgFileName}", imgFileName);
                }
                if (imgFiles.Length > 0)
                    _logger.LogInformation("Copied {Count} .img channel file(s) to staging", imgFiles.Length);

                // ✅ IMPROVEMENT 3: Validate copied files exist and have content before marking as Completed
                foreach (var (sourcePath, copiedFile) in expectedCopies)
                {
                    if (!File.Exists(copiedFile))
                    {
                        throw new InvalidOperationException($"File copy incomplete: Expected copied file is missing: {copiedFile}");
                    }

                    var sourceInfo = new FileInfo(sourcePath);
                    var copiedInfo = new FileInfo(copiedFile);
                    if (copiedInfo.Length == 0 || copiedInfo.Length != sourceInfo.Length)
                    {
                        throw new InvalidOperationException($"Copied file length mismatch: {copiedFile} ({copiedInfo.Length} bytes) vs {sourcePath} ({sourceInfo.Length} bytes)");
                    }
                }

                _logger.LogInformation("✅ Validated {FileCount} copied files with source-length match", expectedCopies.Count);

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
        /// Check if a file is stable (finished writing) by monitoring size,
        /// last-write time, and whether a writer still holds the file open.
        /// </summary>
        private async Task<bool> WaitForFileStabilityAsync(string filePath, int maxWaitMs = 15000)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                long previousSize = -1;
                DateTime previousLastWriteUtc = DateTime.MinValue;
                int stableCount = 0;
                const int requiredStableChecks = 4;
                const int checkIntervalMs = 500;
                int totalWaitMs = 0;

                while (totalWaitMs < maxWaitMs)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        long currentSize = fileInfo.Length;
                        var currentLastWriteUtc = fileInfo.LastWriteTimeUtc;

                        if (currentSize == previousSize
                            && currentLastWriteUtc == previousLastWriteUtc
                            && currentSize > 0
                            && CanOpenForStableRead(filePath))
                        {
                            stableCount++;
                            if (stableCount >= requiredStableChecks)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                            previousSize = currentSize;
                            previousLastWriteUtc = currentLastWriteUtc;
                        }

                        await Task.Delay(checkIntervalMs);
                        totalWaitMs += checkIntervalMs;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        stableCount = 0;
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
        /// Copy file with bounded retry. The file is copied to a temp file first,
        /// then promoted only if the source did not change during the copy.
        /// </summary>
        private async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 4)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var tempDestinationPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    var sourceBefore = new FileInfo(sourcePath);
                    if (!sourceBefore.Exists || sourceBefore.Length <= 0)
                    {
                        throw new IOException($"Source file is missing or empty: {sourcePath}");
                    }

                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
                    {
                        using (var destinationStream = new FileStream(tempDestinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                            await destinationStream.FlushAsync();
                        }
                    }

                    var sourceAfter = new FileInfo(sourcePath);
                    var tempFileInfo = new FileInfo(tempDestinationPath);
                    if (sourceBefore.Length != sourceAfter.Length
                        || sourceBefore.LastWriteTimeUtc != sourceAfter.LastWriteTimeUtc
                        || tempFileInfo.Length != sourceAfter.Length)
                    {
                        throw new IOException($"Source changed while copying: {sourcePath}");
                    }

                    File.Move(tempDestinationPath, destinationPath, overwrite: true);
                    _logger.LogDebug("Successfully copied file: {SourcePath} -> {DestinationPath}", sourcePath, destinationPath);
                    return;
                }
                catch (Exception ex) when (IsRetryableCopyException(ex) && attempt < maxRetries)
                {
                    TryDeleteFile(tempDestinationPath);
                    var delay = GetRetryDelay(ex, attempt);
                    _logger.LogWarning(
                        ex,
                        "Retryable file copy error (attempt {Attempt}/{MaxRetries}) copying {SourcePath}. Waiting {DelayMs}ms before retry",
                        attempt,
                        maxRetries,
                        sourcePath,
                        delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    TryDeleteFile(tempDestinationPath);
                    _logger.LogError(ex, "Unexpected error copying file (attempt {Attempt}/{MaxRetries}): {SourcePath}", attempt, maxRetries, sourcePath);
                    throw;
                }
            }
        }

        private async Task<string[]> GetDirectoriesWithRetryAsync(string path, string operationName)
        {
            return await RunFileSystemOperationWithRetryAsync(() => Directory.GetDirectories(path), operationName, path);
        }

        private async Task<string[]> GetFilesWithRetryAsync(string path, string searchPattern, string operationName)
        {
            return await RunFileSystemOperationWithRetryAsync(() => Directory.GetFiles(path, searchPattern), operationName, path);
        }

        private async Task<T> RunFileSystemOperationWithRetryAsync<T>(Func<T> operation, string operationName, string path, int maxRetries = 4)
        {
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (IOException ex) when (IsTransientNetworkError(ex) && attempt < maxRetries)
                {
                    var delay = GetRetryDelay(ex, attempt);
                    _logger.LogWarning(
                        ex,
                        "Transient file-system error during {OperationName} for {Path} (attempt {Attempt}/{MaxRetries}). Waiting {DelayMs}ms before retry",
                        operationName,
                        path,
                        attempt,
                        maxRetries,
                        delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
            }

            return operation();
        }

        private static bool ExpectedCopiesAlreadyPresent(IEnumerable<(string SourcePath, string DestinationPath)> expectedCopies)
        {
            foreach (var (sourcePath, destinationPath) in expectedCopies)
            {
                if (!File.Exists(destinationPath))
                {
                    return false;
                }

                var sourceInfo = new FileInfo(sourcePath);
                var destinationInfo = new FileInfo(destinationPath);
                if (sourceInfo.Length <= 0 || destinationInfo.Length != sourceInfo.Length)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanOpenForStableRead(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length > 0;
        }

        private static bool IsRetryableCopyException(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
            {
                return true;
            }

            return ex is IOException ioEx
                && (IsTransientNetworkError(ioEx)
                    || ioEx.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                    || unchecked((uint)ioEx.HResult) == 0x80070020U
                    || ioEx.Message.Contains("Source changed while copying", StringComparison.OrdinalIgnoreCase));
        }

        private static TimeSpan GetRetryDelay(Exception ex, int attempt)
        {
            if (ex is IOException ioEx && IsTransientNetworkError(ioEx))
            {
                return TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, attempt)));
            }

            return TimeSpan.FromMilliseconds(Math.Min(2000, 500 * Math.Pow(2, attempt - 1)));
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup for temp copy files.
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
            if (msg.Contains("could not find a part of the path", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("did not respond", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("remote system is not available", StringComparison.OrdinalIgnoreCase)) return true;
            // HRESULTs: 0x80070035 (path not found), 0x80070040 (network name deleted), 0x8007003B (unexpected error on network)
            switch (unchecked((uint)ex.HResult))
            {
                case 0x80070003U:
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

        /// <summary>
        /// Validate that a single path segment from scanner metadata is safe to
        /// drop into <see cref="Path.Combine"/> without enabling traversal:
        /// non-empty, no path separators, no "..".  Used by
        /// <see cref="ProcessSerialFolderAsync"/> as a first-line filter.
        /// </summary>
        private static bool IsSafePathSegment(string? segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return false;
            if (segment.Contains("..", StringComparison.Ordinal)) return false;
            if (segment.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return false;
            // Reserved Windows device names + control chars get caught by Path.GetFullPath
            // when we resolve the combined path against the root anyway.
            return true;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return Path.EndsInDirectorySeparator(fullPath)
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;
        }

        private static bool IsPathInsideRoot(string path, string rootWithTrailingSeparator)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(rootWithTrailingSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }
}
