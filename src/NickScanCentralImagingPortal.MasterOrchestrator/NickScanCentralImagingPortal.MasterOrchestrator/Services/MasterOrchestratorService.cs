using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.MasterOrchestrator.Models;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NickScanCentralImagingPortal.MasterOrchestrator.Services;

public class MasterOrchestratorService : BackgroundService
{
    private readonly ILogger<MasterOrchestratorService> _logger;
    private readonly MasterOrchestratorConfig _config;
    private readonly Dictionary<string, ServiceInfo> _services = new();
    private readonly HttpClient _httpClient;

    public MasterOrchestratorService(
        ILogger<MasterOrchestratorService> logger,
        IOptions<MasterOrchestratorConfig> config,
        HttpClient httpClient)
    {
        _logger = logger;
        _config = config.Value;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.HealthCheck.TimeoutSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(" Master Orchestrator starting...");

        try
        {
            // Initialize services
            await InitializeServices();

            // Start services in order
            await StartApiService();
            await StartWebAppService();

            _logger.LogInformation(" All services started successfully!");

            // Monitor services
            await MonitorServices(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Master Orchestrator failed");
            throw;
        }
    }

    private Task InitializeServices()
    {
        _services["Api"] = new ServiceInfo
        {
            Name = "API Service",
            Status = ServiceStatus.Starting,
            Url = $"http://localhost:{_config.ApiService.Port}"
        };

        _services["WebApp"] = new ServiceInfo
        {
            Name = "Web App",
            Status = ServiceStatus.Starting,
            Url = $"http://localhost:{_config.WebAppService.Port}"
        };

        _logger.LogInformation(" Services initialized: {ServiceCount}", _services.Count);
        return Task.CompletedTask;
    }

    private async Task StartApiService()
    {
        var serviceInfo = _services["Api"];
        _logger.LogInformation(" Starting {ServiceName} on port {Port}...", 
            serviceInfo.Name, _config.ApiService.Port);

        try
        {
            var process = await StartServiceProcess(_config.ApiService, "API");
            serviceInfo.Process = process;
            serviceInfo.Status = ServiceStatus.Starting;

            // Wait for API to be healthy
            await WaitForServiceHealth(serviceInfo, _config.ApiService.StartupTimeoutSeconds);
            
            serviceInfo.Status = ServiceStatus.Running;
            _logger.LogInformation(" {ServiceName} is running and healthy", serviceInfo.Name);
        }
        catch (Exception ex)
        {
            serviceInfo.Status = ServiceStatus.Failed;
            _logger.LogError(ex, " Failed to start {ServiceName}", serviceInfo.Name);
            throw;
        }
    }

    private async Task StartWebAppService()
    {
        var serviceInfo = _services["WebApp"];
        _logger.LogInformation(" Starting {ServiceName} on port {Port}...", 
            serviceInfo.Name, _config.WebAppService.Port);

        try
        {
            // Set API URL environment variable for Web App
            Environment.SetEnvironmentVariable("API_BASE_URL", _services["Api"].Url);

            var process = await StartServiceProcess(_config.WebAppService, "WebApp");
            serviceInfo.Process = process;
            serviceInfo.Status = ServiceStatus.Starting;

            // Wait for Web App to be healthy
            await WaitForServiceHealth(serviceInfo, _config.WebAppService.StartupTimeoutSeconds);
            
            serviceInfo.Status = ServiceStatus.Running;
            _logger.LogInformation(" {ServiceName} is running and healthy", serviceInfo.Name);
        }
        catch (Exception ex)
        {
            serviceInfo.Status = ServiceStatus.Failed;
            _logger.LogError(ex, " Failed to start {ServiceName}", serviceInfo.Name);
            throw;
        }
    }

    private Task<Process> StartServiceProcess(ServiceConfig config, string serviceName)
    {
        var projectPath = Path.GetFullPath(config.ProjectPath);
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project path not found: {projectPath}");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --urls=\"http://localhost:{config.Port}\"",
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(processInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start process for {serviceName}");
        }

        _logger.LogInformation(" {ServiceName} process started (PID: {ProcessId})", 
            serviceName, process.Id);

        return Task.FromResult(process);
    }

    private async Task WaitForServiceHealth(ServiceInfo serviceInfo, int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var healthUrl = $"{serviceInfo.Url}{_config.ApiService.HealthEndpoint}";
                var response = await _httpClient.GetAsync(healthUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(" {ServiceName} health check passed", serviceInfo.Name);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(" {ServiceName} health check failed: {Error}", 
                    serviceInfo.Name, ex.Message);
            }

            await Task.Delay(2000); // Wait 2 seconds before next check
        }

        throw new TimeoutException($"Service {serviceInfo.Name} failed to become healthy within {timeoutSeconds} seconds");
    }

