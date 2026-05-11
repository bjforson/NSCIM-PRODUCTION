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
        Assert.Contains("existingStatus.Status = completenessDecision.Status", step2Update);
        Assert.Contains("completenessDecision.IsComplete", step2Update);
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
