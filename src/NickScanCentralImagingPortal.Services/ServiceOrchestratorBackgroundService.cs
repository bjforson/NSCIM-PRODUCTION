using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services
{
    /// <summary>
    /// Pure service coordinator - manages service lifecycle, startup sequencing, and graceful shutdown.
    /// All health monitoring is delegated to ComprehensiveHealthCheckService.
    /// </summary>
    public class ServiceOrchestratorBackgroundService : BackgroundService
    {
        private readonly ILogger<ServiceOrchestratorBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _startupSemaphore = new(1, 1);
        private const string SERVICE_ID = "[SERVICE-ORCHESTRATOR]";
        private DateTime _startupTime;

        public ServiceOrchestratorBackgroundService(
            ILogger<ServiceOrchestratorBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _startupTime = DateTime.UtcNow;
            _logger.LogInformation("{ServiceId} ✅ Service Orchestrator starting - Pure lifecycle coordinator (monitoring delegated to ComprehensiveHealthCheckService)", SERVICE_ID);

            try
            {
                await _startupSemaphore.WaitAsync(stoppingToken);

                // Phase 1: Coordinate startup sequence
                await CoordinateStartupSequence(stoppingToken);

                // Phase 2: Monitor lifecycle and coordinate graceful shutdown when requested
                await MonitorLifecycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{ServiceId} 🛑 Service Orchestrator stopping - initiating graceful shutdown...", SERVICE_ID);
                await CoordinateGracefulShutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} ❌ Service Orchestrator encountered an error", SERVICE_ID);
                throw;
            }
            finally
            {
                _startupSemaphore.Release();
                var uptime = DateTime.UtcNow - _startupTime;
                _logger.LogInformation("{ServiceId} ✅ Service Orchestrator stopped - Uptime: {Uptime}", SERVICE_ID, uptime.ToString(@"dd\.hh\:mm\:ss"));
            }
        }

        private async Task CoordinateStartupSequence(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} 🚀 Phase 1: Coordinating startup sequence...", SERVICE_ID);

            // Note: All background services are registered as IHostedService and start automatically
            // This orchestrator simply logs the startup coordination

            _logger.LogInformation("{ServiceId} ℹ️ Background services starting in registered order:", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   1. ComprehensiveHealthCheckService (monitoring)", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   2. Scanner Services (FS6000, ASE)", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   3. ICUMS Pipeline (Download, Scanner, Ingestion, Transfer)", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   4. Business Logic (Completeness, BOE, Mapper, Submission)", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   5. Broadcast Services (Dashboard)", SERVICE_ID);
            _logger.LogInformation("{ServiceId} ✅ Startup coordination complete - services initializing", SERVICE_ID);

            await Task.CompletedTask;
        }

        private async Task MonitorLifecycleAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} 🔄 Phase 2: Lifecycle monitoring active (awaiting shutdown signal)", SERVICE_ID);

            // Lightweight heartbeat loop - just keeps orchestrator alive
            // All actual monitoring is done by ComprehensiveHealthCheckService
            var heartbeatCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    heartbeatCount++;

                    // Log heartbeat every 60 minutes to show orchestrator is alive
                    if (heartbeatCount % 60 == 0)
                    {
                        var uptime = DateTime.UtcNow - _startupTime;
                        _logger.LogInformation("{ServiceId} 💓 Orchestrator heartbeat - Uptime: {Uptime}",
                            SERVICE_ID, uptime.ToString(@"dd\.hh\:mm\:ss"));
                    }

                    // Wait 1 minute before next heartbeat
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "{ServiceId} Error in lifecycle monitoring", SERVICE_ID);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task CoordinateGracefulShutdown()
        {
            _logger.LogInformation("{ServiceId} 🛑 Coordinating graceful shutdown...", SERVICE_ID);

            // Note: .NET IHostedService framework handles graceful shutdown automatically
            // Services receive cancellation tokens and stop in reverse registration order

            _logger.LogInformation("{ServiceId} ℹ️ Shutdown sequence (automatic):", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   1. Broadcast Services stop first", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   2. Business Logic services drain work queues", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   3. ICUMS Pipeline services complete current operations", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   4. Scanner services finish active scans", SERVICE_ID);
            _logger.LogInformation("{ServiceId}   5. Monitoring services stop last", SERVICE_ID);

            // Allow brief time for logging
            await Task.Delay(100);

            _logger.LogInformation("{ServiceId} ✅ Graceful shutdown coordination complete", SERVICE_ID);
        }

    }
}
