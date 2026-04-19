using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/shift-templates")]
    [Authorize]
    public class ShiftTemplateController : ControllerBase
    {
        private readonly IShiftTemplateService _service;
        private readonly ILogger<ShiftTemplateController> _logger;

        public ShiftTemplateController(
            IShiftTemplateService service,
            ILogger<ShiftTemplateController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ShiftTemplate>>> GetShiftTemplates(
            [FromQuery] Guid? siteId = null,
            [FromQuery] string? status = null)
        {
            try
            {
                var activeOnly = status != "INACTIVE";
                var templates = await _service.GetAllAsync(siteId, activeOnly);

                if (!string.IsNullOrEmpty(status))
                {
                    templates = templates.Where(t => t.Status == status);
                }

                return Ok(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shift templates");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ShiftTemplate>> GetShiftTemplate(Guid id)
        {
            try
            {
                var template = await _service.GetByIdAsync(id);
                if (template == null)
                {
                    return NotFound(new { error = "ShiftTemplate not found", code = "SHIFT_TEMPLATE_NOT_FOUND" });
                }
                return Ok(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shift template {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<ShiftTemplate>> CreateShiftTemplate([FromBody] ShiftTemplate template)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var created = await _service.CreateAsync(template);
                return CreatedAtAction(nameof(GetShiftTemplate), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "VALIDATION_ERROR" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shift template");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ShiftTemplate>> UpdateShiftTemplate(Guid id, [FromBody] ShiftTemplate template)
        {
            try
            {
                if (id != template.Id)
                {
                    return BadRequest(new { error = "ID mismatch", code = "VALIDATION_ERROR" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var updated = await _service.UpdateAsync(template);
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message, code = "NOT_FOUND" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating shift template {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteShiftTemplate(Guid id)
        {
            try
            {
                var deleted = await _service.DeleteAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "ShiftTemplate not found", code = "NOT_FOUND" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting shift template {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }
    }
}

