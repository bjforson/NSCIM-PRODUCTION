using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Logging;
using NickScanCentralImagingPortal.Services.Settings;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Background service that archives successfully processed ICUMS files
    /// Archive Solution: Compresses and moves files to organized archive structure
    /// </summary>
    public class IcumFileArchiveService : BackgroundService
    {
        private const string SERVICE_ID = "IcumFileArchiveService";
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<IcumFileArchiveService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _processingInterval;
        private readonly int _archiveAfterHours;
        private readonly int _batchSize;
        private readonly int _retentionYears;

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. ProcessArchiveCycleAsync
        // writes these and ExecuteAsync reads them for the per-iteration summary.
        private int _cycleCount = 0;
        private int _lastCycleArchived = 0;
        private int _lastCycleFailed = 0;
        private int _lastCycleSkipped = 0;

        public IcumFileArchiveService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<IcumFileArchiveService> logger,
            IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _configuration = configuration;
            _processingInterval = TimeSpan.FromHours(6); // Run every 6 hours
            _archiveAfterHours = 24; // Archive files 24 hours after processing
            _batchSize = 100; // Process 100 files per cycle
            _retentionYears = 2; // Retain archives for 2 years
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} Archive Service starting. Interval: {Interval}, Archive After: {Hours} hours",
                SERVICE_ID, _processingInterval, _archiveAfterHours);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_processingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }

                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(IcumFileArchiveService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastCycleArchived = 0;
                _lastCycleFailed = 0;
                _lastCycleSkipped = 0;
                int _failedThisCycle = 0;

                try
                {
                    await ProcessArchiveCycleAsync();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "{ServiceId} Error in archive processing cycle", SERVICE_ID);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = files archived this cycle; skipped = files seen
                // but not eligible (no BOE docs / missing on disk); failed =
                // per-file archive errors plus loop-level exceptions.
                _logger.LogIterationSummary(
                    "ICUMS-ARCHIVE",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastCycleArchived,
                    itemsSkipped: _lastCycleSkipped,
                    itemsFailed: _lastCycleFailed + _failedThisCycle);
            }
            _logger.LogInformation("{ServiceId} Archive Service stopped", SERVICE_ID);
        }

        private async Task ProcessArchiveCycleAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

            // Get downloads path from configuration provider (scoped service)
            var configProvider = scope.ServiceProvider.GetRequiredService<ICUMSConfigurationProvider>();
            var downloadsPath = await configProvider.GetDownloadsPathAsync();
            var archiveBasePath = Path.Combine(Path.GetDirectoryName(downloadsPath) ?? downloadsPath, "ICUMS Archive");

            _logger.LogInformation("{ServiceId} Starting archive cycle. Looking for files older than {Hours} hours",
                SERVICE_ID, _archiveAfterHours);

            // Step 1: Find files ready for archive
            var filesToArchive = await repository.GetFilesReadyForArchiveAsync(_archiveAfterHours, _batchSize);
            _logger.LogInformation("{ServiceId} Found {Count} files ready for archive", SERVICE_ID, filesToArchive.Count);

            if (filesToArchive.Count == 0)
            {
                return;
            }

            int archivedCount = 0;
            int failedCount = 0;
            int skippedCount = 0;

            foreach (var file in filesToArchive)
            {
                try
                {
                    // Validate file exists and was successfully processed
                    if (!File.Exists(file.FilePath))
                    {
                        _logger.LogWarning("{ServiceId} File not found, skipping: {FilePath}", SERVICE_ID, file.FilePath);
                        skippedCount++;
                        continue;
                    }

                    // Check if file has associated BOE documents (validation)
                    var hasDocuments = await repository.FileHasBOEDocumentsAsync(file.Id);
                    if (!hasDocuments)
                    {
                        _logger.LogWarning("{ServiceId} File {FileName} has no BOE documents, skipping archive",
                            SERVICE_ID, file.FileName);
                        skippedCount++;
                        continue;
                    }

                    // Archive the file
                    var archivedFile = await ArchiveFileAsync(file, downloadsPath, archiveBasePath, repository);
                    if (archivedFile != null)
                    {
                        archivedCount++;
                        _logger.LogInformation("{ServiceId} ✅ Archived file {FileName} (ID: {Id})",
                            SERVICE_ID, file.FileName, archivedFile.Id);
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex, "{ServiceId} ❌ Failed to archive file {FileName}: {Error}",
                        SERVICE_ID, file.FileName, ex.Message);
                }
            }

            _logger.LogInformation("{ServiceId} Archive cycle completed. Archived: {Archived}, Failed: {Failed}",
                SERVICE_ID, archivedCount, failedCount);

            // Audit 8.13 (Sprint 5G2 follow-up): publish per-cycle counts to
            // ExecuteAsync's heartbeat emitter.
            _lastCycleArchived = archivedCount;
            _lastCycleFailed = failedCount;
            _lastCycleSkipped = skippedCount;

            // Step 2: Update archive indexes
            await UpdateArchiveIndexesAsync(archiveBasePath);

            // Step 3: Check retention policy (run monthly on first day)
            if (DateTime.UtcNow.Day == 1)
            {
                await ProcessRetentionPolicyAsync(repository, archiveBasePath);
            }
        }

        private async Task<ArchivedFile?> ArchiveFileAsync(
            DownloadedFile file,
            string downloadsPath,
            string archiveBasePath,
            IIcumDownloadsRepository repository)
        {
            var fileInfo = new FileInfo(file.FilePath);
            var originalSize = fileInfo.Length;

            // Determine file type from directory structure
            var fileType = DetermineFileType(file.FilePath, downloadsPath);
            var containerNumbers = await ExtractContainerNumbersAsync(file.Id, repository);

            // Create archive directory structure: Archive/YYYY/MM/FileType/
            var archiveDate = file.ProcessedDate ?? DateTime.UtcNow;
            var archiveDir = Path.Combine(
                archiveBasePath,
                archiveDate.Year.ToString(),
                archiveDate.Month.ToString("D2"),
                fileType
            );
            Directory.CreateDirectory(archiveDir);

            // Create archive filename (simplified, without full path)
            var archiveFileName = Path.GetFileNameWithoutExtension(file.FileName) + ".gz";
            var archiveFilePath = Path.Combine(archiveDir, archiveFileName);

            // Compress file using GZip
            long compressedSize;
            using (var sourceStream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var targetStream = File.Create(archiveFilePath))
            using (var compressionStream = new GZipStream(targetStream, CompressionLevel.Optimal))
            {
                await sourceStream.CopyToAsync(compressionStream);
            }

            compressedSize = new FileInfo(archiveFilePath).Length;
            var compressionRatio = originalSize > 0
                ? ((double)(originalSize - compressedSize) / originalSize) * 100
                : 0;

            // Create archive record
            var archivedFile = new ArchivedFile
            {
                DownloadedFileId = file.Id,
                OriginalFileName = file.FileName,
                OriginalFilePath = file.FilePath,
                ArchiveFileName = archiveFileName,
                ArchiveFilePath = archiveFilePath,
                ArchiveDirectory = Path.Combine(archiveDate.Year.ToString(), archiveDate.Month.ToString("D2"), fileType),
                OriginalSizeBytes = originalSize,
                ArchivedSizeBytes = compressedSize,
                CompressionRatio = compressionRatio,
                CompressionType = "GZip",
                ProcessedDate = file.ProcessedDate ?? DateTime.UtcNow,
                ArchivedDate = DateTime.UtcNow,
                ContainerNumbers = containerNumbers,
                DocumentCount = await repository.GetBOEDocumentCountForFileAsync(file.Id),
                FileType = fileType,
                IsRestored = false
            };

            var archiveId = await repository.SaveArchivedFileAsync(archivedFile);
            archivedFile.Id = archiveId;

            // Delete original file after successful archive
            File.Delete(file.FilePath);
            _logger.LogDebug("{ServiceId} Deleted original file: {FilePath}", SERVICE_ID, file.FilePath);

            return archivedFile;
        }

        private string DetermineFileType(string filePath, string downloadsPath)
        {
            // Determine file type from directory structure
            var relativePath = Path.GetRelativePath(downloadsPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (parts.Length > 0)
            {
                var directory = parts[0];
                if (directory == "ContainerData") return "ContainerData";
                if (directory == "BatchData") return "BatchData";
                if (directory == "ScanResults") return "ScanResults";
                if (directory == "StatusChecks") return "StatusChecks";
            }

            // Fallback: determine from filename
            if (filePath.Contains("ContainerData", StringComparison.OrdinalIgnoreCase)) return "ContainerData";
            if (filePath.Contains("BatchData", StringComparison.OrdinalIgnoreCase)) return "BatchData";

            return "Unknown";
        }

        private async Task<string?> ExtractContainerNumbersAsync(int fileId, IIcumDownloadsRepository repository)
        {
            try
            {
                var documents = await repository.GetBOEDocumentsByFileIdAsync(fileId);
                var containerNumbers = documents
                    .SelectMany(d => new[] { d.ContainerNumber })
                    .Where(cn => !string.IsNullOrEmpty(cn))
                    .Distinct()
                    .Take(10) // Limit to first 10 to avoid very long strings
                    .ToList();

                return containerNumbers.Any() ? string.Join(",", containerNumbers) : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Failed to extract container numbers for file {FileId}",
                    SERVICE_ID, fileId);
                return null;
            }
        }

        private async Task UpdateArchiveIndexesAsync(string archiveBasePath)
        {
            try
            {
                // Guard: skip silently if archive folder doesn't exist yet (no files have
                // been archived, or the folder was removed by cleanup). Prevents the daily
                // DirectoryNotFoundException that surfaces as an ERR in logs. Fixed 2026-04-17.
                if (!Directory.Exists(archiveBasePath))
                {
                    return;
                }

                // Update monthly indexes
                var yearDirs = Directory.GetDirectories(archiveBasePath);
                foreach (var yearDir in yearDirs)
                {
                    var year = Path.GetFileName(yearDir);
                    var monthDirs = Directory.GetDirectories(yearDir);

                    foreach (var monthDir in monthDirs)
                    {
                        var month = Path.GetFileName(monthDir);
                        await UpdateMonthlyIndexAsync(monthDir, int.Parse(year), int.Parse(month));
                    }
                }

                // Update master index
                await UpdateMasterIndexAsync(archiveBasePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error updating archive indexes", SERVICE_ID);
            }
        }

        private async Task UpdateMonthlyIndexAsync(string monthDir, int year, int month)
        {
            var indexPath = Path.Combine(monthDir, "index.json");
            var files = Directory.GetFiles(monthDir, "*.gz");

            var index = new
            {
                Year = year,
                Month = month,
                IndexDate = DateTime.UtcNow,
                TotalFiles = files.Length,
                TotalSizeBytes = files.Sum(f => new FileInfo(f).Length),
                Files = files.Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new
                    {
                        FileName = fi.Name,
                        OriginalSize = 0, // Would need to read from ArchivedFile record
                        CompressedSize = fi.Length,
                        ArchiveDate = fi.CreationTime
                    };
                }).ToList()
            };

            var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(indexPath, json);
        }

        private async Task UpdateMasterIndexAsync(string archiveBasePath)
        {
            var indexPath = Path.Combine(archiveBasePath, "index.json");
            var yearDirs = Directory.GetDirectories(archiveBasePath)
                .Where(d => int.TryParse(Path.GetFileName(d), out _))
                .OrderBy(d => d);

            var years = new List<object>();
            long totalFiles = 0;
            long totalSize = 0;

            foreach (var yearDir in yearDirs)
            {
                var year = int.Parse(Path.GetFileName(yearDir));
                var monthDirs = Directory.GetDirectories(yearDir)
                    .Where(d => int.TryParse(Path.GetFileName(d), out _))
                    .OrderBy(d => d);

                var months = new List<object>();
                foreach (var monthDir in monthDirs)
                {
                    var month = int.Parse(Path.GetFileName(monthDir));
                    var files = Directory.GetFiles(monthDir, "*.gz");
                    var fileCount = files.Length;
                    var sizeBytes = files.Sum(f => new FileInfo(f).Length);

                    totalFiles += fileCount;
                    totalSize += sizeBytes;

                    months.Add(new
                    {
                        Month = month,
                        FileCount = fileCount,
                        SizeBytes = sizeBytes
                    });
                }

                years.Add(new
                {
                    Year = year,
                    Months = months
                });
            }

            var masterIndex = new
            {
                LastUpdated = DateTime.UtcNow,
                TotalArchivedFiles = totalFiles,
                TotalSizeBytes = totalSize,
                TotalCompressedSizeBytes = totalSize, // Same as total for now
                Years = years
            };

            var json = JsonSerializer.Serialize(masterIndex, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(indexPath, json);
        }

        private async Task ProcessRetentionPolicyAsync(IIcumDownloadsRepository repository, string archiveBasePath)
        {
            _logger.LogInformation("{ServiceId} Processing retention policy (retention: {Years} years)",
                SERVICE_ID, _retentionYears);

            var filesToDelete = await repository.GetArchivedFilesForRetentionCheckAsync(_retentionYears);
            _logger.LogInformation("{ServiceId} Found {Count} files exceeding retention period",
                SERVICE_ID, filesToDelete.Count);

            int deletedCount = 0;
            foreach (var archivedFile in filesToDelete)
            {
                try
                {
                    // Delete physical file
                    if (File.Exists(archivedFile.ArchiveFilePath))
                    {
                        File.Delete(archivedFile.ArchiveFilePath);
                    }

                    // Delete database record
                    await repository.DeleteArchivedFileAsync(archivedFile.Id);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Failed to delete archived file {FileName}",
                        SERVICE_ID, archivedFile.ArchiveFileName);
                }
            }

            _logger.LogInformation("{ServiceId} Retention policy completed. Deleted {Count} files",
                SERVICE_ID, deletedCount);

            // Rebuild indexes after deletion
            await UpdateArchiveIndexesAsync(archiveBasePath);
        }
    }
}

