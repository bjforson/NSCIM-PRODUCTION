using System.Runtime.CompilerServices;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Architecture;

public class ImageDecisionSaveGuardrailTests
{
    [Fact]
    public void ApiService_PreservesStructuredHttpFailureDetails()
    {
        var apiService = ReadRepoFile("src/NickScanWebApp.Shared/Services/ApiService.cs");

        Assert.Contains("EnsureSuccessOrThrowAsync", apiService);
        Assert.Contains("throw new ApiException(message, status, raw, method, endpoint);", apiService);
        Assert.Contains("public string? ResponseBody { get; }", apiService);
        Assert.Contains("catch (ApiException)", apiService);
        Assert.DoesNotContain("response.EnsureSuccessStatusCode()", apiService);
    }

    [Fact]
    public void SplitDecisionSavePaths_RequireSplitChoiceBeforeFullscreenSave()
    {
        var viewer = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor");
        var dialog = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewDialog.razor");
        var splitChoice = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/SplitChoiceDialog.razor");

        Assert.Contains("IsSplitChoiceRequiredForDecision()", viewer);
        Assert.Contains("Disabled=\"@IsSplitChoiceRequiredForDecision()\"", viewer);
        Assert.Contains("GetUserFacingApiError(ex)", viewer);

        Assert.Contains("IsSplitChoiceRequiredForDecision(sourceResolution)", dialog);
        Assert.Contains("_perContainerSplitState[containerNumber] = \"ChoiceRequired\";", dialog);

        Assert.Contains("!string.IsNullOrWhiteSpace(_loadErrorMessage)", splitChoice);
        Assert.Contains("return \"ChoiceRequired\";", splitChoice);
    }

    [Fact]
    public void SplitDecisionPersistence_PreservesSplitLineageThroughImageAnalysisAndAudit()
    {
        var decisionController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDecisionController.cs");
        var legacyController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs");
        var sideEffects = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionSideEffectsService.cs");
        var agentWorker = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionAgent/DecisionAgentWorker.cs");
        var auditController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/AuditReviewController.cs");
        var decisionClient = ReadRepoFile("src/NickScanWebApp.Shared/Services/ImageAnalysisDecisionClient.cs");

        Assert.Contains("SplitDecisionEligibility.RequiresSplitResolution(arForSplit)", decisionController);
        Assert.Contains("ValidateAllContainersWithImagesHaveDecisionsAsync(", decisionController);
        Assert.Contains("SplitDecisionEligibility.IsDecisionCompatible(splitRecord, d)", decisionController);
        Assert.Contains("code = \"split_choice_required\"", decisionController);

        Assert.Contains("SplitDecisionEligibility.RequiresSplitResolution", legacyController);
        Assert.Contains("SplitJobId = r.SplitJobId", legacyController);
        Assert.Contains("SplitResultId = r.SplitResultId", legacyController);
        Assert.DoesNotContain("completedDecisionRequested", legacyController);

        Assert.Contains("SplitDecisionEligibility.RequiresSplitResolution", sideEffects);
        Assert.Contains("SplitDecisionEligibility.IsDecisionCompatible(record, decision)", sideEffects);
        Assert.Contains("SplitDecisionEligibility.RequiresSplitResolution", agentWorker);
        Assert.Contains("SplitResultId = record.SplitResultId", agentWorker);

        Assert.Contains("SplitDecisionEligibility.IsDecisionCompatible(analysisRecord, d)", auditController);
        Assert.Contains("ImageAnalysisDecisionId = @p0", auditController);
        Assert.Contains("LoadAuditAnalysisContextAsync", auditController);
        Assert.DoesNotContain("via fallback (ContainerNumber+ScannerType only)", auditController);

        Assert.Contains("groupIdentifier", decisionClient);
        Assert.Contains("scannerType", decisionClient);
        Assert.Contains("BuildContainerDecisionsPath(containerNumber, groupIdentifier, scannerType)", decisionClient);
    }

    private static string ReadRepoFile(string relativePath, [CallerFilePath] string callerPath = "")
    {
        return File.ReadAllText(Path.Combine(ResolveRepoRoot(callerPath), relativePath));
    }

    private static string ResolveRepoRoot(string callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException($"Could not resolve repository root from caller path {callerPath}.");
    }
}
