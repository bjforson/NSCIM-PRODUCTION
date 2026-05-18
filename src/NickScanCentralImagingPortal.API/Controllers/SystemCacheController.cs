using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Caching;

namespace NickScanCentralImagingPortal.API.Controllers;

[Authorize]
[ApiController]
[Route("api/cache/system")]
public sealed class SystemCacheController : ControllerBase
{
    private readonly ICacheService _activeCache;
    private readonly ISystemCacheService _systemCache;
    private readonly SystemCacheMetrics _metrics;
    private readonly SystemCacheWarmupService _warmupService;
    private readonly SystemCacheOptions _options;
    private readonly ILogger<SystemCacheController> _logger;

    public SystemCacheController(
        ICacheService activeCache,
        ISystemCacheService systemCache,
        SystemCacheMetrics metrics,
        SystemCacheWarmupService warmupService,
        IOptions<SystemCacheOptions> options,
        ILogger<SystemCacheController> logger)
    {
        _activeCache = activeCache;
        _systemCache = systemCache;
        _metrics = metrics;
        _warmupService = warmupService;
        _options = options.Value;
        _logger = logger;
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("status")]
    public ActionResult<SystemCacheStatusSnapshot> GetStatus()
    {
        var activeImplementation = _activeCache.GetType().Name;
        var systemCacheActive = _activeCache is ISystemCacheService;

        return Ok(new SystemCacheStatusSnapshot(
            UseSystemCacheService: _options.UseSystemCacheService,
            SystemCacheActive: systemCacheActive,
            ActiveImplementation: activeImplementation,
            L1Enabled: _options.UseL1MemoryCache,
            L2Enabled: _options.UseDistributedCache,
            DistributedInvalidationIndexEnabled: _options.UseDistributedInvalidationIndex,
            StampedeProtectionEnabled: _options.EnableStampedeProtection,
            DefaultExpirationMinutes: _options.DefaultExpirationMinutes,
            L1ExpirationSeconds: _options.L1ExpirationSeconds,
            StampedeLockTimeoutSeconds: _options.StampedeLockTimeoutSeconds,
            InvalidationIndexExpirationMinutes: _options.InvalidationIndexExpirationMinutes,
            WarmupEnabled: _options.WarmupEnabled,
            Metrics: _metrics.Snapshot()));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("metrics")]
    public ActionResult<SystemCacheMetricsSnapshot> GetMetrics()
    {
        return Ok(_metrics.Snapshot());
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("warmup")]
    public ActionResult<SystemCacheWarmupSnapshot> GetWarmup()
    {
        return Ok(_warmupService.Snapshot());
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("warmup/run")]
    public async Task<ActionResult<SystemCacheWarmupRunResult>> RunWarmup(
        [FromBody] SystemCacheWarmupRunRequest? request,
        CancellationToken cancellationToken)
    {
        var force = request?.Force ?? true;
        var result = await _warmupService.RunOnceAsync("admin-api", force, cancellationToken);

        if (result.AlreadyRunning)
        {
            return Conflict(result);
        }

        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invalidate/prefix")]
    public async Task<ActionResult<SystemCacheInvalidationResult>> InvalidatePrefix(
        [FromBody] SystemCacheInvalidatePrefixRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prefix))
        {
            return BadRequest(new { Message = "Prefix is required." });
        }

        var before = _metrics.Snapshot().PrefixInvalidatedKeys;
        await _systemCache.RemoveByPrefixAsync(request.Prefix, cancellationToken);
        var after = _metrics.Snapshot().PrefixInvalidatedKeys;
        var removedKeys = Math.Max(0, after - before);

        _logger.LogInformation(
            "System cache prefix invalidation requested for {Prefix}; removed {Count} key(s)",
            request.Prefix,
            removedKeys);

        return Ok(new SystemCacheInvalidationResult(
            Scope: "prefix",
            Value: request.Prefix,
            RemovedKeys: removedKeys));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invalidate/tag")]
    public async Task<ActionResult<SystemCacheInvalidationResult>> InvalidateTag(
        [FromBody] SystemCacheInvalidateTagRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Tag))
        {
            return BadRequest(new { Message = "Tag is required." });
        }

        var before = _metrics.Snapshot().TagInvalidatedKeys;
        await _systemCache.RemoveByTagAsync(request.Tag, cancellationToken);
        var after = _metrics.Snapshot().TagInvalidatedKeys;
        var removedKeys = Math.Max(0, after - before);

        _logger.LogInformation(
            "System cache tag invalidation requested for {Tag}; removed {Count} key(s)",
            request.Tag,
            removedKeys);

        return Ok(new SystemCacheInvalidationResult(
            Scope: "tag",
            Value: request.Tag,
            RemovedKeys: removedKeys));
    }
}

public sealed record SystemCacheStatusSnapshot(
    bool UseSystemCacheService,
    bool SystemCacheActive,
    string ActiveImplementation,
    bool L1Enabled,
    bool L2Enabled,
    bool DistributedInvalidationIndexEnabled,
    bool StampedeProtectionEnabled,
    int DefaultExpirationMinutes,
    int L1ExpirationSeconds,
    int StampedeLockTimeoutSeconds,
    int InvalidationIndexExpirationMinutes,
    bool WarmupEnabled,
    SystemCacheMetricsSnapshot Metrics);

public sealed record SystemCacheInvalidatePrefixRequest(string Prefix);

public sealed record SystemCacheInvalidateTagRequest(string Tag);

public sealed record SystemCacheInvalidationResult(string Scope, string Value, long RemovedKeys);
