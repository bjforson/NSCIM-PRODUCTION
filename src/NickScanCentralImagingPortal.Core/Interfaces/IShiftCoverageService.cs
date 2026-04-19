using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftCoverageService
    {
        Task<CoverageAnalysis> GetCoverageAnalysisAsync(Guid siteId, DateTime dateFrom, DateTime dateTo, Guid? shiftTemplateId = null);
        Task<IEnumerable<ShiftCoverageRequirement>> GetRequirementsAsync(Guid? siteId = null, Guid? laneId = null, bool activeOnly = true);
        Task<ShiftCoverageRequirement> CreateRequirementAsync(ShiftCoverageRequirement requirement);
        Task<ShiftCoverageRequirement> UpdateRequirementAsync(ShiftCoverageRequirement requirement);
        Task<bool> DeleteRequirementAsync(Guid id);
    }

    public class CoverageAnalysis
    {
        public Guid SiteId { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public List<DailyCoverage> Coverage { get; set; } = new();
        public CoverageSummary Summary { get; set; } = new();
    }

    public class DailyCoverage
    {
        public DateTime Date { get; set; }
        public List<ShiftCoverage> Shifts { get; set; } = new();
    }

    public class ShiftCoverage
    {
        public Guid ShiftTemplateId { get; set; }
        public string ShiftTemplateName { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int Required { get; set; }
        public int? Preferred { get; set; }
        public int Scheduled { get; set; }
        public int Confirmed { get; set; }
        public int InProgress { get; set; }
        public int Completed { get; set; }
        public string Status { get; set; } = string.Empty; // COVERED, UNDERSTAFFED, UNSTAFFED
        public List<ShiftAssignmentInfo> Assignments { get; set; } = new();
    }

    public class ShiftAssignmentInfo
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class CoverageSummary
    {
        public int TotalShifts { get; set; }
        public int CoveredShifts { get; set; }
        public int UnderstaffedShifts { get; set; }
        public int UnstaffedShifts { get; set; }
        public double CoveragePercentage { get; set; }
    }
}

