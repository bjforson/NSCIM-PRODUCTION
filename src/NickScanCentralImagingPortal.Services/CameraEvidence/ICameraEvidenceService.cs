using System.Net;
using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public interface ICameraEvidenceService
    {
        Task<CameraEvidenceHealthDto> GetHealthAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<CameraEvidenceSiteDto>> GetSitesAsync(CancellationToken cancellationToken);
        Task<CameraEvidenceSiteDto> UpsertSiteAsync(CameraEvidenceSiteUpsertRequest request, string actorUserId, CancellationToken cancellationToken);
        Task<IReadOnlyList<CameraEvidenceSourceDto>> GetSourcesAsync(Guid? siteId, CancellationToken cancellationToken);
        Task<CameraEvidenceSourceDto> UpsertSourceAsync(CameraEvidenceSourceUpsertRequest request, string actorUserId, CancellationToken cancellationToken);
        Task<IReadOnlyList<ProtectCameraDto>> GetProtectCamerasAsync(Guid siteId, CancellationToken cancellationToken);
        Task<CameraEvidenceSnapshotTestResultDto> TestSnapshotAsync(Guid sourceId, string actorUserId, CancellationToken cancellationToken);
        Task<CameraEvidenceWebhookAcceptedDto> IngestWebhookAsync(
            string siteKey,
            IReadOnlyDictionary<string, string?> headers,
            string rawBody,
            IPAddress? remoteIpAddress,
            CancellationToken cancellationToken);
        Task<CameraEvidenceEventPageDto> GetEventsAsync(
            string? siteKey,
            Guid? sourceId,
            string? reviewStatus,
            int page,
            int pageSize,
            CancellationToken cancellationToken);
        Task<CameraEvidenceEventDetailDto?> GetEventAsync(Guid eventId, CancellationToken cancellationToken);
        Task<CameraEvidenceFrameFile?> GetFrameFileAsync(Guid frameId, CancellationToken cancellationToken);
        Task<CameraEvidenceReviewDecisionDto> ReviewOcrResultAsync(
            Guid ocrResultId,
            CameraEvidenceReviewRequest request,
            string reviewerUserId,
            CancellationToken cancellationToken);
        Task<int> ProcessPendingWorkAsync(CancellationToken cancellationToken);
    }
}
