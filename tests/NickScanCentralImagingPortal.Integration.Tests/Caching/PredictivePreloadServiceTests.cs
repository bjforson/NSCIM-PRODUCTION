using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Caching;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public sealed class PredictivePreloadServiceTests
{
    [Fact]
    public async Task RunOnceAsync_WhenDisabled_SkipsAndUpdatesState()
    {
        await using var db = NewInMemoryDb();
        await using var icumDb = NewInMemoryIcumDb();
        var cache = new InMemoryCacheService();
        var state = new PredictivePreloadState();
        var service = NewService(db, icumDb, cache, state, new PredictivePreloadOptions { Enabled = false });

        var result = await service.RunOnceAsync();

        Assert.False(result.Enabled);
        Assert.Equal("Predictive preload disabled", result.SkippedReason);
        var snapshot = state.Snapshot(new PredictivePreloadOptions { Enabled = false });
        Assert.False(snapshot.IsRunning);
        Assert.Equal(1, snapshot.TotalRuns);
    }

    [Fact]
    public async Task PreloadAssignmentAsync_CachesCompactAssignmentContextAndContainerList()
    {
        await using var db = NewInMemoryDb();
        await using var icumDb = NewInMemoryIcumDb();
        var groupId = Guid.NewGuid();
        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = "BL-1001",
            NormalizedGroupIdentifier = "BL-1001",
            GroupType = "BL",
            ScannerType = "FS6000",
            Priority = 42
        });
        db.AnalysisRecords.AddRange(
            new AnalysisRecord { GroupId = groupId, ContainerNumber = "MSCU1234567", ScannerType = "FS6000" },
            new AnalysisRecord { GroupId = groupId, ContainerNumber = "TGHU7654321", ScannerType = "FS6000" },
            new AnalysisRecord { GroupId = groupId, ContainerNumber = "TGHU7654321", ScannerType = "FS6000" });
        await db.SaveChangesAsync();

        var cache = new InMemoryCacheService();
        var service = NewService(
            db,
            icumDb,
            cache,
            new PredictivePreloadState(),
            new PredictivePreloadOptions { MaxContainersPerGroup = 10, CacheTtlSeconds = 300 });

        var result = await service.PreloadAssignmentAsync(groupId, "Analyst", "Ready");

        Assert.True(result.Success);
        Assert.Equal(2, result.ContainerCount);

        var context = await cache.GetAsync<PredictiveAssignmentContext>(PredictivePreloadKeys.Assignment(groupId));
        Assert.NotNull(context);
        Assert.Equal(groupId, context.GroupId);
        Assert.Equal("Analyst", context.Role);
        Assert.Equal("BL-1001", context.GroupIdentifier);
        Assert.Equal(42, context.Priority);
        Assert.Equal(2, context.ContainerNumbers.Count);

        var containers = await cache.GetAsync<List<string>>(PredictivePreloadKeys.AssignmentContainers(groupId));
        Assert.NotNull(containers);
        Assert.Equal(["MSCU1234567", "TGHU7654321"], containers);
    }

    [Fact]
    public async Task PreloadContainerContextAsync_CachesSummaryFirstPagesAndImageMetadata()
    {
        await using var db = NewInMemoryDb();
        await using var icumDb = NewInMemoryIcumDb();
        var containerNumber = "MSCU1234567";
        var scanId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
        {
            ContainerNumber = containerNumber,
            ScannerType = "FS6000",
            Status = "Complete",
            WorkflowStage = "ImageAnalysis",
            ClearanceType = "IM",
            GroupIdentifier = "BL-1001",
            BOEDocumentId = 100,
            HasScannerData = true,
            HasICUMSData = true,
            HasImageData = true,
            OverallCompleteness = 100,
            ScanDate = now.AddMinutes(-20),
            UpdatedAt = now.AddMinutes(-5)
        });
        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            ContainerNumber = containerNumber,
            ScanTime = now.AddMinutes(-20),
            PicNumber = "PIC-1001",
            TruckPlate = "GT-1234-22",
            OperatorId = "OP-7",
            ScanResult = "Clear",
            HasImage = true,
            ImageCount = 1
        });
        db.FS6000Images.Add(new FS6000Image
        {
            ScanId = scanId,
            ImageType = "Main",
            FileName = "MSCU1234567-main.jpg",
            FileSizeBytes = 12345,
            CreatedAt = now.AddMinutes(-19)
        });
        await db.SaveChangesAsync();

        icumDb.BOEDocuments.Add(new BOEDocument
        {
            Id = 100,
            DownloadedFileId = 1,
            ContainerNumber = containerNumber,
            DeclarationNumber = "DEC-1001",
            BlNumber = "BL-1001",
            ClearanceType = "IM",
            GoodsDescription = "Textiles",
            ConsigneeName = "Acme Imports",
            CreatedAt = now.AddMinutes(-18),
            UpdatedAt = now.AddMinutes(-4)
        });
        icumDb.ManifestItems.Add(new DownloadedManifestItem
        {
            BOEDocumentId = 100,
            ItemIndex = 1,
            HsCode = "6109",
            Description = "Cotton shirts",
            Quantity = 20,
            Unit = "CTN",
            CreatedAt = now.AddMinutes(-18),
            UpdatedAt = now.AddMinutes(-4)
        });
        await icumDb.SaveChangesAsync();

        var cache = new InMemoryCacheService();
        var service = NewService(
            db,
            icumDb,
            cache,
            new PredictivePreloadState(),
            new PredictivePreloadOptions { CacheTtlSeconds = 300, FirstPageSize = 10 });

        var result = await service.PreloadContainerContextAsync("mscu1234567");

        Assert.True(result.Success);
        Assert.True(result.ScannerFieldCount > 0);
        Assert.True(result.IcumFieldCount > 0);
        Assert.Equal(1, result.ImageMetadataCount);

        var context = await cache.GetAsync<PredictiveContainerContext>(PredictivePreloadKeys.ContainerContext(containerNumber));
        Assert.NotNull(context);
        Assert.Equal(containerNumber, context.ContainerNumber);
        Assert.NotNull(context.Summary);
        Assert.Equal(100, context.Summary.CompletenessScore);
        Assert.NotNull(context.ScannerFirstPage);
        Assert.Contains(context.ScannerFirstPage.Data, f => f.Field == "Picture Number" && f.Value == "PIC-1001");
        Assert.NotNull(context.IcumsFirstPage);
        Assert.Contains(context.IcumsFirstPage.Data, f => f.Field == "Declaration Number" && f.Value == "DEC-1001");
        Assert.NotNull(context.BoeSummary);
        Assert.Equal("Textiles", context.BoeSummary.GoodsDescription);
        Assert.Single(context.ImageMetadata);
    }

    [Fact]
    public async Task InvalidateAssignmentAsync_RemovesAssignmentContextAndContainerList()
    {
        await using var db = NewInMemoryDb();
        await using var icumDb = NewInMemoryIcumDb();
        var groupId = Guid.NewGuid();
        var cache = new InMemoryCacheService();
        await cache.SetAsync(PredictivePreloadKeys.Assignment(groupId), new PredictiveAssignmentContext { GroupId = groupId });
        await cache.SetAsync(PredictivePreloadKeys.AssignmentContainers(groupId), new List<string> { "MSCU1234567" });
        await cache.SetAsync(PredictivePreloadKeys.ContainerContext("MSCU1234567"), new PredictiveContainerContext { ContainerNumber = "MSCU1234567" });

        var service = NewService(db, icumDb, cache, new PredictivePreloadState(), new PredictivePreloadOptions());

        await service.InvalidateAssignmentAsync(groupId);

        Assert.False(await cache.ExistsAsync(PredictivePreloadKeys.Assignment(groupId)));
        Assert.False(await cache.ExistsAsync(PredictivePreloadKeys.AssignmentContainers(groupId)));
        Assert.False(await cache.ExistsAsync(PredictivePreloadKeys.ContainerContext("MSCU1234567")));
    }

    [Fact]
    public void ControllerStatus_ReturnsStateSnapshot()
    {
        var state = new PredictivePreloadState();
        state.MarkCompleted(new PredictivePreloadRunResult
        {
            Enabled = true,
            StartedAtUtc = DateTime.UtcNow.AddSeconds(-1),
            FinishedAtUtc = DateTime.UtcNow,
            SuccessCount = 2,
            FailureCount = 1
        });
        var controller = NewController(new StubPredictivePreloadService(), state);

        var action = controller.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var snapshot = Assert.IsType<PredictivePreloadStatusSnapshot>(ok.Value);
        Assert.Equal(1, snapshot.TotalRuns);
        Assert.Equal(2, snapshot.TotalSuccesses);
        Assert.Equal(1, snapshot.TotalFailures);
    }

    [Fact]
    public async Task ControllerInvalidateAssignment_CallsService()
    {
        var groupId = Guid.NewGuid();
        var service = new StubPredictivePreloadService();
        var controller = NewController(service, new PredictivePreloadState());

        var action = await controller.InvalidateAssignment(groupId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(groupId, service.InvalidatedGroupId);
    }

    [Fact]
    public async Task ControllerGetAssignmentContext_WhenCached_ReturnsContext()
    {
        var groupId = Guid.NewGuid();
        var service = new StubPredictivePreloadService
        {
            AssignmentContext = new PredictiveAssignmentContext
            {
                GroupId = groupId,
                Role = "Analyst",
                GroupIdentifier = "BL-1001"
            }
        };
        var controller = NewController(service, new PredictivePreloadState());

        var action = await controller.GetAssignmentContext(groupId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var context = Assert.IsType<PredictiveAssignmentContext>(ok.Value);
        Assert.Equal(groupId, context.GroupId);
        Assert.Equal("Analyst", context.Role);
    }

    [Fact]
    public async Task ControllerGetAssignmentContext_WhenMissing_ReturnsNotFound()
    {
        var controller = NewController(new StubPredictivePreloadService(), new PredictivePreloadState());

        var action = await controller.GetAssignmentContext(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action.Result);
    }

    [Fact]
    public async Task ControllerGetContainerContext_WhenCached_ReturnsContext()
    {
        var service = new StubPredictivePreloadService
        {
            ContainerContext = new PredictiveContainerContext
            {
                ContainerNumber = "MSCU1234567",
                Summary = new PredictiveContainerSummary { ContainerNumber = "MSCU1234567" }
            }
        };
        var controller = NewController(service, new PredictivePreloadState());

        var action = await controller.GetContainerContext("MSCU1234567", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var context = Assert.IsType<PredictiveContainerContext>(ok.Value);
        Assert.Equal("MSCU1234567", context.ContainerNumber);
    }

    [Fact]
    public async Task ControllerPreloadContainer_CallsService()
    {
        var service = new StubPredictivePreloadService();
        var controller = NewController(service, new PredictivePreloadState());

        var action = await controller.PreloadContainer("MSCU1234567", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var result = Assert.IsType<PredictivePreloadContainerResult>(ok.Value);
        Assert.True(result.Success);
        Assert.Equal("MSCU1234567", service.PreloadedContainerNumber);
    }

    [Fact]
    public async Task ControllerRunOnce_WhenAlreadyRunning_ReturnsConflict()
    {
        var state = new PredictivePreloadState();
        state.MarkStarted();
        var controller = NewController(new StubPredictivePreloadService(), state);

        var action = await controller.RunOnce(CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(action.Result);
    }

    private static ApplicationDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PredictivePreload_{Guid.NewGuid():N}")
            .EnableServiceProviderCaching(false)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IcumDownloadsDbContext NewInMemoryIcumDb()
    {
        var options = new DbContextOptionsBuilder<IcumDownloadsDbContext>()
            .UseInMemoryDatabase($"PredictivePreloadIcum_{Guid.NewGuid():N}")
            .EnableServiceProviderCaching(false)
            .Options;
        return new IcumDownloadsDbContext(options);
    }

    private static PredictivePreloadService NewService(
        ApplicationDbContext db,
        IcumDownloadsDbContext icumDb,
        ICacheService cache,
        PredictivePreloadState state,
        PredictivePreloadOptions options)
    {
        return new PredictivePreloadService(
            readyGroupsCache: null!,
            dbContext: db,
            icumDownloadsDbContext: icumDb,
            cache: cache,
            scopeFactory: null!,
            options: Options.Create(options),
            state: state,
            logger: NullLogger<PredictivePreloadService>.Instance);
    }

    private static PredictivePreloadController NewController(
        IPredictivePreloadService service,
        PredictivePreloadState state)
    {
        return new PredictivePreloadController(
            service,
            state,
            Options.Create(new PredictivePreloadOptions()),
            NullLogger<PredictivePreloadController>.Instance);
    }

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            return Task.FromResult(_entries.TryGetValue(key, out var value) ? value as T : null);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _entries.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _entries.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_entries.ContainsKey(key));
        }

        public async Task<T> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class
        {
            var cached = await GetAsync<T>(key, cancellationToken);
            if (cached != null)
            {
                return cached;
            }

            var value = await factory();
            await SetAsync(key, value, expiration, cancellationToken);
            return value;
        }
    }

    private sealed class StubPredictivePreloadService : IPredictivePreloadService
    {
        public Guid? InvalidatedGroupId { get; private set; }
        public string? InvalidatedContainerNumber { get; private set; }
        public string? PreloadedContainerNumber { get; private set; }
        public PredictiveAssignmentContext? AssignmentContext { get; set; }
        public PredictiveContainerContext? ContainerContext { get; set; }

        public Task<PredictivePreloadRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PredictivePreloadRunResult
            {
                Enabled = true,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow
            });
        }

        public Task<PredictivePreloadAssignmentResult> PreloadAssignmentAsync(
            Guid groupId,
            string role,
            string eligibleStatus,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PredictivePreloadAssignmentResult
            {
                GroupId = groupId,
                Role = role,
                Success = true,
                CompletedAtUtc = DateTime.UtcNow
            });
        }

        public Task<PredictivePreloadContainerResult> PreloadContainerContextAsync(
            string containerNumber,
            CancellationToken cancellationToken = default)
        {
            PreloadedContainerNumber = containerNumber;
            return Task.FromResult(new PredictivePreloadContainerResult
            {
                ContainerNumber = containerNumber,
                Success = true,
                CompletedAtUtc = DateTime.UtcNow
            });
        }

        public Task InvalidateAssignmentAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            InvalidatedGroupId = groupId;
            return Task.CompletedTask;
        }

        public Task InvalidateContainerContextAsync(string containerNumber, CancellationToken cancellationToken = default)
        {
            InvalidatedContainerNumber = containerNumber;
            return Task.CompletedTask;
        }

        public Task InvalidateRoleAssignmentsAsync(string role, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<PredictiveAssignmentContext?> GetAssignmentContextAsync(
            Guid groupId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AssignmentContext?.GroupId == groupId ? AssignmentContext : null);
        }

        public Task<PredictiveContainerContext?> GetContainerContextAsync(
            string containerNumber,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                string.Equals(ContainerContext?.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase)
                    ? ContainerContext
                    : null);
        }
    }
}
