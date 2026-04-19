namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Result of multi-container validation
    /// </summary>
    public class MultiContainerValidationResult
    {
        public string Container1 { get; set; } = string.Empty;
        public string Container2 { get; set; } = string.Empty;

        /// <summary>
        /// True if both containers belong to the same record (same BOE or same Master BL)
        /// </summary>
        public bool IsSameRecord { get; set; }

        /// <summary>
        /// True if one or both containers don't have BOE data yet
        /// </summary>
        public bool PendingBOEData { get; set; }

        /// <summary>
        /// True if this is a cross-record case requiring special tracking
        /// </summary>
        public bool RequiresSpecialTracking { get; set; }

        // Comparison flags
        public bool SameDeclaration { get; set; }
        public bool SameConsignee { get; set; }
        public bool SameMasterBL { get; set; }
        public bool SameRotation { get; set; }
        public bool SameCRMS { get; set; }
        public bool SameClearanceType { get; set; }

        /// <summary>
        /// Type of cross-record issue detected
        /// </summary>
        public CrossRecordType CrossRecordType { get; set; }

        public List<string> Warnings { get; set; } = new();
        public List<string> CriticalIssues { get; set; } = new();

        public void AddWarning(string warning) => Warnings.Add(warning);
        public void AddCritical(string critical) => CriticalIssues.Add(critical);
    }

    /// <summary>
    /// Types of cross-record issues
    /// </summary>
    public enum CrossRecordType
    {
        None = 0,
        DifferentBOEs = 1,          // Different declarations, same importer
        DifferentRiskLevels = 2,     // Mixed CRMS levels
        DifferentClearanceTypes = 3, // Import vs Export mix
        DifferentImporters = 4       // Most severe - different consignees
    }

    /// <summary>
    /// Analytics data for cross-record scans
    /// </summary>
    public class CrossRecordAnalytics
    {
        public int TotalCrossRecordScans { get; set; }
        public int DifferentImportersCount { get; set; }
        public int DifferentRiskLevelsCount { get; set; }
        public int DifferentClearanceTypesCount { get; set; }
        public int DifferentBOEsOnlyCount { get; set; }

        public int FS6000Count { get; set; }
        public int ASECount { get; set; }

        public List<DailyBreakdown> DailyBreakdown { get; set; } = new();
        public List<CrossRecordScanDto> Records { get; set; } = new();
    }

    public class DailyBreakdown
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// DTO for cross-record scan data
    /// </summary>
    public class CrossRecordScanDto
    {
        public int Id { get; set; }
        public string OriginalScanRecord { get; set; } = string.Empty;
        public Guid ScannerRecordId { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }

        // Container 1
        public string Container1 { get; set; } = string.Empty;
        public string? Container1_BOE { get; set; }
        public string? Container1_Consignee { get; set; }
        public string? Container1_CRMS { get; set; }
        public string? Container1_ClearanceType { get; set; }
        public string? Container1_MasterBL { get; set; }
        public string? Container1_Rotation { get; set; }

        // Container 2
        public string Container2 { get; set; } = string.Empty;
        public string? Container2_BOE { get; set; }
        public string? Container2_Consignee { get; set; }
        public string? Container2_CRMS { get; set; }
        public string? Container2_ClearanceType { get; set; }
        public string? Container2_MasterBL { get; set; }
        public string? Container2_Rotation { get; set; }

        // Classification
        public string CrossRecordType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public bool RequiresReview { get; set; }

        // Comparisons
        public bool SameDeclaration { get; set; }
        public bool SameConsignee { get; set; }
        public bool SameMasterBL { get; set; }
        public bool SameRotation { get; set; }
        public bool SameCRMS { get; set; }
        public bool SameClearanceType { get; set; }

        // Review
        public string ReviewStatus { get; set; } = "Pending";
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
    }

    /// <summary>
    /// Information about an image that's part of a cross-record scan
    /// </summary>
    public class CrossRecordImageInfo
    {
        public bool IsCrossRecordImage { get; set; }
        public string OriginalScanRecord { get; set; } = string.Empty;
        public string CurrentContainer { get; set; } = string.Empty;
        public int PositionInScan { get; set; } // 1 or 2
        public string SiblingContainer { get; set; } = string.Empty;

        public string CrossRecordType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;

        // Sibling details for comparison
        public string? SiblingBOE { get; set; }
        public string? SiblingConsignee { get; set; }
        public string? SiblingCRMS { get; set; }
    }
}

