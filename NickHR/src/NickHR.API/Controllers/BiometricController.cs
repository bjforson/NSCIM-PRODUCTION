using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickHR.Core.DTOs;
using NickHR.Core.Entities.Leave;
using NickHR.Core.Enums;
using NickHR.Infrastructure.Data;

namespace NickHR.API.Controllers;

[ApiController]
[Route("api/attendance/biometric")]
public class BiometricController : ControllerBase
{
    private readonly NickHRDbContext _db;

    public BiometricController(NickHRDbContext db)
    {
        _db = db;
    }

    /// <summary>Clock in from a biometric device.</summary>
    [HttpPost("clock-in")]
    public async Task<IActionResult> ClockIn([FromBody] BiometricClockRequest request)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeCode == request.EmployeeCode);

        if (employee == null)
            return NotFound(ApiResponse<object>.Fail("Employee not found."));

        var today = DateTime.UtcNow.Date;
        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Date == today);

        if (existing != null && existing.ClockIn.HasValue)
            return BadRequest(ApiResponse<object>.Fail("Already clocked in today."));

        if (existing == null)
        {
            existing = new AttendanceRecord
            {
                EmployeeId = employee.Id,
                Date = today,
                ClockIn = request.Timestamp ?? DateTime.UtcNow,
                AttendanceType = AttendanceType.Present,
                Notes = $"Biometric device: {request.DeviceId}"
            };
            _db.AttendanceRecords.Add(existing);
        }
        else
        {
            existing.ClockIn = request.Timestamp ?? DateTime.UtcNow;
            existing.Notes = $"Biometric device: {request.DeviceId}";
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { existing.Id, existing.ClockIn }, "Clocked in."));
    }

    /// <summary>Clock out from a biometric device.</summary>
    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut([FromBody] BiometricClockRequest request)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeCode == request.EmployeeCode);

        if (employee == null)
            return NotFound(ApiResponse<object>.Fail("Employee not found."));

        var today = DateTime.UtcNow.Date;
        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Date == today);

        if (existing == null || !existing.ClockIn.HasValue)
            return BadRequest(ApiResponse<object>.Fail("No clock-in record found for today."));

        var clockOut = request.Timestamp ?? DateTime.UtcNow;
        existing.ClockOut = clockOut;
        existing.WorkHours = (decimal)(clockOut - existing.ClockIn!.Value).TotalHours;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { existing.Id, existing.ClockIn, existing.ClockOut, existing.WorkHours }, "Clocked out."));
    }

    /// <summary>Get today's attendance status for an employee.</summary>
    [HttpGet("status/{employeeCode}")]
    public async Task<IActionResult> GetStatus(string employeeCode)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeCode == employeeCode);

        if (employee == null)
            return NotFound(ApiResponse<object>.Fail("Employee not found."));

        var today = DateTime.UtcNow.Date;
        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Date == today);

        return Ok(ApiResponse<object>.Ok(new
        {
            EmployeeCode = employeeCode,
            Date = today,
            ClockIn = record?.ClockIn,
            ClockOut = record?.ClockOut,
            WorkHours = record?.WorkHours,
            Status = record == null ? "Not Clocked In" : record.ClockOut.HasValue ? "Clocked Out" : "Clocked In"
        }));
    }
}

public class BiometricClockRequest
{
    public string EmployeeCode { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    public string? DeviceId { get; set; }
}
