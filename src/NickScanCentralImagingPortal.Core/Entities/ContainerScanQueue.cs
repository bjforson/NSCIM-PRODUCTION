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

    /// <summary>
    /// Maps WCO regime codes to their direction (import / export / transit) per the
    /// canonical Ghana Customs ICUMS list at
    /// https://external.unipassghana.com/co/code/popup/selectRegimeCode.do
    /// (verified 2026-05-03; 34 codes total).
    ///
    /// Per the ICUMS Manual, transit cargo is declared via a Bonded Transportation
    /// (BT) Declaration, NOT a regular BOE — see "BONDED TRANSPORTATION (BT)
    /// DECLARATION PROCESS" in the External User Guide. The fact we see regime-80
    /// rows in `boedocuments` is a v1 ingestion-side conflation.
    ///
    /// IMPORTANT — fyco-rule semantics (clarified 2026-05-04):
    /// FS6000 lives at ATSL Takoradi sea-port terminal; fyco=EXPORT means cargo
    /// is physically departing TKD on a vessel. Transit cargo arrives at TKD by
    /// vessel and leaves Ghana by ROAD (overland to Mali/Burkina/Niger), so a
    /// transit BOE matched to fyco=EXPORT is a REAL anomaly the fyco rule must
    /// catch. **Do NOT use IsTransit() to skip the fyco rule.** It is intended
    /// for the INGEST-side implicit CMR→IM upgrade switch only — keeps regime-80
    /// CMR-typed messages from being incorrectly auto-flipped to IM.
    /// </summary>
    public static class RegimeDirectionMap
    {
        // Transit / Transhipment / CoastWise Removal — Ghana Customs codes 80/88/89.
        private static readonly HashSet<string> TransitRegimes = new(StringComparer.OrdinalIgnoreCase)
        {
            "80", // Transit / Transhipment / CoastWise Removal
            "88", // Transit / Transhipment following Transit / Transhipment
            "89", // Transit of petroleum products from Bond
        };

        // Export-direction regimes — Ghana Customs codes 1*/2*/3* per the canonical
        // ICUMS list (verified 2026-05-03). Used by the fyco rule: an FS6000 scan
        // tagged fyco=EXPORT (cargo physically departing ATSL Takoradi by vessel)
        // can ONLY legitimately match a BOE whose regime is in this set. Any other
        // regime (40 home use, 70 warehousing, 80 transit, 90 free zone, etc.)
        // matched to fyco=EXPORT is a wrong match — block.
        private static readonly HashSet<string> ExportRegimes = new(StringComparer.OrdinalIgnoreCase)
        {
            "10", // Direct Export
            "19", // Petroleum export following Petroleum Operations
            "20", // Direct Temporary Export
            "24", // Temporary Export following Import into Home Use
            "27", // Temporary Export Following Warehousing
            "30", // Direct Re-export for Goods landed but not entered
            "34", // Re-export following home consumption
            "35", // Re-export following temporary Admission
            "37", // Re-export following warehousing
            "39", // Other Re-export
        };

        // Free-Zone regimes — Ghana Customs codes 90/94/95/97/99 per the canonical
        // ICUMS list (verified 2026-05-03). Free-zone cargo follows a separate
        // workflow from regular import (warehousing in a Free Zone Enclave) and
        // shouldn't be lumped under the generic 'BOE' bucket.
        private static readonly HashSet<string> FreeZoneRegimes = new(StringComparer.OrdinalIgnoreCase)
        {
            "90", // Free Zone Direct Import
            "94",
            "95",
            "97",
            "99",
        };

        // Standard BOE regimes — everything that is neither transit nor free-zone.
        // Listed explicitly (verified 2026-05-03) so an unknown / new regime code
        // lands in the NULL documenttype bucket instead of being silently bucketed
        // as 'BOE'. The canonical map has 34 codes total: 10 export + 5 free-zone +
        // 3 transit + these 25 "regular BOE" codes (home use, warehousing,
        // temporary admission, etc.).
        private static readonly HashSet<string> BoeRegimes = new(StringComparer.OrdinalIgnoreCase)
        {
            // 1* + 3* exports (also accepted as BOE-typed documents — fyco rule
            // distinguishes direction separately via IsExport).
            "10", "19", "20", "24", "27",
            "30", "34", "35", "37", "39",
            // 4* home use family.
            "40", "45", "47", "48", "49",
            // 5* temporary admission family.
            "50", "57", "59",
            // 6* re-importation.
            "61", "62",
            // 7* warehousing family.
            "70", "72", "75", "77", "79",
        };

        public static bool IsTransit(string? regimeCode)
        {
            if (string.IsNullOrWhiteSpace(regimeCode)) return false;
            return TransitRegimes.Contains(regimeCode.Trim());
        }

        public static bool IsExport(string? regimeCode)
        {
            if (string.IsNullOrWhiteSpace(regimeCode)) return false;
            return ExportRegimes.Contains(regimeCode.Trim());
        }

        /// <summary>
        /// Classifies a regime code into a coarse documenttype bucket so downstream
        /// consumers can filter on documenttype = 'Transit' / 'BOE' / 'Free Zone'
        /// instead of hard-coding regime-set membership in every rule.
        ///
        /// Returns null for blank input or for any regime code not present in the
        /// canonical Ghana Customs ICUMS list (verified 2026-05-03). NULL input
        /// is the normal case for pre-declaration CMR rows that arrive before any
        /// IM/EX upgrade brings a regimecode along.
        ///
        /// Stamped at ingest by IcumJsonIngestionService and persisted to
        /// boedocuments.documenttype. Mirrors the SQL backfill in
        /// tools/migrations/documenttype-tagging/01-add-documenttype-column.sql —
        /// keep in lockstep.
        /// </summary>
        public static string? ClassifyDocumentType(string? regimeCode)
        {
            if (string.IsNullOrWhiteSpace(regimeCode)) return null;
            var trimmed = regimeCode.Trim();
            if (TransitRegimes.Contains(trimmed)) return "Transit";
            if (FreeZoneRegimes.Contains(trimmed)) return "Free Zone";
            if (BoeRegimes.Contains(trimmed)) return "BOE";
            return null; // unknown regime — leave NULL so it surfaces in audit query
        }
    }
}

