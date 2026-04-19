using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Monitoring;
using NickScanCentralImagingPortal.Services.Monitoring;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR hub for real-time monitoring and alerts
    /// </summary>
    public class MonitoringHub : Hub
    {
        private readonly ILogger<MonitoringHub> _logger;
        private readonly ComprehensiveHealthCheckService _healthCheckService;
        private static readonly ConcurrentDictionary<string, string> _connectedClients = new();

        public MonitoringHub(
            ILogger<MonitoringHub> logger,
            ComprehensiveHealthCheckService healthCheckService)
        {
            _logger = logger;
            _healthCheckService = healthCheckService;
        }

        public override async Task OnConnectedAsync()
        {
            var clientId = Context.ConnectionId;
            var clientInfo = $"{Context.User?.Identity?.Name ?? "Anonymous"} ({Context.GetHttpContext()?.Connection.RemoteIpAddress})";

            _connectedClients.TryAdd(clientId, clientInfo);
            _logger.LogInformation("📡 Monitoring client connected: {ClientInfo} (ConnectionId: {ConnectionId})", clientInfo, clientId);

            // Send current system status to the newly connected client
            await SendSystemStatusUpdate();

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var clientId = Context.ConnectionId;
            var clientInfo = _connectedClients.GetValueOrDefault(clientId, "Unknown");

            _connectedClients.TryRemove(clientId, out _);
            _logger.LogInformation("📡 Monitoring client disconnected: {ClientInfo} (ConnectionId: {ConnectionId})", clientInfo, clientId);

            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected with exception");
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Join a specific monitoring group (e.g., "alerts", "database", "filesystem")
        /// </summary>
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} joined monitoring group: {GroupName}", Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Leave a specific monitoring group
        /// </summary>
        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} left monitoring group: {GroupName}", Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Request immediate system status update
        /// </summary>
        public async Task RequestSystemStatus()
        {
            _logger.LogDebug("Client {ConnectionId} requested system status update", Context.ConnectionId);
            await SendSystemStatusUpdate();
        }

        /// <summary>
        /// Subscribe to specific service alerts
        /// </summary>
        public async Task SubscribeToServiceAlerts(string serviceName)
        {
            var groupName = $"alerts-{serviceName.ToLower()}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} subscribed to alerts for service: {ServiceName}", Context.ConnectionId, serviceName);
        }

        /// <summary>
        /// Unsubscribe from specific service alerts
        /// </summary>
        public async Task UnsubscribeFromServiceAlerts(string serviceName)
        {
            var groupName = $"alerts-{serviceName.ToLower()}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} unsubscribed from alerts for service: {ServiceName}", Context.ConnectionId, serviceName);
        }

        #region Hub Methods for Broadcasting Updates

        /// <summary>
        /// Send system status update to all connected clients
        /// </summary>
        public async Task SendSystemStatusUpdate()
        {
            try
            {
                var healthSummary = _healthCheckService.GetSystemHealthSummary();
                var connectedClientsCount = _connectedClients.Count;

                var update = new
                {
                    Type = "SystemStatusUpdate",
                    Data = healthSummary,
                    ConnectedClients = connectedClientsCount,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.All.SendAsync("SystemStatusUpdate", update);
                _logger.LogDebug("📊 System status update sent to {ClientCount} clients", connectedClientsCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending system status update");
            }
        }

        /// <summary>
        /// Send alert to all clients or specific groups
        /// </summary>
        public async Task SendAlert(string alertType, string message, string? serviceName = null, string? severity = "Medium")
        {
            try
            {
                var alert = new
                {
                    Type = "Alert",
                    AlertType = alertType,
                    Message = message,
                    ServiceName = serviceName,
                    Severity = severity,
                    Timestamp = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(serviceName))
                {
                    // Send to specific service alert group
                    var groupName = $"alerts-{serviceName.ToLower()}";
                    await Clients.Group(groupName).SendAsync("Alert", alert);
                    _logger.LogInformation("🚨 Alert sent to service group {GroupName}: {Message}", groupName, message);
                }
                else
                {
                    // Send to all clients
                    await Clients.All.SendAsync("Alert", alert);
                    _logger.LogInformation("🚨 Alert sent to all clients: {Message}", message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending alert");
            }
        }

        /// <summary>
        /// Send performance metrics update
        /// </summary>
        public async Task SendPerformanceUpdate(object performanceData)
        {
            try
            {
                var update = new
                {
                    Type = "PerformanceUpdate",
                    Data = performanceData,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.Group("performance").SendAsync("PerformanceUpdate", update);
                _logger.LogDebug("📈 Performance update sent to performance monitoring group");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending performance update");
            }
        }

        /// <summary>
        /// Send database statistics update
        /// </summary>
        public async Task SendDatabaseUpdate(object databaseData)
        {
            try
            {
                var update = new
                {
                    Type = "DatabaseUpdate",
                    Data = databaseData,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.Group("database").SendAsync("DatabaseUpdate", update);
                _logger.LogDebug("🗄️ Database update sent to database monitoring group");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending database update");
            }
        }

        /// <summary>
        /// Send file system status update
        /// </summary>
        public async Task SendFileSystemUpdate(object fileSystemData)
        {
            try
            {
                var update = new
                {
                    Type = "FileSystemUpdate",
                    Data = fileSystemData,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.Group("filesystem").SendAsync("FileSystemUpdate", update);
                _logger.LogDebug("📁 File system update sent to file system monitoring group");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending file system update");
            }
        }

        /// <summary>
        /// Send service health update for a specific service
        /// </summary>
        public async Task SendServiceHealthUpdate(string serviceName, ServiceHealthStatus serviceStatus)
        {
            try
            {
                var update = new
                {
                    Type = "ServiceHealthUpdate",
                    ServiceName = serviceName,
                    Data = serviceStatus,
                    Timestamp = DateTime.UtcNow
                };

                // Send to all clients and specific service group
                await Clients.All.SendAsync("ServiceHealthUpdate", update);

                var serviceGroup = $"service-{serviceName.ToLower()}";
                await Clients.Group(serviceGroup).SendAsync("ServiceHealthUpdate", update);

                _logger.LogDebug("🔧 Service health update sent for {ServiceName}: {Status}", serviceName, serviceStatus.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending service health update for {ServiceName}", serviceName);
            }
        }

        #endregion

        #region Public Static Methods for External Broadcasting

        /// <summary>
        /// Broadcast system status update to all connected clients
        /// </summary>
        public static async Task BroadcastSystemStatusUpdate(IHubContext<MonitoringHub> hubContext, ComprehensiveHealthCheckService healthCheckService)
        {
            try
            {
                var healthSummary = healthCheckService.GetSystemHealthSummary();
                var update = new
                {
                    Type = "SystemStatusUpdate",
                    Data = healthSummary,
                    Timestamp = DateTime.UtcNow
                };

                await hubContext.Clients.All.SendAsync("SystemStatusUpdate", update);
            }
            catch (Exception ex)
            {
                // Log error but don't throw to prevent disrupting the health check service
                Console.WriteLine($"Error broadcasting system status update: {ex.Message}");
            }
        }

        /// <summary>
        /// Broadcast alert to all connected clients
        /// </summary>
        public static async Task BroadcastAlert(IHubContext<MonitoringHub> hubContext, string alertType, string message, string? serviceName = null, string? severity = "Medium")
        {
            try
            {
                var alert = new
                {
                    Type = "Alert",
                    AlertType = alertType,
                    Message = message,
                    ServiceName = serviceName,
                    Severity = severity,
                    Timestamp = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(serviceName))
                {
                    var groupName = $"alerts-{serviceName.ToLower()}";
                    await hubContext.Clients.Group(groupName).SendAsync("Alert", alert);
                }
                else
                {
                    await hubContext.Clients.All.SendAsync("Alert", alert);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting alert: {ex.Message}");
            }
        }

        #endregion
    }
}
