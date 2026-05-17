namespace NickScanCentralImagingPortal.Core.DTOs.CameraEvidence
{
    public sealed class CameraEvidenceHealthDto
    {
        public bool Enabled { get; set; }
        public bool WebhookIngestionEnabled { get; set; }
        public bool MediaFetchEnabled { get; set; }
        public bool OcrEnabled { get; set; }
        public bool ExternalVisionFallbackEnabled { get; set; }
        public bool CoreReadOnlyLookupEnabled { get; set; }
        public bool CoreDisplayPromotionEnabled { get; set; }
        public bool CoreDecisionSupportEnabled { get; set; }
        public bool CoreAutomationEnabled { get; set; }
        public int SiteCount { get; set; }
        public int SourceCount { get; set; }
        public int PendingQueueCount { get; set; }
        public int ReviewBacklogCount { get; set; }
        public List<CameraEvidenceSiteHealthDto> Sites { get; set; } = new();
    }

    public sealed class CameraEvidenceSiteHealthDto
    {
        public Guid? SiteId { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public bool IsEnabled { get; set; }
        public int SourceCount { get; set; }
        public int PendingQueueCount { get; set; }
        public string Status { get; set; } = "Configured";
        public string? Message { get; set; }
    }

    public sealed class CameraEvidenceSiteDto
    {
        public Guid Id { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKeySecretName { get; set; }
        public string? WebhookSecretName { get; set; }
        public string? AllowedWebhookSourceCidrsJson { get; set; }
        public bool VerifySsl { get; set; }
        public int RequestTimeoutSeconds { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public int SourceCount { get; set; }
    }

    public sealed class CameraEvidenceSiteUpsertRequest
    {
        public Guid? Id { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKeySecretName { get; set; }
        public string? WebhookSecretName { get; set; }
        public string? AllowedWebhookSourceCidrsJson { get; set; }
        public bool VerifySsl { get; set; } = true;
        public int RequestTimeoutSeconds { get; set; } = 10;
        public bool IsEnabled { get; set; }
    }

    public sealed class CameraEvidenceSourceDto
    {
        public Guid Id { get; set; }
        public Guid SiteId { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public string Provider { get; set; } = "UniFiProtect";
        public string? ProtectCameraId { get; set; }
        public string? ProtectDeviceKey { get; set; }
        public string? MacAddress { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string? OperationalZone { get; set; }
        public string ExpectedTextType { get; set; } = "unknown";
        public string CaptureMode { get; set; } = "snapshot";
        public string OcrProfile { get; set; } = "default";
        public bool IsEnabled { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class CameraEvidenceSourceUpsertRequest
    {
        public Guid? Id { get; set; }
        public Guid SiteId { get; set; }
        public string? ProtectCameraId { get; set; }
        public string? ProtectDeviceKey { get; set; }
        public string? MacAddress { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string? OperationalZone { get; set; }
        public string ExpectedTextType { get; set; } = "unknown";
        public string CaptureMode { get; set; } = "snapshot";
        public string OcrProfile { get; set; } = "default";
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class CameraEvidenceEventPageDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<CameraEvidenceEventListItemDto> Data { get; set; } = new();
    }

    public class CameraEvidenceEventListItemDto
    {
        public Guid Id { get; set; }
        public Guid SiteId { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public Guid? SourceId { get; set; }
        public string? SourceName { get; set; }
        public string? AlarmName { get; set; }
        public string? TriggerType { get; set; }
        public string? ProtectDeviceKey { get; set; }
        public DateTime? EventTimestampUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string? ProcessingError { get; set; }
        public int FrameCount { get; set; }
        public int OcrResultCount { get; set; }
    }

    public sealed class CameraEvidenceEventDetailDto : CameraEvidenceEventListItemDto
    {
        public string? ProviderEventId { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? TriggerKey { get; set; }
        public string RawPayloadJson { get; set; } = "{}";
        public List<CameraEvidenceFrameDto> Frames { get; set; } = new();
    }

    public sealed class CameraEvidenceFrameDto
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public Guid SiteId { get; set; }
        public Guid SourceId { get; set; }
        public string CaptureMode { get; set; } = "snapshot";
        public DateTime FrameTimestampUtc { get; set; }
        public int? RelativeOffsetMs { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool IsHighQuality { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public List<CameraEvidenceOcrResultDto> OcrResults { get; set; } = new();
    }

    public sealed class CameraEvidenceOcrResultDto
    {
        public Guid Id { get; set; }
        public Guid FrameId { get; set; }
        public Guid SiteId { get; set; }
        public Guid SourceId { get; set; }
        public string Engine { get; set; } = string.Empty;
        public string? EngineVersion { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string? NormalizedText { get; set; }
        public string CandidateType { get; set; } = "unknown";
        public double Confidence { get; set; }
        public string ValidationStatus { get; set; } = "NotValidated";
        public string? ValidationReasonsJson { get; set; }
        public string? BoundingBoxesJson { get; set; }
        public string ReviewStatus { get; set; } = "Pending";
        public DateTime CreatedAtUtc { get; set; }
        public List<CameraEvidenceReviewDecisionDto> ReviewDecisions { get; set; } = new();
    }

    public sealed class CameraEvidenceReviewDecisionDto
    {
        public Guid Id { get; set; }
        public Guid OcrResultId { get; set; }
        public string ReviewerUserId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string? CorrectedText { get; set; }
        public string? CorrectedCandidateType { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class CameraEvidenceReviewRequest
    {
        public string Decision { get; set; } = string.Empty;
        public string? CorrectedText { get; set; }
        public string? CorrectedCandidateType { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class CameraEvidenceWebhookAcceptedDto
    {
        public Guid EventId { get; set; }
        public string SiteKey { get; set; } = string.Empty;
        public bool Accepted { get; set; }
        public bool Duplicate { get; set; }
        public bool MediaFetchQueued { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    public sealed class CameraEvidenceSnapshotTestResultDto
    {
        public bool Success { get; set; }
        public Guid? FrameId { get; set; }
        public string? ContentType { get; set; }
        public long? ByteCount { get; set; }
        public string? Sha256 { get; set; }
        public string? Error { get; set; }
    }

    public sealed class ProtectCameraDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Mac { get; set; }
        public string? Type { get; set; }
        public bool IsConnected { get; set; }
        public string RawJson { get; set; } = "{}";
    }
}
