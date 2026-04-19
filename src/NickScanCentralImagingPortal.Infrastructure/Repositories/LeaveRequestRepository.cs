using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class LeaveRequestRepository : RepositoryGuid<LeaveRequest>, ILeaveRequestRepository
    {
        private readonly ILogger<LeaveRequestRepository> _logger;

        public LeaveRequestRepository(ApplicationDbContext context, ILogger<LeaveRequestRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public async Task<IEnumerable<LeaveRequest>> GetByEmployeeIdAsync(Guid employeeId, string? status = null)
        {
            var query = _dbSet
                .Where(lr => lr.EmployeeId == employeeId);

            if (!string.IsNullOrEmpty(status))
                query = query.Where(lr => lr.Status == status);

            return await query
                .OrderByDescending(lr => lr.StartDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<LeaveRequest>> GetByStatusAsync(string status)
        {
            return await _dbSet
                .Where(lr => lr.Status == status)
                .OrderByDescending(lr => lr.StartDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<LeaveRequest>> GetOverlappingRequestsAsync(DateTime startDate, DateTime endDate, Guid? employeeId = null)
        {
            var query = _dbSet
                .Where(lr => lr.Status == "PENDING" || lr.Status == "APPROVED")
                .Where(lr => (lr.StartDate <= endDate && lr.EndDate >= startDate));

            if (employeeId.HasValue)
                query = query.Where(lr => lr.EmployeeId == employeeId.Value);

            return await query.ToListAsync();
        }

        public async Task<bool> HasOverlappingLeaveAsync(Guid employeeId, DateTime startDate, DateTime endDate, Guid? excludeId = null)
        {
            var query = _dbSet
                .Where(lr => lr.EmployeeId == employeeId
                    && (lr.Status == "PENDING" || lr.Status == "APPROVED")
                    && (lr.StartDate <= endDate && lr.EndDate >= startDate));

            if (excludeId.HasValue)
                query = query.Where(lr => lr.Id != excludeId.Value);

            return await query.AnyAsync();
        }
    }
}

