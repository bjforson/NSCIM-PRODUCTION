using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Controllers
{
    public class PublicStatsControllerTests
    {
        private const string FreshCacheKey = "public.system-stats.v1";

        [Fact]
        public async Task Get_WhenRefreshInProgress_ReturnsStaleSnapshotWithoutStartingAnotherRefresh()
        {
            await using var db = NewDbContext();
            using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
            var orchestrator = new BlockingOrchestrator();

            var controller = NewController(db, cache, orchestrator);
            var initial = ReadOkValue(await controller.Get());

            Assert.NotNull(initial);
            Assert.Equal(1, orchestrator.HealthCallCount);

            cache.Remove(FreshCacheKey);
            orchestrator.BlockOnHealthCallNumber = 2;

            var refreshTask = controller.Get();
            await orchestrator.BlockedCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var staleTasks = Enumerable.Range(0, 4)
                .Select(_ => NewController(db, cache, orchestrator).Get())
                .ToArray();
            var staleResponses = await Task.WhenAll(staleTasks);

            foreach (var response in staleResponses)
            {
                Assert.NotNull(ReadOkValue(response));
            }

            Assert.Equal(2, orchestrator.HealthCallCount);

            orchestrator.ReleaseBlockedCall();
            var refreshed = ReadOkValue(await refreshTask);

            Assert.NotNull(refreshed);
            Assert.Equal(2, orchestrator.HealthCallCount);
        }

        private static ApplicationDbContext NewDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"PublicStatsControllerTests_{Guid.NewGuid():N}")
                .EnableServiceProviderCaching(false)
                .Options;

            return new ApplicationDbContext(options);
        }

        private static PublicStatsController NewController(
            ApplicationDbContext db,
            IMemoryCache cache,
            IImageProcessingOrchestrator orchestrator)
        {
            return new PublicStatsController(
                db,
                cache,
                orchestrator,
                NullLogger<PublicStatsController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext(),
                },
            };
        }

        private static PublicSystemStats ReadOkValue(ActionResult<PublicSystemStats> result)
        {
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            return Assert.IsType<PublicSystemStats>(ok.Value);
        }

        private sealed class BlockingOrchestrator : IImageProcessingOrchestrator
        {
            private int _healthCallCount;
            private readonly TaskCompletionSource _blockedCallStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _releaseBlockedCall =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int BlockOnHealthCallNumber { get; set; }
            public int HealthCallCount => Volatile.Read(ref _healthCallCount);
            public TaskCompletionSource BlockedCallStarted => _blockedCallStarted;

            public Task<ScannerProcessingResult> ProcessContainerAsync(string containerId, string scannerType)
            {
                return Task.FromResult(new ScannerProcessingResult());
            }

            public Task<Container> CreateContainerFromScannerDataAsync(ScannerData scannerData)
            {
                return Task.FromResult(new Container());
            }

            public Task<ScannerProcessingResult> ProcessImageAsync(string imageId, string scannerType)
            {
                return Task.FromResult(new ScannerProcessingResult());
            }

            public async Task<Dictionary<string, bool>> GetSystemHealthAsync()
            {
                var callNumber = Interlocked.Increment(ref _healthCallCount);
                if (callNumber == BlockOnHealthCallNumber)
                {
                    _blockedCallStarted.TrySetResult();
                    await _releaseBlockedCall.Task;
                }

                return new Dictionary<string, bool> { ["test"] = true };
            }

            public IEnumerable<string> GetAvailableScannerTypes()
            {
                return Array.Empty<string>();
            }

            public void ReleaseBlockedCall()
            {
                _releaseBlockedCall.TrySetResult();
            }
        }
    }
}
