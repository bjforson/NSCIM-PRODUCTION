using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers;

[Authorize]
[ApiController]
[Route("api/ManualBOERequest")]
public sealed class ManualBOERequestCompatibilityController : ControllerBase
{
    private readonly IManualBOESelectivityService _manualBoeService;
    private readonly ILogger<ManualBOERequestCompatibilityController> _logger;

    public ManualBOERequestCompatibilityController(
        IManualBOESelectivityService manualBoeService,
        ILogger<ManualBOERequestCompatibilityController> logger)
    {
        _manualBoeService = manualBoeService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ManualBOERequestCompatibilityResponse>> Create(
        [FromBody] ManualBOERequestCompatibilityCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContainerNumber))
        {
            return BadRequest(new { error = "ContainerNumber is required" });
        }

        var containerNumber = request.ContainerNumber.Trim();
        var requestedBy = !string.IsNullOrWhiteSpace(request.RequestedBy)
            ? request.RequestedBy.Trim()
            : User.Identity?.Name ?? "System";

        try
        {
            var boeRequest = await _manualBoeService.CreateManualBOERequestAsync(containerNumber, requestedBy);
            return Ok(ManualBOERequestCompatibilityResponse.FromEntity(boeRequest));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual BOE compatibility request failed for container {ContainerNumber}", containerNumber);
            return StatusCode(500, new { error = "Manual BOE request failed", details = ex.Message });
        }
    }
}

public sealed class ManualBOERequestCompatibilityCreateRequest
{
    public string ContainerNumber { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
}

public sealed class ManualBOERequestCompatibilityResponse
{
    public int Id { get; set; }
    public string ContainerNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public DateTime RequestDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Message { get; set; } = string.Empty;

    public static ManualBOERequestCompatibilityResponse FromEntity(ManualBOERequest request) => new()
    {
        Id = request.Id,
        ContainerNumber = request.ContainerNumber,
        Status = request.Status,
        RequestedBy = request.RequestedBy,
        RequestDate = request.RequestDate,
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
        Message = $"BOE request queued for {request.ContainerNumber}"
    };
}
