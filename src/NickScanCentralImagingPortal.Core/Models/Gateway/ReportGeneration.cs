using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Report generation request
    /// </summary>
    public class ReportGenerationRequest
    {
        public string ReportType { get; set; } = string.Empty; // "Container", "ICUMS", "Scanner", "Validation"
        public string Format { get; set; } = "PDF"; // "PDF", "Excel", "CSV"
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> Filters { get; set; } = new(); // Additional filters
        public Dictionary<string, object> Parameters { get; set; } = new(); // Custom parameters
        public bool IncludeCharts { get; set; } = true;
        public bool IncludeRawData { get; set; } = false;
    }

    /// <summary>
    /// Report generation response
    /// </summary>
    public class ReportGenerationResponse
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString();
        public string ReportType { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = "Completed"; // "Pending", "Processing", "Completed", "Failed"
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public int GenerationTimeMs { get; set; }

        // For immediate download
        public byte[]? FileData { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }

        // For async generation
        public string? DownloadUrl { get; set; }
        public int? FileSizeBytes { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}

