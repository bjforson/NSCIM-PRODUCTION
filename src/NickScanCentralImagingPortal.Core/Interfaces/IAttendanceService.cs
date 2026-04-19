using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IAttendanceService
    {
        Task<AttendanceRecord?> GetByIdAsync(Guid id);
        Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateTime date);
        Task<IEnumerable<AttendanceRecord>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<IEnumerable<AttendanceRecord>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null);
        Task<AttendanceRecord> CheckInAsync(Guid? shiftAssignmentId, Guid employeeId, Guid siteId, DateTime date, DateTime checkInTime, string source = "MANUAL");
        Task<AttendanceRecord> CheckOutAsync(Guid attendanceRecordId, DateTime checkOutTime, string source = "MANUAL");
        Task<AttendanceRecord> CreateOrUpdateAsync(AttendanceRecord record);
        Task<AttendanceRecord> UpdateAsync(AttendanceRecord record);
        Task CalculateAttendanceMetricsAsync(AttendanceRecord record);
    }
}

