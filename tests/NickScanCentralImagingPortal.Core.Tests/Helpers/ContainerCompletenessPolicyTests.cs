using NickScanCentralImagingPortal.Core.Helpers;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Helpers;

public class ContainerCompletenessPolicyTests
{
    [Fact]
    public void Evaluate_ReturnsImageAnalysisOnlyWhenAllEvidenceAndGroupArePresent()
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: true,
            hasImageData: true,
            clearanceType: "IM",
            groupIdentifier: "DEC123");

        Assert.Equal("Complete", decision.Status);
        Assert.Equal("ImageAnalysis", decision.WorkflowStage);
        Assert.True(decision.IsComplete);
        Assert.False(decision.IsAwaitingDeclaration);
    }

    [Fact]
    public void Evaluate_HoldsCmrRowsWithoutDeclaration()
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: true,
            hasImageData: true,
            clearanceType: "CMR",
            groupIdentifier: "");

        Assert.Equal("AwaitingDeclaration", decision.Status);
        Assert.Equal("Pending", decision.WorkflowStage);
        Assert.False(decision.IsComplete);
        Assert.True(decision.IsAwaitingDeclaration);
    }

    [Fact]
    public void Evaluate_HoldsCmrCompositeRowsWhenFeatureFlagIsOff()
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: true,
            hasImageData: true,
            clearanceType: "CMR",
            groupIdentifier: "",
            cmrCompositeProgressionEnabled: false,
            cmrRotationNumber: "ROT123",
            cmrContainerNumber: "PIDU4444900",
            cmrBlNumber: "BL789");

        Assert.Equal("AwaitingDeclaration", decision.Status);
        Assert.Equal("Pending", decision.WorkflowStage);
        Assert.False(decision.IsComplete);
        Assert.True(decision.IsAwaitingDeclaration);
    }

    [Fact]
    public void Evaluate_AllowsCmrCompositeRowsWhenFeatureFlagIsOn()
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: true,
            hasImageData: true,
            clearanceType: "CMR",
            groupIdentifier: "",
            cmrCompositeProgressionEnabled: true,
            cmrRotationNumber: "ROT123",
            cmrContainerNumber: "PIDU4444900",
            cmrBlNumber: "BL789");

        Assert.Equal("Complete", decision.Status);
        Assert.Equal("ImageAnalysis", decision.WorkflowStage);
        Assert.True(decision.IsComplete);
        Assert.False(decision.IsAwaitingDeclaration);
    }

    [Theory]
    [InlineData(null, "PIDU4444900", "BL789")]
    [InlineData("ROT123", null, "BL789")]
    [InlineData("ROT123", "PIDU4444900", null)]
    public void Evaluate_HoldsCmrCompositeRowsWhenFeatureFlagIsOnButKeyIsIncomplete(
        string? rotationNumber,
        string? containerNumber,
        string? blNumber)
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: true,
            hasImageData: true,
            clearanceType: "CMR",
            groupIdentifier: "",
            cmrCompositeProgressionEnabled: true,
            cmrRotationNumber: rotationNumber,
            cmrContainerNumber: containerNumber,
            cmrBlNumber: blNumber);

        Assert.Equal("AwaitingDeclaration", decision.Status);
        Assert.Equal("Pending", decision.WorkflowStage);
        Assert.False(decision.IsComplete);
        Assert.True(decision.IsAwaitingDeclaration);
    }

    [Fact]
    public void Evaluate_ReturnsMissingWhenEvidenceIsIncomplete()
    {
        var decision = ContainerCompletenessPolicy.Evaluate(
            hasScannerData: true,
            hasICUMSData: false,
            hasImageData: true,
            clearanceType: "IM",
            groupIdentifier: "DEC123");

        Assert.Equal("Missing", decision.Status);
        Assert.Equal("Pending", decision.WorkflowStage);
        Assert.False(decision.IsComplete);
        Assert.False(decision.IsAwaitingDeclaration);
    }
}
