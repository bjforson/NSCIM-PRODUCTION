using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickHR.Core.DTOs;
using NickHR.Core.Enums;
using NickHR.Infrastructure.Data;

namespace NickHR.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin,HRManager,HROfficer,DepartmentManager")]
public class DashboardController : ControllerBase
{
    private readonly NickHRDbContext _db;

    public DashboardController(NickHRDbContext db)
    {
        _db = db;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> GetSummary()
    {
        var today = DateTime.UtcNow.Date;
        var startOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonth = startOfMonth.AddMonths(1);

        var activeStatuses = new[]
        {
            EmploymentStatus.Active,
            EmploymentStatus.OnProbation,
            EmploymentStatus.Confirmed
        };

        var exitStatuses = new[]
        {
            EmploymentStatus.Terminated,
            EmploymentStatus.Resigned,
            EmploymentStatus.Retired,
            EmploymentStatus.Deceased
        };

        var totalEmployees = await _db.Employees
            .CountAsync(e => activeStatuses.Contains(e.EmploymentStatus));

        var newHiresThisMonth = await _db.Employees
            .CountAsync(e => e.HireDate >= startOfMonth && e.HireDate < endOfMonth);

        var exitsThisMonth = await _db.Employees
            .CountAsync(e =>
                exitStatuses.Contains(e.EmploymentStatus) &&
                e.UpdatedAt >= startOfMonth && e.UpdatedAt < endOfMonth);

        var openPositions = await _db.JobRequisitions
            .CountAsync(jr =>
                jr.Status == JobRequisitionStatus.Approved ||
                jr.Status == JobRequisitionStatus.Published);

        var leaveTodayCount = await _db.LeaveRequests
            .CountAsync(lr =>
                lr.Status == LeaveRequestStatus.Approved &&
                lr.StartDate.Date <= today &&
                lr.EndDate.Date >= today);

        var summary = new DashboardSummaryDto
        {
            TotalEmployees = totalEmployees,
            NewHiresThisMonth = newHiresThisMonth,
            ExitsThisMonth = exitsThisMonth,
            OpenPositions = openPositions,
            OnLeaveToday = leaveTodayCount
        };

        return Ok(ApiResponse<DashboardSummaryDto>.Ok(summary));
    }
}

public class DashboardSummaryDto
{
    public int TotalEmployees { get; set; }
    public int NewHiresThisMonth { get; set; }
    public int ExitsThisMonth { get; set; }
    public int OpenPositions { get; set; }
    public int OnLeaveToday { get; set; }
}
