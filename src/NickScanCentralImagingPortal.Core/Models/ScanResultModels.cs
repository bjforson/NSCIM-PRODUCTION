using System.Text.Json.Serialization;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class ScanResultRequest
    {
        [JsonPropertyName("scanData")]
        public ScanData ScanData { get; set; } = new ScanData();
    }

    public class ScanData
    {
        [JsonPropertyName("DeclarationNumber")]
        public string DeclarationNumber { get; set; } = string.Empty;

        [JsonPropertyName("VersionNumber")]
        public int VersionNumber { get; set; }

        [JsonPropertyName("RotationNumber")]
        public string? RotationNumber { get; set; }

        [JsonPropertyName("BLNumber")]
        public string? BlNumber { get; set; }

        [JsonPropertyName("HouseBL")]
        public string? HouseBl { get; set; }

        [JsonPropertyName("ContainerNumber")]
        public string ContainerNumber { get; set; } = string.Empty;

        [JsonPropertyName("ScanReferenceNumber")]
        public string ScanReferenceNumber { get; set; } = string.Empty;

        [JsonPropertyName("ScanDate")]
        public string ScanDate { get; set; } = string.Empty;

        [JsonPropertyName("ScanStartDate")]
        public string ScanStartDate { get; set; } = string.Empty;

        [JsonPropertyName("ScanEndDate")]
        public string ScanEndDate { get; set; } = string.Empty;

        [JsonPropertyName("ScanAnalysisStartDate")]
        public string ScanAnalysisStartDate { get; set; } = string.Empty;

        [JsonPropertyName("ScanAnalysisEndDate")]
        public string ScanAnalysisEndDate { get; set; } = string.Empty;

        [JsonPropertyName("TruckPlateNumber")]
        public string? TruckPlateNumber { get; set; }

        [JsonPropertyName("Verdict")]
        public string Verdict { get; set; } = string.Empty;

        [JsonPropertyName("FindingsDescription")]
        public string FindingsDescription { get; set; } = string.Empty;

        [JsonPropertyName("ImageAnalystName")]
        public string ImageAnalystName { get; set; } = string.Empty;

        [JsonPropertyName("CustomOfficerName")]
        public string CustomOfficerName { get; set; } = string.Empty;

        [JsonPropertyName("ImageDocument")]
        public string? ImageDocument { get; set; }
    }

    public class ScanResultResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("statusMsg")]
        public string StatusMsg { get; set; } = string.Empty;
    }

    public class ReadStatusRequest
    {
        [JsonPropertyName("containerNumbers")]
        public List<ContainerNumberInfo> ContainerNumbers { get; set; } = new List<ContainerNumberInfo>();
    }

    public class ContainerNumberInfo
    {
        [JsonPropertyName("RotationNumber")]
        public string RotationNumber { get; set; } = string.Empty;

        [JsonPropertyName("BLNumber")]
        public string? BlNumber { get; set; }

        [JsonPropertyName("ContainerNumber")]
        public string? ContainerNumber { get; set; }
    }

    public class ReadStatusResponse
    {
        [JsonPropertyName("readstatus")]
        public List<ReadStatusItem> ReadStatus { get; set; } = new List<ReadStatusItem>();
    }

    public class ReadStatusItem
    {
        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("ContainerNumber")]
        public string ContainerNumber { get; set; } = string.Empty;
    }
}
