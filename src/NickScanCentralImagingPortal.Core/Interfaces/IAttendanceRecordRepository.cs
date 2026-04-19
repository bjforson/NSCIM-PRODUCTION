using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IAttendanceRecordRepository : IRepositoryGuid<AttendanceRecord>
    {
        Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateTime date);
        Task<IEnumerable<AttendanceRecord>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<AttendanceRecord>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<AttendanceRecord>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null);
        Task<IEnumerable<AttendanceRecord>> GetByStatusAsync(string status, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<AttendanceRecord?> GetByShiftAssignmentIdAsync(Guid shiftAssignmentId);
    }
}

