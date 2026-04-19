using System;
using System.Collections.Generic;

namespace NickScanWebApp.New.Models
{
    /// <summary>
    /// Shared DTOs for Audit workflow
    /// </summary>
    public class AuditGroupDto
    {
        public string GroupIdentifier { get; set; } = "";
        public string ScannerType { get; set; } = "";
        public bool IsConsolidated { get; set; }
        public int TotalContainers { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string SubmittedBy { get; set; } = "";
        public List<ImageAnalysisDecisionSummary> ImageAnalysisDecisions { get; set; } = new();
    }

    public class ImageAnalysisDecisionSummary
    {
        public string ContainerNumber { get; set; } = "";
        public string Decision { get; set; } = "";
        public string ReviewedBy { get; set; } = "";
        public DateTime ReviewedAt { get; set; }
        public string? Comments { get; set; }
        public string? Tags { get; set; }
    }
}

