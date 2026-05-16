using System.Runtime.CompilerServices;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Architecture;

public class CmrCompositeRecordIntakeGuardrailTests
{
    [Fact]
    public void RecordReconciliationWorker_HasFeatureGatedCmrCompositePass()
    {
        var worker = ReadRepoFile("src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs");

        Assert.Contains("_cmrCompositeProgressionEnabled", worker);
        Assert.Contains("ReconcileCmrCompositeRecordsAsync", worker);
        Assert.Contains("CmrCompositeKeyHelper.TryCreate", worker);
        Assert.Contains("BuildOrUpdateCmrRecordAsync", worker);
        Assert.Contains("UpsertCmrCompositeRecordAsync", worker);
    }

    [Fact]
    public void RecordBackedIntake_UsesCmrGroupTypeAndDuplicateProtection()
    {
        var orchestrator = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");

        Assert.Contains("GetRecordBackedGroupType(record.ClearanceType)", orchestrator);
        Assert.Contains("FindExistingCmrCompositeGroupForRealRecordAsync", orchestrator);
        Assert.Contains("CmrCompositeProgression:Enabled", orchestrator);
        Assert.Contains("existing CMR group", orchestrator);
    }

    [Fact]
    public void RecordBackedIntake_BackstampsBlankCompletenessGroupIdentifiers()
    {
        var orchestrator = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");

        Assert.Contains("groupidentifier IS NULL OR btrim(groupidentifier) = ''", orchestrator);
        Assert.Contains("existing record group", orchestrator);
    }

    [Fact]
    public void CargoGroupSummaryPath_SupportsCmrCompositeOperationalKeys()
    {
        var cargoGroupService = ReadRepoFile("src/NickScanCentralImagingPortal.Services/CargoGrouping/CargoGroupService.cs");

        Assert.Contains("CmrCompositeKeyHelper.IsOperationalKey(groupIdentifier)", cargoGroupService);
        Assert.Contains("BuildCmrCompositeGroupAsync", cargoGroupService);
        Assert.Contains("FindCmrDocumentsForOperationalKeyAsync", cargoGroupService);
        Assert.Contains("GetCmrCompositeDataAsync", cargoGroupService);
        Assert.Contains("RecordExpectedContainers", cargoGroupService);
        Assert.Contains("CmrCompositeKeyHelper.TryCreate", cargoGroupService);
    }

    [Fact]
    public void ImageAnalysisSummarySurfaces_UseScenarioAwareIcumsOnlyLookup()
    {
        var dialog = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewDialog.razor");
        var viewer = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor");

        Assert.Contains("scenario-aware cargo resolver", dialog);
        Assert.Contains("loadScannerData: false", dialog);
        Assert.Contains("loadImageData: false", dialog);
        Assert.Contains("loadICUMSData: true", dialog);

        Assert.Contains("GetGoodsPanelLookupCandidates", viewer);
        Assert.Contains("TryLoadCargoGroupForGoodsPanel", viewer);
        Assert.Contains("CargoGroupClient.TryGetCargoGroupAsync<CargoGroupResponse>", viewer);
        Assert.Contains("loadScannerData: false", viewer);
        Assert.Contains("loadImageData: false", viewer);
        Assert.Contains("loadICUMSData: true", viewer);
    }

    [Fact]
    public void SplitChoiceFullscreenHandoff_UsesResolvedChildContainerAndResolution()
    {
        var decisionView = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageDecisionView.razor");

        Assert.Contains("GetDecisionContainerNumber(effectiveResolution)", decisionView);
        Assert.Contains("[\"ContainerNumber\"] = currentContainerNumber", decisionView);
        Assert.Contains("[\"SourceScanResolution\"] = effectiveResolution", decisionView);
        Assert.Contains("var fullImageUrl = GetFullImageSrc(image)", decisionView);
    }

    [Fact]
    public void SplitChoiceFullscreenImageRequests_PreferResolvedScanContainerHint()
    {
        var viewer = ReadRepoFile("src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor");

        Assert.Contains("GetImageRequestContainerHint()", viewer);
        Assert.Contains("StrictSingleContainerToken(SourceScanResolution?.ContainerNumber)", viewer);
        Assert.Contains("ScanAssetClient.BuildImagePath", viewer);
        Assert.Contains("ContainerNumber = containerHint", viewer);
        Assert.Contains("StrictSingleContainerToken(ContainerNumber)", viewer);
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
