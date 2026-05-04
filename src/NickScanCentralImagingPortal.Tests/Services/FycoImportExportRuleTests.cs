using System.Text.RegularExpressions;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    /// <summary>
    /// Unit tests for the FycoPresent import/export agreement rule used in
    /// ContainerValidationService.ValidateFycoImportExportAsync. Tests the
    /// pure decision logic (flag parsing + outcome table).
    /// </summary>
    public class FycoImportExportRuleTests
    {
        [Theory]
        // Legacy boolean-ish markers (the original spec)
        [InlineData("1",      true)]
        [InlineData("true",   true)]
        [InlineData("TRUE",   true)]
        [InlineData("Y",      true)]
        [InlineData("YES",    true)]
        [InlineData("yes",    true)]
        [InlineData("0",      false)]
        [InlineData("false",  false)]
        [InlineData("N",      false)]
        [InlineData("NO",     false)]
        [InlineData("",       false)]
        [InlineData(null,     false)]
        [InlineData("unknown",false)]
        // Typed-verbiage markers — what FS6000 operators actually enter (see
        // memory reference_port_match_rules_enabled_2026_05_02.md). The regex
        // \bex(p)?ort\b catches the dominant forms.
        [InlineData("WAYBILL/EXPORT",   true)]
        [InlineData("waybill/export",   true)]
        [InlineData("EXPORT",           true)]
        [InlineData("export",           true)]
        [InlineData("WAY-BILL/EXPORT",  true)]
        [InlineData("WAYBILL/EXORT",    true)]
        [InlineData("WWAYBILL/EXPORT",  true)]
        // Import-side typed values must NOT be treated as export
        [InlineData("IMPORT",           false)]
        [InlineData("import",           false)]
        // Deeper typos that still slip through — documented residue
        [InlineData("WAYBILL/EPORT",    false)]   // missing 'x'
        [InlineData("WAYBILL/EXPORRT",  false)]   // doubled 'r'
        [InlineData("WAYBILL/EXPROT",   false)]   // letter swap
        [InlineData("WAYBILL/EXPOR",    false)]   // truncated
        public void IsExportFlag_RecognisesExportMarkers(string? raw, bool expected)
        {
            Assert.Equal(expected, IsExportFlag(raw));
        }

        [Theory]
        [InlineData("1",     "EX",  FycoOutcome.Pass)]
        [InlineData("YES",   "EX",  FycoOutcome.Pass)]
        [InlineData("true",  "EX",  FycoOutcome.Pass)]
        [InlineData("1",     "IM",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("0",     "IM",  FycoOutcome.Pass)]
        [InlineData("NO",    "IM",  FycoOutcome.Pass)]
        [InlineData("0",     "EX",  FycoOutcome.FailFycoSaysImportButBoeIsExport)]
        [InlineData("N",     "EX",  FycoOutcome.FailFycoSaysImportButBoeIsExport)]
        [InlineData("1",     "CMR", FycoOutcome.Skip)]
        [InlineData("0",     "cmr", FycoOutcome.Skip)]
        [InlineData("YES",   null,  FycoOutcome.Skip)]
        [InlineData("YES",   "",    FycoOutcome.Skip)]
        [InlineData(null,    "EX",  FycoOutcome.Skip)]
        // Typed-verbiage — production data shape (60% of real FycoPresent values)
        [InlineData("WAYBILL/EXPORT", "EX", FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "IM", FycoOutcome.FailFycoSaysExportButBoeIsImport)]   // 70326214329 case
        [InlineData("WAY-BILL/EXPORT","IM", FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("EXPORT",         "IM", FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("IMPORT",         "EX", FycoOutcome.FailFycoSaysImportButBoeIsExport)]
        [InlineData("IMPORT",         "IM", FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "CMR",FycoOutcome.Skip)]                                // CMR always skips
        [InlineData("",      "IM",  FycoOutcome.Skip)]
        public void EvaluateFyco_DecisionTable(string? fycoPresent, string? clearanceType, FycoOutcome expected)
        {
            Assert.Equal(expected, Evaluate(fycoPresent, clearanceType));
        }

        public enum FycoOutcome { Pass, FailFycoSaysExportButBoeIsImport, FailFycoSaysImportButBoeIsExport, Skip }

        // ── 3-LAYER FYCO RULE (clarified 2026-05-04) ──
        // FS6000 at ATSL Takoradi sea terminal. fyco=EXPORT = cargo physically
        // departing TKD on a vessel. The matching BOE must:
        //   layer 2 — clearancetype ∈ {EX, CMR}  (IM blocked)
        //   layer 3 — regime ∈ export set {10,19,20,24,27,30,34,35,37,39}, OR
        //             regime null/empty WITH clearancetype=CMR (defer)
        [Theory]
        // Layer 2 — clearancetype IM blocks regardless of regime
        [InlineData("WAYBILL/EXPORT", "IM",  "10",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "IM",  "40",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "IM",  "80",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "IM",  null,  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        // Layer 3 — clearancetype EX with non-export regime → fail (rare but caught)
        [InlineData("WAYBILL/EXPORT", "EX",  "40",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "EX",  "70",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "EX",  "80",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "EX",  "90",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        // Layer 3 — clearancetype CMR with non-export regime → fail (CMR with IM-side regime is suspicious)
        [InlineData("WAYBILL/EXPORT", "CMR", "40",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "CMR", "80",  FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        // Pass — clearancetype EX or CMR with regime in export set
        [InlineData("WAYBILL/EXPORT", "EX",  "10",  FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "EX",  "19",  FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "EX",  "20",  FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "EX",  "30",  FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "EX",  "39",  FycoOutcome.Pass)]
        [InlineData("WAYBILL/EXPORT", "CMR", "10",  FycoOutcome.Pass)]
        // Pass — CMR with empty regime (pre-declaration; defer)
        [InlineData("WAYBILL/EXPORT", "CMR", null,  FycoOutcome.Skip)]
        [InlineData("WAYBILL/EXPORT", "CMR", "",    FycoOutcome.Skip)]
        // fyco=IMPORT branch — existing semantics (regime irrelevant)
        [InlineData("IMPORT",         "IM",  "40",  FycoOutcome.Pass)]
        [InlineData("IMPORT",         "EX",  "10",  FycoOutcome.FailFycoSaysImportButBoeIsExport)]
        [InlineData("IMPORT",         "CMR", "40",  FycoOutcome.Skip)]
        public void EvaluateFyco_LayeredRule(string? fyco, string? clearance, string? regime, FycoOutcome expected)
        {
            Assert.Equal(expected, EvaluateLayered(fyco, clearance, regime));
        }

        private static readonly System.Collections.Generic.HashSet<string> ExportRegimes =
            new(System.StringComparer.OrdinalIgnoreCase)
            { "10", "19", "20", "24", "27", "30", "34", "35", "37", "39" };

        // Mirrors ContainerValidationService.ValidateFycoImportExportAsync's
        // 3-layer logic for unit-test coverage.
        private static FycoOutcome EvaluateLayered(string? fyco, string? clearance, string? regime)
        {
            if (string.IsNullOrWhiteSpace(fyco)) return FycoOutcome.Skip;
            if (string.IsNullOrWhiteSpace(clearance)) return FycoOutcome.Skip;

            var isFycoExport = IsExportFlag(fyco);
            var c = clearance.Trim();
            var isClearanceImport = c.StartsWith("IM", System.StringComparison.OrdinalIgnoreCase);
            var isClearanceExport = c.StartsWith("EX", System.StringComparison.OrdinalIgnoreCase);
            var isClearanceCmr = c.Equals("CMR", System.StringComparison.OrdinalIgnoreCase);

            if (isFycoExport)
            {
                // Layer 2: clearance IM blocks
                if (isClearanceImport) return FycoOutcome.FailFycoSaysExportButBoeIsImport;

                // Layer 3: regime check
                if (string.IsNullOrWhiteSpace(regime))
                {
                    return isClearanceCmr ? FycoOutcome.Skip : FycoOutcome.Pass; // CMR-no-regime defers; EX-no-regime passes
                }
                return ExportRegimes.Contains(regime.Trim())
                    ? FycoOutcome.Pass
                    : FycoOutcome.FailFycoSaysExportButBoeIsImport; // collapsed outcome — both layer 2 & 3 use same enum
            }

            // fyco=IMPORT/UNKNOWN — existing semantics
            if (isClearanceCmr) return FycoOutcome.Skip;
            return isClearanceExport
                ? FycoOutcome.FailFycoSaysImportButBoeIsExport
                : FycoOutcome.Pass;
        }

        // Mirrors ContainerValidationService.IsExportFlag — kept in sync deliberately
        // (no shared production helper to avoid pulling EF + DI deps into the test asm).
        private static readonly Regex ExportTokenRegex =
            new(@"\bex(p)?ort\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool IsExportFlag(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var t = raw.Trim();
            return t.Equals("1")
                || t.Equals("true", System.StringComparison.OrdinalIgnoreCase)
                || t.Equals("Y",    System.StringComparison.OrdinalIgnoreCase)
                || t.Equals("YES",  System.StringComparison.OrdinalIgnoreCase)
                || ExportTokenRegex.IsMatch(t);
        }

        private static FycoOutcome Evaluate(string? fycoPresent, string? clearanceType)
        {
            if (string.IsNullOrWhiteSpace(fycoPresent)) return FycoOutcome.Skip;
            if (string.IsNullOrWhiteSpace(clearanceType)) return FycoOutcome.Skip;
            if (clearanceType.Equals("CMR", System.StringComparison.OrdinalIgnoreCase)) return FycoOutcome.Skip;

            var isExportFlag = IsExportFlag(fycoPresent);
            var isBoeExport  = clearanceType.Equals("EX", System.StringComparison.OrdinalIgnoreCase);

            if (isExportFlag == isBoeExport) return FycoOutcome.Pass;
            return isExportFlag
                ? FycoOutcome.FailFycoSaysExportButBoeIsImport
                : FycoOutcome.FailFycoSaysImportButBoeIsExport;
        }
    }
}
