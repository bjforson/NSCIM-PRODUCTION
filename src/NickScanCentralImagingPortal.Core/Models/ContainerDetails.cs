namespace NickScanCentralImagingPortal.Core.Models
{
    public class ContainerDetails
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public string ConsigneeName { get; set; } = string.Empty;
        public string ShipperName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? AssignedOperator { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? ProcessingCompletedAt { get; set; }
        public string? ScannerType { get; set; }
        public int ImageCount { get; set; }
        public int ProcessingResultsCount { get; set; }
        public int Priority { get; set; } = 1;
        public string? Comments { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}
