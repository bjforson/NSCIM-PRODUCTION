using NickScanCentralImagingPortal.Core.DTOs.AiWorkflow;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IOpsLogTriageService
    {
        Task<OpsTriageResultDto> TriageRecentAsync(int maxItems, CancellationToken cancellationToken = default);
    }
}
