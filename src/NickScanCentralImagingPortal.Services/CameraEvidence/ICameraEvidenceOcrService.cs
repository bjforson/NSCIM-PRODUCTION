using NickScanCentralImagingPortal.Core.Entities.CameraEvidence;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public interface ICameraEvidenceOcrService
    {
        Task<CameraEvidenceOcrExtraction> ExtractAsync(
            CameraEvidenceFrame frame,
            CameraEvidenceSource source,
            CancellationToken cancellationToken);
    }
}
