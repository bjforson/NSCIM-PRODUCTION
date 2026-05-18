using Microsoft.Extensions.DependencyInjection;
using NickScanCentralImagingPortal.Services.Caching;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public sealed class SystemCacheWarmupServiceTests
{
    [Fact]
    public async Task RunOnceAsync_WhenDisabledAndNotForced_SkipsProviders()
    {
        using var harness = NewHarness(
            new SystemCacheOptions { WarmupEnabled = false },
            new StubWarmupProvider("stub", 5));

        var result = await harness.Service.RunOnceAsync("test", force: false);

        Assert.True(result.Skipped);
        Assert.Equal("System cache warmup disabled", result.SkippedReason);
        Assert.Equal(0, result.ProviderCount);
        Assert.Equal(0, harness.Provider.RunCount);
        Assert.Equal(1, harness.State.Snapshot(new SystemCacheOptions(), []).TotalSkipped);
    }

    [Fact]
    public async Task RunOnceAsync_WhenForced_RunsProviderAndUpdatesState()
    {
        using var harness = NewHarness(
            new SystemCacheOptions { WarmupEnabled = false },
            new StubWarmupProvider("stub", 5));

        var result = await harness.Service.RunOnceAsync("test", force: true);
        var snapshot = harness.Service.Snapshot();

        Assert.True(result.Started);
        Assert.False(result.Skipped);
        Assert.Equal(1, result.ProviderCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(5, result.WarmedKeyCount);
        Assert.Equal(1, harness.Provider.RunCount);
        Assert.Equal(1, snapshot.TotalSuccesses);
        Assert.Contains("stub", snapshot.RegisteredProviders);
    }

    [Fact]
    public async Task RunOnceAsync_WhenNoProviders_SkipsWithReason()
    {
        using var harness = NewHarness(new SystemCacheOptions { WarmupEnabled = true });

        var result = await harness.Service.RunOnceAsync("test", force: false);

        Assert.True(result.Skipped);
        Assert.Equal("No system cache warmup providers registered", result.SkippedReason);
        Assert.Equal(0, result.ProviderCount);
    }

    [Fact]
    public async Task PredictivePreloadWarmupProvider_MapsPredictivePreloadResult()
    {
        var predictive = new StubPredictivePreloadService(new PredictivePreloadRunResult
        {
            Enabled = true,
            CandidateCount = 2,
            SuccessCount = 1,
            FailureCount = 0,
            Assignments =
            [
                new PredictivePreloadAssignmentResult
                {
                    Success = true,
                    ContainerPreloadSuccessCount = 2
                }
            ]
        });
        var provider = new PredictivePreloadWarmupProvider(predictive);

        var result = await provider.WarmupAsync();

        Assert.True(result.Success);
        Assert.Equal("predictive-preload", result.ProviderName);
        Assert.Equal(3, result.WarmedKeyCount);
        Assert.Equal(1, predictive.RunCount);
        Assert.Contains("Candidates=2", result.Message);
    }

    private static SystemCacheWarmupHarness NewHarness(
        SystemCacheOptions options,
        StubWarmupProvider? provider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SystemCacheOptions>(configured =>
        {
            configured.WarmupEnabled = options.WarmupEnabled;
            configured.MaxWarmupConcurrency = options.MaxWarmupConcurrency;
            configured.WarmupStartupDelaySeconds = options.WarmupStartupDelaySeconds;
            configured.WarmupIntervalMinutes = options.WarmupIntervalMinutes;
            configured.WarmupJitterSeconds = options.WarmupJitterSeconds;
        });
        services.AddSingleton<SystemCacheWarmupState>();
        services.AddSingleton<SystemCacheWarmupService>();
        if (provider is not null)
        {
            services.AddSingleton<ISystemCacheWarmupProvider>(provider);
        }

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        return new SystemCacheWarmupHarness(
            serviceProvider,
            serviceProvider.GetRequiredService<SystemCacheWarmupService>(),
            serviceProvider.GetRequiredService<SystemCacheWarmupState>(),
            provider ?? new StubWarmupProvider("unused", 0));
    }

    private sealed class StubWarmupProvider : ISystemCacheWarmupProvider
    {
        private readonly int _warmedKeyCount;

        public StubWarmupProvider(string name, int warmedKeyCount)
        {
            Name = name;
            _warmedKeyCount = warmedKeyCount;
        }

        public string Name { get; }

        public int RunCount { get; private set; }

        public Task<SystemCacheWarmupProviderResult> WarmupAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            return Task.FromResult(SystemCacheWarmupProviderResult.Succeeded(
                Name,
                _warmedKeyCount,
                TimeSpan.FromMilliseconds(1)));
        }
    }

    private sealed class StubPredictivePreloadService : IPredictivePreloadService
    {
        private readonly PredictivePreloadRunResult _result;

        public StubPredictivePreloadService(PredictivePreloadRunResult result)
        {
            _result = result;
        }

        public int RunCount { get; private set; }

        public Task<PredictivePreloadRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            return Task.FromResult(_result);
        }

        public Task<PredictivePreloadAssignmentResult> PreloadAssignmentAsync(
            Guid groupId,
            string role,
            string eligibleStatus,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PredictivePreloadContainerResult> PreloadContainerContextAsync(
            string containerNumber,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task InvalidateAssignmentAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task InvalidateContainerContextAsync(string containerNumber, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task InvalidateRoleAssignmentsAsync(string role, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PredictiveAssignmentContext?> GetAssignmentContextAsync(
            Guid groupId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PredictiveContainerContext?> GetContainerContextAsync(
            string containerNumber,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed record SystemCacheWarmupHarness(
        ServiceProvider ServiceProvider,
        SystemCacheWarmupService Service,
        SystemCacheWarmupState State,
        StubWarmupProvider Provider) : IDisposable
    {
        public void Dispose()
        {
            ServiceProvider.Dispose();
        }
    }
}
