using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public interface IIcumBackupService
    {
        Task<bool> BackupJsonResponseAsync(string category, string jsonContent, object identifier, string suffix = "");
        Task<bool> InitializeBackupDirectoryAsync();
        Task<BackupMetrics> GetBackupMetricsAsync();
        Task<bool> ValidateBackupDirectoryAsync();
    }

    public class IcumBackupService : IIcumBackupService
    {
        private readonly ILogger<IcumBackupService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _backupDirectory;
        private const string SERVICE_ID = "[ICUMS-BACKUP]";

        // ✅ PERFORMANCE FIX: Track initialization status to avoid repeated initialization
        private static bool _isInitialized = false;
        private static readonly object _initLock = new object();

        // Metrics tracking
        private int _backupFilesCreated = 0;
        private long _totalDataDownloadedBytes = 0;
        private DateTime _lastBackupTime = DateTime.MinValue;
        private int _backupFailures = 0;

        public IcumBackupService(ILogger<IcumBackupService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _backupDirectory = _configuration["ICUMS:BackupDirectory"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup";
        }

        public Task<bool> InitializeBackupDirectoryAsync()
        {
            // ✅ PERFORMANCE FIX: Only initialize once (thread-safe check)
            if (_isInitialized)
            {
                return Task.FromResult(true); // Already initialized, skip
            }

            // ✅ Use lock for thread-safe initialization
            lock (_initLock)
            {
                // Double-check after acquiring lock
                if (_isInitialized)
                {
                    return Task.FromResult(true);
                }

                try
                {
                    _logger.LogInformation("{ServiceId} Initializing ICUMS backup directory: {BackupDirectory}", SERVICE_ID, _backupDirectory);

                    // Create main backup directory
                    if (!Directory.Exists(_backupDirectory))
                    {
                        Directory.CreateDirectory(_backupDirectory);
                        _logger.LogInformation("{ServiceId} Created main backup directory: {BackupDirectory}", SERVICE_ID, _backupDirectory);
                    }

                    // Create subdirectories for organization
                    var subdirs = new[] { "BatchData", "ContainerData", "ScanResults", "StatusChecks" };
                    foreach (var subdir in subdirs)
                    {
                        var path = Path.Combine(_backupDirectory, subdir);
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                            _logger.LogDebug("Created backup subdirectory: {Subdirectory}", subdir);
                        }
                    }

                    _logger.LogInformation("{ServiceId} ICUMS backup directory structure initialized successfully", SERVICE_ID);
                    _isInitialized = true; // Mark as initialized
                    return Task.FromResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize backup directory: {BackupDirectory}", _backupDirectory);
                    return Task.FromResult(false);
                }
            }
        }

        public async Task<bool> BackupJsonResponseAsync(string category, string jsonContent, object identifier, string suffix = "")
        {
            try
            {
                // ✅ PERFORMANCE FIX: Ensure directory is initialized before backup (only once, thread-safe)
                // The static flag ensures this only runs once across all instances
                if (!_isInitialized)
                {
                    await InitializeBackupDirectoryAsync();
                }

                if (string.IsNullOrEmpty(jsonContent))
                {
                    _logger.LogWarning("Cannot backup empty JSON content for category: {Category}", category);
                    return false;
                }

                // Generate filename
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{category}_{identifier}_{timestamp}";

                if (!string.IsNullOrEmpty(suffix))
                {
                    fileName += $"_{suffix}";
                }

                fileName += ".json";

                // Determine target directory
                var targetDir = Path.Combine(_backupDirectory, category);

                // Ensure directory exists (this should be rare after initialization)
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    _logger.LogDebug("Created backup directory: {TargetDir}", targetDir);
                }

                var backupPath = Path.Combine(targetDir, fileName);

                // Format JSON with pretty printing
                string formattedJson;
                try
                {
                    var jsonObject = JsonSerializer.Deserialize<object>(jsonContent);
                    formattedJson = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                }
                catch (JsonException)
                {
                    // If JSON parsing fails, use original content
                    formattedJson = jsonContent;
                    _logger.LogWarning("JSON parsing failed, using original content for backup");
                }

                // Write backup file
                await File.WriteAllTextAsync(backupPath, formattedJson, Encoding.UTF8);

                // Update metrics
                _backupFilesCreated++;
                _totalDataDownloadedBytes += Encoding.UTF8.GetByteCount(jsonContent);
                _lastBackupTime = DateTime.UtcNow;

                _logger.LogDebug("Successfully backed up JSON response to: {BackupPath}", backupPath);
                return true;
            }
            catch (Exception ex)
            {
                _backupFailures++;
                _logger.LogError(ex, "Failed to backup JSON response for category: {Category}, identifier: {Identifier}",
                    category, identifier);
                return false;
            }
        }

        public async Task<BackupMetrics> GetBackupMetricsAsync()
        {
            return await Task.FromResult(new BackupMetrics
            {
                BackupFilesCreated = _backupFilesCreated,
                TotalDataDownloadedBytes = _totalDataDownloadedBytes,
                LastBackupTime = _lastBackupTime,
                BackupFailures = _backupFailures,
                BackupDirectory = _backupDirectory,
                BackupSuccessRate = _backupFilesCreated + _backupFailures > 0
                    ? (double)_backupFilesCreated / (_backupFilesCreated + _backupFailures)
                    : 0
            });
        }

        public async Task<bool> ValidateBackupDirectoryAsync()
        {
            try
            {
                // Check if main directory exists and is writable
                if (!Directory.Exists(_backupDirectory))
                {
                    _logger.LogError("Backup directory does not exist: {BackupDirectory}", _backupDirectory);
                    return false;
                }

                // Test write permissions
                var testFile = Path.Combine(_backupDirectory, $"test_{Guid.NewGuid()}.tmp");
                try
                {
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup directory is not writable: {BackupDirectory}", _backupDirectory);
                    return false;
                }

                // Check subdirectories
                var subdirs = new[] { "BatchData", "ContainerData", "ScanResults", "StatusChecks" };
                foreach (var subdir in subdirs)
                {
                    var path = Path.Combine(_backupDirectory, subdir);
                    if (!Directory.Exists(path))
                    {
                        _logger.LogWarning("Backup subdirectory missing: {Subdirectory}", subdir);
                    }
                }

                _logger.LogInformation("Backup directory validation successful: {BackupDirectory}", _backupDirectory);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup directory validation failed: {BackupDirectory}", _backupDirectory);
                return false;
            }
        }
    }

    public class BackupMetrics
    {
        public int BackupFilesCreated { get; set; }
        public long TotalDataDownloadedBytes { get; set; }
        public DateTime LastBackupTime { get; set; }
        public int BackupFailures { get; set; }
        public string BackupDirectory { get; set; } = string.Empty;
        public double BackupSuccessRate { get; set; }
    }
}
