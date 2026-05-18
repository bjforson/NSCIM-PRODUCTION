using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheWarmupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SystemCacheOptions> _options;
    private readonly SystemCacheWarmupState _state;
    private readonly ILogger<SystemCacheWarmupService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public SystemCacheWarmupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SystemCacheOptions> options,
        SystemCacheWarmupState state,
        ILogger<SystemCacheWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        if (!options.WarmupEnabled)
        {
            _logger.LogInformation("System cache warmup background service disabled");
            return;
        }

        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, options.WarmupStartupDelaySeconds));
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        do
        {
            await RunOnceAsync("background", force: false, stoppingToken);

            var delay = GetNextDelay(_options.CurrentValue);
            if (delay <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(delay, stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    public SystemCacheWarmupSnapshot Snapshot()
    {
        using var scope = _scopeFactory.CreateScope();
        var providerNames = scope.ServiceProvider
            .GetServices<ISystemCacheWarmupProvider>()
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return _state.Snapshot(_options.CurrentValue, providerNames);
    }

    public async Task<SystemCacheWarmupRunResult> RunOnceAsync(
        string trigger,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var startedAtUtc = DateTime.UtcNow;
        var result = new SystemCacheWarmupRunResult
        {
            Enabled = options.WarmupEnabled,
            Force = force,
            Trigger = trigger,
            StartedAtUtc = startedAtUtc
        };

        if (!options.WarmupEnabled && !force)
        {
            result.Skipped = true;
            result.SkippedReason = "System cache warmup disabled";
            result.FinishedAtUtc = DateTime.UtcNow;
            _state.MarkCompleted(result);
            return result;
        }

        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            result.Skipped = true;
            result.AlreadyRunning = true;
            result.SkippedReason = "System cache warmup already running";
            result.FinishedAtUtc = DateTime.UtcNow;
            _state.MarkCompleted(result);
            return result;
        }

        try
        {
            result.Started = true;
            _state.MarkStarted(startedAtUtc);

            using var scope = _scopeFactory.CreateScope();
            var providers = scope.ServiceProvider
                .GetServices<ISystemCacheWarmupProvider>()
                .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.ProviderCount = providers.Count;
            if (providers.Count == 0)
            {
                result.Skipped = true;
                result.SkippedReason = "No system cache warmup providers registered";
                result.FinishedAtUtc = DateTime.UtcNow;
                _state.MarkCompleted(result);
                return result;
            }

            result.Providers = await RunProvidersAsync(providers, options, cancellationToken);
            result.SuccessCount = result.Providers.Count(p => p.Success && !p.Skipped);
            result.FailureCount = result.Providers.Count(p => !p.Success);
            result.SkippedCount = result.Providers.Count(p => p.Skipped);
            result.WarmedKeyCount = result.Providers.Sum(p => Math.Max(0, p.WarmedKeyCount));
            result.FinishedAtUtc = DateTime.UtcNow;

            _state.MarkCompleted(result);
            _logger.LogInformation(
                "System cache warmup completed: Trigger={Trigger}, Providers={ProviderCount}, Success={SuccessCount}, Failed={FailureCount}, Skipped={SkippedCount}, WarmedKeys={WarmedKeyCount}",
                trigger,
                result.ProviderCount,
                result.SuccessCount,
                result.FailureCount,
                result.SkippedCount,
                result.WarmedKeyCount);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.FailureCount++;
            result.FinishedAtUtc = DateTime.UtcNow;
            _state.MarkFailed(result.FinishedAtUtc.Value, ex);
            _logger.LogWarning(ex, "System cache warmup failed for trigger {Trigger}", trigger);
            return result;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static async Task<List<SystemCacheWarmupProviderResult>> RunProvidersAsync(
        List<ISystemCacheWarmupProvider> providers,
        SystemCacheOptions options,
        CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Max(1, options.MaxWarmupConcurrency);
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = providers.Select(provider => RunProviderBoundedAsync(provider, gate, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static async Task<SystemCacheWarmupProviderResult> RunProviderBoundedAsync(
        ISystemCacheWarmupProvider provider,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await provider.WarmupAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return SystemCacheWarmupProviderResult.Failed(provider.Name, stopwatch.Elapsed, ex);
        }
        finally
        {
            gate.Release();
        }
    }

    private static TimeSpan GetNextDelay(SystemCacheOptions options)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(0, options.WarmupIntervalMinutes));
        if (interval <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var jitterSeconds = Math.Max(0, options.WarmupJitterSeconds);
        if (jitterSeconds == 0)
        {
            return interval;
        }

        return interval + TimeSpan.FromSeconds(Random.Shared.Next(0, jitterSeconds + 1));
    }
}
