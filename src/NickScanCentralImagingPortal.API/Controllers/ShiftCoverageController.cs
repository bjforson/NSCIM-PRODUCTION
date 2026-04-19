using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/shift-coverage")]
    [Authorize]
    public class ShiftCoverageController : ControllerBase
    {
        private readonly IShiftCoverageService _service;
        private readonly ILogger<ShiftCoverageController> _logger;

        public ShiftCoverageController(
            IShiftCoverageService service,
            ILogger<ShiftCoverageController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<CoverageAnalysis>> GetCoverageAnalysis(
            [FromQuery] Guid siteId,
            [FromQuery] DateTime dateFrom,
            [FromQuery] DateTime dateTo,
            [FromQuery] Guid? shiftTemplateId = null)
        {
            try
            {
                var analysis = await _service.GetCoverageAnalysisAsync(siteId, dateFrom, dateTo, shiftTemplateId);
                return Ok(analysis);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "VALIDATION_ERROR" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting coverage analysis");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpGet("requirements")]
        public async Task<ActionResult<IEnumerable<ShiftCoverageRequirement>>> GetCoverageRequirements(
            [FromQuery] Guid? siteId = null,
            [FromQuery] Guid? laneId = null,
            [FromQuery] bool activeOnly = true)
        {
            try
            {
                var requirements = await _service.GetRequirementsAsync(siteId, laneId, activeOnly);
                return Ok(requirements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting coverage requirements");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost("requirements")]
        public async Task<ActionResult<ShiftCoverageRequirement>> CreateCoverageRequirement(
            [FromBody] ShiftCoverageRequirement requirement)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var created = await _service.CreateRequirementAsync(requirement);
                return CreatedAtAction(nameof(GetCoverageRequirements), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating coverage requirement");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPut("requirements/{id}")]
        public async Task<ActionResult<ShiftCoverageRequirement>> UpdateCoverageRequirement(
            Guid id,
            [FromBody] ShiftCoverageRequirement requirement)
        {
            try
            {
                if (id != requirement.Id)
                {
                    return BadRequest(new { error = "ID mismatch", code = "VALIDATION_ERROR" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var updated = await _service.UpdateRequirementAsync(requirement);
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message, code = "NOT_FOUND" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating coverage requirement {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpDelete("requirements/{id}")]
        public async Task<ActionResult> DeleteCoverageRequirement(Guid id)
        {
            try
            {
                var deleted = await _service.DeleteRequirementAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "CoverageRequirement not found", code = "NOT_FOUND" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting coverage requirement {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }
    }
}

