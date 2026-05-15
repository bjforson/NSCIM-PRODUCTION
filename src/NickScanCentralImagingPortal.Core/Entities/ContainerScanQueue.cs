using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

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
        /// Number of failed processing attempts.
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
        public const string EagleA25 = "EagleA25";
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
    ///
    /// Single source of truth for FycoPresent → direction. The previous parallel
    /// implementation in ContainerValidationService.IsExportFlag (regex-only,
    /// matching `\bex(p)?ort\b` plus literals 1/true/Y/YES) missed export-typo
    /// records that this classifier accepted, and vice versa. Audit 3.08
    /// (2026-05-05) closed the divergence — IsExportFlag now delegates to
    /// FycoClassifier.IsExport. Keep the two parsers in lockstep here.
    /// </summary>
    public static class FycoClassifier
    {
        // Broadened export-token regex. Catches the canonical "EXPORT" plus
        // 1-letter typos (EXPOR / EXPOT / EXPROT / EPORT / EXORT) and the
        // operator-typed waybill verbiage (WAYBILL, WABILL, WAYBILLL,
        // WAY-BILL, "WAYBILL/EXPORT", "WAYBILL/EXPOT"). The waybill arm
        // accepts an optional separator before BILL so WAY-BILL, WAY.BILL,
        // WAY/BILL all match. The export arm enumerates explicit deletion-
        // typos rather than over-permissive character classes — that keeps
        // unrelated words ("EXIST", "EXPENSE") from being mis-classified.
        //
        // Verified against the 8 known-failing typo cases in the audit:
        //   EXPOR / EXPOT / EXPROT / EPORT / EXORT
        //   WAYBILL/EXPOT / WAYBILL/EXPROT / WAYBILLL/EXPORT
        // Plus the canonical forms:
        //   1 / true / Y / YES (literal flags handled in IsExport)
        //   EXPORT, WAYBILL, WABILL, WAY-BILL/EXPORT, WAYBILL/.EXPORT
        private static readonly Regex ExportTokenRegex = new(
            @"\b(?:export|expor|expot|exort|eport|exprot|wa+y?[-./\s]?b?il+l?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static FycoCategory Classify(string? fycoPresent)
        {
            if (string.IsNullOrWhiteSpace(fycoPresent)) return FycoCategory.Unknown;
            var upper = fycoPresent.Trim().ToUpperInvariant();
            if (IsExport(upper))
                return FycoCategory.Export;
            if (upper.Contains("IMPORT") || upper == "IMPRT")
                return FycoCategory.Import;
            return FycoCategory.Unknown;
        }

        /// <summary>
        /// Returns true if the raw FycoPresent string indicates an export.
        /// Recognises:
        ///   - Literal boolean-ish flags (1, true, Y, YES — case-insensitive).
        ///   - The canonical EXPORT word and 1-letter deletion / transposition
        ///     typos (EXPOR, EXPOT, EXPROT, EPORT, EXORT).
        ///   - The waybill family (WAYBILL, WABILL, WAYBILLL, WAY-BILL,
        ///     WAY.BILL, WAY/BILL) — operators commonly type free-text waybill
        ///     verbiage to indicate exports.
        ///   - Compound strings combining the two (WAYBILL/EXPORT,
        ///     WAY-BILL/EXPORT, WAYBILL/EXPOT, WAYBILL/EXPROT, etc.) — \b
        ///     boundaries make either side sufficient.
        ///
        /// This is the canonical export-direction parser. ContainerValidationService
        /// .IsExportFlag delegates here. Update one place; both gates follow.
        /// </summary>
        public static bool IsExport(string? fycoPresent)
        {
            if (string.IsNullOrWhiteSpace(fycoPresent)) return false;
            var trimmed = fycoPresent.Trim();
            if (trimmed.Equals("1")
                || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("YES", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return ExportTokenRegex.IsMatch(trimmed);
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

        // Import-direction regimes — Ghana Customs codes 4*/5*/6*/7* + free-zone
        // 9* per the canonical ICUMS list (verified 2026-05-03). Used by the
        // ingest-side implicit CMR→IM upgrade switch (see audit 3.07,
        // 2026-05-05): when an upgraded CMR row arrives with a regime in this
        // set, the row is upgraded to ClearanceType="IM". Replaces the prior
        // first-char heuristic that mis-bucketed regime 27 (export) as IM via
        // first-char '2'. Free-zone codes are included because the legacy
        // first-char '9' switch routed them to IM and the change is intended
        // to be classification-preserving for known regimes.
        private static readonly HashSet<string> ImportRegimes = new(StringComparer.OrdinalIgnoreCase)
        {
            // 4* home use family.
            "40", "45", "47", "48", "49",
            // 5* temporary admission family.
            "50", "57", "59",
            // 6* re-importation.
            "61", "62",
            // 7* warehousing family.
            "70", "72", "75", "77", "79",
            // 9* free zone (lumped with import for classification continuity;
            // distinct documenttype bucket via ClassifyDocumentType).
            "90", "94", "95", "97", "99",
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
        /// True when the regime code maps to import direction (4*/5*/6*/7* + free-zone 9*).
        /// Added 2026-05-05 (audit 3.07) so the ingest-side implicit CMR→IM upgrade switch
        /// in IcumJsonIngestionService.cs can replace its first-char heuristic with a
        /// direct lookup. Fail-closed for blank / unknown codes.
        /// </summary>
        public static bool IsImport(string? regimeCode)
        {
            if (string.IsNullOrWhiteSpace(regimeCode)) return false;
            return ImportRegimes.Contains(regimeCode.Trim());
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

    /// <summary>
    /// Outcome of the 3-layer fyco/clearancetype/regime rule. Returned by
    /// <see cref="FycoRuleEvaluator.Evaluate"/> so the three callers (CCS Step 1,
    /// CCS Step 2, ContainerDataMapperService) all reach the same verdict and
    /// write a consistent MatchQualityFlag.
    /// </summary>
    public enum FycoRuleOutcome
    {
        /// <summary>Rule does not apply (non-FS6000 scanner, or no fyco signal).</summary>
        NotApplicable,
        /// <summary>Rule passed — direction agrees.</summary>
        Pass,
        /// <summary>fyco=Export but BOE.ClearanceType=IM. Block + Critical flag.</summary>
        FailLayer2_ClearanceTypeImport,
        /// <summary>fyco=Export + EX/CMR clearance + non-export regime. Block + Critical flag.</summary>
        FailLayer3_NonExportRegime,
        /// <summary>BOE is export but fyco unknown — allowed match, Warning-severity flag.</summary>
        WarningSuspicious_UnknownFycoVsExportBoe,
    }

    /// <summary>
    /// Result of <see cref="FycoRuleEvaluator.Evaluate"/>. Carries the outcome
    /// plus a ready-to-persist description so the calling site can write its
    /// flag without re-deriving the message text.
    /// </summary>
    public sealed record FycoRuleResult(
        FycoRuleOutcome Outcome,
        string? FlagDescription,
        string? FycoPresentRaw,
        string? BoeClearanceType,
        string? BoeRegimeCode)
    {
        public bool IsBlockingFailure =>
            Outcome == FycoRuleOutcome.FailLayer2_ClearanceTypeImport ||
            Outcome == FycoRuleOutcome.FailLayer3_NonExportRegime;

        public bool IsWarning => Outcome == FycoRuleOutcome.WarningSuspicious_UnknownFycoVsExportBoe;

        public static FycoRuleResult NotApplicable() =>
            new(FycoRuleOutcome.NotApplicable, null, null, null, null);
    }

    /// <summary>
    /// Single source of truth for the 3-layer fyco rule. Audit 3.03 (2026-05-05)
    /// hoisted this out of three duplicated implementations:
    ///   - ContainerCompletenessService Step 1 (queue-driven path)
    ///   - ContainerCompletenessService Step 2 (re-check path) — was MISSING
    ///     entirely; a previously-Complete container could keep stale
    ///     hasICUMSData=true after a CMR→IM upgrade landed mid-flight.
    ///   - ContainerDataMapperService.MapContainerDataAsync (mapper belt-and-braces)
    ///
    /// All three now call <see cref="Evaluate"/> with already-fetched FS6000
    /// FycoPresent + BOE clearance/regime, and write their existing
    /// MatchQualityFlag using the returned description so the database keeps a
    /// single description shape per (container, FycoMismatch) flag.
    ///
    /// Pure (no DB / no logger). Each caller does its own scan + BOE fetch and
    /// its own flag persistence — keeps the rule unit-testable and decoupled
    /// from EF / DbContext lifetimes.
    /// </summary>
    public static class FycoRuleEvaluator
    {
        /// <summary>
        /// Evaluates the 3-layer fyco rule. Returns Pass / NotApplicable for
        /// non-FS6000 scanners and for non-Export fyco signals. Returns
        /// FailLayer2 / FailLayer3 / WarningSuspicious otherwise.
        /// </summary>
        /// <param name="scannerType">e.g. "FS6000", "ASE". Rule only fires for FS6000.</param>
        /// <param name="fs6000FycoPresent">The FycoPresent value from the most recent FS6000 scan, or null.</param>
        /// <param name="boeClearanceType">BOEDocument.ClearanceType — typically "IM"/"EX"/"CMR".</param>
        /// <param name="boeRegimeCode">BOEDocument.RegimeCode — typically a 2-digit string like "40", "10", "80".</param>
        public static FycoRuleResult Evaluate(
            string? scannerType,
            string? fs6000FycoPresent,
            string? boeClearanceType,
            string? boeRegimeCode)
        {
            // Rule only meaningful for FS6000 (scanner sits at ATSL Takoradi
            // sea terminal; fyco=EXPORT means departing TKD on a vessel).
            if (!string.Equals(scannerType, CommonScannerTypes.FS6000, StringComparison.OrdinalIgnoreCase))
            {
                return FycoRuleResult.NotApplicable();
            }

            var scanFyco = FycoClassifier.Classify(fs6000FycoPresent);
            var clearance = (boeClearanceType ?? string.Empty).Trim().ToUpperInvariant();
            var boeIsImport = clearance.StartsWith("IM");
            var boeIsExport = clearance.StartsWith("EX");
            var boeIsCmr = clearance.Equals("CMR");

            // Layer 2 — fyco=Export vs clearancetype=IM is the strongest mismatch.
            if (scanFyco == FycoCategory.Export && boeIsImport)
            {
                return new FycoRuleResult(
                    FycoRuleOutcome.FailLayer2_ClearanceTypeImport,
                    $"Scan FycoPresent='{fs6000FycoPresent}' classifies as Export, but BOE.ClearanceType='{boeClearanceType}' is Import. Cargo physically departing TKD cannot be an import.",
                    fs6000FycoPresent,
                    boeClearanceType,
                    boeRegimeCode);
            }

            // Layer 3 — fyco=Export + EX/CMR clearance + non-empty regime
            // that is NOT in the export set. Empty regime + CMR is OK
            // (defer to BOE arrival).
            if (scanFyco == FycoCategory.Export
                && (boeIsExport || boeIsCmr)
                && !string.IsNullOrWhiteSpace(boeRegimeCode)
                && !RegimeDirectionMap.IsExport(boeRegimeCode))
            {
                return new FycoRuleResult(
                    FycoRuleOutcome.FailLayer3_NonExportRegime,
                    $"Scan FycoPresent='{fs6000FycoPresent}' (Export) but BOE.RegimeCode='{boeRegimeCode}' is not an export regime (export set: 10,19,20,24,27,30,34,35,37,39). Clearance was '{boeClearanceType}'.",
                    fs6000FycoPresent,
                    boeClearanceType,
                    boeRegimeCode);
            }

            // Suspicious-but-allowed: export BOE with no Fyco confirmation.
            if (scanFyco == FycoCategory.Unknown && boeIsExport)
            {
                return new FycoRuleResult(
                    FycoRuleOutcome.WarningSuspicious_UnknownFycoVsExportBoe,
                    $"BOE.ClearanceType='{boeClearanceType}' is Export, but scan FycoPresent='{fs6000FycoPresent ?? "(empty)"}' provides no confirmation. Match allowed but flagged.",
                    fs6000FycoPresent,
                    boeClearanceType,
                    boeRegimeCode);
            }

            return new FycoRuleResult(
                FycoRuleOutcome.Pass,
                null,
                fs6000FycoPresent,
                boeClearanceType,
                boeRegimeCode);
        }
    }
}
