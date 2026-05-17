using System.Net;
using NickScanCentralImagingPortal.Core.Entities.CameraEvidence;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed record CameraEvidenceRuntimeSite(
        CameraEvidenceSite Site,
        string ApiKey,
        string? WebhookSecret,
        IReadOnlyList<string> AllowedWebhookSourceCidrs);

    public sealed record UniFiProtectSnapshotResult(
        byte[] Content,
        string ContentType,
        bool IsHighQuality,
        string SnapshotParametersJson);

    public sealed record CameraEvidenceFrameFile(
        string FullPath,
        string ContentType,
        string FileName);

    public sealed record CameraEvidenceOcrExtraction(
        string Engine,
        string? EngineVersion,
        string RawText,
        string? NormalizedText,
        string CandidateType,
        double Confidence,
        string ValidationStatus,
        string? ValidationReasonsJson,
        string? BoundingBoxesJson);

    public sealed record WebhookAuthenticationResult(
        bool Success,
        HttpStatusCode StatusCode,
        string? Message);
}
