using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR hub for real-time container scan queue updates
    /// </summary>
    [Authorize]
    public class ContainerScanQueueHub : Hub
    {
        private readonly ILogger<ContainerScanQueueHub> _logger;

        public ContainerScanQueueHub(ILogger<ContainerScanQueueHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Notify all clients about a new queue item
        /// </summary>
        public async Task NotifyQueueItemAdded(int itemId, string containerNumber, string scannerType, string status)
        {
            _logger.LogInformation("Broadcasting new queue item: {ItemId} - {Container} from {Scanner}", itemId, containerNumber, scannerType);

            await Clients.All.SendAsync("QueueItemAdded", new
            {
                ItemId = itemId,
                ContainerNumber = containerNumber,
                ScannerType = scannerType,
                Status = status,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about a queue item status change
        /// </summary>
        public async Task NotifyQueueItemUpdated(int itemId, string containerNumber, string oldStatus, string newStatus)
        {
            _logger.LogInformation("Broadcasting queue item update: {ItemId} - {Container} {OldStatus} -> {NewStatus}", itemId, containerNumber, oldStatus, newStatus);

            await Clients.All.SendAsync("QueueItemUpdated", new
            {
                ItemId = itemId,
                ContainerNumber = containerNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Notify all clients about queue statistics update
        /// </summary>
        public async Task NotifyQueueStatisticsUpdate(object statistics)
        {
            _logger.LogDebug("Broadcasting queue statistics update");

            await Clients.All.SendAsync("QueueStatisticsUpdated", statistics);
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected to ContainerScanQueueHub: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected from ContainerScanQueueHub: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
