using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftAssignmentService
    {
        Task<ShiftAssignment?> GetByIdAsync(Guid id);
        Task<IEnumerable<ShiftAssignment>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<ShiftAssignment>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<ShiftAssignment>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null);
        Task<ShiftAssignment> CreateAsync(ShiftAssignment assignment);
        Task<BulkAssignmentResult> CreateBulkAsync(IEnumerable<ShiftAssignment> assignments, bool validateConflicts = true);
        Task<ShiftAssignment> UpdateAsync(ShiftAssignment assignment);
        Task<bool> UpdateStatusAsync(Guid id, string status, string? notes = null);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> HasConflictAsync(Guid employeeId, Guid siteId, DateTime date, Guid shiftTemplateId, Guid? excludeId = null);
    }

    public class BulkAssignmentResult
    {
        public int Created { get; set; }
        public int Failed { get; set; }
        public List<BulkAssignmentError> Errors { get; set; } = new();
        public List<ShiftAssignment> Assignments { get; set; } = new();
    }

    public class BulkAssignmentError
    {
        public int Index { get; set; }
        public ShiftAssignment? Assignment { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}

