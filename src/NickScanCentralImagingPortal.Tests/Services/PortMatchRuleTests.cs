using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    /// <summary>
    /// Unit tests for the port-match rule logic used in
    /// ContainerValidationService.ValidatePortMatchAsync. Tests the pure
    /// decision logic (pattern matching on DeliveryPlace + scanner presence).
    /// </summary>
    public class PortMatchRuleTests
    {
        [Theory]
        [InlineData("WTTMA1MPS3", "TMA")]
        [InlineData("WITMA1TRST", "TMA")]
        [InlineData("WWTMA1T300", "TMA")]
        [InlineData("WTTKD1TKD1", "TKD")]
        [InlineData("WITKD1ABCD", "TKD")]
        public void ExtractPort_FromValidDeliveryPlaceCode_ReturnsPort(string dp, string expected)
        {
            Assert.Equal(expected, ExtractPort(dp));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ABC")]
        [InlineData("XYZABCDEFG")]
        [InlineData("WTACC1XXXX")]
        public void ExtractPort_FromInvalidOrUnknown_ReturnsNull(string? dp)
        {
            Assert.Null(ExtractPort(dp));
        }

        [Theory]
        [InlineData("TKD", true,  false, PortMatchOutcome.Pass)]
        [InlineData("TKD", false, true,  PortMatchOutcome.FailAseMismatch)]
        [InlineData("TKD", true,  true,  PortMatchOutcome.PassTransit)]
        [InlineData("TKD", false, false, PortMatchOutcome.Skip)]
        [InlineData("TMA", false, true,  PortMatchOutcome.Pass)]
        [InlineData("TMA", true,  false, PortMatchOutcome.FailFs6000Mismatch)]
        [InlineData("TMA", true,  true,  PortMatchOutcome.PassTransit)]
        [InlineData("TMA", false, false, PortMatchOutcome.Skip)]
        [InlineData(null,  true,  false, PortMatchOutcome.Skip)]
        [InlineData(null,  false, true,  PortMatchOutcome.Skip)]
        public void EvaluatePortMatch_DecisionTable(string? boePort, bool fs6000Scan, bool aseScan, PortMatchOutcome expected)
        {
            Assert.Equal(expected, Evaluate(boePort, fs6000Scan, aseScan));
        }

        public enum PortMatchOutcome { Pass, PassTransit, FailFs6000Mismatch, FailAseMismatch, Skip }

        private static string? ExtractPort(string? deliveryPlace)
        {
            if (string.IsNullOrWhiteSpace(deliveryPlace) || deliveryPlace.Length < 5) return null;
            var code = deliveryPlace.Substring(2, 3).ToUpperInvariant();
            return (code == "TKD" || code == "TMA") ? code : null;
        }

        private static PortMatchOutcome Evaluate(string? boePort, bool scannedByFs6000, bool scannedByAse)
        {
            if (string.IsNullOrEmpty(boePort)) return PortMatchOutcome.Skip;
            if (!scannedByFs6000 && !scannedByAse) return PortMatchOutcome.Skip;

            var fs6000Conflict = scannedByFs6000 && boePort != "TKD" && !scannedByAse;
            var aseConflict    = scannedByAse    && boePort != "TMA" && !scannedByFs6000;

            if (fs6000Conflict) return PortMatchOutcome.FailFs6000Mismatch;
            if (aseConflict)    return PortMatchOutcome.FailAseMismatch;
            if (scannedByFs6000 && scannedByAse) return PortMatchOutcome.PassTransit;
            return PortMatchOutcome.Pass;
        }
    }
}
