using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public interface IUniFiProtectClient
    {
        Task<string> GetApplicationInfoAsync(CameraEvidenceRuntimeSite site, CancellationToken cancellationToken);
        Task<IReadOnlyList<ProtectCameraDto>> GetCamerasAsync(CameraEvidenceRuntimeSite site, CancellationToken cancellationToken);
        Task<UniFiProtectSnapshotResult> GetSnapshotAsync(
            CameraEvidenceRuntimeSite site,
            string cameraId,
            string channel,
            bool highQuality,
            CancellationToken cancellationToken);
    }
}
