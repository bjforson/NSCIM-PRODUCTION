namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Result of mapping validation
    /// </summary>
    public class MappingValidationResult
    {
        public bool IsValid { get; set; }
        public string? ValidationMessage { get; set; }
        public List<string> Issues { get; set; } = new();
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Container data ready for submission
    /// </summary>
    public class ContainerSubmissionData
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public int ScannerDataId { get; set; }
        public int ICUMSDataId { get; set; }
        public int RelationId { get; set; }
        public Guid? ScanImageAssetId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public string? SourceContainerLabel { get; set; }
        public List<string> ImagePaths { get; set; } = new();
        public Dictionary<string, object> ReportData { get; set; } = new();
        public DateTime ScanDate { get; set; }
        public DateTime ICUMSDataDate { get; set; }
    }
}
