using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Unified queue publishing service for container scans
    /// All scanner ingestion services use this interface to push scans to the completeness queue
    /// This abstraction makes the system future-proof - new scanners just implement this pattern
    /// </summary>
    public interface IContainerScanQueuePublisher
    {
        /// <summary>
        /// Publish a single container scan to the queue for completeness processing
        /// </summary>
        /// <param name="containerNumber">Container number from scanner</param>
        /// <param name="scannerType">Scanner type identifier (e.g., "FS6000", "ASE", "HeimannSmith", or any future scanner)</param>
        /// <param name="inspectionId">Unique inspection/scan ID from scanner system (can be Guid or int, stored as string)</param>
        /// <param name="scanDate">Date/time when container was scanned</param>
        /// <param name="priority">Queue priority (0=Normal, 1=High, 2=Urgent). Default: 0</param>
        /// <param name="metadata">Optional metadata as JSON string (e.g., source file path, batch ID)</param>
        /// <returns>Queue item ID if successfully added, or existing item ID if duplicate</returns>
        Task<int> PublishScanAsync(
            string containerNumber,
            string scannerType,
            string? inspectionId,
            DateTime scanDate,
            int priority = 0,
            string? metadata = null);

        /// <summary>
        /// Publish multiple container scans to the queue in batch (more efficient)
        /// </summary>
        /// <param name="scans">List of scan items to publish</param>
        /// <returns>Number of items successfully added (excluding duplicates)</returns>
        Task<int> PublishScansBatchAsync(List<ContainerScanInfo> scans);

        /// <summary>
        /// Check if a scan is already queued (by container number, scanner type, and inspection ID)
        /// </summary>
        Task<bool> IsScanQueuedAsync(string containerNumber, string scannerType, string? inspectionId);
    }

    /// <summary>
    /// Container scan information for queue publishing
    /// </summary>
    public class ContainerScanInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? InspectionId { get; set; }
        public DateTime ScanDate { get; set; }
        public int Priority { get; set; } = 0;
        public string? Metadata { get; set; }
    }
}

