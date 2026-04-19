using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IReportGenerationService
    {
        Task<ReportGenerationResponse> GenerateReportAsync(ReportGenerationRequest request);
    }
}

