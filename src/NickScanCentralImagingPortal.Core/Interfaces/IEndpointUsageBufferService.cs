using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Buffers endpoint usage records for bulk insertion.
    /// Use this instead of per-request inserts to reduce DB round-trips and lock contention.
    /// </summary>
    public interface IEndpointUsageBufferService
    {
        /// <summary>
        /// Enqueue a record for batched insertion. Non-blocking.
        /// </summary>
        void Enqueue(EndpointUsageRecord record);
    }
}
