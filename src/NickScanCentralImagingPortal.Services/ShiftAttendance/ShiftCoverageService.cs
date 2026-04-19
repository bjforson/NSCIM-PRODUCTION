using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ShiftAttendance
{
    public class ShiftCoverageService : IShiftCoverageService
    {
        private readonly IShiftCoverageRepository _coverageRepository;
        private readonly IShiftAssignmentRepository _assignmentRepository;
        private readonly IShiftTemplateRepository _templateRepository;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ShiftCoverageService> _logger;

        public ShiftCoverageService(
            IShiftCoverageRepository coverageRepository,
            IShiftAssignmentRepository assignmentRepository,
            IShiftTemplateRepository templateRepository,
            ApplicationDbContext context,
            ILogger<ShiftCoverageService> logger)
        {
            _coverageRepository = coverageRepository;
            _assignmentRepository = assignmentRepository;
            _templateRepository = templateRepository;
            _context = context;
            _logger = logger;
        }

        public async Task<CoverageAnalysis> GetCoverageAnalysisAsync(Guid siteId, DateTime dateFrom, DateTime dateTo, Guid? shiftTemplateId = null)
        {
            var site = await _context.Sites.FindAsync(siteId);
            if (site == null)
            {
                throw new InvalidOperationException($"Site with ID '{siteId}' not found.");
            }

            var analysis = new CoverageAnalysis
            {
                SiteId = siteId,
                SiteName = site.Name,
                DateFrom = dateFrom,
                DateTo = dateTo
            };

            // Get all shift templates (or specific one)
            var templates = shiftTemplateId.HasValue
                ? new[] { await _templateRepository.GetByIdAsync(shiftTemplateId.Value) }.Where(t => t != null).Cast<ShiftTemplate>()
                : await _templateRepository.GetActiveTemplatesAsync();

            // Get requirements for this site
            var requirements = await _coverageRepository.GetBySiteIdAsync(siteId, true);

            // Group by date
            for (var date = dateFrom.Date; date <= dateTo.Date; date = date.AddDays(1))
            {
                var dailyCoverage = new DailyCoverage { Date = date };

                foreach (var template in templates)
                {
                    var requirement = requirements.FirstOrDefault(r =>
                        r.ShiftTemplateId == template.Id
                        && r.EffectiveFrom <= date
                        && (r.EffectiveTo == null || r.EffectiveTo >= date));

                    // Get assignments for this date and template
                    var assignments = await _assignmentRepository.GetByDateRangeAsync(date, date, siteId);
                    var templateAssignments = assignments
                        .Where(a => a.ShiftTemplateId == template.Id && a.Status != "CANCELLED" && a.Status != "NO_SHOW")
                        .ToList();

                    var shiftCoverage = new ShiftCoverage
                    {
                        ShiftTemplateId = template.Id,
                        ShiftTemplateName = template.Name,
                        StartTime = template.StartTime,
                        EndTime = template.EndTime,
                        Required = requirement?.MinimumHeadcount ?? 0,
                        Preferred = requirement?.PreferredHeadcount,
                        Scheduled = templateAssignments.Count,
                        Confirmed = templateAssignments.Count(a => a.Status == "CONFIRMED" || a.Status == "IN_PROGRESS" || a.Status == "COMPLETED"),
                        InProgress = templateAssignments.Count(a => a.Status == "IN_PROGRESS"),
                        Completed = templateAssignments.Count(a => a.Status == "COMPLETED"),
                        Assignments = templateAssignments.Select(a => new ShiftAssignmentInfo
                        {
                            Id = a.Id,
                            EmployeeId = a.EmployeeId,
                            EmployeeName = "Employee", // Will be populated via Include if needed
                            Status = a.Status
                        }).ToList()
                    };

                    // Determine status
                    if (shiftCoverage.Scheduled >= shiftCoverage.Required)
                    {
                        shiftCoverage.Status = "COVERED";
                    }
                    else if (shiftCoverage.Scheduled > 0)
                    {
                        shiftCoverage.Status = "UNDERSTAFFED";
                    }
                    else
                    {
                        shiftCoverage.Status = "UNSTAFFED";
                    }

                    dailyCoverage.Shifts.Add(shiftCoverage);
                }

                analysis.Coverage.Add(dailyCoverage);
            }

            // Calculate summary
            var allShifts = analysis.Coverage.SelectMany(c => c.Shifts).ToList();
            analysis.Summary = new CoverageSummary
            {
                TotalShifts = allShifts.Count,
                CoveredShifts = allShifts.Count(s => s.Status == "COVERED"),
                UnderstaffedShifts = allShifts.Count(s => s.Status == "UNDERSTAFFED"),
                UnstaffedShifts = allShifts.Count(s => s.Status == "UNSTAFFED"),
                CoveragePercentage = allShifts.Count > 0
                    ? (double)allShifts.Count(s => s.Status == "COVERED") / allShifts.Count * 100
                    : 0
            };

            return analysis;
        }

        public async Task<IEnumerable<ShiftCoverageRequirement>> GetRequirementsAsync(Guid? siteId = null, Guid? laneId = null, bool activeOnly = true)
        {
            if (laneId.HasValue)
            {
                return await _coverageRepository.GetByLaneIdAsync(laneId.Value, activeOnly);
            }
            else if (siteId.HasValue)
            {
                return await _coverageRepository.GetBySiteIdAsync(siteId.Value, activeOnly);
            }
            else
            {
                return await _coverageRepository.GetAllAsync();
            }
        }

        public async Task<ShiftCoverageRequirement> CreateRequirementAsync(ShiftCoverageRequirement requirement)
        {
            requirement.Id = Guid.NewGuid();
            requirement.CreatedAt = DateTime.UtcNow;

            return await _coverageRepository.AddAsync(requirement);
        }

        public async Task<ShiftCoverageRequirement> UpdateRequirementAsync(ShiftCoverageRequirement requirement)
        {
            var existing = await _coverageRepository.GetByIdAsync(requirement.Id);
            if (existing == null)
            {
                throw new InvalidOperationException($"Coverage requirement with ID '{requirement.Id}' not found.");
            }

            requirement.UpdatedAt = DateTime.UtcNow;
            await _coverageRepository.UpdateAsync(requirement);
            return requirement;
        }

        public async Task<bool> DeleteRequirementAsync(Guid id)
        {
            var requirement = await _coverageRepository.GetByIdAsync(id);
            if (requirement == null)
            {
                return false;
            }

            // Soft delete by setting IsActive to false
            requirement.IsActive = false;
            requirement.UpdatedAt = DateTime.UtcNow;

            await _coverageRepository.UpdateAsync(requirement);
            return true;
        }
    }
}

