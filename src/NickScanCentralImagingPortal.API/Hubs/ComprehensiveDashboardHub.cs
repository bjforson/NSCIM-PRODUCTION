using Microsoft.AspNetCore.SignalR;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Dashboard;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time comprehensive dashboard updates
    /// Pushes live data to connected clients every 5 seconds
    /// </summary>
    public class ComprehensiveDashboardHub : Hub
    {
        private readonly ILogger<ComprehensiveDashboardHub> _logger;
        private readonly IComprehensiveDashboardService _dashboardService;

        public ComprehensiveDashboardHub(
            ILogger<ComprehensiveDashboardHub> logger,
            IComprehensiveDashboardService dashboardService)
        {
            _logger = logger;
            _dashboardService = dashboardService;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected to Comprehensive Dashboard Hub: {ConnectionId}", Context.ConnectionId);

            // Send initial data immediately on connection
            try
            {
                var data = await _dashboardService.GetComprehensiveDashboardDataAsync();
                await Clients.Caller.SendAsync("ReceiveDashboardUpdate", data);
                _logger.LogInformation("Sent initial dashboard data to {ConnectionId}", Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial dashboard data");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected from Comprehensive Dashboard Hub: {ConnectionId}", Context.ConnectionId);

            if (exception != null)
            {
                _logger.LogError(exception, "Client disconnected with error");
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client can request immediate data refresh
        /// </summary>
        public async Task RequestRefresh()
        {
            _logger.LogInformation("Dashboard refresh requested by {ConnectionId}", Context.ConnectionId);

            try
            {
                var data = await _dashboardService.GetComprehensiveDashboardDataAsync();
                await Clients.Caller.SendAsync("ReceiveDashboardUpdate", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing dashboard data");
                await Clients.Caller.SendAsync("ReceiveError", new { message = "Failed to refresh dashboard data" });
            }
        }

        /// <summary>
        /// Client acknowledges an alert
        /// </summary>
        public async Task AcknowledgeAlert(string alertId)
        {
            _logger.LogInformation("Alert {AlertId} acknowledged by {ConnectionId}", alertId, Context.ConnectionId);

            // TODO: Update alert acknowledgment in database
            await Clients.All.SendAsync("AlertAcknowledged", alertId);
        }
    }

    /// <summary>
    /// Background service that pushes updates to all connected dashboard clients
    /// </summary>
    public class DashboardBroadcastService : BackgroundService
    {
        private readonly ILogger<DashboardBroadcastService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<ComprehensiveDashboardHub> _hubContext;
        private readonly IConfiguration _configuration;

        public DashboardBroadcastService(
            ILogger<DashboardBroadcastService> logger,
            IServiceProvider serviceProvider,
            IHubContext<ComprehensiveDashboardHub> hubContext,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=== Dashboard Broadcast Service Started ===");

            var startupDelaySeconds = _configuration.GetValue<int>("BackgroundServices:DashboardBroadcastService:StartupDelaySeconds", 15);
            if (startupDelaySeconds > 0)
            {
                _logger.LogDebug("Dashboard Broadcast Service staggering startup: {Seconds}s delay", startupDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                int broadcastIntervalSeconds = 30;
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dashboardService = scope.ServiceProvider.GetRequiredService<IComprehensiveDashboardService>();

                        // Get comprehensive data
                        var data = await dashboardService.GetComprehensiveDashboardDataAsync();

                        // Broadcast to all connected clients
                        await _hubContext.Clients.All.SendAsync("ReceiveDashboardUpdate", data, stoppingToken);

                        _logger.LogDebug("Dashboard update broadcast to all clients");

                        // Read interval from scoped ISettingsProvider (cannot inject scoped into singleton)
                        var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                        broadcastIntervalSeconds = await settingsProvider.GetIntAsync("BackgroundServices", "DashboardBroadcast.IntervalSeconds", 60);
                    }

                    _logger.LogDebug("⏰ Next dashboard broadcast in {Interval} seconds (from settings)", broadcastIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(broadcastIntervalSeconds), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting dashboard update");
                    await Task.Delay(TimeSpan.FromSeconds(broadcastIntervalSeconds), stoppingToken);
                }
            }

            _logger.LogInformation("Dashboard Broadcast Service stopping");
        }
    }
}