    private async Task MonitorServices(CancellationToken stoppingToken)
    {
        _logger.LogInformation(" Starting service monitoring...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var service in _services.Values)
                {
                    await CheckServiceHealth(service);
                }

                // Log service status summary
                var statusSummary = string.Join(", ", 
                    _services.Values.Select(s => $"{s.Name}: {s.Status}"));
                _logger.LogInformation(" Service Status - {StatusSummary}", statusSummary);

                await Task.Delay(TimeSpan.FromSeconds(_config.HealthCheck.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error during service monitoring");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task CheckServiceHealth(ServiceInfo serviceInfo)
    {
        try
        {
            // Check if process is still running
            if (serviceInfo.Process?.HasExited == true)
            {
                serviceInfo.Status = ServiceStatus.Stopped;
                serviceInfo.ConsecutiveFailures++;
                
                _logger.LogWarning(" {ServiceName} process has exited", serviceInfo.Name);
                
                if (_config.AutoRestart)
                {
                    await RestartService(serviceInfo);
                }
                return;
            }

            // Perform health check
            var healthUrl = $"{serviceInfo.Url}{_config.ApiService.HealthEndpoint}";
            var response = await _httpClient.GetAsync(healthUrl);
            
            if (response.IsSuccessStatusCode)
            {
                serviceInfo.Status = ServiceStatus.Running;
                serviceInfo.ConsecutiveFailures = 0;
                serviceInfo.LastHealthCheck = DateTime.UtcNow;
            }
            else
            {
                serviceInfo.Status = ServiceStatus.Unhealthy;
                serviceInfo.ConsecutiveFailures++;
                serviceInfo.LastHealthCheck = DateTime.UtcNow;
                
                _logger.LogWarning(" {ServiceName} health check failed: {StatusCode}", 
                    serviceInfo.Name, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            serviceInfo.Status = ServiceStatus.Unhealthy;
            serviceInfo.ConsecutiveFailures++;
            serviceInfo.LastHealthCheck = DateTime.UtcNow;
            
            _logger.LogWarning(" {ServiceName} health check error: {Error}", 
                serviceInfo.Name, ex.Message);
        }
    }

    private async Task RestartService(ServiceInfo serviceInfo)
    {
        _logger.LogInformation(" Restarting {ServiceName}...", serviceInfo.Name);
        
        try
        {
            // Clean up old process
            serviceInfo.Process?.Kill();
            serviceInfo.Process?.Dispose();
            
            // Determine which service to restart
            ServiceConfig config;
            if (serviceInfo.Name == "API Service")
            {
                config = _config.ApiService;
            }
            else
            {
                config = _config.WebAppService;
            }

            // Start new process
            var process = await StartServiceProcess(config, serviceInfo.Name);
            serviceInfo.Process = process;
            serviceInfo.Status = ServiceStatus.Starting;

            // Wait for health
            await WaitForServiceHealth(serviceInfo, config.StartupTimeoutSeconds);
            
            serviceInfo.Status = ServiceStatus.Running;
            serviceInfo.ConsecutiveFailures = 0;
            
            _logger.LogInformation(" {ServiceName} restarted successfully", serviceInfo.Name);
        }
        catch (Exception ex)
        {
            serviceInfo.Status = ServiceStatus.Failed;
            _logger.LogError(ex, " Failed to restart {ServiceName}", serviceInfo.Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(" Master Orchestrator stopping...");

        // Stop all services gracefully
        foreach (var service in _services.Values)
        {
            try
            {
                if (service.Process != null && !service.Process.HasExited)
                {
                    _logger.LogInformation(" Stopping {ServiceName}...", service.Name);
                    service.Process.Kill();
                    await service.Process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error stopping {ServiceName}", service.Name);
            }
        }

        _logger.LogInformation(" Master Orchestrator stopped");
        await base.StopAsync(cancellationToken);
    }
}
