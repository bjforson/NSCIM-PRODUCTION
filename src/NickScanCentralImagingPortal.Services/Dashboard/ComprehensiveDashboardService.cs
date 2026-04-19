using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Dashboard
{
    public interface IComprehensiveDashboardService
    {
        Task<ComprehensiveDashboardData> GetComprehensiveDashboardDataAsync();
    }

    public class ComprehensiveDashboardService : IComprehensiveDashboardService
    {
        private readonly ApplicationDbContext _appDb;
        private readonly IcumDbContext _icumDb;
        private readonly IcumDownloadsDbContext _downloadsDb;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ComprehensiveDashboardService> _logger;
        private readonly DateTime _applicationStartTime;

        public ComprehensiveDashboardService(
            ApplicationDbContext appDb,
            IcumDbContext icumDb,
            IcumDownloadsDbContext downloadsDb,
            IConfiguration configuration,
            ILogger<ComprehensiveDashboardService> logger)
        {
            _appDb = appDb;
            _icumDb = icumDb;
            _downloadsDb = downloadsDb;
            _configuration = configuration;
            _logger = logger;
            _applicationStartTime = Process.GetCurrentProcess().StartTime;
        }

        public async Task<ComprehensiveDashboardData> GetComprehensiveDashboardDataAsync()
        {
            _logger.LogInformation("Gathering comprehensive dashboard data...");

            // DbContext is NOT thread-safe. Run sequentially to avoid "context instance while it is being configured" errors.
            var data = new ComprehensiveDashboardData
            {
                SystemOverview = await GetSystemOverviewAsync(),
                BackgroundServices = GetBackgroundServicesStatus(),
                Scanners = await GetScannerStatusAsync(),
                Databases = await GetDatabaseStatisticsAsync(),
                ICUMSIntegration = GetICUMSIntegrationStatus(),
                Queues = await GetQueueStatisticsAsync(),
                ContainerValidation = await GetContainerValidationWorkflowAsync(),
                ImageProcessing = await GetImageProcessingMetricsAsync(),
                VehicleImports = await GetVehicleImportStatisticsAsync(),
                FileSystem = GetFileSystemStatus(),
                Performance = GetPerformanceMetrics(),
                Errors = await GetErrorStatisticsAsync(),
                RecentActivity = await GetRecentActivityAsync(),
                Trends = await GetTrendDataAsync(),
                CurrentOperations = GetCurrentOperations(),
                Alerts = await GetAlertsAsync(),
                UserActivity = await GetUserActivityAsync(),
                RBACStatus = await GetRBACStatusAsync(),
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Comprehensive dashboard data gathered successfully");
            return data;
        }

        #region System Overview

        private async Task<SystemOverview> GetSystemOverviewAsync()
        {
            try
            {
                var uptime = DateTime.UtcNow - _applicationStartTime;
                // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
                var todayStart = DateTime.UtcNow.Date;
                var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
                var errors24h = await _appDb.FS6000FileProcessings
                    .Where(f => f.ProcessingStatus == "Failed" && f.CreatedAt >= todayStart && f.CreatedAt < todayEnd)
                    .CountAsync();

                // Calculate health score (0-100)
                var healthScore = 100.0;
                healthScore -= errors24h * 2; // -2 points per error
                healthScore = Math.Max(0, Math.Min(100, healthScore));

                var status = healthScore >= 90 ? "Healthy" :
                            healthScore >= 70 ? "Degraded" : "Critical";

                return new SystemOverview
                {
                    Status = status,
                    Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
                    Version = "1.0.0",
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    LastRestartAt = _applicationStartTime,
                    TotalErrors24h = errors24h,
                    TotalWarnings24h = 0, // TODO: Implement warning tracking
                    HealthScore = healthScore
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system overview");
                return new SystemOverview { Status = "Error" };
            }
        }

        #endregion

        #region Background Services

        private Dictionary<string, BackgroundServiceStatus> GetBackgroundServicesStatus()
        {
            // TODO: Implement actual service health monitoring
            // For now, return mock data with realistic structure
            return new Dictionary<string, BackgroundServiceStatus>
            {
                ["ServiceOrchestrator"] = new BackgroundServiceStatus
                {
                    Name = "ServiceOrchestratorBackgroundService",
                    Status = "Running",
                    LastHeartbeat = DateTime.UtcNow.AddSeconds(-5),
                    Priority = 1,
                    Metrics = new Dictionary<string, object>
                    {
                        ["ServicesManaged"] = 11
                    }
                },
                ["ASEService"] = new BackgroundServiceStatus
                {
                    Name = "AseBackgroundService",
                    Status = "Running",
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-2),
                    Priority = 1,
                    Metrics = new Dictionary<string, object>
                    {
                        ["SyncInterval"] = "15 minutes",
                        ["LastSyncDuration"] = "3.2s"
                    }
                },
                ["FS6000Service"] = new BackgroundServiceStatus
                {
                    Name = "FS6000BackgroundService",
                    Status = "Running",
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-1),
                    Priority = 3
                }
            };
        }

        #endregion

        #region Scanner Status

        private async Task<Dictionary<string, ScannerDetailedStatus>> GetScannerStatusAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
            var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek); // Start of week (Sunday)
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var scanners = new Dictionary<string, ScannerDetailedStatus>();

            // ASE Scanner
            try
            {
                // ✅ Use ScanTime (actual scan time) instead of CreatedAt for scan counts
                var aseScansToday = await _appDb.AseScans.CountAsync(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd);
                var aseScansWeek = await _appDb.AseScans.CountAsync(s => s.ScanTime >= weekStart);
                var aseScansMonth = await _appDb.AseScans.CountAsync(s => s.ScanTime >= monthStart);
                var lastAseScan = await _appDb.AseScans.OrderByDescending(s => s.ScanTime).FirstOrDefaultAsync();

                scanners["ASE"] = new ScannerDetailedStatus
                {
                    ScannerId = "ASE-001",
                    ScannerType = "ASE",
                    Location = "Terminal A - Gate 5",
                    Status = lastAseScan != null && lastAseScan.ScanTime >= DateTime.UtcNow.AddMinutes(-30) ? "Online" : "Offline",
                    Health = new ScannerHealth
                    {
                        Score = 95,
                        LastScan = lastAseScan?.ScanTime,
                        ScansToday = aseScansToday,
                        ScansThisWeek = aseScansWeek,
                        ScansThisMonth = aseScansMonth,
                        SuccessRate = 98.5
                    },
                    Performance = new ScannerPerformance
                    {
                        ImageProcessingTime = "2.1s",
                        ConversionSuccessRate = 99.2,
                        ImagesProcessed24h = aseScansToday
                    },
                    Errors = new ServiceErrors
                    {
                        Last24h = 0
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE scanner status");
            }

            // FS6000 Scanner
            try
            {
                // ✅ Use ScanTime (actual scan time) instead of CreatedAt for scan counts
                var fs6000ScansToday = await _appDb.FS6000Scans.CountAsync(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd);
                var fs6000ScansWeek = await _appDb.FS6000Scans.CountAsync(s => s.ScanTime >= weekStart);
                var fs6000ScansMonth = await _appDb.FS6000Scans.CountAsync(s => s.ScanTime >= monthStart);
                var lastFS6000Scan = await _appDb.FS6000Scans.OrderByDescending(s => s.ScanTime).FirstOrDefaultAsync();
                var failedFiles = await _appDb.FS6000FileProcessings.CountAsync(f => f.ProcessingStatus == "Failed" && f.CreatedAt >= todayStart && f.CreatedAt < todayEnd);

                scanners["FS6000"] = new ScannerDetailedStatus
                {
                    ScannerId = "FS6000-001",
                    ScannerType = "FS6000",
                    Location = "Terminal B - Gate 3",
                    Status = lastFS6000Scan != null && lastFS6000Scan.ScanTime >= DateTime.UtcNow.AddMinutes(-30) ? "Online" : "Offline",
                    Health = new ScannerHealth
                    {
                        Score = 88,
                        LastScan = lastFS6000Scan?.ScanTime,
                        ScansToday = fs6000ScansToday,
                        ScansThisWeek = fs6000ScansWeek,
                        ScansThisMonth = fs6000ScansMonth,
                        SuccessRate = 97.8
                    },
                    Performance = new ScannerPerformance
                    {
                        ImageProcessingTime = "1.5s",
                        ConversionSuccessRate = 97.8,
                        ImagesProcessed24h = fs6000ScansToday
                    },
                    Errors = new ServiceErrors
                    {
                        Last24h = failedFiles
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting FS6000 scanner status");
            }

            // Heimann Smith Scanner
            scanners["HeimannSmith"] = new ScannerDetailedStatus
            {
                ScannerId = "HS-001",
                ScannerType = "HeimannSmith",
                Location = "Terminal C",
                Status = "Ready",
                Health = new ScannerHealth
                {
                    Score = 0
                }
            };

            return scanners;
        }

        #endregion

        #region Database Statistics

        private async Task<DatabaseStatistics> GetDatabaseStatisticsAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
            var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek); // Start of week (Sunday)
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var stats = new DatabaseStatistics();

            try
            {
                // NS_CIS Database
                stats.NS_CIS = new DatabaseInfo
                {
                    Name = "NS_CIS",
                    Status = "Connected",
                    TableCounts = new Dictionary<string, int>
                    {
                        ["Containers"] = await _appDb.Containers.CountAsync(),
                        ["ContainerImages"] = await SafeCountAsync(_appDb.ContainerImages, "ContainerImages"),
                        ["FS6000Scans"] = await _appDb.FS6000Scans.CountAsync(),
                        ["FS6000Images"] = await _appDb.FS6000Images.CountAsync(),
                        ["AseScans"] = await _appDb.AseScans.CountAsync(),
                        ["Users"] = await _appDb.Users.CountAsync(),
                        ["Roles"] = await _appDb.Roles.CountAsync(),
                        ["Permissions"] = await _appDb.Permissions.CountAsync(),
                        ["ImageCaches"] = await _appDb.ImageCaches.CountAsync()
                    },
                    Growth = new DatabaseGrowth
                    {
                        RecordsToday = await _appDb.Containers.CountAsync(c => c.CreatedAt >= todayStart && c.CreatedAt < todayEnd),
                        RecordsThisWeek = await _appDb.Containers.CountAsync(c => c.CreatedAt >= weekStart),
                        RecordsThisMonth = await _appDb.Containers.CountAsync(c => c.CreatedAt >= monthStart)
                    },
                    Performance = new DatabasePerformance
                    {
                        AvgQueryTime = "125ms",
                        TotalQueries24h = 15240
                    }
                };

                // ICUMS Database
                stats.ICUMS = new DatabaseInfo
                {
                    Name = "ICUMS",
                    Status = "Connected",
                    TableCounts = new Dictionary<string, int>
                    {
                        ["IcumContainerData"] = await _icumDb.IcumContainerData.CountAsync(),
                        ["IcumManifestItems"] = await _icumDb.IcumManifestItems.CountAsync()
                    },
                    Performance = new DatabasePerformance
                    {
                        AvgQueryTime = "85ms"
                    }
                };

                // ICUMS_Downloads Database
                stats.ICUMS_Downloads = new DatabaseInfo
                {
                    Name = "ICUMS_Downloads",
                    Status = "Connected",
                    TableCounts = new Dictionary<string, int>
                    {
                        ["DownloadedFiles"] = await _downloadsDb.DownloadedFiles.CountAsync(),
                        ["BOEDocuments"] = await _downloadsDb.BOEDocuments.CountAsync(),
                        ["VehicleImports"] = await _downloadsDb.VehicleImports.CountAsync(),
                        ["ICUMSDownloadQueue"] = await _downloadsDb.ICUMSDownloadQueue.CountAsync()
                    },
                    Performance = new DatabasePerformance
                    {
                        AvgQueryTime = "65ms"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database statistics");
            }

            return stats;
        }

        /// <summary>
        /// Returns count or 0 if table does not exist (SqlException 208). Handles databases where ContainerImages/ContainerAnnotations were dropped or not migrated.
        /// </summary>
        private async Task<int> SafeCountAsync<T>(IQueryable<T> query, string tableName)
        {
            try
            {
                return await query.CountAsync();
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogDebug("Table {Table} does not exist, returning 0", tableName);
                return 0;
            }
        }

        #endregion

        #region Queue Statistics

        private async Task<QueueStatistics> GetQueueStatisticsAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)

            try
            {
                // Download Queue
                var downloadQueue = await _downloadsDb.ICUMSDownloadQueue
                    .Where(q => q.Status == "Pending")
                    .ToListAsync();

                var downloadQueueStats = new QueueInfo
                {
                    TotalPending = downloadQueue.Count,
                    HighPriority = downloadQueue.Count(q => q.Priority >= 8),
                    MediumPriority = downloadQueue.Count(q => q.Priority >= 5 && q.Priority < 8),
                    LowPriority = downloadQueue.Count(q => q.Priority < 5),
                    Completed24h = await _downloadsDb.ICUMSDownloadQueue
                        .CountAsync(q => q.Status == "Completed" && q.CompletedAt.HasValue && q.CompletedAt.Value >= todayStart && q.CompletedAt.Value < todayEnd),
                    Failed24h = await _downloadsDb.ICUMSDownloadQueue
                        .CountAsync(q => q.Status == "Failed" && q.LastAttemptAt.HasValue && q.LastAttemptAt.Value >= todayStart && q.LastAttemptAt.Value < todayEnd)
                };

                if (downloadQueue.Any())
                {
                    var oldest = downloadQueue.OrderBy(q => q.QueuedAt).First();
                    downloadQueueStats.OldestRequest = new DashboardQueueItem
                    {
                        ContainerNumber = oldest.ContainerNumber,
                        RequestedAt = oldest.QueuedAt,
                        Priority = oldest.Priority >= 8 ? "High" : oldest.Priority >= 5 ? "Medium" : "Low",
                        Retries = oldest.RetryCount
                    };
                }

                // Submission Queue
                var submissionQueue = await _appDb.ICUMSSubmissionQueues
                    .Where(q => q.Status == "Pending" || q.Status == "Retrying")
                    .ToListAsync();

                var submissionQueueStats = new QueueInfo
                {
                    TotalPending = submissionQueue.Count,
                    HighPriority = submissionQueue.Count(q => q.Priority >= 8),
                    MediumPriority = submissionQueue.Count(q => q.Priority >= 5 && q.Priority < 8),
                    LowPriority = submissionQueue.Count(q => q.Priority < 5),
                    Processing = submissionQueue.Count(q => q.Status == "Retrying"),
                    Completed24h = await _appDb.ICUMSSubmissionQueues
                        .CountAsync(q => q.Status == "Completed" && q.UpdatedAt >= todayStart && q.UpdatedAt < todayEnd),
                    Failed24h = await _appDb.ICUMSSubmissionQueues
                        .CountAsync(q => q.Status == "Failed" && q.UpdatedAt >= todayStart && q.UpdatedAt < todayEnd)
                };

                return new QueueStatistics
                {
                    DownloadQueue = downloadQueueStats,
                    SubmissionQueue = submissionQueueStats
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue statistics");
                return new QueueStatistics();
            }
        }

        #endregion

        #region Container Validation Workflow

        private async Task<ContainerValidationWorkflow> GetContainerValidationWorkflowAsync()
        {
            try
            {
                var totalContainers = await _appDb.FS6000Scans.CountAsync() + await _appDb.AseScans.CountAsync();

                // Get completeness statuses
                var completeStatuses = await _appDb.ContainerCompletenessStatuses
                    .Where(s => s.HasICUMSData)
                    .CountAsync();

                // Get submission stats
                var submitted = await _appDb.ICUMSSubmissionQueues
                    .CountAsync(q => q.Status == "Completed");

                var pipeline = new WorkflowPipeline
                {
                    TotalContainers = totalContainers,
                    Stages = new Dictionary<string, PipelineStage>
                    {
                        ["Scanned"] = new PipelineStage
                        {
                            Count = totalContainers,
                            Percentage = 100
                        },
                        ["DataComplete"] = new PipelineStage
                        {
                            Count = completeStatuses,
                            Percentage = totalContainers > 0 ? (double)completeStatuses / totalContainers * 100 : 0
                        },
                        ["Submitted"] = new PipelineStage
                        {
                            Count = submitted,
                            Percentage = totalContainers > 0 ? (double)submitted / totalContainers * 100 : 0
                        }
                    }
                };

                return new ContainerValidationWorkflow
                {
                    Pipeline = pipeline,
                    Throughput = new WorkflowThroughput
                    {
                        Daily = 450,
                        Bottleneck = completeStatuses < totalContainers * 0.8 ? "ICUMS data availability" : "None"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting container validation workflow");
                return new ContainerValidationWorkflow();
            }
        }

        #endregion

        #region Image Processing

        private async Task<ImageProcessingMetrics> GetImageProcessingMetricsAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)

            try
            {
                var fs6000ImagesProcessed = await _appDb.FS6000Images.CountAsync(i => i.CreatedAt >= todayStart && i.CreatedAt < todayEnd);
                var aseImagesProcessed = await _appDb.ImageCaches
                    .CountAsync(i => i.ScannerType == "ASE" && i.CachedAt >= todayStart && i.CachedAt < todayEnd);

                return new ImageProcessingMetrics
                {
                    Pipelines = new Dictionary<string, ImagePipelineMetrics>
                    {
                        ["FS6000"] = new ImagePipelineMetrics
                        {
                            ImagesProcessed24h = fs6000ImagesProcessed,
                            AverageSize = "2.1 MB",
                            ConversionSuccessRate = 97.8,
                            CacheHitRate = 89.6
                        },
                        ["ASE"] = new ImagePipelineMetrics
                        {
                            ImagesProcessed24h = aseImagesProcessed,
                            AverageSize = "3.5 MB",
                            ConversionSuccessRate = 99.2
                        }
                    },
                    Cache = new ImageCacheMetrics
                    {
                        TotalEntries = await _appDb.ImageCaches.CountAsync(),
                        HitRate = 89.6
                    },
                    Annotations = new AnnotationMetrics
                    {
                        Total = await SafeCountAsync(_appDb.ContainerAnnotations.Where(a => !a.IsDeleted), "ContainerAnnotations"),
                        Created24h = await SafeCountAsync(_appDb.ContainerAnnotations.Where(a => a.CreatedAt >= todayStart && a.CreatedAt < todayEnd), "ContainerAnnotations")
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image processing metrics");
                return new ImageProcessingMetrics();
            }
        }

        #endregion

        #region Vehicle Imports

        private async Task<DashboardVehicleStats> GetVehicleImportStatisticsAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
            var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek); // Start of week (Sunday)
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            try
            {
                var topMakes = await _downloadsDb.VehicleImports
                    .GroupBy(v => v.Make)
                    .Select(g => new TopMake { Make = g.Key ?? "Unknown", Count = g.Count() })
                    .OrderByDescending(m => m.Count)
                    .Take(5)
                    .ToListAsync();

                return new DashboardVehicleStats
                {
                    Statistics = new VehicleStats
                    {
                        TotalVehicles = await _downloadsDb.VehicleImports.CountAsync(),
                        ImportedToday = await _downloadsDb.VehicleImports.CountAsync(v => v.CreatedAt >= todayStart && v.CreatedAt < todayEnd),
                        ImportedThisWeek = await _downloadsDb.VehicleImports.CountAsync(v => v.CreatedAt >= weekStart),
                        ImportedThisMonth = await _downloadsDb.VehicleImports.CountAsync(v => v.CreatedAt >= monthStart)
                    },
                    TopMakes = topMakes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting vehicle import statistics");
                return new DashboardVehicleStats();
            }
        }

        #endregion

        #region File System

        private FileSystemStatus GetFileSystemStatus()
        {
            try
            {
                var paths = new Dictionary<string, FileSystemPath>();

                var fs6000BasePath = _configuration["FS6000:FileSync:BasePath"] ?? @"C:\Temp\FS6000";
                CheckPath(paths, "FS6000Processed", Path.Combine(fs6000BasePath, "Processed"));
                CheckPath(paths, "FS6000Archive", Path.Combine(fs6000BasePath, "Archive"));
                CheckPath(paths, "FS6000Failed", Path.Combine(fs6000BasePath, "Failed"));
                CheckPath(paths, "ICUMSDownloads", _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads");

                // Get disk space
                var diskSpace = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DiskSpaceInfo
                    {
                        Drive = d.Name,
                        TotalGB = d.TotalSize / 1024 / 1024 / 1024,
                        FreeGB = d.AvailableFreeSpace / 1024 / 1024 / 1024,
                        UsedGB = (d.TotalSize - d.AvailableFreeSpace) / 1024 / 1024 / 1024,
                        UsagePercent = (double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100,
                        Status = GetDiskStatus((double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100)
                    })
                    .ToList();

                return new FileSystemStatus
                {
                    Paths = paths,
                    DiskSpace = diskSpace
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file system status");
                return new FileSystemStatus();
            }
        }

        private void CheckPath(Dictionary<string, FileSystemPath> paths, string key, string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    var totalSize = files.Sum(f => new FileInfo(f).Length);

                    paths[key] = new FileSystemPath
                    {
                        Path = path,
                        Accessible = true,
                        FileCount = files.Length,
                        TotalSizeGB = totalSize / 1024.0 / 1024.0 / 1024.0,
                        RequiresAttention = key == "FS6000Failed" && files.Length > 0
                    };
                }
                else
                {
                    paths[key] = new FileSystemPath
                    {
                        Path = path,
                        Accessible = false
                    };
                }
            }
            catch
            {
                paths[key] = new FileSystemPath
                {
                    Path = path,
                    Accessible = false
                };
            }
        }

        private string GetDiskStatus(double usagePercent)
        {
            var criticalPercent = _configuration.GetValue<int>("Alerts:DiskUsageCriticalPercent", 90);
            var warningPercent = _configuration.GetValue<int>("Alerts:DiskUsageWarningPercent", 80);
            return usagePercent >= criticalPercent ? "Critical" :
                   usagePercent >= warningPercent ? "Warning" : "Healthy";
        }

        #endregion

        #region Performance Metrics

        private DashboardPerformanceMetrics GetPerformanceMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / 1024 / 1024;

                return new DashboardPerformanceMetrics
                {
                    CPU = new CPUMetrics
                    {
                        UsagePercent = 45.2, // TODO: Implement actual CPU monitoring
                        ProcessesRunning = Process.GetProcesses().Length,
                        Cores = Environment.ProcessorCount
                    },
                    Memory = new MemoryMetrics
                    {
                        APIProcessMB = memoryMB,
                        UsagePercent = 67.5 // TODO: Implement actual memory monitoring
                    },
                    Network = new NetworkMetrics
                    {
                        InternetConnected = true,
                        ICUMSReachable = true,
                        ASEDbReachable = true,
                        NetworkShareReachable = Directory.Exists(_configuration["FS6000:NetworkSharePath"] ?? @"Z:\23301FS01")
                    },
                    API = new APIMetrics
                    {
                        AvgResponseTime = "125ms",
                        ActiveConnections = 12 // TODO: Get from SignalR hub
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance metrics");
                return new DashboardPerformanceMetrics();
            }
        }

        #endregion

        #region Recent Activity

        private async Task<List<Core.Models.ActivityEvent>> GetRecentActivityAsync()
        {
            var activities = new List<Core.Models.ActivityEvent>();
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)

            try
            {
                // Recent FS6000 scans
                var recentFS6000 = await _appDb.FS6000Scans
                    .OrderByDescending(s => s.ScanTime)
                    .Take(20)
                    .ToListAsync();

                foreach (var scan in recentFS6000)
                {
                    activities.Add(new Core.Models.ActivityEvent
                    {
                        Timestamp = scan.ScanTime,
                        Type = "Scan",
                        Service = "FS6000",
                        Icon = "Scanner",
                        Message = $"Container {scan.ContainerNumber} scanned",
                        Severity = "Info",
                        ContainerNumber = scan.ContainerNumber
                    });
                }

                // Recent ASE scans
                var recentASE = await _appDb.AseScans
                    .OrderByDescending(s => s.ScanTime)
                    .Take(20)
                    .ToListAsync();

                foreach (var scan in recentASE)
                {
                    activities.Add(new Core.Models.ActivityEvent
                    {
                        Timestamp = scan.ScanTime,
                        Type = "Scan",
                        Service = "ASE",
                        Icon = "Scanner",
                        Message = $"Container {scan.ContainerNumber} scanned",
                        Severity = "Info",
                        ContainerNumber = scan.ContainerNumber
                    });
                }

                // Recent submissions
                var recentSubmissions = await _appDb.ICUMSSubmissionQueues
                    .Where(q => q.Status == "Completed" && q.UpdatedAt >= todayStart && q.UpdatedAt < todayEnd)
                    .OrderByDescending(q => q.UpdatedAt)
                    .Take(20)
                    .ToListAsync();

                foreach (var submission in recentSubmissions)
                {
                    activities.Add(new Core.Models.ActivityEvent
                    {
                        Timestamp = submission.UpdatedAt,
                        Type = "Submission",
                        Service = "ICUMSSubmission",
                        Icon = "CloudUpload",
                        Message = $"Container {submission.ContainerNumber} submitted to ICUMS",
                        Severity = "Success",
                        ContainerNumber = submission.ContainerNumber
                    });
                }

                return activities.OrderByDescending(a => a.Timestamp).Take(100).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent activity");
                return activities;
            }
        }

        #endregion

        #region Trend Data

        private async Task<TrendData> GetTrendDataAsync()
        {
            try
            {
                var hourlyData = new List<HourlyTrend>();
                var now = DateTime.UtcNow;

                // Get hourly scans for last 24 hours
                for (int h = 23; h >= 0; h--)
                {
                    var hourStart = now.AddHours(-h).Date.AddHours(now.AddHours(-h).Hour);
                    var hourEnd = hourStart.AddHours(1);

                    var aseCount = await _appDb.AseScans
                        .CountAsync(s => s.ScanTime >= hourStart && s.ScanTime < hourEnd);
                    var fs6000Count = await _appDb.FS6000Scans
                        .CountAsync(s => s.ScanTime >= hourStart && s.ScanTime < hourEnd);

                    hourlyData.Add(new HourlyTrend
                    {
                        Hour = hourStart.Hour,
                        ASE = aseCount,
                        FS6000 = fs6000Count,
                        Total = aseCount + fs6000Count
                    });
                }

                // Get daily scans for last 7 days
                var dailyData = new List<DailyTrend>();
                for (int d = 6; d >= 0; d--)
                {
                    var dayStart = now.AddDays(-d).Date;
                    var dayEnd = dayStart.AddDays(1);

                    var aseCount = await _appDb.AseScans
                        .CountAsync(s => s.ScanTime >= dayStart && s.ScanTime < dayEnd);
                    var fs6000Count = await _appDb.FS6000Scans
                        .CountAsync(s => s.ScanTime >= dayStart && s.ScanTime < dayEnd);

                    dailyData.Add(new DailyTrend
                    {
                        Date = dayStart.ToString("yyyy-MM-dd"),
                        ASE = aseCount,
                        FS6000 = fs6000Count,
                        Total = aseCount + fs6000Count
                    });
                }

                return new TrendData
                {
                    ScansPerHour24h = hourlyData,
                    ScansPerDay7d = dailyData
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trend data");
                return new TrendData();
            }
        }

        #endregion

        #region Error Statistics

        private async Task<ErrorStatistics> GetErrorStatisticsAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)

            try
            {
                var failedFiles = await _appDb.FS6000FileProcessings
                    .Where(f => f.ProcessingStatus == "Failed" && f.CreatedAt >= todayStart && f.CreatedAt < todayEnd)
                    .ToListAsync();

                var failedSubmissions = await _appDb.ICUMSSubmissionQueues
                    .Where(q => q.Status == "Failed" && q.UpdatedAt >= todayStart && q.UpdatedAt < todayEnd)
                    .ToListAsync();

                var recentErrors = new List<ErrorEvent>();

                foreach (var file in failedFiles.Take(10))
                {
                    recentErrors.Add(new ErrorEvent
                    {
                        Timestamp = file.CreatedAt,
                        Service = "FS6000Ingestion",
                        Severity = "Error",
                        Message = file.ErrorMessage ?? "Processing failed",
                        File = file.FileName
                    });
                }

                foreach (var submission in failedSubmissions.Take(10))
                {
                    recentErrors.Add(new ErrorEvent
                    {
                        Timestamp = submission.UpdatedAt,
                        Service = "ICUMSSubmission",
                        Severity = "Error",
                        Message = submission.ErrorMessage ?? "Submission failed",
                        ContainerNumber = submission.ContainerNumber
                    });
                }

                return new ErrorStatistics
                {
                    Errors24h = failedFiles.Count + failedSubmissions.Count,
                    RecentErrors = recentErrors.OrderByDescending(e => e.Timestamp).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting error statistics");
                return new ErrorStatistics();
            }
        }

        #endregion

        #region Alerts

        private async Task<AlertsSummary> GetAlertsAsync()
        {
            var alerts = new List<Alert>();

            try
            {
                // INDUSTRY STANDARD: Queue backlog > 5 items
                var downloadQueueSize = await _downloadsDb.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == "Pending");

                if (downloadQueueSize > 5)
                {
                    alerts.Add(new Alert
                    {
                        Id = "queue-backlog-download",
                        Severity = "High",
                        Type = "QueueBacklog",
                        Message = $"ICUMS Download Queue has {downloadQueueSize} pending requests (threshold: 5)",
                        Service = "ICUMSDownloadQueue",
                        CreatedAt = DateTime.UtcNow,
                        Acknowledged = false
                    });
                }

                // INDUSTRY STANDARD: Failed files > 0
                // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
                var todayStart = DateTime.UtcNow.Date;
                var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
                var failedFiles = await _appDb.FS6000FileProcessings
                    .CountAsync(f => f.ProcessingStatus == "Failed" && f.CreatedAt >= todayStart && f.CreatedAt < todayEnd);

                if (failedFiles > 0)
                {
                    alerts.Add(new Alert
                    {
                        Id = "failed-files-fs6000",
                        Severity = "High",
                        Type = "FailedFiles",
                        Message = $"{failedFiles} FS6000 files failed processing in last 24h",
                        Service = "FS6000Ingestion",
                        CreatedAt = DateTime.UtcNow,
                        Acknowledged = false,
                        ActionRequired = @"Review failed files in C:\Temp\FS6000\Failed"
                    });
                }

                // INDUSTRY STANDARD: Disk space thresholds
                var diskWarningPercent = _configuration.GetValue<int>("Alerts:DiskUsageWarningPercent", 80);
                var diskCriticalPercent = _configuration.GetValue<int>("Alerts:DiskUsageCriticalPercent", 90);
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
                foreach (var drive in drives)
                {
                    var usagePercent = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100;
                    if (usagePercent > diskWarningPercent)
                    {
                        alerts.Add(new Alert
                        {
                            Id = $"disk-space-{drive.Name}",
                            Severity = usagePercent > diskCriticalPercent ? "Critical" : "Medium",
                            Type = "DiskSpace",
                            Message = $"Disk {drive.Name} usage at {usagePercent:F1}%",
                            Service = "FileSystem",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                return new AlertsSummary
                {
                    Critical = alerts.Count(a => a.Severity == "Critical"),
                    High = alerts.Count(a => a.Severity == "High"),
                    Medium = alerts.Count(a => a.Severity == "Medium"),
                    Low = alerts.Count(a => a.Severity == "Low"),
                    ActiveAlerts = alerts.OrderByDescending(a => a.CreatedAt).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alerts");
                return new AlertsSummary();
            }
        }

        #endregion

        #region User Activity & RBAC

        private async Task<UserActivitySummary> GetUserActivityAsync()
        {
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) instead of 24-hour rolling window
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)

            try
            {
                var activeUsers = await _appDb.Users
                    .Where(u => u.LastLoginAt >= todayStart && u.LastLoginAt < todayEnd)
                    .CountAsync();

                return new UserActivitySummary
                {
                    ActiveUsers24h = activeUsers,
                    TotalLogins24h = activeUsers // TODO: Implement actual login tracking
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user activity");
                return new UserActivitySummary();
            }
        }

        private async Task<RBACStatusSummary> GetRBACStatusAsync()
        {
            try
            {
                var roleDistribution = await _appDb.Users
                    .Where(u => u.IsActive)
                    .GroupBy(u => u.Role)
                    .Select(g => new { Role = g.Key.ToString(), Count = g.Count() })
                    .ToDictionaryAsync(x => x.Role, x => x.Count);

                return new RBACStatusSummary
                {
                    TotalPermissions = await _appDb.Permissions.CountAsync(),
                    TotalRoles = await _appDb.Roles.CountAsync(),
                    TotalUsers = await _appDb.Users.CountAsync(),
                    ActiveUsers = await _appDb.Users.CountAsync(u => u.IsActive),
                    RoleDistribution = roleDistribution
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RBAC status");
                return new RBACStatusSummary();
            }
        }

        #endregion

        #region Supporting Methods

        private ICUMSIntegrationStatus GetICUMSIntegrationStatus()
        {
            // TODO: Implement actual ICUMS integration monitoring
            return new ICUMSIntegrationStatus
            {
                APIStatus = "Connected"
            };
        }

        private List<ActiveOperation> GetCurrentOperations()
        {
            // TODO: Implement actual operation tracking
            return new List<ActiveOperation>();
        }

        #endregion
    }
}

