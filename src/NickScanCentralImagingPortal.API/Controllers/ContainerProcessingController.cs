using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.DTOs.ContainerProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for container processing operations
    /// Provides grouped container data with smart grouping by clearance type
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ContainerProcessingController : ControllerBase
    {
        private readonly IContainerProcessingRepository _repository;
        private readonly ILogger<ContainerProcessingController> _logger;

        public ContainerProcessingController(
            IContainerProcessingRepository repository,
            ILogger<ContainerProcessingController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Get container groups with smart grouping by clearance type
        /// IM/EX: Grouped by BOE Number
        /// CMR: Grouped by BL Number
        /// </summary>
        [HttpGet("groups")]
        public async Task<ActionResult<List<ContainerGroupDto>>> GetGroups(
            [FromQuery] string? clearanceType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _logger.LogInformation("API: Getting container groups - ClearanceType: {ClearanceType}, Page: {Page}",
                    clearanceType, page);

                var groups = await _repository.GetContainerGroupsAsync(clearanceType, page, pageSize);

                _logger.LogInformation("API: Returning {Count} container groups", groups.Count);

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error getting container groups");
                return StatusCode(500, new { Error = "Failed to retrieve container groups", Details = ex.Message });
            }
        }

        /// <summary>
        /// Get summary statistics for container processing
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<ContainerProcessingSummaryDto>> GetSummary()
        {
            try
            {
                _logger.LogInformation("API: Getting container processing summary");

                var summary = await _repository.GetSummaryStatisticsAsync();

                _logger.LogInformation("API: Returning summary - {Total} total containers in {Groups} groups",
                    summary.TotalContainers, summary.TotalGroups);

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error getting container processing summary");
                return StatusCode(500, new { Error = "Failed to retrieve summary", Details = ex.Message });
            }
        }

        /// <summary>
        /// Get details for a specific container group
        /// </summary>
        [HttpGet("group/{clearanceType}/{groupingValue}")]
        public async Task<ActionResult<ContainerGroupDto>> GetGroupDetails(string clearanceType, string groupingValue)
        {
            try
            {
                _logger.LogInformation("API: Getting group details - Type: {Type}, Value: {Value}",
                    clearanceType, groupingValue);

                var group = await _repository.GetContainerGroupDetailsAsync(clearanceType, groupingValue);

                if (group == null)
                {
                    return NotFound(new { Error = "Group not found" });
                }

                return Ok(group);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error getting group details");
                return StatusCode(500, new { Error = "Failed to retrieve group details", Details = ex.Message });
            }
        }
    }
}

