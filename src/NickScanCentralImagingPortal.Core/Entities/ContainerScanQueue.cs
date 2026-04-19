using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Queue for container scans that need completeness checking
    /// Scanner services push container numbers to this queue when data is ingested
    /// Container Completeness Service consumes from this queue for processing
    /// </summary>
    public class ContainerScanQueue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string ScannerType { get; set; } = string.Empty; // 'FS6000', 'ASE', 'HeimannSmith'

        /// <summary>
        /// Unique inspection/scan ID from the scanner system
        /// FS6000: Guid (stored as string)
        /// ASE: int (stored as string)
        /// Allows tracking of multiple scans for the same container
        /// </summary>
        [MaxLength(50)]
        public string? InspectionId { get; set; }

        /// <summary>
        /// Date/time when the container was scanned (from scanner system)
        /// </summary>
        [Required]
        public DateTime ScanDate { get; set; }

        /// <summary>
        /// Queue status: Pending, Processing, Completed, Failed
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = QueueStatus.Pending;

        /// <summary>
        /// Priority: 0=Normal, 1=High, 2=Urgent
        /// New scans default to Normal (0)
        /// Re-checks can be higher priority
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Number of times processing has been attempted
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// Maximum number of retries before marking as failed
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// When the item was added to the queue
        /// </summary>
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When processing was started (status changed to Processing)
        /// </summary>
        public DateTime? ProcessedAt { get; set; }

        /// <summary>
        /// When processing was completed successfully
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Error message from last failed attempt
        /// </summary>
        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Additional metadata as JSON (e.g., source file path, batch ID)
        /// </summary>
        [MaxLength(2000)]
        public string? Metadata { get; set; }

        /// <summary>
        /// Timestamp when record was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when record was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Queue status values for ContainerScanQueue
    /// </summary>
    public static class ContainerScanQueueStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// Common scanner type constants for convenience
    /// Note: These are for reference only - the system accepts any scanner type string
    /// New scanners can use any string identifier without code changes
    /// </summary>
    public static class CommonScannerTypes
    {
        public const string FS6000 = "FS6000";
        public const string ASE = "ASE";
        public const string HeimannSmith = "HeimannSmith";
        public const string Nuctech = "Nuctech";
    }

    /// <summary>
    /// Maps scanner types to their physical port locations.
    /// DeliveryPlace codes from ICUMS contain a 3-letter port identifier (e.g. TMA = Tema, TKD = Takoradi).
    /// </summary>
    public static class ScannerLocationMap
    {
        public const string TemaCode = "TMA";
        public const string TakoradiCode = "TKD";

        private static readonly Dictionary<string, string> _scannerToPortCode = new(StringComparer.OrdinalIgnoreCase)
        {
            { CommonScannerTypes.FS6000, TakoradiCode },
            { CommonScannerTypes.ASE, TemaCode },
        };

        public static string? GetExpectedPortCode(string scannerType)
        {
            return _scannerToPortCode.TryGetValue(scannerType, out var code) ? code : null;
        }

        public static bool IsLocationMatch(string scannerType, string? deliveryPlace)
        {
            var expectedCode = GetExpectedPortCode(scannerType);
            if (expectedCode == null) return true; // unknown scanner → no gate
            if (string.IsNullOrWhiteSpace(deliveryPlace)) return true; // null delivery place → allow (flag separately)
            return deliveryPlace.Contains(expectedCode, StringComparison.OrdinalIgnoreCase);
        }

        public static string? ExtractPortCode(string? deliveryPlace)
        {
            if (string.IsNullOrWhiteSpace(deliveryPlace)) return null;
            if (deliveryPlace.Contains(TemaCode, StringComparison.OrdinalIgnoreCase)) return TemaCode;
            if (deliveryPlace.Contains(TakoradiCode, StringComparison.OrdinalIgnoreCase)) return TakoradiCode;
            return null;
        }
    }

    /// <summary>
    /// Classifies FS6000 FycoPresent values into Import / Export / Unknown.
    /// Handles known scanner typos (WWAYBILL, WABILL, WAY-BILL, IMPRT, etc.).
    /// </summary>
    public static class FycoClassifier
    {
        public static FycoCategory Classify(string? fycoPresent)
        {
            if (string.IsNullOrWhiteSpace(fycoPresent)) return FycoCategory.Unknown;
            var upper = fycoPresent.Trim().ToUpperInvariant();
            if (upper.Contains("EXPORT") || upper.Contains("WAYBILL") || upper.Contains("WABILL"))
                return FycoCategory.Export;
            if (upper.Contains("IMPORT") || upper == "IMPRT")
                return FycoCategory.Import;
            return FycoCategory.Unknown;
        }
    }

    public enum FycoCategory
    {
        Unknown,
        Import,
        Export
    }
}

