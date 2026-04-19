using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class AttendanceRecordRepository : RepositoryGuid<AttendanceRecord>, IAttendanceRecordRepository
    {
        private readonly ILogger<AttendanceRecordRepository> _logger;

        public AttendanceRecordRepository(ApplicationDbContext context, ILogger<AttendanceRecordRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public async Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateTime date)
        {
            return await _dbSet
                .FirstOrDefaultAsync(ar => ar.EmployeeId == employeeId && ar.Date == date.Date);
        }

        public async Task<IEnumerable<AttendanceRecord>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(ar => ar.EmployeeId == employeeId);

            if (dateFrom.HasValue)
                query = query.Where(ar => ar.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(ar => ar.Date <= dateTo.Value.Date);

            return await query
                .OrderByDescending(ar => ar.Date)
                .ToListAsync();
        }

        public async Task<IEnumerable<AttendanceRecord>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(ar => ar.SiteId == siteId);

            if (dateFrom.HasValue)
                query = query.Where(ar => ar.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(ar => ar.Date <= dateTo.Value.Date);

            return await query
                .OrderByDescending(ar => ar.Date)
                .ToListAsync();
        }

        public async Task<IEnumerable<AttendanceRecord>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null)
        {
            var query = _dbSet
                .Where(ar => ar.Date >= dateFrom.Date && ar.Date <= dateTo.Date);

            if (siteId.HasValue)
                query = query.Where(ar => ar.SiteId == siteId.Value);

            return await query
                .OrderByDescending(ar => ar.Date)
                .ToListAsync();
        }

        public async Task<IEnumerable<AttendanceRecord>> GetByStatusAsync(string status, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(ar => ar.Status == status);

            if (dateFrom.HasValue)
                query = query.Where(ar => ar.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(ar => ar.Date <= dateTo.Value.Date);

            return await query
                .OrderByDescending(ar => ar.Date)
                .ToListAsync();
        }

        public async Task<AttendanceRecord?> GetByShiftAssignmentIdAsync(Guid shiftAssignmentId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(ar => ar.ShiftAssignmentId == shiftAssignmentId);
        }
    }
}

