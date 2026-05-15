using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Services.Caching;

namespace NickScanCentralImagingPortal.API.Controllers;

[Authorize]
[ApiController]
[Route("api/cache/predictive")]
public sealed class PredictivePreloadController : ControllerBase
{
    private readonly IPredictivePreloadService _preloadService;
    private readonly PredictivePreloadState _state;
    private readonly PredictivePreloadOptions _options;
    private readonly ILogger<PredictivePreloadController> _logger;

    public PredictivePreloadController(
        IPredictivePreloadService preloadService,
        PredictivePreloadState state,
        IOptions<PredictivePreloadOptions> options,
        ILogger<PredictivePreloadController> logger)
    {
        _preloadService = preloadService;
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("status")]
    public ActionResult<PredictivePreloadStatusSnapshot> GetStatus()
    {
        return Ok(_state.Snapshot(_options));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("run-once")]
    public async Task<ActionResult<PredictivePreloadRunResult>> RunOnce(CancellationToken cancellationToken)
    {
        if (_state.IsRunning)
        {
            return Conflict(new { Message = "Predictive preload is already running." });
        }

        try
        {
            var result = await _preloadService.RunOnceAsync(cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { Message = "Predictive preload run was cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual predictive preload run failed");
            return StatusCode(500, new { Message = "Predictive preload run failed.", Error = ex.Message });
        }
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invalidate/assignment/{groupId:guid}")]
    public async Task<IActionResult> InvalidateAssignment(Guid groupId, CancellationToken cancellationToken)
    {
        await _preloadService.InvalidateAssignmentAsync(groupId, cancellationToken);
        return Ok(new { Message = "Predictive assignment cache invalidated.", GroupId = groupId });
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invalidate/container/{containerNumber}")]
    public async Task<IActionResult> InvalidateContainer(string containerNumber, CancellationToken cancellationToken)
    {
        await _preloadService.InvalidateContainerContextAsync(containerNumber, cancellationToken);
        return Ok(new { Message = "Predictive container cache invalidated.", ContainerNumber = containerNumber });
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("preload/container/{containerNumber}")]
    public async Task<ActionResult<PredictivePreloadContainerResult>> PreloadContainer(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var result = await _preloadService.PreloadContainerContextAsync(containerNumber, cancellationToken);
        return result.Success ? Ok(result) : StatusCode(500, result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("assignment/{groupId:guid}")]
    public async Task<ActionResult<PredictiveAssignmentContext>> GetAssignmentContext(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var context = await _preloadService.GetAssignmentContextAsync(groupId, cancellationToken);
        if (context == null)
        {
            return NotFound(new { Message = "Predictive assignment cache entry was not found.", GroupId = groupId });
        }

        return Ok(context);
    }

    [HttpGet("container/{containerNumber}")]
    public async Task<ActionResult<PredictiveContainerContext>> GetContainerContext(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var context = await _preloadService.GetContainerContextAsync(containerNumber, cancellationToken);
        if (context == null)
        {
            return NotFound(new { Message = "Predictive container cache entry was not found.", ContainerNumber = containerNumber });
        }

        return Ok(context);
    }
}
