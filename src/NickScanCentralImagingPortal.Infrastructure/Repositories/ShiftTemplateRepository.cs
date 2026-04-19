using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ShiftTemplateRepository : RepositoryGuid<ShiftTemplate>, IShiftTemplateRepository
    {
        private readonly ILogger<ShiftTemplateRepository> _logger;

        public ShiftTemplateRepository(ApplicationDbContext context, ILogger<ShiftTemplateRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public async Task<ShiftTemplate?> GetByCodeAsync(string code)
        {
            return await _dbSet
                .FirstOrDefaultAsync(st => st.Code == code);
        }

        public async Task<IEnumerable<ShiftTemplate>> GetBySiteIdAsync(Guid? siteId)
        {
            var query = _dbSet.AsQueryable();

            if (siteId.HasValue)
            {
                query = query.Where(st => st.SiteId == siteId);
            }
            else
            {
                query = query.Where(st => st.SiteId == null);
            }

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<ShiftTemplate>> GetActiveTemplatesAsync()
        {
            return await _dbSet
                .Where(st => st.Status == "ACTIVE")
                .ToListAsync();
        }
    }
}

