using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ServiceLifecycle;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class SystemAdminController : ControllerBase
    {
        private readonly ILogger<SystemAdminController> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IServiceLifecycleManager? _lifecycleManager;

        public SystemAdminController(
            ILogger<SystemAdminController> logger,
            IServiceProvider serviceProvider,
            IHostApplicationLifetime appLifetime,
            IServiceLifecycleManager? lifecycleManager = null)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _appLifetime = appLifetime;
            _lifecycleManager = lifecycleManager;
        }

        [HttpGet("services")]
        public async Task<ActionResult<List<ServiceInfo>>> GetAllServices()
        {
            try
            {
                var services = new List<ServiceInfo>();

                // Get all registered hosted services from the service provider
                var hostedServices = _serviceProvider.GetServices<IHostedService>();

                // Get service statuses from lifecycle manager if available
                Dictionary<string, ServiceStatus>? statuses = null;
                if (_lifecycleManager != null)
                {
                    statuses = _lifecycleManager.GetAllServiceStatuses();
                }

                foreach (var service in hostedServices)
                {
                    var serviceType = service.GetType();
                    var serviceName = GetServiceDisplayName(serviceType);
                    var typeName = serviceType.Name;

                    // Get real status from lifecycle manager if available
                    string status = "Unknown";
                    TimeSpan? uptime = null;
                    DateTime? lastRestart = null;
                    bool isHealthy = false;

                    if (_lifecycleManager != null)
                    {
                        // Try to find service by type name or display name
                        var managedService = _lifecycleManager.GetService(typeName) ??
                                           _lifecycleManager.GetService(serviceName);

                        if (managedService != null)
                        {
                            var serviceStatus = managedService.Status;
                            status = serviceStatus.ToString();

                            // Get health status
                            try
                            {
                                isHealthy = await managedService.IsHealthyAsync();
                            }
                            catch
                            {
                                isHealthy = serviceStatus == ServiceStatus.Running;
                            }

                            // Get uptime and last restart from manager
                            var manager = _lifecycleManager as ServiceLifecycleManager;
                            if (manager != null)
                            {
                                lastRestart = manager.GetLastRestartTime(typeName) ??
                                            manager.GetLastRestartTime(serviceName);
                                uptime = manager.GetServiceUptime(typeName) ??
                                        manager.GetServiceUptime(serviceName);
                            }
                        }
                        else if (statuses != null && statuses.TryGetValue(typeName, out var serviceStatus))
                        {
                            status = serviceStatus.ToString();
                            isHealthy = serviceStatus == ServiceStatus.Running;
                        }
                        else
                        {
                            // Fallback: assume running if registered
                            status = "Running";
                            isHealthy = true;
                        }
                    }
                    else
                    {
                        // Fallback to old method
                        status = GetServiceStatus(service);
                        isHealthy = status == "Running";
                    }

                    var serviceInfo = new ServiceInfo
                    {
                        Name = serviceName,
                        Type = "Background Service",
                        Status = status,
                        Description = GetServiceDescription(serviceType),
                        LastRun = DateTime.UtcNow, // Placeholder
                        LastRestart = lastRestart,
                        Uptime = uptime,
                        HealthStatus = isHealthy ? "Healthy" : (status == "Failed" ? "Unhealthy" : "Degraded"),
                        CanRestart = _lifecycleManager != null,
                        CanStop = _lifecycleManager != null && status == "Running",
                        CanStart = _lifecycleManager != null && (status == "Stopped" || status == "Failed")
                    };

                    services.Add(serviceInfo);
                }

                // Add additional services that might not be IHostedService but are important
                var additionalServices = new[]
                {
                    new ServiceInfo { Name = "API Server", Type = "Web Service", Status = "Running", Description = "Main API server handling HTTP requests", HealthStatus = "Healthy", CanRestart = false },
                    new ServiceInfo { Name = "Database Contexts", Type = "Data Service", Status = "Running", Description = "Entity Framework database contexts", HealthStatus = "Healthy", CanRestart = false },
                    new ServiceInfo { Name = "Memory Cache", Type = "Cache Service", Status = "Running", Description = "In-memory caching service", HealthStatus = "Healthy", CanRestart = false },
                    new ServiceInfo { Name = "HTTP Clients", Type = "Network Service", Status = "Running", Description = "HTTP client services for external APIs", HealthStatus = "Healthy", CanRestart = false }
                };

                services.AddRange(additionalServices);

                return Ok(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting services");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private string GetServiceDisplayName(Type serviceType)
        {
            var typeName = serviceType.Name;

            // Clean up service names for display (matches ServiceControlPanel for status lookup)
            return typeName switch
            {
                "ServiceOrchestratorBackgroundService" => "ServiceOrchestrator",
                "AseBackgroundService" => "AseDatabaseSyncService",
                "IcumBackgroundService" => "IcumBackgroundService",
                "IcumPipelineOrchestratorService" => "IcumPipelineOrchestratorService",
                "IcumFileScannerService" => "IcumFileScannerService",
                "IcumJsonIngestionService" => "IcumJsonIngestionService",
                "IcumDataTransferService" => "IcumDataTransferService",
                "ICUMSDownloadBackgroundService" => "ICUMSDownloadBackgroundService",
                "FS6000BackgroundService" => "FS6000BackgroundService",
                "ContainerCompletenessOrchestratorService" => "ContainerCompletenessOrchestratorService",
                "ImageAnalysisOrchestratorService" => "ImageAnalysisOrchestratorService",
                "ContainerCompletenessService" => "ContainerCompletenessService",
                "ManualBOESelectivityService" => "ManualBOESelectivityService",
                "ContainerDataMapperService" => "ContainerDataMapperService",
                "ICUMSSubmissionService" => "ICUMSSubmissionService",
                "DashboardBroadcastService" => "DashboardBroadcastService",
                _ => typeName.Replace("BackgroundService", "").Replace("Service", "")
            };
        }

        private string GetServiceStatus(IHostedService service)
        {
            // This is a simplified status check
            // In a real implementation, you'd track the actual service state
            try
            {
                // Check if service is running by attempting to access it
                var serviceType = service.GetType();

                // For now, assume all services are running if they're registered
                // In a production system, you'd implement proper health checking
                return "Running";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetServiceDescription(Type serviceType)
        {
            var typeName = serviceType.Name;

            return typeName switch
            {
                "ServiceOrchestratorBackgroundService" => "Manages and orchestrates all background services",
                "AseBackgroundService" => "Syncs ASE scanner data from external database",
                "IcumBackgroundService" => "Handles ICUMS API communication and data fetching",
                "IcumFileScannerService" => "Scans ICUMS downloads directory for new JSON files",
                "IcumJsonIngestionService" => "Processes and ingests ICUMS JSON files into database",
                "IcumDataTransferService" => "Transfers processed ICUMS data from downloads to main database",
                "ICUMSDownloadBackgroundService" => "Manages ICUMS download queue and processing",
                "FS6000BackgroundService" => "Processes FS6000 scanner XML files and images",
                "ContainerCompletenessService" => "Validates container data completeness across all sources",
                "ManualBOESelectivityService" => "Handles manual BOE selectivity processing",
                "ContainerDataMapperService" => "Maps and correlates container data from multiple sources",
                "ICUMSSubmissionService" => "Submits container data back to ICUMS system",
                _ => $"Background service: {typeName}"
            };
        }

        [HttpGet("system-info")]
        public ActionResult<SystemInfo> GetSystemInfo()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var info = new SystemInfo
                {
                    MachineName = Environment.MachineName,
                    OSVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    DotNetVersion = Environment.Version.ToString(),
                    WorkingSetMemoryMB = process.WorkingSet64 / 1024 / 1024,
                    TotalProcessorTime = process.TotalProcessorTime.ToString(),
                    StartTime = process.StartTime,
                    Uptime = DateTime.Now - process.StartTime
                };

                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting system info");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("logs")]
        public ActionResult<LogResponse> GetLogs([FromQuery] int count = 100, [FromQuery] string? level = null)
        {
            try
            {
                // This would read from your log persistence service
                var logs = new List<SystemLogEntry>();

                // Placeholder - integrate with your LogPersistenceService
                for (int i = 0; i < Math.Min(count, 50); i++)
                {
                    logs.Add(new SystemLogEntry
                    {
                        Timestamp = DateTime.Now.AddMinutes(-i),
                        Level = i % 5 == 0 ? "Error" : i % 3 == 0 ? "Warning" : "Info",
                        Message = $"Sample log message {i}",
                        Source = "System"
                    });
                }

                return Ok(new LogResponse { Logs = logs, TotalCount = logs.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting logs");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("service/{serviceName}/restart")]
        public async Task<ActionResult<ServiceRestartResponse>> RestartService(string serviceName)
        {
            try
            {
                _logger.LogInformation("[SYSTEM-ADMIN] Restart requested for service: {ServiceName} by user: {User}",
                    serviceName, User.Identity?.Name ?? "Unknown");

                if (_lifecycleManager == null)
                {
                    return StatusCode(503, new ServiceRestartResponse
                    {
                        ServiceName = serviceName,
                        Success = false,
                        Message = "Service lifecycle manager is not available",
                        RestartInitiatedAt = DateTime.UtcNow
                    });
                }

                var restartStartTime = DateTime.UtcNow;
                var success = await _lifecycleManager.RestartServiceAsync(serviceName);

                var response = new ServiceRestartResponse
                {
                    ServiceName = serviceName,
                    Status = _lifecycleManager.GetServiceStatus(serviceName).ToString(),
                    Success = success,
                    Message = success
                        ? "Service restarted successfully"
                        : "Service restart failed - check logs for details",
                    RestartInitiatedAt = restartStartTime,
                    RestartDuration = DateTime.UtcNow - restartStartTime
                };

                if (success)
                {
                    return Ok(response);
                }
                else
                {
                    return StatusCode(500, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error restarting service {ServiceName}", serviceName);
                return StatusCode(500, new ServiceRestartResponse
                {
                    ServiceName = serviceName,
                    Success = false,
                    Message = $"Error restarting service: {ex.Message}",
                    RestartInitiatedAt = DateTime.UtcNow
                });
            }
        }

        [HttpPost("service/{serviceName}/stop")]
        public async Task<ActionResult> StopService(string serviceName)
        {
            try
            {
                _logger.LogInformation("[SYSTEM-ADMIN] Stop requested for service: {ServiceName} by user: {User}",
                    serviceName, User.Identity?.Name ?? "Unknown");

                if (_lifecycleManager == null)
                {
                    return StatusCode(503, new { error = "Service lifecycle manager is not available" });
                }

                var success = await _lifecycleManager.StopServiceAsync(serviceName);

                if (success)
                {
                    return Ok(new { message = $"Service {serviceName} stopped successfully" });
                }
                else
                {
                    return StatusCode(500, new { error = $"Failed to stop service {serviceName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error stopping service {ServiceName}", serviceName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("service/{serviceName}/start")]
        public async Task<ActionResult> StartService(string serviceName)
        {
            try
            {
                _logger.LogInformation("[SYSTEM-ADMIN] Start requested for service: {ServiceName} by user: {User}",
                    serviceName, User.Identity?.Name ?? "Unknown");

                if (_lifecycleManager == null)
                {
                    return StatusCode(503, new { error = "Service lifecycle manager is not available" });
                }

                var success = await _lifecycleManager.StartServiceAsync(serviceName);

                if (success)
                {
                    return Ok(new { message = $"Service {serviceName} started successfully" });
                }
                else
                {
                    return StatusCode(500, new { error = $"Failed to start service {serviceName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error starting service {ServiceName}", serviceName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("service/{serviceName}/status")]
        public async Task<ActionResult<ServiceStatusDto>> GetServiceStatus(string serviceName)
        {
            try
            {
                if (_lifecycleManager == null)
                {
                    return StatusCode(503, new ServiceStatusDto
                    {
                        ServiceName = serviceName,
                        Status = "Unknown",
                        IsHealthy = false
                    });
                }

                var status = _lifecycleManager.GetServiceStatus(serviceName);
                var isHealthy = await _lifecycleManager.IsServiceHealthyAsync(serviceName);
                var manager = _lifecycleManager as ServiceLifecycleManager;

                var dto = new ServiceStatusDto
                {
                    ServiceName = serviceName,
                    Status = status.ToString(),
                    IsHealthy = isHealthy,
                    LastRestart = manager?.GetLastRestartTime(serviceName),
                    Uptime = manager?.GetServiceUptime(serviceName)
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting service status for {ServiceName}", serviceName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("service/{serviceName}/health")]
        public async Task<ActionResult<ServiceHealthDto>> GetServiceHealth(string serviceName)
        {
            try
            {
                if (_lifecycleManager == null)
                {
                    return StatusCode(503, new ServiceHealthDto
                    {
                        ServiceName = serviceName,
                        Status = "Unknown",
                        IsHealthy = false,
                        LastChecked = DateTime.UtcNow
                    });
                }

                var status = _lifecycleManager.GetServiceStatus(serviceName);
                var isHealthy = await _lifecycleManager.IsServiceHealthyAsync(serviceName);

                return Ok(new ServiceHealthDto
                {
                    ServiceName = serviceName,
                    Status = status.ToString(),
                    IsHealthy = isHealthy,
                    LastChecked = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting service health for {ServiceName}", serviceName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gracefully shuts down the application, allowing all background services to complete their work
        /// </summary>
        [HttpPost("shutdown")]
        public ActionResult Shutdown()
        {
            try
            {
                _logger.LogWarning("[SYSTEM-ADMIN] Graceful shutdown requested by user: {User}",
                    User.Identity?.Name ?? "Unknown");

                // Trigger graceful shutdown - this will:
                // 1. Stop accepting new requests
                // 2. Allow current requests to complete
                // 3. Signal all background services via CancellationToken
                // 4. Wait for services to complete (up to ShutdownTimeout)
                Task.Run(() =>
                {
                    // Give the response time to be sent before shutting down
                    Thread.Sleep(1000);
                    _appLifetime.StopApplication();
                });

                return Ok(new
                {
                    message = "Graceful shutdown initiated. The service will stop after current operations complete.",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error initiating shutdown");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gracefully restarts the application by shutting down and spawning a new instance via PowerShell.
        /// Uses the entry assembly location to correctly rebuild the "dotnet DLL" command.
        /// </summary>
        [HttpPost("restart")]
        public ActionResult Restart()
        {
            try
            {
                _logger.LogWarning("[SYSTEM-ADMIN] Graceful restart requested by user: {User}",
                    User.Identity?.Name ?? "Unknown");

                var (script, mode) = BuildRestartScript();
                if (script == null)
                    return StatusCode(500, new { error = "Unable to determine process info for restart." });

                var scriptPath = Path.Combine(Path.GetTempPath(), $"restart-nscim-api-{Guid.NewGuid():N}.ps1");
                System.IO.File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);
                _logger.LogInformation("[SYSTEM-ADMIN] Restart script launched: {Path}", scriptPath);

                Task.Run(() =>
                {
                    Thread.Sleep(2000);
                    _appLifetime.StopApplication();
                });

                return Ok(new
                {
                    message = "Graceful restart initiated. The service will shutdown and restart automatically.",
                    timestamp = DateTime.UtcNow,
                    mode
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error initiating restart");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Restarts the WebApp process by locating it and spawning a restart script.
        /// </summary>
        [HttpPost("restart-webapp")]
        public ActionResult RestartWebApp()
        {
            try
            {
                _logger.LogWarning("[SYSTEM-ADMIN] WebApp restart requested by user: {User}",
                    User.Identity?.Name ?? "Unknown");

                var webappProcesses = Process.GetProcessesByName("dotnet")
                    .Where(p =>
                    {
                        try
                        {
                            using var searcher = new System.Management.ManagementObjectSearcher(
                                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {p.Id}");
                            var cmdLine = searcher.Get().Cast<System.Management.ManagementObject>()
                                .FirstOrDefault()?["CommandLine"]?.ToString() ?? "";
                            return cmdLine.Contains("NickScanWebApp.New.dll", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    })
                    .ToList();

                if (webappProcesses.Count == 0)
                    return NotFound(new { error = "WebApp process not found." });

                var webappProc = webappProcesses.First();
                string? cmdLine = null;
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {webappProc.Id}");
                    cmdLine = searcher.Get().Cast<System.Management.ManagementObject>()
                        .FirstOrDefault()?["CommandLine"]?.ToString();
                }
                catch { }

                string dotnetExe;
                string dllPath;
                string workDir;

                if (!string.IsNullOrEmpty(cmdLine) && cmdLine.Contains("NickScanWebApp.New.dll"))
                {
                    dotnetExe = "dotnet";
                    var idx = cmdLine.IndexOf("NickScanWebApp.New.dll", StringComparison.OrdinalIgnoreCase);
                    var beforeDll = cmdLine[..idx].Trim().TrimEnd('"');
                    var lastSpace = beforeDll.LastIndexOfAny(new[] { ' ', '"' });
                    dllPath = lastSpace >= 0
                        ? cmdLine[(lastSpace + 1)..(idx + "NickScanWebApp.New.dll".Length)].Trim('"')
                        : cmdLine.Trim('"');
                    workDir = Path.GetDirectoryName(dllPath) ?? Environment.CurrentDirectory;
                }
                else
                {
                    return StatusCode(500, new { error = "Could not determine WebApp DLL path from command line." });
                }

                var script = $@"
Start-Sleep -Seconds 3
$maxWait = 30; $waited = 0
while ((Get-Process -Id {webappProc.Id} -ErrorAction SilentlyContinue) -and $waited -lt $maxWait) {{
    Start-Sleep -Seconds 1; $waited++
}}
if (Get-Process -Id {webappProc.Id} -ErrorAction SilentlyContinue) {{
    Stop-Process -Id {webappProc.Id} -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}}
Start-Process -FilePath 'dotnet' -ArgumentList '{dllPath.Replace("'", "''")}' -WorkingDirectory '{workDir.Replace("'", "''")}' -WindowStyle Hidden
";

                var scriptPath = Path.Combine(Path.GetTempPath(), $"restart-nscim-webapp-{Guid.NewGuid():N}.ps1");
                System.IO.File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);

                try { webappProc.Kill(entireProcessTree: false); } catch { }

                return Ok(new
                {
                    message = "WebApp restart initiated.",
                    timestamp = DateTime.UtcNow,
                    webappPid = webappProc.Id,
                    dllPath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error restarting WebApp");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Shuts down the WebApp process without restarting it.
        /// </summary>
        [HttpPost("shutdown-webapp")]
        public ActionResult ShutdownWebApp()
        {
            try
            {
                _logger.LogWarning("[SYSTEM-ADMIN] WebApp shutdown requested by user: {User}",
                    User.Identity?.Name ?? "Unknown");

                var webappProcesses = Process.GetProcessesByName("dotnet")
                    .Where(p =>
                    {
                        try
                        {
                            using var searcher = new System.Management.ManagementObjectSearcher(
                                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {p.Id}");
                            var cmdLine = searcher.Get().Cast<System.Management.ManagementObject>()
                                .FirstOrDefault()?["CommandLine"]?.ToString() ?? "";
                            return cmdLine.Contains("NickScanWebApp.New.dll", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    })
                    .ToList();

                if (webappProcesses.Count == 0)
                    return NotFound(new { error = "WebApp process not found." });

                var webappProc = webappProcesses.First();
                var pid = webappProc.Id;

                try { webappProc.Kill(entireProcessTree: false); } catch { }

                return Ok(new
                {
                    message = "WebApp shutdown initiated.",
                    timestamp = DateTime.UtcNow,
                    killedPid = pid
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error shutting down WebApp");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private (string? script, string mode) BuildRestartScript()
        {
            var process = Process.GetCurrentProcess();
            var hostExe = process.MainModule?.FileName ?? Environment.ProcessPath ?? "";
            var dllPath = Assembly.GetEntryAssembly()?.Location ?? "";
            var workDir = Path.GetDirectoryName(dllPath) ?? Environment.CurrentDirectory;
            var pid = process.Id;

            if (string.IsNullOrEmpty(dllPath))
                return (null, "Unknown");

            var isDotnetHost = hostExe.Contains("dotnet", StringComparison.OrdinalIgnoreCase);

            string startCmd;
            if (isDotnetHost)
                startCmd = $"Start-Process -FilePath 'dotnet' -ArgumentList '{dllPath.Replace("'", "''")}' -WorkingDirectory '{workDir.Replace("'", "''")}' -WindowStyle Hidden";
            else
                startCmd = $"Start-Process -FilePath '{hostExe.Replace("'", "''")}' -WorkingDirectory '{workDir.Replace("'", "''")}' -WindowStyle Hidden";

            var script = $@"
Start-Sleep -Seconds 3
$maxWait = 30; $waited = 0
while ((Get-Process -Id {pid} -ErrorAction SilentlyContinue) -and $waited -lt $maxWait) {{
    Start-Sleep -Seconds 1; $waited++
}}
if (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{
    Stop-Process -Id {pid} -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}}
{startCmd}
";
            return (script, isDotnetHost ? "dotnet-hosted" : "self-contained");
        }

        [HttpGet("performance")]
        public ActionResult<SystemPerformanceMetrics> GetPerformanceMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var metrics = new SystemPerformanceMetrics
                {
                    CpuUsagePercent = GetCpuUsage(),
                    MemoryUsageMB = process.WorkingSet64 / 1024 / 1024,
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount,
                    GCTotalMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024
                };

                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SYSTEM-ADMIN] Error getting performance metrics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;

                Thread.Sleep(500);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return cpuUsageTotal * 100;
            }
            catch
            {
                return 0;
            }
        }
    }

    public class ServiceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? LastRun { get; set; }
        public DateTime? LastRestart { get; set; }
        public TimeSpan? Uptime { get; set; }
        public string? HealthStatus { get; set; }
        public string? LastError { get; set; }
        public bool CanRestart { get; set; } = true;
        public bool CanStop { get; set; } = true;
        public bool CanStart { get; set; } = true;
    }

    public class ServiceRestartResponse
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Message { get; set; }
        public DateTime RestartInitiatedAt { get; set; }
        public TimeSpan? RestartDuration { get; set; }
    }

    public class ServiceStatusDto
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? LastRestart { get; set; }
        public TimeSpan? Uptime { get; set; }
        public bool IsHealthy { get; set; }
        public string? LastError { get; set; }
    }

    public class ServiceHealthDto
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class SystemInfo
    {
        public string MachineName { get; set; } = string.Empty;
        public string OSVersion { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public string DotNetVersion { get; set; } = string.Empty;
        public long WorkingSetMemoryMB { get; set; }
        public string TotalProcessorTime { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    public class SystemLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public class LogResponse
    {
        public List<SystemLogEntry> Logs { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class SystemPerformanceMetrics
    {
        public double CpuUsagePercent { get; set; }
        public long MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public long GCTotalMemoryMB { get; set; }
    }
}

