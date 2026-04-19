using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for loose cargo data access
    /// </summary>
    public class LooseCargoRepository : ILooseCargoRepository
    {
        private readonly IcumDownloadsDbContext _context;
        private readonly ILogger<LooseCargoRepository> _logger;

        public LooseCargoRepository(
            IcumDownloadsDbContext context,
            ILogger<LooseCargoRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Base filter for loose cargo: records with no container number OR known
        /// loose-cargo identifiers (BULK, LOOSE, NSP-*, ZRR-*, CDM-*).
        /// </summary>
        private static IQueryable<BOEDocument> ApplyLooseCargoFilter(IQueryable<BOEDocument> query)
        {
            return query.Where(b =>
                b.ContainerNumber == null ||
                b.ContainerNumber == "" ||
                b.ContainerNumber == "N/A" ||
                b.ContainerNumber == "BULK" ||
                b.ContainerNumber == "LOOSE" ||
                b.ContainerNumber.StartsWith("NSP") ||
                b.ContainerNumber.StartsWith("ZRR") ||
                b.ContainerNumber.StartsWith("CDM"));
        }

        public async Task<(List<BOEDocument> records, int totalCount)> GetLooseCargoRecordsAsync(
            string? clearanceType = null,
            string? crmsLevel = null,
            string? searchTerm = null,
            int pageNumber = 1,
            int pageSize = 100,
            string? sortBy = null,
            bool sortDescending = false)
        {
            try
            {
                // Base query for loose cargo (no container number or known loose-cargo identifiers)
                var query = ApplyLooseCargoFilter(_context.BOEDocuments).AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(clearanceType))
                {
                    query = query.Where(b => b.ClearanceType == clearanceType);
                }

                if (!string.IsNullOrEmpty(crmsLevel))
                {
                    query = query.Where(b => b.CrmsLevel == crmsLevel);
                }

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    var searchLower = searchTerm.ToLower();
                    query = query.Where(b =>
                        (b.DeclarationNumber != null && b.DeclarationNumber.ToLower().Contains(searchLower)) ||
                        (b.BlNumber != null && b.BlNumber.ToLower().Contains(searchLower)) ||
                        (b.ConsigneeName != null && b.ConsigneeName.ToLower().Contains(searchLower)) ||
                        (b.ShipperName != null && b.ShipperName.ToLower().Contains(searchLower)) ||
                        (b.GoodsDescription != null && b.GoodsDescription.ToLower().Contains(searchLower)) ||
                        (b.HouseBl != null && b.HouseBl.ToLower().Contains(searchLower)));
                }

                // Get total count before pagination
                var totalCount = await query.CountAsync();

                // Apply sorting
                query = ApplySorting(query, sortBy, sortDescending);

                // Apply pagination
                var records = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} loose cargo records (page {Page}, total {Total})",
                    records.Count, pageNumber, totalCount);

                return (records, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving loose cargo records");
                throw;
            }
        }

        public async Task<LooseCargoStatistics> GetStatisticsAsync()
        {
            try
            {
                var baseQuery = ApplyLooseCargoFilter(_context.BOEDocuments);

                var stats = new LooseCargoStatistics
                {
                    TotalRecords = await baseQuery.CountAsync(),
                    Imports = await baseQuery.CountAsync(b => b.ClearanceType == "IM"),
                    Exports = await baseQuery.CountAsync(b => b.ClearanceType == "EX"),
                    Transit = await baseQuery.CountAsync(b => b.ClearanceType == "CMR"),
                    HighRisk = await baseQuery.CountAsync(b => b.CrmsLevel == "Red"),
                    MediumRisk = await baseQuery.CountAsync(b => b.CrmsLevel == "Yellow"),
                    LowRisk = await baseQuery.CountAsync(b => b.CrmsLevel == "Green"),
                    RecentRecords = await baseQuery.CountAsync(b =>
                        b.CreatedAt >= DateTime.UtcNow.AddDays(-7)),
                    TotalDutyPaid = await baseQuery.SumAsync(b => b.TotalDutyPaid ?? 0),
                    OldestRecord = await baseQuery.MinAsync(b => (DateTime?)b.CreatedAt),
                    NewestRecord = await baseQuery.MaxAsync(b => (DateTime?)b.CreatedAt)
                };

                _logger.LogInformation("Retrieved loose cargo statistics: {Stats}", stats);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving loose cargo statistics");
                throw;
            }
        }

        public async Task<BOEDocument?> GetByIdAsync(int id)
        {
            try
            {
                return await ApplyLooseCargoFilter(_context.BOEDocuments)
                    .FirstOrDefaultAsync(b => b.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving loose cargo by ID: {Id}", id);
                throw;
            }
        }

        public async Task<BOEDocument?> GetByDeclarationNumberAsync(string declarationNumber)
        {
            try
            {
                return await ApplyLooseCargoFilter(_context.BOEDocuments)
                    .FirstOrDefaultAsync(b => b.DeclarationNumber == declarationNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving loose cargo by declaration number: {DeclarationNumber}",
                    declarationNumber);
                throw;
            }
        }

        public async Task<List<DownloadedManifestItem>> GetManifestItemsAsync(int boeDocumentId)
        {
            try
            {
                return await _context.ManifestItems
                    .Where(m => m.BOEDocumentId == boeDocumentId)
                    .OrderBy(m => m.ItemNo)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving manifest items for BOE document: {Id}", boeDocumentId);
                throw;
            }
        }

        public async Task<List<BOEDocument>> GetRecentRecordsAsync(int days = 7)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-days);
                return await ApplyLooseCargoFilter(_context.BOEDocuments)
                    .Where(b => b.CreatedAt >= cutoffDate)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(100)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent loose cargo records");
                throw;
            }
        }

        public async Task<bool> ExistsByDeclarationNumberAsync(string declarationNumber)
        {
            try
            {
                return await ApplyLooseCargoFilter(_context.BOEDocuments)
                    .AnyAsync(b => b.DeclarationNumber == declarationNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of declaration number: {DeclarationNumber}",
                    declarationNumber);
                throw;
            }
        }

        /// <summary>
        /// Apply sorting to query
        /// </summary>
        private IQueryable<BOEDocument> ApplySorting(
            IQueryable<BOEDocument> query,
            string? sortBy,
            bool sortDescending)
        {
            if (string.IsNullOrEmpty(sortBy))
            {
                // Default sort by CreatedAt descending
                return query.OrderByDescending(b => b.CreatedAt);
            }

            return sortBy.ToLower() switch
            {
                "declarationnumber" => sortDescending
                    ? query.OrderByDescending(b => b.DeclarationNumber)
                    : query.OrderBy(b => b.DeclarationNumber),
                "clearancetype" => sortDescending
                    ? query.OrderByDescending(b => b.ClearanceType)
                    : query.OrderBy(b => b.ClearanceType),
                "crmslevel" => sortDescending
                    ? query.OrderByDescending(b => b.CrmsLevel)
                    : query.OrderBy(b => b.CrmsLevel),
                "declarationdate" => sortDescending
                    ? query.OrderByDescending(b => b.DeclarationDate)
                    : query.OrderBy(b => b.DeclarationDate),
                "createdat" => sortDescending
                    ? query.OrderByDescending(b => b.CreatedAt)
                    : query.OrderBy(b => b.CreatedAt),
                "totaldutypaid" => sortDescending
                    ? query.OrderByDescending(b => b.TotalDutyPaid)
                    : query.OrderBy(b => b.TotalDutyPaid),
                _ => query.OrderByDescending(b => b.CreatedAt)
            };
        }
    }
}

