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
        [InlineData("",      "IM",  FycoOutcome.Skip)]
        public void EvaluateFyco_DecisionTable(string? fycoPresent, string? clearanceType, FycoOutcome expected)
        {
            Assert.Equal(expected, Evaluate(fycoPresent, clearanceType));
        }

        public enum FycoOutcome { Pass, FailFycoSaysExportButBoeIsImport, FailFycoSaysImportButBoeIsExport, Skip }

        private static bool IsExportFlag(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var t = raw.Trim();
            return t.Equals("1")
                || t.Equals("true", System.StringComparison.OrdinalIgnoreCase)
                || t.Equals("Y",    System.StringComparison.OrdinalIgnoreCase)
                || t.Equals("YES",  System.StringComparison.OrdinalIgnoreCase);
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
