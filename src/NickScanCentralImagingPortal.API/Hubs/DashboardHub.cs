using Microsoft.AspNetCore.SignalR;

namespace NickScanCentralImagingPortal.API.Hubs
{
    public class DashboardHub : Hub
    {
        private readonly ILogger<DashboardHub> _logger;

        public DashboardHub(ILogger<DashboardHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        // Method to send system status updates to all clients
        public async Task BroadcastSystemStatus(object status)
        {
            await Clients.All.SendAsync("ReceiveSystemStatus", status);
        }

        // Method to send service health updates to all clients
        public async Task BroadcastServiceHealth(object health)
        {
            await Clients.All.SendAsync("ReceiveServiceHealth", health);
        }

        // Method to send new activity to all clients
        public async Task BroadcastActivity(object activity)
        {
            await Clients.All.SendAsync("ReceiveActivity", activity);
        }

        // Method to send performance metrics to all clients
        public async Task BroadcastMetrics(object metrics)
        {
            await Clients.All.SendAsync("ReceiveMetrics", metrics);
        }
    }
}
