using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/shift-assignments")]
    [Authorize]
    public class ShiftAssignmentController : ControllerBase
    {
        private readonly IShiftAssignmentService _service;
        private readonly ILogger<ShiftAssignmentController> _logger;

        public ShiftAssignmentController(
            IShiftAssignmentService service,
            ILogger<ShiftAssignmentController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ShiftAssignment>>> GetShiftAssignments(
            [FromQuery] Guid? employeeId = null,
            [FromQuery] Guid? siteId = null,
            [FromQuery] Guid? laneId = null,
            [FromQuery] DateTime? date = null,
            [FromQuery] DateTime? dateFrom = null,
            [FromQuery] DateTime? dateTo = null,
            [FromQuery] string? status = null)
        {
            try
            {
                IEnumerable<ShiftAssignment> assignments;

                if (employeeId.HasValue)
                {
                    assignments = await _service.GetByEmployeeIdAsync(employeeId.Value, dateFrom, dateTo);
                }
                else if (siteId.HasValue)
                {
                    assignments = await _service.GetBySiteIdAsync(siteId.Value, dateFrom, dateTo);
                }
                else if (dateFrom.HasValue && dateTo.HasValue)
                {
                    assignments = await _service.GetByDateRangeAsync(dateFrom.Value, dateTo.Value, siteId);
                }
                else
                {
                    return BadRequest(new { error = "Must provide employeeId, siteId, or dateFrom/dateTo", code = "VALIDATION_ERROR" });
                }

                if (date.HasValue)
                {
                    assignments = assignments.Where(a => a.Date.Date == date.Value.Date);
                }

                if (!string.IsNullOrEmpty(status))
                {
                    assignments = assignments.Where(a => a.Status == status);
                }

                return Ok(assignments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shift assignments");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ShiftAssignment>> GetShiftAssignment(Guid id)
        {
            try
            {
                var assignment = await _service.GetByIdAsync(id);
                if (assignment == null)
                {
                    return NotFound(new { error = "ShiftAssignment not found", code = "NOT_FOUND" });
                }
                return Ok(assignment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shift assignment {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<ShiftAssignment>> CreateShiftAssignment([FromBody] ShiftAssignment assignment)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var created = await _service.CreateAsync(assignment);
                return CreatedAtAction(nameof(GetShiftAssignment), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "SHIFT_CONFLICT" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shift assignment");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost("bulk")]
        public async Task<ActionResult<BulkAssignmentResult>> CreateBulkShiftAssignments(
            [FromBody] BulkAssignmentRequest request)
        {
            try
            {
                if (!ModelState.IsValid || request?.Assignments == null)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var result = await _service.CreateBulkAsync(
                    request.Assignments,
                    request.ValidateConflicts ?? true);

                if (result.Failed > 0 && result.Created == 0)
                {
                    return BadRequest(result);
                }

                return result.Failed > 0
                    ? StatusCode(207, result) // Partial success
                    : Created("", result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bulk shift assignments");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ShiftAssignment>> UpdateShiftAssignment(Guid id, [FromBody] ShiftAssignment assignment)
        {
            try
            {
                if (id != assignment.Id)
                {
                    return BadRequest(new { error = "ID mismatch", code = "VALIDATION_ERROR" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var updated = await _service.UpdateAsync(assignment);
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "SHIFT_CONFLICT" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating shift assignment {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPatch("{id}/status")]
        public async Task<ActionResult<ShiftAssignment>> UpdateShiftAssignmentStatus(
            Guid id,
            [FromBody] UpdateStatusRequest request)
        {
            try
            {
                var updated = await _service.UpdateStatusAsync(id, request.Status, request.Notes);
                if (!updated)
                {
                    return NotFound(new { error = "ShiftAssignment not found", code = "NOT_FOUND" });
                }

                var assignment = await _service.GetByIdAsync(id);
                return Ok(assignment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating shift assignment status {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteShiftAssignment(Guid id)
        {
            try
            {
                var deleted = await _service.DeleteAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "ShiftAssignment not found", code = "NOT_FOUND" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting shift assignment {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }
    }

    public class BulkAssignmentRequest
    {
        public List<ShiftAssignment> Assignments { get; set; } = new();
        public bool? ValidateConflicts { get; set; } = true;
        public bool? ValidateCertifications { get; set; } = true;
        public bool? SkipFailures { get; set; } = false;
    }

    public class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}

