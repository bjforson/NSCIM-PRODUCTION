using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface ILeaveRequestRepository : IRepositoryGuid<LeaveRequest>
    {
        Task<IEnumerable<LeaveRequest>> GetByEmployeeIdAsync(Guid employeeId, string? status = null);
        Task<IEnumerable<LeaveRequest>> GetByStatusAsync(string status);
        Task<IEnumerable<LeaveRequest>> GetOverlappingRequestsAsync(DateTime startDate, DateTime endDate, Guid? employeeId = null);
        Task<bool> HasOverlappingLeaveAsync(Guid employeeId, DateTime startDate, DateTime endDate, Guid? excludeId = null);
    }
}

