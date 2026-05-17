using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Architecture;

public class StateOwnershipGuardrailTests
{
    [Fact]
    public void ContainerAndIcumDownloadQueues_DoNotIncrementRetryCountWhenProcessingStarts()
    {
        var containerQueue = ReadRepoFile("src/NickScanCentralImagingPortal.Infrastructure/Repositories/ContainerScanQueueRepository.cs");
        var icumsQueue = ReadRepoFile("src/NickScanCentralImagingPortal.Infrastructure/Repositories/ICUMSDownloadQueueRepository.cs");

        Assert.DoesNotContain("RetryCount++", SliceMethod(containerQueue, "public async Task MarkAsProcessingAsync(int id)"));
        Assert.DoesNotContain("RetryCount++", SliceMethod(icumsQueue, "public async Task MarkAsProcessingAsync(int id)"));
    }

    [Fact]
    public void ContainerCompletenessQueueCompletion_HappensAfterCompletenessSave()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs");
        var mainQueueProcessing = Slice(
            service,
            "var existingStatus = await dbContext.ContainerCompletenessStatuses",
            "// Save all changes (completeness records + BOE requests)");
        var saveAndComplete = Slice(
            service,
            "await dbContext.SaveChangesAsync(stoppingToken);",
            "// Event-driven record promotion");

