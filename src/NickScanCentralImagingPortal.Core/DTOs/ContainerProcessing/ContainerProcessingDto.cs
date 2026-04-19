namespace NickScanCentralImagingPortal.Core.DTOs.ContainerProcessing
{
    /// <summary>
    /// DTO for container processing page - groups containers by clearance type logic
    /// </summary>
    public class ContainerGroupDto
    {
        public string ClearanceType { get; set; } = string.Empty; // IM, EX, CMR
        public string GroupingKey { get; set; } = string.Empty; // BOE for IM/EX, BL for CMR
        public string GroupingValue { get; set; } = string.Empty; // The actual BOE/BL number
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public List<ContainerProcessingItemDto> Containers { get; set; } = new();
        public DateTime? LatestScanDate { get; set; }
        public string PrimaryScannerType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Individual container item within a group
    /// </summary>
    public class ContainerProcessingItemDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string? BlNumber { get; set; }
        public string? BoeNumber { get; set; }
        public string? RotationNumber { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;

        // Data availability flags
        public bool HasScannerData { get; set; }
        public bool HasICUMSData { get; set; }
        public bool HasImages { get; set; }
        public bool HasBOE { get; set; }

        // Additional metadata
        public int ImageCount { get; set; }
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public DateTime? ScanDate { get; set; }
        public int CompletenessScore { get; set; }
        public string Status { get; set; } = string.Empty; // Complete, Incomplete, etc.
    }

    /// <summary>
    /// Summary statistics for container processing page
    /// </summary>
    public class ContainerProcessingSummaryDto
    {
        public int TotalGroups { get; set; }
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public int IncompleteContainers { get; set; }
        public double CompletionRate { get; set; }

        // By clearance type
        public int IMGroups { get; set; }
        public int EXGroups { get; set; }
        public int CMRGroups { get; set; }

        // By scanner type
        public int FS6000Containers { get; set; }
        public int ASEContainers { get; set; }
        public int HeimannContainers { get; set; }
    }
}

