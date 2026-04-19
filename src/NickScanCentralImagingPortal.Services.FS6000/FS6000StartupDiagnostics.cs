using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class FS6000StartupDiagnostics
    {
        private readonly ILogger _logger;
        private readonly FileSyncConfiguration _config;
        private readonly IServiceScopeFactory? _serviceScopeFactory;

        public FS6000StartupDiagnostics(
            ILogger logger,
            IOptions<FileSyncConfiguration> config,
            IServiceScopeFactory? serviceScopeFactory = null)
        {
            _logger = logger;
            _config = config.Value;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<DiagnosticReport> RunDiagnosticsAsync()
        {
            _logger.LogInformation("🔍 Running FS6000 Startup Diagnostics...");
            _logger.LogInformation("================================================");

            var report = new DiagnosticReport();

            // Check 0: Establish network share connection if configured
            EnsureNetworkShareConnection();

            // Check 1: Configuration
            report.ConfigurationValid = CheckConfiguration();

            // Check 2: Source Directory
            report.SourceDirectoryAccessible = CheckSourceDirectory();

            // Check 3: Destination Directories
            report.DestinationDirectoryAccessible = CheckDestinationDirectory();

            // Check 4: Database Connectivity
            report.DatabaseAccessible = await CheckDatabaseAsync();

            // Check 5: Folder Structure (with timeout)
            report.ValidFoldersFound = await CheckFolderStructureAsync();

            // Overall status
            report.AllChecksPass = report.ConfigurationValid &&
                                  report.SourceDirectoryAccessible &&
                                  report.DestinationDirectoryAccessible &&
                                  report.DatabaseAccessible &&
                                  report.ValidFoldersFound >= 0; // Allow 0 folders but warn

            LogDiagnosticReport(report);

            return report;
        }

        private bool CheckConfiguration()
        {
            _logger.LogInformation("📋 [1/5] Checking configuration...");

            try
            {
                _config.Validate(_logger);
                _logger.LogInformation("✅ Configuration is valid");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Configuration is invalid");
                return false;
            }
        }

        private bool CheckSourceDirectory()
        {
            _logger.LogInformation("📁 [2/5] Checking source directory: {SourcePath} (with 30s timeout)", _config.SourcePath);

            try
            {
                // Use Task.Run with timeout to prevent indefinite hangs on network drives
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var checkTask = Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(_config.SourcePath))
                        {
                            _logger.LogError("❌ Source directory does not exist: {SourcePath}", _config.SourcePath);
                            _logger.LogError("❌ Please ensure Z:\\ drive is mounted and accessible");
                            return false;
                        }

                        var dirs = Directory.GetDirectories(_config.SourcePath);
                        _logger.LogInformation("✅ Source directory accessible with {Count} subdirectories", dirs.Length);

                        // List first few directories for debugging
                        if (dirs.Length > 0)
                        {
                            var sampleDirs = string.Join(", ", dirs.Take(5).Select(d => Path.GetFileName(d)));
                            _logger.LogInformation("   Sample directories: {SampleDirs}", sampleDirs);
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error accessing source directory");
                        return false;
                    }
                }, cts.Token);

                // Wait for the task with timeout
                if (checkTask.Wait(TimeSpan.FromSeconds(30)))
                {
                    return checkTask.Result;
                }
                else
                {
                    _logger.LogError("❌ Source directory check TIMED OUT after 30 seconds");
                    _logger.LogError("❌ Z:\\ drive is too slow or unresponsive");
                    _logger.LogError("❌ This may indicate network issues or the drive is not properly mounted");
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
                _logger.LogError(ex, "❌ Unexpected error checking source directory: {SourcePath}", _config.SourcePath);
                return false;
            }
        }

        private bool CheckDestinationDirectory()
        {
            _logger.LogInformation("📁 [3/5] Checking destination directory: {DestinationPath}", _config.DestinationPath);

            try
            {
                if (!Directory.Exists(_config.DestinationPath))
                {
                    _logger.LogInformation("   Creating destination directory...");
                    Directory.CreateDirectory(_config.DestinationPath);
                    _logger.LogInformation("✅ Created destination directory");
                }
                else
                {
                    _logger.LogInformation("✅ Destination directory exists");
                }

                // Test write access
                var testFile = Path.Combine(_config.DestinationPath, $"test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                _logger.LogInformation("✅ Destination directory is writable");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Cannot access or write to destination directory");
                return false;
            }
        }

        private async Task<bool> CheckDatabaseAsync()
        {
            _logger.LogInformation("💾 [4/5] Checking database connectivity...");

            try
            {
                if (_serviceScopeFactory == null)
                {
                    _logger.LogWarning("⚠️ Service scope factory not available, skipping database check");
                    return true;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var canConnect = await context.Database.CanConnectAsync();
                if (canConnect)
                {
                    _logger.LogInformation("✅ Database is accessible");

                    // Check if FS6000SyncLogs table exists
                    try
                    {
                        var syncCount = await context.FS6000SyncLogs.CountAsync();
                        _logger.LogInformation("   FS6000SyncLogs table exists with {Count} records", syncCount);
                    }
                    catch
                    {
                        _logger.LogWarning("⚠️ FS6000SyncLogs table may not exist yet");
                    }
                }
                else
                {
                    _logger.LogError("❌ Cannot connect to database");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Database connectivity check failed");
                return false;
            }
        }

        private async Task<int> CheckFolderStructureAsync()
        {
            _logger.LogInformation("📂 [5/5] Checking folder structure in source (quick scan with 30s timeout)...");

            try
            {
                // Use Task.WhenAny for proper async timeout
                var checkTask = Task.Run(() =>
                {
                    try
                    {
                        var yearFolders = Directory.GetDirectories(_config.SourcePath)
                            .Where(d =>
                            {
                                var yearName = Path.GetFileName(d);
                                return yearName.Length == 4 && int.TryParse(yearName, out var year) && year >= _config.MinimumYear;
                            })
                            .ToList();

                        if (!yearFolders.Any())
                        {
                            _logger.LogWarning("⚠️ No valid year folders found (expected: {MinYear}+)", _config.MinimumYear);
                            _logger.LogWarning("⚠️ Expected structure: {SourcePath}\\{MinYear}\\{MinMonthDay}\\0001\\",
                                _config.SourcePath, _config.MinimumYear, _config.MinimumMonthDay.ToString("0000"));

                            // List what we actually found
                            var allDirs = Directory.GetDirectories(_config.SourcePath);
                            if (allDirs.Any())
                            {
                                var dirNames = string.Join(", ", allDirs.Take(10).Select(d => Path.GetFileName(d)));
                                _logger.LogWarning("⚠️ Found directories: {DirNames}", dirNames);
                            }

                            return 0;
                        }

                        // Quick scan: only check first year folder for performance
                        var firstYearFolder = yearFolders.First();
                        var monthDayFolders = Directory.GetDirectories(firstYearFolder).Take(2).ToList(); // Sample first 2 month-day folders
                        var totalSerialFolders = 0;

                        foreach (var monthDayFolder in monthDayFolders)
                        {
                            var serialFolders = Directory.GetDirectories(monthDayFolder).Take(2).ToList(); // Sample first 2 serial folders
                            totalSerialFolders += serialFolders.Count;
                        }

                        _logger.LogInformation("✅ Found {YearCount} year folders (quick scan: {SerialCount} serial folders sampled)",
                            yearFolders.Count, totalSerialFolders);

                        return totalSerialFolders;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error checking folder structure");
                        return -1;
                    }
                });

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(checkTask, timeoutTask);

                if (completedTask == checkTask)
                {
                    return await checkTask;
                }
                else
                {
                    _logger.LogWarning("⚠️ Folder structure check TIMED OUT after 30 seconds");
                    _logger.LogWarning("⚠️ Z:\\ has too many folders or is slow - will proceed anyway");
                    return 0; // Return 0 but don't fail the diagnostic
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking folder structure");
                return -1;
            }
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, System.Text.StringBuilder remoteName, ref int length);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string? lpLocalName;
            public string lpRemoteName;
            public string? lpComment;
            public string? lpProvider;
        }

        private void EnsureNetworkShareConnection()
        {
            try
            {
                var sourcePath = _config.SourcePath;
                if (string.IsNullOrEmpty(sourcePath)) return;

                string uncShare;
                if (sourcePath.StartsWith(@"\\"))
                {
                    var parts = sourcePath.TrimStart('\\').Split('\\');
                    if (parts.Length >= 2)
                        uncShare = $@"\\{parts[0]}\{parts[1]}";
                    else
                        return;
                }
                else if (sourcePath.Length >= 2 && sourcePath[1] == ':')
                {
                    // Mapped drive — check if it's already accessible
                    if (Directory.Exists(sourcePath)) return;

                    // Try resolving the mapped drive to UNC
                    var remoteName = new System.Text.StringBuilder(260);
                    int length = 260;
                    if (WNetGetConnection(sourcePath.Substring(0, 2), remoteName, ref length) == 0)
                    {
                        uncShare = remoteName.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("Cannot resolve mapped drive {Drive} to UNC path", sourcePath.Substring(0, 2));
                        return;
                    }
                }
                else
                {
                    return;
                }

                // Check if the UNC share is already accessible
                try
                {
                    if (Directory.Exists(uncShare))
                    {
                        _logger.LogInformation("✅ Network share {Share} is already accessible", uncShare);
                        return;
                    }
                }
                catch { /* not accessible, try connecting */ }

                // Read credentials from config (NetworkShare section) or environment
                var username = _config.NetworkShareUsername
                    ?? Environment.GetEnvironmentVariable("NICKSCAN_FS6000_SHARE_USERNAME");
                var password = _config.NetworkSharePassword
                    ?? Environment.GetEnvironmentVariable("NICKSCAN_FS6000_SHARE_PASSWORD");

                if (string.IsNullOrEmpty(username))
                {
                    _logger.LogWarning("No network share credentials configured. Set FS6000:FileSync:NetworkShareUsername/Password or NICKSCAN_FS6000_SHARE_USERNAME/PASSWORD environment variables.");
                    return;
                }

                _logger.LogInformation("🔗 Establishing network connection to {Share} as {User}...", uncShare, username);

                var netResource = new NETRESOURCE
                {
                    dwType = 1, // RESOURCETYPE_DISK
                    lpRemoteName = uncShare
                };

                int result = WNetAddConnection2(ref netResource, password, username, 0);
                if (result == 0)
                {
                    _logger.LogInformation("✅ Network share {Share} connected successfully", uncShare);
                }
                else if (result == 1219) // ERROR_SESSION_CREDENTIAL_CONFLICT — already connected
                {
                    _logger.LogInformation("✅ Network share {Share} already has an active session", uncShare);
                }
                else
                {
                    _logger.LogError("❌ Failed to connect to network share {Share}: Win32 error {ErrorCode}", uncShare, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error establishing network share connection");
            }
        }

        private void LogDiagnosticReport(DiagnosticReport report)
        {
            _logger.LogInformation("================================================");
            _logger.LogInformation("📊 FS6000 DIAGNOSTIC REPORT:");
            _logger.LogInformation("   Configuration Valid: {Status}", report.ConfigurationValid ? "✅ PASS" : "❌ FAIL");
            _logger.LogInformation("   Source Directory Accessible: {Status}", report.SourceDirectoryAccessible ? "✅ PASS" : "❌ FAIL");
            _logger.LogInformation("   Destination Directory Accessible: {Status}", report.DestinationDirectoryAccessible ? "✅ PASS" : "❌ FAIL");
            _logger.LogInformation("   Database Accessible: {Status}", report.DatabaseAccessible ? "✅ PASS" : "❌ FAIL");
            _logger.LogInformation("   Valid Folders Found: {Count}", report.ValidFoldersFound);
            _logger.LogInformation("------------------------------------------------");
            _logger.LogInformation("   Overall Status: {Status}", report.AllChecksPass ? "✅ PASS - Service can start" : "❌ FAIL - Service cannot start");
            _logger.LogInformation("================================================");
        }
    }

    public class DiagnosticReport
    {
        public bool ConfigurationValid { get; set; }
        public bool SourceDirectoryAccessible { get; set; }
        public bool DestinationDirectoryAccessible { get; set; }
        public bool DatabaseAccessible { get; set; }
        public int ValidFoldersFound { get; set; }
        public bool AllChecksPass { get; set; }
    }
}