        Assert.DoesNotContain("MarkAsCompletedAsync(queueItem.Id)", mainQueueProcessing);
        Assert.Contains("queueItemsToComplete", mainQueueProcessing);
        Assert.Contains("MarkAsCompletedAsync(queueItemId)", saveAndComplete);
    }

    [Fact]
    public void ContainerCompletenessStep2_ReusesCmrPendingGateBeforeImageAnalysis()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs");
        var step2Update = Slice(
            service,
            "bool dataChanged = existingStatus.HasICUMSData",
            "existingStatus.UpdatedAt = DateTime.UtcNow;");

        Assert.Contains("ContainerCompletenessPolicy.Evaluate", step2Update);
        Assert.Contains("cmrCompositeProgressionEnabled: _cmrCompositeProgressionEnabled", step2Update);
        Assert.Contains("cmrRotationNumber: primaryBOE?.RotationNumber", step2Update);
        Assert.Contains("cmrContainerNumber: container.ContainerNumber", step2Update);
        Assert.Contains("cmrBlNumber: primaryBOE?.BlNumber", step2Update);
        Assert.Contains("existingStatus.Status = completenessDecision.Status", step2Update);
        Assert.Contains("completenessDecision.IsComplete", step2Update);
    }

    [Fact]
    public void ContainerCompletenessService_PreservesCmrCompositeGroupIdentifiers()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs");

        Assert.Contains("ShouldPreserveCmrCompositeGroupIdentifier", service);
        Assert.Contains("CmrCompositeKeyHelper.IsOperationalKey(existingGroupIdentifier)", service);
        Assert.Contains("CmrCompositeKeyHelper.IsOperationalKey(record.GroupIdentifier)", service);
    }

    [Fact]
    public void RecordCompletenessPath_IncludesCmrCompositeRecordsBehindFeatureFlag()
    {
        var worker = ReadRepoFile("src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs");
        var builder = ReadRepoFile("src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs");
        var combined = worker + "\n" + builder;

        Assert.Contains("CmrCompositeProgression", combined);
        Assert.Contains("\"CMR\"", combined);
        Assert.Contains("CmrCompositeKeyHelper", combined);
        Assert.Contains("BuildOrUpdateCmrRecordAsync", combined);
        Assert.Contains("RecordExpectedContainer", combined);
        Assert.DoesNotContain("CMR is handled by the 1.13.0 implicit", combined);
    }

    [Fact]
    public void ContainerStatusReconciliation_IncludesCmrCompositePolicyInputs()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerStatusReconciliationService.cs");
        var reconciliationUpdate = Slice(
            service,
            "var boeRecords = await icumDownloadsRepository.GetBOEDocumentsByContainerNumberAsync(container.ContainerNumber);",
            "container.OverallCompleteness =");

        Assert.Contains("CmrCompositeKeyHelper", service);
        Assert.Contains("primaryBOE.ClearanceType", reconciliationUpdate);
        Assert.Contains("cmrCompositeProgressionEnabled", reconciliationUpdate);
        Assert.Contains("cmrRotationNumber: primaryBOE.RotationNumber", reconciliationUpdate);
        Assert.Contains("cmrContainerNumber: container.ContainerNumber", reconciliationUpdate);
        Assert.Contains("cmrBlNumber: primaryBOE.BlNumber", reconciliationUpdate);
        Assert.Contains("cmrCompositeKey.OperationalKey", reconciliationUpdate);
    }

    [Fact]
    public void RecordAnchoredImageIntake_UsesCmrGroupTypeAndRealContainers()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");
        var recordAnchoredIntake = Slice(
            service,
            "private async Task RunRecordAnchoredIntakeAsync(",
            "private async Task TryLinkGroupToRecordAsync(");

        Assert.Contains("record.ClearanceType", recordAnchoredIntake);
        Assert.Contains("\"CMR\"", recordAnchoredIntake);
        Assert.Contains("GetRecordBackedGroupType(record.ClearanceType)", recordAnchoredIntake);
        Assert.Contains("RecordCompletenessStatusId = record.Id", recordAnchoredIntake);
        Assert.Contains("ContainerNumber = child.ContainerNumber", recordAnchoredIntake);
    }

    [Fact]
    public void TwoContainerSplitIntake_DoesNotPromoteScanPairToCargoGroupIdentifier()
    {
        var splitIntake = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeService.cs");
        var refreshSplitCompleteness = Slice(
            splitIntake,
            "private async Task RefreshSplitCompletenessRowsAsync(",
            "private static IReadOnlyList<string> ParseTwoContainerNumbers");
        var orchestrator = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");
        var readyGroupsCache = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs");
        var imageAnalysisController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs");
        var statusValidator = ReadRepoFile("src/NickScanCentralImagingPortal.Core/Helpers/AnalysisStatusValidator.cs");

        Assert.DoesNotContain("status.GroupIdentifier = groupIdentifier", refreshSplitCompleteness);
        Assert.Contains("status.GroupIdentifier = null", refreshSplitCompleteness);
        Assert.Contains("Clearing composite scan-pair GroupIdentifier", refreshSplitCompleteness);
        Assert.Contains("IsCompositeContainerPairIdentifier(g.GroupIdentifier)", orchestrator);
        Assert.Contains("QuarantineCompositeContainerPairGroupsAsync", orchestrator);
        Assert.Contains("[COMPOSITE-SCAN-GUARD]", orchestrator);
        Assert.Contains("ExpireStaleActiveAssignmentsAsync", orchestrator);
        Assert.Contains("[ASSIGNMENT-JANITOR]", orchestrator);
        Assert.Contains("a.LeaseUntilUtc <= now", orchestrator);
        Assert.Contains("AnalysisStatuses.Cancelled", Slice(
            orchestrator,
            "private async Task QuarantineCompositeContainerPairGroupsAsync(",
            "private async Task CloseStaleDecidedGroupsAsync"));
        Assert.Contains("composite scan-pair quarantine", statusValidator);
        Assert.Contains("IsCompositeContainerPairIdentifier(g.GroupIdentifier)", readyGroupsCache);
        Assert.Contains("IsCompositeContainerPairIdentifier(row.Entry.GroupIdentifier)", imageAnalysisController);
    }

    [Fact]
    public void CmrCompositePath_HasDuplicateProtectionGuardrails()
    {
        var worker = ReadRepoFile("src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs");
        var builder = ReadRepoFile("src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs");
        var intake = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");
        var combined = worker + "\n" + builder + "\n" + intake;

        Assert.Contains("RecordCompletenessStatusId == r.Id", intake);
        Assert.Contains("RecordCompletenessStatusId = record.Id", intake);
        Assert.Contains("CmrCompositeKeyHelper.TryCreate", combined);
        Assert.Contains("cmrKey.OperationalKey", combined);
        Assert.Contains("FindExistingCmrCompositeGroupForRealRecordAsync", intake);
        Assert.DoesNotContain("GroupType = \"BL\"", Slice(
            intake,
            "private async Task RunRecordAnchoredIntakeAsync(",
            "private async Task TryLinkGroupToRecordAsync("));
    }

    [Fact]
    public void ContainerCompletenessIdentity_IncludesInspectionIdInModelAndMigration()
    {
        var dbContext = ReadRepoFile("src/NickScanCentralImagingPortal.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("tools/migrations/sprint-5G3/01-ccs-identity-includes-inspectionid.sql");

        Assert.Contains("new { e.ContainerNumber, e.ScannerType, e.InspectionId }", dbContext);
        Assert.Contains("COALESCE(inspectionid, '')", migration);
        Assert.Contains("ix_ccs_container_scanner_inspection_unique", migration);
    }

    [Fact]
    public void IcumSubmissionQueue_UsesWorkerConsumedStatusVocabulary()
    {
        var files = new[]
        {
            "src/NickScanCentralImagingPortal.Core/Entities/ICUMSSubmissionQueue.cs",
            "src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ICUMSSubmissionService.cs",
            "src/NickScanCentralImagingPortal.API/Controllers/ICUMSSubmissionQueueController.cs",
            "src/NickScanWebApp.New/Pages/Customs/IcumsSubmissionQueue.razor",
        };

        var forbiddenStatusLiterals = new Regex(
            @"(Status\s*(==|=)\s*""(Queued|Submitting|Successful)""|status=Successful)",
            RegexOptions.Compiled);

        var violations = files
            .Select(path => new { path, content = ReadRepoFile(path) })
            .SelectMany(file => forbiddenStatusLiterals
                .Matches(file.content)
                .Select(match => $"{file.path}: {match.Value}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "ICUMS submission queue statuses must stay aligned with the worker vocabulary: Pending, Processing, Submitted, Failed, Cancelled.\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AseIngestionAndRecovery_ShareSingleContainerQueueSplitHelper()
    {
        var aseSync = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ASE/AseDatabaseSyncService.cs");
        var recovery = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/QueueRecoveryService.cs");
        var factory = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ASE/AseScanQueueItemFactory.cs");
        var recoveryAseBatch = Slice(
            recovery,
            "private async Task ProcessAseBatchAsync(",
            "// Publish recovered scans to queue");

        Assert.Contains("AseScanQueueItemFactory.CreateFromScan(scan)", aseSync);
        Assert.Contains("AseScanQueueItemFactory.Create(", recoveryAseBatch);
        Assert.Contains("OriginalContainerNumber", factory);
        Assert.Contains("MultiContainerScan", factory);
        Assert.Contains("SplitTokenIndex", factory);
        Assert.Contains("SplitTokenCount", factory);
        Assert.DoesNotContain("ContainerNumber = containerNumber", recoveryAseBatch);
        Assert.DoesNotContain("InspectionId = inspectionId", recoveryAseBatch);
    }

    [Fact]
    public void ContainerScanQueuePublisher_RejectsCompositeContainerIdentifiers()
    {
        var publisher = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerScanQueuePublisherService.cs");

        Assert.Contains("ContainerNumberListMatcher.IsCompositeContainerIdentifier(containerNumber)", publisher);
        Assert.Contains("ContainerNumberListMatcher.IsCompositeContainerIdentifier(s.ContainerNumber)", publisher);
        Assert.Contains("ContainerNumber must be one physical container", publisher);
        Assert.Contains("composite source label", publisher);
    }

    [Fact]
    public void ContainerCompleteness_AseImageEvidenceIsTokenAware()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs");
        var aseImageEvidence = Slice(
            service,
            "private static async Task<int> CountAseImagesForContainerAsync(",
            "public async Task<bool> ValidateAndFixContainerDataIntegrityAsync");

        Assert.Contains("ContainerNumberListMatcher.Normalize(containerNumber)", aseImageEvidence);
        Assert.Contains("ContainerNumberListMatcher.ContainsContainer", aseImageEvidence);
        Assert.Contains("s.ContainerNumber.ToUpper().Contains(normalizedContainer)", aseImageEvidence);
    }

    [Fact]
    public void ImageAnalysis_AsePayloadAndIntakeResolveSplitSourceRows()
    {
        var service = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");

        Assert.Contains("TryParseBaseAseInspectionId(inspectionId, out var aseInspId)", service);
        Assert.Contains("ResolveLatestAseScanForContainerAsync", service);
        Assert.Contains("ContainerNumberListMatcher.ContainsContainer", service);
        Assert.Contains("s.ContainerNumber.ToUpper().Contains(normalizedContainer)", service);
        Assert.DoesNotContain("int.TryParse(inspectionId, out var aseInspId)", service);
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

    private static string SliceMethod(string value, string startMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");

        var nextMethod = value.IndexOf("\n        public ", start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(nextMethod > start, $"Next method marker not found after {startMarker}");

        return value[start..nextMethod];
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");

        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after {startMarker}: {endMarker}");

        return value[start..end];
    }
}
