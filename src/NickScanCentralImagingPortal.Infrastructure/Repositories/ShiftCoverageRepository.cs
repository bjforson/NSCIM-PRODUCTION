using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ShiftCoverageRepository : RepositoryGuid<ShiftCoverageRequirement>, IShiftCoverageRepository
    {
        private readonly ILogger<ShiftCoverageRepository> _logger;

        public ShiftCoverageRepository(ApplicationDbContext context, ILogger<ShiftCoverageRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public async Task<IEnumerable<ShiftCoverageRequirement>> GetBySiteIdAsync(Guid siteId, bool activeOnly = true)
        {
            var query = _dbSet
                .Where(scr => scr.SiteId == siteId);

            if (activeOnly)
            {
                query = query.Where(scr => scr.IsActive
                    && scr.EffectiveFrom <= DateTime.UtcNow.Date
                    && (scr.EffectiveTo == null || scr.EffectiveTo >= DateTime.UtcNow.Date));
            }

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<ShiftCoverageRequirement>> GetByLaneIdAsync(Guid laneId, bool activeOnly = true)
        {
            var query = _dbSet
                .Where(scr => scr.LaneId == laneId);

            if (activeOnly)
            {
                query = query.Where(scr => scr.IsActive
                    && scr.EffectiveFrom <= DateTime.UtcNow.Date
                    && (scr.EffectiveTo == null || scr.EffectiveTo >= DateTime.UtcNow.Date));
            }

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<ShiftCoverageRequirement>> GetByShiftTemplateIdAsync(Guid shiftTemplateId, bool activeOnly = true)
        {
            var query = _dbSet
                .Where(scr => scr.ShiftTemplateId == shiftTemplateId);

            if (activeOnly)
            {
                query = query.Where(scr => scr.IsActive
                    && scr.EffectiveFrom <= DateTime.UtcNow.Date
                    && (scr.EffectiveTo == null || scr.EffectiveTo >= DateTime.UtcNow.Date));
            }

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<ShiftCoverageRequirement>> GetActiveRequirementsAsync(DateTime date, Guid? siteId = null)
        {
            var query = _dbSet
                .Where(scr => scr.IsActive
                    && scr.EffectiveFrom <= date.Date
                    && (scr.EffectiveTo == null || scr.EffectiveTo >= date.Date));

            if (siteId.HasValue)
                query = query.Where(scr => scr.SiteId == siteId.Value);

            return await query.ToListAsync();
        }
    }
}

