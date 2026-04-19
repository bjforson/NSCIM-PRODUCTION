using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IBatchOperationService
    {
        Task<BatchOperationResponse> QueueBatchOperationAsync(BatchOperationRequest request);
        Task<BatchOperationResponse> GetBatchStatusAsync(string batchId);
    }
}

