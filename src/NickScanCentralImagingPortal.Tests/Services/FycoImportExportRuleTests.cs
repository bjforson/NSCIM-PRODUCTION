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

        // Transit regimes (80/88/89) must be skipped — transit cargo legitimately
        // carries fyco=EXPORT (cargo leaves Ghana for Mali/Burkina Faso/Niger) even
        // when ICUMS stamps clearancetype=IM. Mirrors RegimeDirectionMap.IsTransit
        // + the production rule's transit early-return.
        [Theory]
        [InlineData("WAYBILL/EXPORT", "IM", "80", FycoOutcome.Skip)]
        [InlineData("WAYBILL/EXPORT", "IM", "88", FycoOutcome.Skip)]
        [InlineData("WAYBILL/EXPORT", "IM", "89", FycoOutcome.Skip)]
        [InlineData("EXPORT",         "IM", "80", FycoOutcome.Skip)]
        [InlineData("1",              "IM", "80", FycoOutcome.Skip)]
        // Non-transit regimes still flow through the regular rule
        [InlineData("WAYBILL/EXPORT", "IM", "40", FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "IM", "70", FycoOutcome.FailFycoSaysExportButBoeIsImport)]
        [InlineData("WAYBILL/EXPORT", "IM", null, FycoOutcome.FailFycoSaysExportButBoeIsImport)]   // unknown regime — don't skip
        [InlineData("WAYBILL/EXPORT", "IM", "",   FycoOutcome.FailFycoSaysExportButBoeIsImport)]   // empty regime — don't skip
        [InlineData("WAYBILL/EXPORT", "EX", "10", FycoOutcome.Pass)]                                // export regime, agreed clearance
        [InlineData("0",              "IM", "40", FycoOutcome.Pass)]                                // import-flagged on import BOE — fine
        public void EvaluateFyco_TransitSkipsRule(string? fyco, string? clearance, string? regime, FycoOutcome expected)
        {
            Assert.Equal(expected, EvaluateWithRegime(fyco, clearance, regime));
        }

        private static readonly System.Collections.Generic.HashSet<string> TransitRegimes =
            new(System.StringComparer.OrdinalIgnoreCase) { "80", "88", "89" };

        // Mirrors ContainerValidationService.ValidateFycoImportExportAsync's transit
        // early-return + the underlying RegimeDirectionMap.IsTransit() helper.
        private static FycoOutcome EvaluateWithRegime(string? fyco, string? clearance, string? regime)
        {
            if (!string.IsNullOrWhiteSpace(regime) && TransitRegimes.Contains(regime.Trim()))
                return FycoOutcome.Skip;
            return Evaluate(fyco, clearance);
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
