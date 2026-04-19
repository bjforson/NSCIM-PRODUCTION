using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IAiDatasetExportService
    {
        Task<AiDatasetSnapshot> CreateSnapshotAsync(string name, string createdBy, DateTime? fromUtc, DateTime? toUtc, bool optInOnly, CancellationToken cancellationToken = default);
    }
}
