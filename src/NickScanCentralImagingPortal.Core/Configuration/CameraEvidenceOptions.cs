namespace NickScanCentralImagingPortal.Core.Configuration
{
    public sealed class CameraEvidenceOptions
    {
        public const string SectionName = "CameraEvidence";

        public bool Enabled { get; set; }
        public bool WebhookIngestionEnabled { get; set; }
        public bool MediaFetchEnabled { get; set; }
        public bool OcrEnabled { get; set; }
        public bool ExternalVisionFallbackEnabled { get; set; }
        public bool CoreReadOnlyLookupEnabled { get; set; }
        public bool CoreDisplayPromotionEnabled { get; set; }
        public bool CoreDecisionSupportEnabled { get; set; }
        public bool CoreAutomationEnabled { get; set; }

        public string StorageRoot { get; set; } = "Data/CameraEvidence";
        public int WorkerPollSeconds { get; set; } = 5;
        public int MaxMediaFetchAttempts { get; set; } = 3;
        public int MaxOcrAttempts { get; set; } = 2;
        public int MaxWorkItemsPerPoll { get; set; } = 10;
        public bool SnapshotHighQualityDefault { get; set; } = true;
        public string DefaultSnapshotChannel { get; set; } = "main";
        public int WebhookRateLimitPerMinute { get; set; } = 120;
        public double OcrLowConfidenceThreshold { get; set; } = 0.55;
    }
}
