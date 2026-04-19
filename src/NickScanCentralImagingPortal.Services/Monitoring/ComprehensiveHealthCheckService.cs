using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Monitoring;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ASE;
using NickScanCentralImagingPortal.Services.FS6000;
using NickScanCentralImagingPortal.Services.IcumApi;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Comprehensive health check service that monitors every aspect of the system
    /// </summary>
    public class ComprehensiveHealthCheckService : BackgroundService, IComprehensiveHealthCheckService
    {
        private readonly ILogger<ComprehensiveHealthCheckService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, ServiceHealthStatus> _serviceStatuses = new();
        private readonly Dictionary<string, DateTime> _lastHealthCheckTimes = new();
        private readonly Dictionary<string, int> _consecutiveFailures = new();
        private readonly object _lockObject = new();
        private const string SERVICE_ID = "[HEALTH-CHECK]";

        // Health check intervals (in minutes)
        private readonly Dictionary<string, int> _healthCheckIntervals = new()
        {
            { "Database", 1 },
            { "FileSystem", 2 },
            { "Network", 5 },
            { "FS6000BackgroundService", 1 },
            { "AseBackgroundService", 2 },
            { "IcumBackgroundService", 5 },
            { "ImageProcessingService", 1 },
            { "FileSyncService", 2 },
            { "IngestionService", 1 },
            { "WebAPI", 1 },
            { "WebApp", 2 },
            { "SystemResources", 1 },
            { "ImageAnalysisOrchestrator", 2 },  // H3: Track Intake/Assignment workflow failures
            { "RawImageEngine", 2 },  // Python raw image decoding service (port 5320)
            { "AssignmentQueue", 2 }  // Materialized AnalysisQueueEntries cache invariant check + auto-repair
        };

        public ComprehensiveHealthCheckService(
            ILogger<ComprehensiveHealthCheckService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Comprehensive Health Check Service starting...");

            var startupDelaySeconds = _configuration.GetValue<int>("BackgroundServices:ComprehensiveHealthCheck:StartupDelaySeconds", 30);
            if (startupDelaySeconds > 0)
            {
                _logger.LogDebug("Comprehensive Health Check Service staggering startup: {Seconds}s delay", startupDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
            }

            // Initialize all service statuses
            InitializeServiceStatuses();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformComprehensiveHealthChecks(stoppingToken);
                    await LogSystemSummary();
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in comprehensive health check service");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private void InitializeServiceStatuses()
        {
            var services = new[]
            {
                "Database", "FileSystem", "Network", "FS6000BackgroundService",
                "AseBackgroundService", "IcumBackgroundService", "ImageProcessingService",
                "FileSyncService", "IngestionService", "WebAPI", "WebApp", "SystemResources",
                "ImageAnalysisOrchestrator", "RawImageEngine", "AssignmentQueue"
            };

            foreach (var service in services)
            {
                _serviceStatuses[service] = new ServiceHealthStatus
                {
                    ServiceName = service,
                    Status = HealthStatus.Unknown,
                    LastChecked = DateTime.MinValue,
                    ResponseTimeMs = 0,
                    ErrorMessage = null,
                    AdditionalInfo = new Dictionary<string, object>()
                };
            }
        }

        private async Task PerformComprehensiveHealthChecks(CancellationToken stoppingToken)
        {
            var tasks = new List<Task>
            {
                CheckDatabaseHealth(stoppingToken),
                CheckFileSystemHealth(stoppingToken),
                CheckNetworkHealth(stoppingToken),
                CheckFS6000BackgroundServiceHealth(stoppingToken),
                CheckAseBackgroundServiceHealth(stoppingToken),
                CheckIcumBackgroundServiceHealth(stoppingToken),
                CheckImageProcessingServiceHealth(stoppingToken),
                CheckFileSyncServiceHealth(stoppingToken),
                CheckIngestionServiceHealth(stoppingToken),
                CheckWebAPIHealth(stoppingToken),
                CheckWebAppHealth(stoppingToken),
                CheckSystemResourcesHealth(stoppingToken),
                CheckImageAnalysisOrchestratorHealth(stoppingToken),
                CheckRawImageEngineHealth(stoppingToken),
                CheckAssignmentQueueHealth(stoppingToken)
            };

            await Task.WhenAll(tasks);
        }

        #region Individual Health Checks

        private async Task CheckDatabaseHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("Database")) return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Test database connectivity
                await dbContext.Database.CanConnectAsync(stoppingToken);

                // Test a simple query
                var containerCount = await dbContext.Containers.CountAsync(stoppingToken);
                var fs6000ScanCount = await dbContext.FS6000Scans.CountAsync(stoppingToken);

                stopwatch.Stop();
                UpdateServiceStatus("Database", HealthStatus.Healthy, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "ContainerCount", containerCount },
                        { "FS6000ScanCount", fs6000ScanCount },
                        { "ConnectionString", "Connected" }
                    });

                _logger.LogDebug("✅ Database health check passed - Containers: {ContainerCount}, FS6000 Scans: {ScanCount}",
                    containerCount, fs6000ScanCount);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("Database", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ Database health check failed");
            }
        }

        private async Task CheckFileSystemHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("FileSystem")) return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var stagingPath = _configuration["FS6000:FileSync:DestinationDirectory"]
                    ?? @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Staging";
                var configuredSource = _configuration["FS6000:FileSync:SourceDirectory"] ?? @"Z:\23301FS01";
                var uncFallback = _configuration["FS6000:FileSync:NetworkSharePath"] ?? @"\\172.16.1.1\image\23301FS01";
                var networkPath = configuredSource.Length >= 2 && configuredSource[1] == ':'
                    ? uncFallback
                    : configuredSource;

                var stagingExists = Directory.Exists(stagingPath);

                var networkExists = false;
                try { networkExists = Directory.Exists(networkPath); }
                catch { /* network drive may be unreachable */ }

                // Test write access to staging directory
                var writeAccess = false;
                if (stagingExists)
                {
                    try
                    {
                        var testFile = Path.Combine(stagingPath, $"health_check_{Guid.NewGuid()}.tmp");
                        await File.WriteAllTextAsync(testFile, "health check", stoppingToken);
                        File.Delete(testFile);
                        writeAccess = true;
                    }
                    catch { /* write access denied */ }
                }

                var status = (stagingExists && networkExists) ? HealthStatus.Healthy
                    : stagingExists ? HealthStatus.Degraded
                    : HealthStatus.Unhealthy;

                stopwatch.Stop();
                UpdateServiceStatus("FileSystem", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "StagingPathExists", stagingExists },
                        { "NetworkPathExists", networkExists },
                        { "StagingPath", stagingPath },
                        { "NetworkPath", networkPath },
                        { "WriteAccess", writeAccess }
                    });

                _logger.LogDebug("✅ File system health check passed - Staging: {StagingExists}, Network: {NetworkExists}",
                    stagingExists, networkExists);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("FileSystem", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ File system health check failed");
            }
        }

        private async Task CheckNetworkHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("Network")) return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                // Test internet connectivity
                var connectivityUrl = _configuration["HealthChecks:InternetCheckUrl"] ?? "https://www.google.com";
                var internetResponse = await httpClient.GetAsync(connectivityUrl, stoppingToken);

                // Test ICUMS API connectivity (if configured)
                var icumsHealthy = true;
                try
                {
                    // This would be your actual ICUMS API endpoint
                    // var icumsResponse = await httpClient.GetAsync("https://your-icums-api.com/health", stoppingToken);
                    // icumsHealthy = icumsResponse.IsSuccessStatusCode;
                }
                catch
                {
                    icumsHealthy = false;
                }

                var status = internetResponse.IsSuccessStatusCode ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("Network", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "InternetConnectivity", internetResponse.IsSuccessStatusCode },
                        { "ICUMSConnectivity", icumsHealthy },
                        { "InternetStatusCode", (int)internetResponse.StatusCode }
                    });

                _logger.LogDebug("✅ Network health check passed - Internet: {InternetStatus}, ICUMS: {IcumStatus}",
                    internetResponse.IsSuccessStatusCode, icumsHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("Network", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ Network health check failed");
            }
        }

        private Task CheckFS6000BackgroundServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("FS6000BackgroundService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var fileSyncService = scope.ServiceProvider.GetService<IFileSyncService>();
                var ingestionService = scope.ServiceProvider.GetService<IIngestionService>();

                var fileSyncHealthy = fileSyncService != null;
                var ingestionHealthy = ingestionService != null;

                var status = fileSyncHealthy && ingestionHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("FS6000BackgroundService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "FileSyncServiceAvailable", fileSyncHealthy },
                        { "IngestionServiceAvailable", ingestionHealthy }
                    });

                _logger.LogDebug("✅ FS6000 Background Service health check passed - FileSync: {FileSync}, Ingestion: {Ingestion}",
                    fileSyncHealthy, ingestionHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("FS6000BackgroundService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ FS6000 Background Service health check failed");
            }

            return Task.CompletedTask;
        }

        private Task CheckAseBackgroundServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("AseBackgroundService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aseService = scope.ServiceProvider.GetService<IAseDatabaseSyncService>();

                var aseHealthy = aseService != null;
                var status = aseHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("AseBackgroundService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "AseServiceAvailable", aseHealthy }
                    });

                _logger.LogDebug("✅ ASE Background Service health check passed - ASE Service: {AseHealthy}", aseHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("AseBackgroundService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ ASE Background Service health check failed");
            }

            return Task.CompletedTask;
        }

        private Task CheckIcumBackgroundServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("IcumBackgroundService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var icumService = scope.ServiceProvider.GetService<IIcumApiService>();

                var icumHealthy = icumService != null;
                var status = icumHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("IcumBackgroundService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "IcumServiceAvailable", icumHealthy }
                    });

                _logger.LogDebug("✅ ICUMS Background Service health check passed - ICUMS Service: {IcumHealthy}", icumHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("IcumBackgroundService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ ICUMS Background Service health check failed");
            }

            return Task.CompletedTask;
        }

        private Task CheckImageProcessingServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("ImageProcessingService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var imageProcessingService = scope.ServiceProvider.GetService<IImageProcessingService>();

                var serviceHealthy = imageProcessingService != null;
                var status = serviceHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("ImageProcessingService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "ImageProcessingServiceAvailable", serviceHealthy }
                    });

                _logger.LogDebug("✅ Image Processing Service health check passed - Service: {ServiceHealthy}", serviceHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("ImageProcessingService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ Image Processing Service health check failed");
            }

            return Task.CompletedTask;
        }

        private Task CheckFileSyncServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("FileSyncService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var fileSyncService = scope.ServiceProvider.GetService<IFileSyncService>();

                var serviceHealthy = fileSyncService != null;
                var status = serviceHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("FileSyncService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "FileSyncServiceAvailable", serviceHealthy }
                    });

                _logger.LogDebug("✅ File Sync Service health check passed - Service: {ServiceHealthy}", serviceHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("FileSyncService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ File Sync Service health check failed");
            }

            return Task.CompletedTask;
        }

        private Task CheckIngestionServiceHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("IngestionService"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ingestionService = scope.ServiceProvider.GetService<IIngestionService>();

                var serviceHealthy = ingestionService != null;
                var status = serviceHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("IngestionService", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "IngestionServiceAvailable", serviceHealthy }
                    });

                _logger.LogDebug("✅ Ingestion Service health check passed - Service: {ServiceHealthy}", serviceHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("IngestionService", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ Ingestion Service health check failed");
            }

            return Task.CompletedTask;
        }

        private async Task CheckWebAPIHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("WebAPI")) return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var apiHealthUrl = _configuration["HealthChecks:ApiHealthUrl"] ?? "http://localhost:5205/health";
                var response = await httpClient.GetAsync(apiHealthUrl, stoppingToken);
                var apiHealthy = response.IsSuccessStatusCode;

                var status = apiHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

                stopwatch.Stop();
                UpdateServiceStatus("WebAPI", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "APIResponding", apiHealthy },
                        { "StatusCode", (int)response.StatusCode },
                        { "Port", 5299 }
                    });

                _logger.LogDebug("✅ Web API health check passed - API: {ApiHealthy}", apiHealthy);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("WebAPI", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);

                // ✅ FIX: Reduce log noise - connection refused is expected if service isn't running
                // Log at Warning level instead of Error for connection refused exceptions
                if (ex is System.Net.Http.HttpRequestException httpEx &&
                    httpEx.InnerException is System.Net.Sockets.SocketException socketEx &&
                    socketEx.ErrorCode == 10061)
                {
                    _logger.LogWarning("⚠️ Web API health check: Service not available (connection refused)");
                }
                else
                {
                    _logger.LogError(ex, "❌ Web API health check failed");
                }
            }
        }

        private async Task CheckWebAppHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("WebApp")) return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Disable auto-redirect: a 307 HTTP->HTTPS redirect is enough to prove WebApp is alive.
                // Following it would hit a self-signed cert and throw, producing false "unhealthy".
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var httpClient = new HttpClient(handler);
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var ports = new[] { 5299, 5000, 5126, 7263 };
                var webAppHealthy = false;
                var respondingPort = 0;

                foreach (var port in ports)
                {
                    try
                    {
                        var response = await httpClient.GetAsync($"http://localhost:{port}", stoppingToken);
                        // Any HTTP response (2xx/3xx/4xx/5xx) proves the WebApp is listening.
                        // We only reject 0 (connection failure) and exceptions.
                        var statusCode = (int)response.StatusCode;
                        if (statusCode >= 200 && statusCode < 600)
                        {
                            webAppHealthy = true;
                            respondingPort = port;
                            break;
                        }
                    }
                    catch
                    {
                        // Continue to next port
                    }
                }

                var status = webAppHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

                stopwatch.Stop();
                UpdateServiceStatus("WebApp", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "WebAppResponding", webAppHealthy },
                        { "RespondingPort", respondingPort },
                        { "CheckedPorts", ports }
                    });

                _logger.LogDebug("✅ Web App health check passed - WebApp: {WebAppHealthy}, Port: {Port}",
                    webAppHealthy, respondingPort);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("WebApp", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ Web App health check failed");
            }
        }

        private Task CheckSystemResourcesHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("SystemResources"))
                return Task.CompletedTask;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryUsage = process.WorkingSet64 / 1024 / 1024; // MB
                var cpuUsage = process.TotalProcessorTime.TotalMilliseconds;

                // Get available disk space
                var drive = new DriveInfo("C:");
                var freeSpaceGB = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                var totalSpaceGB = drive.TotalSize / 1024 / 1024 / 1024;

                // Determine health status based on thresholds
                var memoryHealthy = memoryUsage < 1000; // Less than 1GB
                var diskHealthy = freeSpaceGB > 5; // More than 5GB free

                var status = memoryHealthy && diskHealthy ? HealthStatus.Healthy :
                           memoryUsage < 2000 && freeSpaceGB > 2 ? HealthStatus.Degraded : HealthStatus.Unhealthy;

                stopwatch.Stop();
                UpdateServiceStatus("SystemResources", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "MemoryUsageMB", memoryUsage },
                        { "MemoryHealthy", memoryHealthy },
                        { "CpuUsageMs", cpuUsage },
                        { "FreeSpaceGB", freeSpaceGB },
                        { "TotalSpaceGB", totalSpaceGB },
                        { "DiskHealthy", diskHealthy },
                        { "DiskUsagePercent", (double)(totalSpaceGB - freeSpaceGB) / totalSpaceGB * 100 }
                    });

                _logger.LogDebug("✅ System Resources health check passed - Memory: {MemoryMB}MB, Disk: {FreeGB}GB free",
                    memoryUsage, freeSpaceGB);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("SystemResources", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ System Resources health check failed");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// H3: Check ImageAnalysisOrchestrator workflow health via ServiceHealthMonitor.
        /// Surfaces Intake/Assignment failures to health dashboard.
        /// </summary>
        private async Task CheckImageAnalysisOrchestratorHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("ImageAnalysisOrchestrator")) return;

            await Task.CompletedTask; // Satisfy async; no I/O

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var monitor = _serviceProvider.GetService<ServiceHealthMonitor>();
                if (monitor == null)
                {
                    UpdateServiceStatus("ImageAnalysisOrchestrator", HealthStatus.Unknown, 0, "ServiceHealthMonitor not registered");
                    return;
                }

                var intakeMetrics = monitor.GetWorkflowMetrics("ImageAnalysisOrchestrator", "Intake");
                var assignmentMetrics = monitor.GetWorkflowMetrics("ImageAnalysisOrchestrator", "Assignment");
                var failedIntake = intakeMetrics?.FailedExecutions ?? 0;
                var failedAssignment = assignmentMetrics?.FailedExecutions ?? 0;
                var totalIntake = intakeMetrics?.TotalExecutions ?? 0;
                var totalAssignment = assignmentMetrics?.TotalExecutions ?? 0;

                var hasRecentFailures = failedIntake > 0 || failedAssignment > 0;
                var status = hasRecentFailures ? HealthStatus.Degraded : HealthStatus.Healthy;

                stopwatch.Stop();
                var additionalInfo = new Dictionary<string, object>
                {
                    { "IntakeTotal", totalIntake },
                    { "IntakeFailed", failedIntake },
                    { "AssignmentTotal", totalAssignment },
                    { "AssignmentFailed", failedAssignment },
                    { "LastIntakeExec", intakeMetrics?.LastExecutionTime.ToString("o") ?? "Never" },
                    { "LastAssignmentExec", assignmentMetrics?.LastExecutionTime.ToString("o") ?? "Never" }
                };
                var lastIntakeError = NickScanCentralImagingPortal.Services.ImageAnalysis.ImageAnalysisOrchestratorService.GetLastIntakeError();
                if (!string.IsNullOrEmpty(lastIntakeError))
                    additionalInfo["LastIntakeError"] = lastIntakeError;
                UpdateServiceStatus("ImageAnalysisOrchestrator", status, stopwatch.ElapsedMilliseconds,
                    additionalInfo: additionalInfo);

                if (hasRecentFailures)
                    _logger.LogWarning("[HEALTH] ImageAnalysisOrchestrator Degraded: Intake failed={Intake}, Assignment failed={Assignment}",
                        failedIntake, failedAssignment);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("ImageAnalysisOrchestrator", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "❌ ImageAnalysisOrchestrator health check failed");
            }
        }

        private async Task CheckRawImageEngineHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("RawImageEngine"))
                return;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var httpFactory = scope.ServiceProvider.GetService<IHttpClientFactory>();
                if (httpFactory == null)
                {
                    UpdateServiceStatus("RawImageEngine", HealthStatus.Unknown, 0, "IHttpClientFactory not registered");
                    return;
                }

                var client = httpFactory.CreateClient("RawImageEngine");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await client.GetAsync("/inspector/health", cts.Token);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    UpdateServiceStatus("RawImageEngine", HealthStatus.Healthy, stopwatch.ElapsedMilliseconds,
                        additionalInfo: new Dictionary<string, object>
                        {
                            { "Port", 5320 },
                            { "ResponseTimeMs", stopwatch.ElapsedMilliseconds }
                        });
                    _logger.LogDebug("{ServiceId} Raw Image Engine health check passed ({Ms}ms)", SERVICE_ID, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    UpdateServiceStatus("RawImageEngine", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds,
                        $"HTTP {(int)response.StatusCode}");
                    _logger.LogWarning("{ServiceId} Raw Image Engine returned {Status}", SERVICE_ID, response.StatusCode);
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                UpdateServiceStatus("RawImageEngine", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, "Health check timed out (10s)");
                _logger.LogError("{ServiceId} Raw Image Engine health check timed out", SERVICE_ID);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("RawImageEngine", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, "Service unreachable");
                _logger.LogError(ex, "{ServiceId} Raw Image Engine unreachable", SERVICE_ID);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("RawImageEngine", HealthStatus.Unhealthy, stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "{ServiceId} Raw Image Engine health check failed", SERVICE_ID);
            }
        }

        /// <summary>
        /// Validates the AnalysisQueueEntries materialized cache is consistent with
        /// the source-of-truth AnalysisAssignments table. On divergence, auto-triggers
        /// ReadyGroupsCacheService.ReconcileQueueAsync to repair the queue.
        ///
        /// This check exists because the queue is maintained via raw SQL upserts that
        /// can silently fail on EF Core / Npgsql version changes (as happened 2026-04-14).
        /// When upserts fail, the queue drifts from truth and analysts see empty lists.
        /// This check catches the drift within 2 minutes and self-heals.
        /// </summary>
        private async Task CheckAssignmentQueueHealth(CancellationToken stoppingToken)
        {
            if (!ShouldCheckService("AssignmentQueue"))
                return;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTime.UtcNow;

                // Count source-of-truth active assignments
                var activeCount = await db.AnalysisAssignments
                    .AsNoTracking()
                    .CountAsync(a => a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);

                // Count materialized queue entries
                var queueCount = await db.AnalysisQueueEntries.AsNoTracking().CountAsync(stoppingToken);

                var divergence = activeCount > 0
                    ? Math.Abs(activeCount - queueCount) / (double)activeCount
                    : 0.0;

                var status = HealthStatus.Healthy;
                string? errorMessage = null;
                bool autoRepairTriggered = false;
                bool autoRepairSucceeded = false;

                bool needsRepair = (queueCount == 0 && activeCount > 0) || divergence > 0.10;

                if (needsRepair)
                {
                    status = HealthStatus.Degraded;
                    errorMessage = queueCount == 0 && activeCount > 0
                        ? $"Empty queue with {activeCount} active assignments"
                        : $"Divergence {(divergence * 100):F1}% exceeds 10% threshold";

                    _logger.LogWarning(
                        "{ServiceId} [HEALTH] AssignmentQueue degraded: {Reason}. Triggering auto-repair...",
                        SERVICE_ID, errorMessage);

                    autoRepairTriggered = true;
                    try
                    {
                        var cache = scope.ServiceProvider.GetService<NickScanCentralImagingPortal.Services.ImageAnalysis.ReadyGroupsCacheService>();
                        if (cache != null)
                        {
                            await cache.ReconcileQueueAsync(db, stoppingToken);
                            autoRepairSucceeded = true;
                            status = HealthStatus.Healthy;
                            errorMessage = $"Auto-repaired via reconciliation (was: {errorMessage})";

                            _logger.LogInformation(
                                "{ServiceId} [HEALTH] AssignmentQueue auto-repair succeeded",
                                SERVICE_ID);
                        }
                        else
                        {
                            status = HealthStatus.Unhealthy;
                            errorMessage = "ReadyGroupsCacheService not registered — cannot auto-repair";
                            _logger.LogError("{ServiceId} [HEALTH] {Msg}", SERVICE_ID, errorMessage);
                        }
                    }
                    catch (Exception repairEx)
                    {
                        status = HealthStatus.Unhealthy;
                        errorMessage = $"Auto-repair failed: {repairEx.Message}";
                        _logger.LogError(repairEx,
                            "{ServiceId} [HEALTH] AssignmentQueue auto-repair failed",
                            SERVICE_ID);
                    }
                }

                stopwatch.Stop();

                UpdateServiceStatus("AssignmentQueue", status, stopwatch.ElapsedMilliseconds,
                    errorMessage: errorMessage,
                    additionalInfo: new Dictionary<string, object>
                    {
                        { "ActiveAssignments", activeCount },
                        { "QueueEntries", queueCount },
                        { "DivergencePercent", Math.Round(divergence * 100, 2) },
                        { "AutoRepairTriggered", autoRepairTriggered },
                        { "AutoRepairSucceeded", autoRepairSucceeded }
                    });

                if (status == HealthStatus.Healthy && !needsRepair)
                {
                    _logger.LogDebug(
                        "{ServiceId} [HEALTH] AssignmentQueue healthy: {Active} active, {Queue} queued",
                        SERVICE_ID, activeCount, queueCount);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateServiceStatus("AssignmentQueue", HealthStatus.Unhealthy,
                    stopwatch.ElapsedMilliseconds, ex.Message);
                _logger.LogError(ex, "{ServiceId} [HEALTH] AssignmentQueue health check failed", SERVICE_ID);
            }
        }

        #endregion

        #region Helper Methods

        private bool ShouldCheckService(string serviceName)
        {
            lock (_lockObject)
            {
                if (!_healthCheckIntervals.ContainsKey(serviceName))
                    return true;

                var lastCheck = _lastHealthCheckTimes.GetValueOrDefault(serviceName, DateTime.MinValue);
                var interval = _healthCheckIntervals[serviceName];

                return DateTime.UtcNow.Subtract(lastCheck).TotalMinutes >= interval;
            }
        }

        private void UpdateServiceStatus(string serviceName, HealthStatus status, long responseTimeMs,
            string? errorMessage = null, Dictionary<string, object>? additionalInfo = null)
        {
            lock (_lockObject)
            {
                _lastHealthCheckTimes[serviceName] = DateTime.UtcNow;

                if (_serviceStatuses.ContainsKey(serviceName))
                {
                    _serviceStatuses[serviceName].Status = status;
                    _serviceStatuses[serviceName].LastChecked = DateTime.UtcNow;
                    _serviceStatuses[serviceName].ResponseTimeMs = responseTimeMs;
                    _serviceStatuses[serviceName].ErrorMessage = errorMessage;

                    if (additionalInfo != null)
                    {
                        foreach (var kvp in additionalInfo)
                        {
                            _serviceStatuses[serviceName].AdditionalInfo[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Track consecutive failures
                if (status == HealthStatus.Unhealthy)
                {
                    _consecutiveFailures[serviceName] = _consecutiveFailures.GetValueOrDefault(serviceName, 0) + 1;
                }
                else
                {
                    _consecutiveFailures[serviceName] = 0;
                }
            }
        }

        private Task LogSystemSummary()
        {
            lock (_lockObject)
            {
                var healthyServices = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Healthy);
                var degradedServices = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Degraded);
                var unhealthyServices = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Unhealthy);
                var totalServices = _serviceStatuses.Count;

                var overallHealth = unhealthyServices == 0 ? degradedServices == 0 ? "🟢 HEALTHY" : "🟡 DEGRADED" : "🔴 UNHEALTHY";

                _logger.LogInformation("{ServiceId} 📊 SYSTEM HEALTH SUMMARY - {OverallHealth} | Healthy: {Healthy}/{Total}, Degraded: {Degraded}, Unhealthy: {Unhealthy}",
                    SERVICE_ID, overallHealth, healthyServices, totalServices, degradedServices, unhealthyServices);

                // Log unhealthy services
                var unhealthyServiceList = _serviceStatuses.Where(s => s.Value.Status == HealthStatus.Unhealthy).ToList();
                if (unhealthyServiceList.Any())
                {
                    _logger.LogWarning("🚨 UNHEALTHY SERVICES: {UnhealthyServices}",
                        string.Join(", ", unhealthyServiceList.Select(s => $"{s.Key} ({s.Value.ErrorMessage})")));
                }

                // Log degraded services
                var degradedServiceList = _serviceStatuses.Where(s => s.Value.Status == HealthStatus.Degraded).ToList();
                if (degradedServiceList.Any())
                {
                    _logger.LogWarning("{ServiceId} ⚠️ DEGRADED SERVICES: {DegradedServices}",
                        SERVICE_ID, string.Join(", ", degradedServiceList.Select(s => s.Key)));
                }
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Public Methods for External Access

        public Dictionary<string, ServiceHealthStatus> GetServiceStatuses()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, ServiceHealthStatus>(_serviceStatuses);
            }
        }

        public ServiceHealthStatus GetServiceStatus(string serviceName)
        {
            lock (_lockObject)
            {
                return _serviceStatuses.GetValueOrDefault(serviceName, new ServiceHealthStatus
                {
                    ServiceName = serviceName,
                    Status = HealthStatus.Unknown,
                    LastChecked = DateTime.MinValue
                });
            }
        }

        public SystemHealthSummary GetSystemHealthSummary()
        {
            lock (_lockObject)
            {
                var healthyCount = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Healthy);
                var degradedCount = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Degraded);
                var unhealthyCount = _serviceStatuses.Count(s => s.Value.Status == HealthStatus.Unhealthy);
                var totalCount = _serviceStatuses.Count;

                var overallStatus = unhealthyCount > 0 ? HealthStatus.Unhealthy :
                                  degradedCount > 0 ? HealthStatus.Degraded : HealthStatus.Healthy;

                return new SystemHealthSummary
                {
                    OverallStatus = overallStatus,
                    Timestamp = DateTime.UtcNow,
                    TotalServices = totalCount,
                    HealthyServices = healthyCount,
                    DegradedServices = degradedCount,
                    UnhealthyServices = unhealthyCount,
                    ServiceStatuses = new Dictionary<string, ServiceHealthStatus>(_serviceStatuses)
                };
            }
        }

        #endregion
    }

}
