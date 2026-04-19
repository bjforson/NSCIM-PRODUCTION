using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/attendance")]
    [Authorize]
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceService _service;
        private readonly ILogger<AttendanceController> _logger;

        public AttendanceController(
            IAttendanceService service,
            ILogger<AttendanceController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AttendanceRecord>>> GetAttendanceRecords(
            [FromQuery] Guid? employeeId = null,
            [FromQuery] Guid? siteId = null,
            [FromQuery] DateTime? dateFrom = null,
            [FromQuery] DateTime? dateTo = null,
            [FromQuery] string? status = null)
        {
            try
            {
                IEnumerable<AttendanceRecord> records;

                if (employeeId.HasValue)
                {
                    records = await _service.GetByEmployeeIdAsync(employeeId.Value, dateFrom, dateTo);
                }
                else if (siteId.HasValue)
                {
                    records = await _service.GetBySiteIdAsync(siteId.Value, dateFrom, dateTo);
                }
                else if (dateFrom.HasValue && dateTo.HasValue)
                {
                    records = await _service.GetBySiteIdAsync(siteId ?? Guid.Empty, dateFrom, dateTo);
                }
                else
                {
                    return BadRequest(new { error = "Must provide employeeId, siteId, or dateFrom/dateTo", code = "VALIDATION_ERROR" });
                }

                if (!string.IsNullOrEmpty(status))
                {
                    records = records.Where(r => r.Status == status);
                }

                return Ok(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance records");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AttendanceRecord>> GetAttendanceRecord(Guid id)
        {
            try
            {
                var record = await _service.GetByIdAsync(id);
                if (record == null)
                {
                    return NotFound(new { error = "AttendanceRecord not found", code = "NOT_FOUND" });
                }
                return Ok(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance record {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost("checkin")]
        public async Task<ActionResult<AttendanceRecord>> CheckIn([FromBody] CheckInRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var record = await _service.CheckInAsync(
                    request.ShiftAssignmentId,
                    request.EmployeeId,
                    request.SiteId,
                    request.Date,
                    request.CheckInTime,
                    request.Source ?? "MANUAL");

                return CreatedAtAction(nameof(GetAttendanceRecord), new { id = record.Id }, record);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "CHECKIN_VALIDATION_ERROR" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording check-in");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost("{id}/checkout")]
        public async Task<ActionResult<AttendanceRecord>> CheckOut(Guid id, [FromBody] CheckOutRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var record = await _service.CheckOutAsync(id, request.CheckOutTime, request.Source ?? "MANUAL");
                return Ok(record);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, code = "CHECKOUT_VALIDATION_ERROR" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording check-out");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<AttendanceRecord>> CreateAttendanceRecord([FromBody] AttendanceRecord record)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var created = await _service.CreateOrUpdateAsync(record);
                return CreatedAtAction(nameof(GetAttendanceRecord), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating attendance record");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<AttendanceRecord>> UpdateAttendanceRecord(Guid id, [FromBody] AttendanceRecord record)
        {
            try
            {
                if (id != record.Id)
                {
                    return BadRequest(new { error = "ID mismatch", code = "VALIDATION_ERROR" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Validation failed", code = "VALIDATION_ERROR", details = ModelState });
                }

                var updated = await _service.UpdateAsync(record);
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message, code = "NOT_FOUND" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating attendance record {Id}", id);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }
    }

    public class CheckInRequest
    {
        public Guid? ShiftAssignmentId { get; set; }
        public Guid EmployeeId { get; set; }
        public Guid SiteId { get; set; }
        public DateTime Date { get; set; }
        public DateTime CheckInTime { get; set; }
        public string? Source { get; set; }
    }

    public class CheckOutRequest
    {
        public DateTime CheckOutTime { get; set; }
        public string? Source { get; set; }
    }
}

