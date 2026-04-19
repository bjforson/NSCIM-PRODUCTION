using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ShiftAssignmentRepository : RepositoryGuid<ShiftAssignment>, IShiftAssignmentRepository
    {
        private readonly ILogger<ShiftAssignmentRepository> _logger;

        public ShiftAssignmentRepository(ApplicationDbContext context, ILogger<ShiftAssignmentRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByEmployeeIdAsync(Guid employeeId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(sa => sa.EmployeeId == employeeId);

            if (dateFrom.HasValue)
                query = query.Where(sa => sa.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(sa => sa.Date <= dateTo.Value.Date);

            return await query
                .OrderBy(sa => sa.Date)
                .ThenBy(sa => sa.ShiftTemplate.StartTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<ShiftAssignment>> GetBySiteIdAsync(Guid siteId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(sa => sa.SiteId == siteId);

            if (dateFrom.HasValue)
                query = query.Where(sa => sa.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(sa => sa.Date <= dateTo.Value.Date);

            return await query
                .OrderBy(sa => sa.Date)
                .ThenBy(sa => sa.ShiftTemplate.StartTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByDateRangeAsync(DateTime dateFrom, DateTime dateTo, Guid? siteId = null)
        {
            var query = _dbSet
                .Where(sa => sa.Date >= dateFrom.Date && sa.Date <= dateTo.Date);

            if (siteId.HasValue)
                query = query.Where(sa => sa.SiteId == siteId.Value);

            return await query
                .OrderBy(sa => sa.Date)
                .ThenBy(sa => sa.ShiftTemplate.StartTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByStatusAsync(string status, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(sa => sa.Status == status);

            if (dateFrom.HasValue)
                query = query.Where(sa => sa.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(sa => sa.Date <= dateTo.Value.Date);

            return await query
                .OrderBy(sa => sa.Date)
                .ToListAsync();
        }

        public async Task<bool> HasConflictAsync(Guid employeeId, Guid siteId, DateTime date, Guid shiftTemplateId, Guid? excludeId = null)
        {
            var query = _dbSet
                .Where(sa => sa.EmployeeId == employeeId
                    && sa.SiteId == siteId
                    && sa.Date == date.Date
                    && sa.Status != "CANCELLED"
                    && sa.Status != "NO_SHOW");

            if (excludeId.HasValue)
                query = query.Where(sa => sa.Id != excludeId.Value);

            return await query.AnyAsync();
        }

        public async Task<IEnumerable<ShiftAssignment>> GetByLaneIdAsync(Guid laneId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var query = _dbSet
                .Where(sa => sa.LaneId == laneId);

            if (dateFrom.HasValue)
                query = query.Where(sa => sa.Date >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(sa => sa.Date <= dateTo.Value.Date);

            return await query
                .OrderBy(sa => sa.Date)
                .ToListAsync();
        }
    }
}

