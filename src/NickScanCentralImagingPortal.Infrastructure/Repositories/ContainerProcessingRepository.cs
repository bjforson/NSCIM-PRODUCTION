using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.ContainerProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ContainerProcessingRepository : IContainerProcessingRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IcumDownloadsDbContext _icumContext;
        private readonly ILogger<ContainerProcessingRepository> _logger;

        public ContainerProcessingRepository(
            ApplicationDbContext context,
            IcumDownloadsDbContext icumContext,
            ILogger<ContainerProcessingRepository> logger)
        {
            _context = context;
            _icumContext = icumContext;
            _logger = logger;
        }

        public async Task<List<ContainerGroupDto>> GetContainerGroupsAsync(string? clearanceTypeFilter = null, int page = 1, int pageSize = 50)
        {
            try
            {
                _logger.LogInformation("Getting container groups - Filter: {Filter}, Page: {Page}, PageSize: {PageSize}",
                    clearanceTypeFilter, page, pageSize);

                // ✅ FIX: Filter out invalid/placeholder container numbers at database level
                // Get all containers with completeness status (show ALL, not just complete)
                var completenessData = await _context.ContainerCompletenessStatuses
                    .Where(c => !string.IsNullOrEmpty(c.ContainerNumber) &&
                               c.ContainerNumber.Length >= 8 &&
                               !c.ContainerNumber.Contains(" "))
                    .Select(c => new
                    {
                        c.ContainerNumber,
                        c.ScannerType,
                        c.ScanDate,
                        c.HasICUMSData,
                        c.Status
                    })
                    .ToListAsync();

                // ✅ FIX: Filter invalid container numbers in memory (char.IsLetter() cannot be translated)
                var invalidContainerNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "XXXX", "SSSS", "Unknown", "PLACEHOLDER", "CONTAINER" };
                completenessData = completenessData
                    .Where(c => c.ContainerNumber.Length >= 4 &&
                               !invalidContainerNumbers.Contains(c.ContainerNumber) &&
                               char.IsLetter(c.ContainerNumber[0]) &&
                               char.IsLetter(c.ContainerNumber[1]) &&
                               char.IsLetter(c.ContainerNumber[2]) &&
                               char.IsLetter(c.ContainerNumber[3]))
                    .ToList();

                _logger.LogInformation("Found {Count} containers", completenessData.Count);

                // Get ICUMS data for these containers (in batches to avoid timeout)
                var containerNumbers = completenessData.Select(c => c.ContainerNumber).ToList();
                var batchSize = 1000;
                var icumsData = new List<dynamic>();

                for (int i = 0; i < containerNumbers.Count; i += batchSize)
                {
                    var batch = containerNumbers.Skip(i).Take(batchSize).ToList();
                    // Round-1 audit C-2: was FromSql with manual single-quote escaping.
                    // Parameterized LINQ Contains() — Npgsql emits ContainerNumber = ANY(@p)
                    // and the values are bound. Eliminates the manual-escape risk if
                    // `batch` ever sources from user input.
                    var batchEntities = await _icumContext.BOEDocuments
                        .Where(b => batch.Contains(b.ContainerNumber))
                        .AsNoTracking()
                        .ToListAsync();

                    // ✅ Project in memory after loading to avoid CTE generation
                    var batchData = batchEntities.Select(b => new
                    {
                        ContainerNumber = b.ContainerNumber,
                        BlNumber = b.BlNumber,
                        DeclarationNumber = b.DeclarationNumber,
                        RotationNumber = b.RotationNumber,
                        ClearanceType = b.ClearanceType
                    }).ToList();

                    icumsData.AddRange(batchData);
                }

                _logger.LogInformation("Retrieved ICUMS data for {Count} containers", icumsData.Count);

                // Combine data and build container items
                var containerItems = (from comp in completenessData
                                      join icum in icumsData on comp.ContainerNumber equals icum.ContainerNumber into icumGroup
                                      from icum in icumGroup.DefaultIfEmpty()
                                      select new ContainerProcessingItemDto
                                      {
                                          ContainerNumber = comp.ContainerNumber,
                                          BlNumber = icum?.BlNumber,
                                          BoeNumber = icum?.DeclarationNumber,
                                          RotationNumber = icum?.RotationNumber,
                                          ScannerType = comp.ScannerType ?? "Unknown",
                                          ClearanceType = icum?.ClearanceType ?? "Unknown",
                                          HasScannerData = !string.IsNullOrEmpty(comp.ScannerType),
                                          HasICUMSData = comp.HasICUMSData,
                                          HasImages = comp.Status == "Complete", // Complete status implies images exist
                                          HasBOE = !string.IsNullOrEmpty(icum?.DeclarationNumber),
                                          ScanDate = comp.ScanDate,
                                          CompletenessScore = CalculateCompletenessScore(
                                              !string.IsNullOrEmpty(comp.ScannerType),
                                              comp.HasICUMSData,
                                              comp.Status == "Complete",
                                              !string.IsNullOrEmpty(icum?.DeclarationNumber)),
                                          Status = comp.Status
                                      }).ToList();

                // Apply clearance type filter if provided
                if (!string.IsNullOrEmpty(clearanceTypeFilter) && clearanceTypeFilter != "All")
                {
                    containerItems = containerItems.Where(c => c.ClearanceType == clearanceTypeFilter).ToList();
                }

                // Group by clearance type logic
                var groups = new List<ContainerGroupDto>();

                // Group IM/EX by BOE Number
                var imExContainers = containerItems.Where(c => c.ClearanceType == "IM" || c.ClearanceType == "EX").ToList();
                var imExGroups = imExContainers
                    .Where(c => !string.IsNullOrEmpty(c.BoeNumber))
                    .GroupBy(c => new { c.ClearanceType, c.BoeNumber })
                    .Select(g => new ContainerGroupDto
                    {
                        ClearanceType = g.Key.ClearanceType!,
                        GroupingKey = "BOE",
                        GroupingValue = g.Key.BoeNumber!,
                        TotalContainers = g.Count(),
                        CompleteContainers = g.Count(c => c.Status == "Complete"),
                        Containers = g.OrderBy(c => c.ContainerNumber).ToList(),
                        LatestScanDate = g.Max(c => c.ScanDate),
                        PrimaryScannerType = g.GroupBy(c => c.ScannerType).OrderByDescending(sg => sg.Count()).First().Key
                    });

                groups.AddRange(imExGroups);

                // Group CMR by BL Number
                var cmrContainers = containerItems.Where(c => c.ClearanceType == "CMR").ToList();
                var cmrGroups = cmrContainers
                    .Where(c => !string.IsNullOrEmpty(c.BlNumber))
                    .GroupBy(c => c.BlNumber)
                    .Select(g => new ContainerGroupDto
                    {
                        ClearanceType = "CMR",
                        GroupingKey = "BL",
                        GroupingValue = g.Key!,
                        TotalContainers = g.Count(),
                        CompleteContainers = g.Count(c => c.Status == "Complete"),
                        Containers = g.OrderBy(c => c.ContainerNumber).ToList(),
                        LatestScanDate = g.Max(c => c.ScanDate),
                        PrimaryScannerType = g.GroupBy(c => c.ScannerType).OrderByDescending(sg => sg.Count()).First().Key
                    });

                groups.AddRange(cmrGroups);

                // Apply pagination
                var totalGroups = groups.Count;
                var paginatedGroups = groups
                    .OrderBy(g => g.ClearanceType)
                    .ThenBy(g => g.GroupingValue)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                _logger.LogInformation("Returning {Count} groups (page {Page} of {TotalPages})",
                    paginatedGroups.Count, page, (int)Math.Ceiling(totalGroups / (double)pageSize));

                return paginatedGroups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting container groups");
                throw;
            }
        }

        public async Task<ContainerProcessingSummaryDto> GetSummaryStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Getting container processing summary statistics - OPTIMIZED VERSION");

                // ✅ PERFORMANCE OPTIMIZATION: Use aggregation queries instead of loading all data
                var totalContainers = await _context.ContainerBOERelations
                    .Where(r => r.IsActive)
                    .CountAsync();

                // Count by scanner type
                var fs6000Count = await _context.ContainerBOERelations
                    .Where(r => r.IsActive && r.ScannerType == "FS6000")
                    .CountAsync();

                var aseCount = await _context.ContainerBOERelations
                    .Where(r => r.IsActive && r.ScannerType == "ASE")
                    .CountAsync();

                var heimannCount = await _context.ContainerBOERelations
                    .Where(r => r.IsActive && r.ScannerType == "Heimann")
                    .CountAsync();

                // ✅ PERFORMANCE FIX: Calculate group counts directly from database without loading all groups
                // This avoids loading 30,000+ containers into memory which causes timeouts
                // Get distinct group identifiers (BOE numbers for IM/EX, BL numbers for CMR) from BOEDocuments
                var imExGroups = await _icumContext.BOEDocuments
                    .Where(b => !string.IsNullOrEmpty(b.DeclarationNumber) &&
                               (b.ClearanceType == "IM" || b.ClearanceType == "EX"))
                    .Select(b => b.DeclarationNumber)
                    .Distinct()
                    .CountAsync();

                var cmrGroups = await _icumContext.BOEDocuments
                    .Where(b => !string.IsNullOrEmpty(b.BlNumber) &&
                               b.ClearanceType == "CMR")
                    .Select(b => b.BlNumber)
                    .Distinct()
                    .CountAsync();

                var totalGroups = imExGroups + cmrGroups;

                // ✅ PERFORMANCE FIX: Count complete containers directly from database
                var completeContainers = await _context.ContainerCompletenessStatuses
                    .Where(c => c.Status == "Complete" &&
                               !string.IsNullOrEmpty(c.ContainerNumber) &&
                               c.ContainerNumber.Length >= 8 &&
                               !c.ContainerNumber.Contains(" "))
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                var summary = new ContainerProcessingSummaryDto
                {
                    TotalContainers = totalContainers,
                    TotalGroups = totalGroups,
                    IMGroups = await _icumContext.BOEDocuments
                        .Where(b => !string.IsNullOrEmpty(b.DeclarationNumber) && b.ClearanceType == "IM")
                        .Select(b => b.DeclarationNumber)
                        .Distinct()
                        .CountAsync(),
                    EXGroups = await _icumContext.BOEDocuments
                        .Where(b => !string.IsNullOrEmpty(b.DeclarationNumber) && b.ClearanceType == "EX")
                        .Select(b => b.DeclarationNumber)
                        .Distinct()
                        .CountAsync(),
                    CMRGroups = cmrGroups,
                    FS6000Containers = fs6000Count,
                    ASEContainers = aseCount,
                    HeimannContainers = heimannCount,
                    CompleteContainers = completeContainers,
                    IncompleteContainers = 0
                };

                summary.IncompleteContainers = summary.TotalContainers - summary.CompleteContainers;
                summary.CompletionRate = summary.TotalContainers > 0
                    ? (double)summary.CompleteContainers / summary.TotalContainers * 100
                    : 0;

                _logger.LogInformation("Summary calculated - Total: {Total}, Groups: {Groups}, IM: {IM}, EX: {EX}, CMR: {CMR}",
                    summary.TotalContainers, summary.TotalGroups, summary.IMGroups, summary.EXGroups, summary.CMRGroups);

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting summary statistics");
                throw;
            }
        }

        public async Task<ContainerGroupDto?> GetContainerGroupDetailsAsync(string clearanceType, string groupingValue)
        {
            try
            {
                _logger.LogInformation("Getting container group details - Type: {Type}, Value: {Value}",
                    clearanceType, groupingValue);

                var allGroups = await GetContainerGroupsAsync(clearanceType, page: 1, pageSize: int.MaxValue);

                var group = allGroups.FirstOrDefault(g =>
                    g.ClearanceType == clearanceType && g.GroupingValue == groupingValue);

                return group;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting container group details");
                throw;
            }
        }

        private int CalculateCompletenessScore(bool hasScanner, bool hasICUMS, bool hasImages, bool hasBOE)
        {
            int score = 0;
            if (hasScanner) score += 25;
            if (hasICUMS) score += 25;
            if (hasImages) score += 25;
            if (hasBOE) score += 25;
            return score;
        }
    }
}

