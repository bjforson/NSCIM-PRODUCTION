namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Unified scanner container data model used across multiple services
    /// </summary>
    public class ScannerContainerData
    {
        public Guid Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public string? FilePath { get; set; }
        public string? ImagePath { get; set; }
        public bool HasImage { get; set; }
    }
}

