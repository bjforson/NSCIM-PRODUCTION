using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface ICrossRecordScanRepository
    {
        Task<CrossRecordScan?> GetByIdAsync(int id);
        Task<CrossRecordScan?> GetByContainerAsync(string containerNumber);
        Task<CrossRecordScan?> FindByContainerPairAsync(string container1, string container2);

        Task<(List<CrossRecordScan> Items, int TotalCount)> GetPagedListAsync(
            int page = 1, int pageSize = 50,
            string? severity = null, string? scannerType = null,
            DateTime? startDate = null, DateTime? endDate = null);

        Task<CrossRecordAnalytics> GetAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null);

        Task MarkAsReviewedAsync(int id, string reviewedBy, string? notes = null);
    }
}
