using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR hub for real-time scanner and processing notifications
    /// </summary>
    [Authorize]
    public class ScannerHub : Hub
    {
        private readonly ILogger<ScannerHub> _logger;

        public ScannerHub(ILogger<ScannerHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Notify all clients about a new scan
        /// </summary>
        public async Task NotifyNewScan(string containerNumber, string scannerType, DateTime scanDate)
        {
            _logger.LogInformation("Broadcasting new scan: {Container} from {Scanner}", containerNumber, scannerType);

            await Clients.All.SendAsync("NewScanReceived", new
            {
                ContainerNumber = containerNumber,
                ScannerType = scannerType,
                ScanDate = scanDate,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about ICUMS data download completion
        /// </summary>
        public async Task NotifyICUMSUpdate(string containerNumber, bool success)
        {
            _logger.LogInformation("Broadcasting ICUMS update for {Container}: {Success}", containerNumber, success);

            await Clients.All.SendAsync("ICUMSDataUpdated", new
            {
                ContainerNumber = containerNumber,
                Success = success,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about processing completion
        /// </summary>
        public async Task NotifyProcessingComplete(string containerNumber, string status, string message)
        {
            _logger.LogInformation("Broadcasting processing complete for {Container}: {Status}", containerNumber, status);

            await Clients.All.SendAsync("ProcessingCompleted", new
            {
                ContainerNumber = containerNumber,
                Status = status,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about validation status change
        /// </summary>
        public async Task NotifyValidationStatusChanged(string containerNumber, string newStatus)
        {
            await Clients.All.SendAsync("ValidationStatusChanged", new
            {
                ContainerNumber = containerNumber,
                Status = newStatus,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about system alerts
        /// </summary>
        public async Task NotifySystemAlert(string severity, string message, string source)
        {
            _logger.LogWarning("Broadcasting system alert: {Severity} from {Source}: {Message}", severity, source, message);

            await Clients.All.SendAsync("SystemAlert", new
            {
                Severity = severity,
                Message = message,
                Source = source,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Client connection established
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Client disconnected
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogError(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}

