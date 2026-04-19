using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public class IcumFileScannerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ThrottledLogger _logger;
        private readonly IConfiguration _configuration;
        private const string SERVICE_ID = "ICUMS-FILE-SCANNER";
        private readonly string _downloadsPath;

        public IcumFileScannerService(
            IServiceProvider serviceProvider,
            ILogger<IcumFileScannerService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = new ThrottledLogger(logger, SERVICE_ID, TimeSpan.FromSeconds(120)); // Throttle to 2 minutes
            _configuration = configuration;
            _downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInfo("StartService", "ICUMS File Scanner Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanForNewFilesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError("ExecuteAsync", "Error in ICUMS File Scanner Service", ex);
                }

                // Scan at configured interval (read from database settings)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    var scanInterval = await settingsProvider.GetIntAsync("BackgroundServices", "IcumFileScannerService.ScanIntervalMinutes", 1);
                    _logger.LogInfo("ScanInterval", $"⏰ Next ICUMS file scan in {scanInterval} minutes (from settings)");
                    await Task.Delay(TimeSpan.FromMinutes(scanInterval), stoppingToken);
                }
            }
        }

        private async Task ScanForNewFilesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

            try
            {
                if (!Directory.Exists(_downloadsPath))
                {
                    _logger.LogWarning("ICUMS downloads directory does not exist: {Path}", _downloadsPath);
                    return;
                }

                // ✅ FIX: Only scan subdirectories (BatchData, ContainerData), not root directory
                var subdirectories = new[] { "BatchData", "ContainerData", "ScanResults", "StatusChecks" };
                var jsonFiles = new List<string>();

                foreach (var subdir in subdirectories)
                {
                    var subdirPath = Path.Combine(_downloadsPath, subdir);
                    if (Directory.Exists(subdirPath))
                    {
                        var filesInSubdir = Directory.GetFiles(subdirPath, "*.json", SearchOption.TopDirectoryOnly);
                        jsonFiles.AddRange(filesInSubdir);
                    }
                }

                if (!jsonFiles.Any())
                {
                    _logger.LogDebug("ScanForNewFiles", "No JSON files found in downloads subdirectories");
                    return;
                }

                _logger.LogInfo("ScanForNewFiles", "Found {Count} JSON files in downloads directory", jsonFiles.Count);

                var newFilesCount = 0;

                foreach (var filePath in jsonFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var fileName = fileInfo.Name;

                        // ✅ FIX 4: Enhanced duplicate detection using file content hash
                        var fileHash = await ComputeFileHashAsync(filePath);

                        // Check if file is already registered (in ANY status - Pending, Processing, Completed, Failed)
                        var existingFile = await downloadsRepository.GetFileByNameAsync(fileName);
                        if (existingFile != null)
                        {
                            // File already exists in database, skip registration
                            continue;
                        }

                        // Additional check: Compare file hashes to detect content duplicates
                        var duplicateByHash = await downloadsRepository.GetFileByHashAsync(fileHash);
                        if (duplicateByHash != null)
                        {
                            _logger.LogInfo("SkipDuplicateFile", "Skipping duplicate file by content hash: {FileName} (duplicate of {ExistingFile})",
                                fileName, duplicateByHash.FileName);
                            continue;
                        }

                        // Register new file
                        var downloadedFile = new DownloadedFile
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            FileSize = fileInfo.Length,
                            FileHash = fileHash, // ✅ FIX 4: Store hash for future deduplication
                            DownloadDate = fileInfo.CreationTimeUtc,
                            ProcessingStatus = "Pending"
                        };

                        await downloadsRepository.SaveDownloadedFileAsync(downloadedFile);
                        newFilesCount++;

                        _logger.LogInfo("RegisterICUMSFile", "Registered new ICUMS file: {FileName} ({Size} bytes)",
                            fileName, fileInfo.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("RegisterICUMSFile", "Failed to register file {FilePath}: {Error}", ex, filePath, ex.Message);
                    }
                }

                if (newFilesCount > 0)
                {
                    _logger.LogInfo("RegisterICUMSFile", "Registered {Count} new ICUMS files for processing", newFilesCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ScanForNewFiles", "Error scanning for ICUMS files", ex);
            }
        }

        /// <summary>
        /// ✅ FIX 4: Compute SHA256 hash of file content for deduplication
        /// </summary>
        private async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var fileStream = File.OpenRead(filePath);
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            return Convert.ToHexString(hashBytes);
        }
    }
}
