using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ProcessingResultRepository : Repository<ProcessingResult>, IProcessingResultRepository
    {
        public ProcessingResultRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<ProcessingResult>> GetByContainerIdAsync(int containerId)
        {
            return await _dbSet
                .Where(pr => pr.ContainerId == containerId)
                .ToListAsync();
        }

        public async Task<IEnumerable<ProcessingResult>> GetByResultTypeAsync(string resultType)
        {
            return await _dbSet
                .Where(pr => pr.ResultType == resultType)
                .ToListAsync();
        }
    }
}
