using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IShiftAssignmentRepository : IRepositoryGuid<ShiftAssignment>
    {
        Task<IEnumerable<ShiftAssignment>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<ShiftAssignment>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<ShiftAssignment>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null);
        Task<IEnumerable<ShiftAssignment>> GetByStatusAsync(string status, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<bool> HasConflictAsync(Guid employeeId, Guid siteId, DateTime date, Guid shiftTemplateId, Guid? excludeId = null);
        Task<IEnumerable<ShiftAssignment>> GetByLaneIdAsync(Guid laneId, DateTime? dateFrom = null, DateTime? dateTo = null);
    }
}

