using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.ApiCompatibility;

public sealed class BackendCompatibilityRoutesTests
{
    [Fact]
    public void RecordCompletenessSummary_DeclaresKebabCaseCompatibilityAlias()
    {
        var method = typeof(RecordCompletenessController).GetMethod(nameof(RecordCompletenessController.GetSummary));

        Assert.NotNull(method);
        Assert.Contains(
            method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>(),
            attribute => attribute.Template == "~/api/record-completeness/summary");
    }

    [Fact]
    public void ManualBoeRequestCompatibilityController_DeclaresLegacyRoute()
    {
        Assert.Contains(
            typeof(ManualBOERequestCompatibilityController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>(),
            attribute => attribute.Template == "api/ManualBOERequest");

        var method = typeof(ManualBOERequestCompatibilityController)
            .GetMethod(nameof(ManualBOERequestCompatibilityController.Create));

        Assert.NotNull(method);
        Assert.Contains(
            method!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Cast<HttpPostAttribute>(),
            attribute => attribute.Template == null);
    }

    [Fact]
    public async Task ManualBoeRequestCompatibilityController_Create_DelegatesToManualBoeService()
    {
        var service = new StubManualBoeService();
        var controller = new ManualBOERequestCompatibilityController(
            service,
            NullLogger<ManualBOERequestCompatibilityController>.Instance);

        var action = await controller.Create(new ManualBOERequestCompatibilityCreateRequest
        {
            ContainerNumber = " MSCU1234567 ",
            RequestedBy = "operator"
        });

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ManualBOERequestCompatibilityResponse>(ok.Value);
        Assert.Equal("MSCU1234567", service.CreatedContainerNumber);
        Assert.Equal("operator", service.CreatedRequestedBy);
        Assert.Equal("MSCU1234567", response.ContainerNumber);
        Assert.Equal("Pending", response.Status);
    }

    [Fact]
    public void ScanAssetsController_DeclaresScannerDataCompatibilityRoute()
    {
        var method = typeof(ScanAssetsController).GetMethod(nameof(ScanAssetsController.GetSourceScannerData));

        Assert.NotNull(method);
        Assert.Contains(
            method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>(),
            attribute => attribute.Template == "{sourceScanId}/scanner-data");
    }

    [Fact]
    public async Task ScanAssetsScannerData_BySourceScanId_ReturnsResolvedFs6000ScannerRecords()
    {
        await using var db = NewInMemoryDb();
        var scanId = Guid.NewGuid();
        var scanTime = new DateTime(2026, 5, 16, 8, 30, 0, DateTimeKind.Utc);

        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            ContainerNumber = "MSCU1234567",
            OriginalScanRecordId = 42,
            ScanTime = scanTime,
            PicNumber = "PIC-42",
            VesselName = "NICK TEST",
            OperatorId = "OP-1",
            ScanResult = "Clear",
            HasImage = true,
            ImageCount = 1,
            SyncStatus = "Synced"
        });
        await db.SaveChangesAsync();

        var controller = new ScanAssetsController(
            new StubScanAssetResolver(),
            imageProcessingService: null!,
            httpClientFactory: null!,
            db,
            NullLogger<ScanAssetsController>.Instance);

        var action = await controller.GetSourceScannerData(
            "42",
            page: 1,
            pageSize: 50,
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var response = Assert.IsType<PagedResult<ScannerDataRecord>>(ok.Value);

        Assert.Equal("Found", response.Status);
        Assert.Contains(response.Data, record => record.Field == "Scanner Type" && record.Value == "FS6000");
        Assert.Contains(response.Data, record => record.Field == "Picture Number" && record.Value == "PIC-42");
        Assert.Contains(response.Data, record => record.Field == "Source Scan Id" && record.Value == "42");
    }

    private static ApplicationDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"BackendCompatibility_{Guid.NewGuid():N}")
            .EnableServiceProviderCaching(false)
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class StubScanAssetResolver : IScanAssetResolver
    {
        public Task<ScanAssetResolution> ResolveAsync(
            ScanAssetResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScanAssetResolution
            {
                Found = true,
                RequestedContainerNumber = request.ContainerNumber,
                ContainerNumber = request.ContainerNumber,
                SourceScannerType = "FS6000",
                SourceScanId = "42",
                OriginalScanRecordId = 42,
                SourceContainerNumbers = request.ContainerNumber,
                ResolvedBy = "TestResolver"
            });
        }
    }

    private sealed class StubManualBoeService : IManualBOESelectivityService
    {
        public string? CreatedContainerNumber { get; private set; }
        public string? CreatedRequestedBy { get; private set; }

        public Task<int> ProcessPendingBOERequestsAsync() => Task.FromResult(0);

        public Task<ManualBOERequest> CreateManualBOERequestAsync(string containerNumber, string requestedBy = "System")
        {
            CreatedContainerNumber = containerNumber;
            CreatedRequestedBy = requestedBy;
            return Task.FromResult(new ManualBOERequest
            {
                Id = 123,
                ContainerNumber = containerNumber,
                RequestedBy = requestedBy,
                Status = "Pending",
                RequestDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public Task<ManualBOERequest> ProcessBOERequestAsync(ManualBOERequest request) => Task.FromResult(request);

        public Task<List<ManualBOERequest>> GetPendingBOERequestsAsync(int limit = 50) => Task.FromResult(new List<ManualBOERequest>());

        public Task<ManualBOERequest> UpdateBOERequestStatusAsync(
            int requestId,
            string status,
            string? errorMessage = null,
            string? icuMSResponseId = null)
        {
            return Task.FromResult(new ManualBOERequest { Id = requestId, Status = status });
        }

        public Task<List<ManualBOERequest>> GetFailedBOERequestsForRetryAsync(int maxRetryCount = 3)
            => Task.FromResult(new List<ManualBOERequest>());

        public Task<BOERequestStatistics> GetBOERequestStatisticsAsync()
            => Task.FromResult(new BOERequestStatistics());

        public Task<int> AutoQueueMissingICUMSContainersAsync() => Task.FromResult(0);
    }
}
