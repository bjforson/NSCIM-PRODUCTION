using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.Utilities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Specialized queries for handling consolidated vs non-consolidated cargo
    /// </summary>
    public class ConsolidatedCargoQueries
    {
        private readonly IcumDownloadsDbContext _context;

        public ConsolidatedCargoQueries(IcumDownloadsDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get non-consolidated cargo grouped by Master BL/Declaration
        /// Returns one record per Master BL with all its containers
        /// </summary>
        public async Task<List<NonConsolidatedCargoGroup>> GetNonConsolidatedCargoGroupsAsync(
            string? clearanceType = null,
            int? limit = null)
        {
            var query = _context.BOEDocuments
                .AsNoTracking()
                .Where(b => !b.IsConsolidated)  // Non-consolidated only
                .Where(b => !string.IsNullOrEmpty(b.DeclarationNumber));

            if (!string.IsNullOrEmpty(clearanceType))
            {
                query = query.Where(b => b.ClearanceType == clearanceType);
            }

            // ✅ MEMORY FIX: Reduce multiplier from 10 to 3 to prevent loading excessive data
            // Most groups have 1-3 containers, so 3x limit should be sufficient while preventing memory bloat
            // If limit is 50, this loads 150 records instead of 500 (70% reduction)
            var allData = limit.HasValue
                ? await query.OrderByDescending(b => b.DeclarationDate).Take(limit.Value * 3).ToListAsync() // Reduced from 10 to 3
                : await query.OrderByDescending(b => b.DeclarationDate).Take(300).ToListAsync(); // ✅ MEMORY FIX: Add hard limit when no limit specified

            // Group and aggregate in memory
            var groups = allData
                .GroupBy(b => new { b.DeclarationNumber, b.BlNumber })
                .Select(g => new
                {
                    g.Key.DeclarationNumber,
                    g.Key.BlNumber,
                    Containers = g.Select(b => b.ContainerNumber).Distinct().ToList(),
                    NoOfContainers = g.First().NoOfContainers,
                    ClearanceType = g.First().ClearanceType,
                    ConsigneeName = g.First().ConsigneeName,
                    RotationNumber = g.First().RotationNumber,
                    GoodsDescription = g.First().GoodsDescription,
                    TotalDutyPaid = g.First().TotalDutyPaid,
                    DeclarationDate = g.First().DeclarationDate,
                    SampleBOEId = g.First().Id
                })
                .OrderByDescending(g => g.DeclarationDate)
                .ToList();

            // Apply limit after grouping
            if (limit.HasValue)
            {
                groups = groups.Take(limit.Value).ToList();
            }

            return groups.Select(g => new NonConsolidatedCargoGroup
            {
                DeclarationNumber = g.DeclarationNumber ?? "",
                MasterBL = g.BlNumber ?? "",
                // ✅ Filter out invalid container numbers (e.g., "DMI15715", "RM1-1101575")
                Containers = ContainerNumberValidator.FilterValidContainerNumbers(g.Containers),
                NoOfContainers = g.NoOfContainers ?? 0,
                ClearanceType = g.ClearanceType ?? "",
                ConsigneeName = g.ConsigneeName ?? "",
                RotationNumber = g.RotationNumber ?? "",
                GoodsDescription = g.GoodsDescription ?? "",
                TotalDutyPaid = g.TotalDutyPaid,
                DeclarationDate = g.DeclarationDate,
                SampleBOEId = g.SampleBOEId
            }).ToList();
        }

        /// <summary>
        /// Get non-consolidated cargo group by specific declaration number
        /// Returns one record for the specified declaration number with all its containers
        /// </summary>
        public async Task<NonConsolidatedCargoGroup?> GetNonConsolidatedCargoGroupByDeclarationAsync(string declarationNumber)
        {
            var query = _context.BOEDocuments
                .AsNoTracking()
                .Where(b => !b.IsConsolidated)  // Non-consolidated only
                .Where(b => b.DeclarationNumber == declarationNumber);

            // ✅ FIX: Use First() instead of FirstOrDefault()! - GroupBy guarantees at least one element per group
            var group = await query
                .GroupBy(b => new { b.DeclarationNumber, b.BlNumber })
                .Select(g => new
                {
                    g.Key.DeclarationNumber,
                    g.Key.BlNumber,
                    Containers = g.Select(b => b.ContainerNumber).Distinct().ToList(),
                    NoOfContainers = g.First().NoOfContainers,
                    ClearanceType = g.First().ClearanceType,
                    ConsigneeName = g.First().ConsigneeName,
                    RotationNumber = g.First().RotationNumber,
                    GoodsDescription = g.First().GoodsDescription,
                    TotalDutyPaid = g.First().TotalDutyPaid,
                    DeclarationDate = g.First().DeclarationDate,
                    SampleBOEId = g.First().Id
                })
                .FirstOrDefaultAsync();

            if (group == null)
            {
                return null;
            }

            return new NonConsolidatedCargoGroup
            {
                DeclarationNumber = group.DeclarationNumber ?? "",
                MasterBL = group.BlNumber ?? "",
                // ✅ Filter out invalid container numbers (e.g., "DMI15715", "RM1-1101575")
                Containers = ContainerNumberValidator.FilterValidContainerNumbers(group.Containers),
                NoOfContainers = group.NoOfContainers ?? 0,
                ClearanceType = group.ClearanceType ?? "",
                ConsigneeName = group.ConsigneeName ?? "",
                RotationNumber = group.RotationNumber ?? "",
                GoodsDescription = group.GoodsDescription ?? "",
                TotalDutyPaid = group.TotalDutyPaid,
                DeclarationDate = group.DeclarationDate,
                SampleBOEId = group.SampleBOEId
            };
        }

        /// <summary>
        /// Get consolidated cargo group by specific Master BL
        /// Returns one record for the specified Master BL with all its House BLs and containers
        /// </summary>
        public async Task<ConsolidatedCargoGroup?> GetConsolidatedCargoGroupByMasterBLAsync(string masterBL)
        {
            var query = _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.IsConsolidated)  // Consolidated only
                .Where(b => b.BlNumber == masterBL); // Filter by Master BL

            var group = await query
                .GroupBy(b => b.BlNumber)
                .Select(g => new
                {
                    MasterBL = g.Key,
                    Containers = g.Select(b => b.ContainerNumber).Distinct().ToList(),
                    HouseBLs = g.Select(b => new
                    {
                        b.HouseBl,
                        b.DeclarationNumber,
                        b.BlNumber,
                        b.ContainerNumber,
                        b.ConsigneeName,
                        b.ClearanceType,
                        b.RotationNumber,
                        b.GoodsDescription,
                        b.TotalDutyPaid,
                        b.DeclarationDate,
                        b.Id
                    }).ToList(),
                    MostRecentDate = g.Max(b => b.CreatedAt)
                })
                .FirstOrDefaultAsync();

            if (group == null)
            {
                return null;
            }

            return new ConsolidatedCargoGroup
            {
                MasterBL = group.MasterBL ?? "",
                // ✅ Filter out invalid container numbers
                ContainerNumbers = ContainerNumberValidator.FilterValidContainerNumbers(group.Containers),
                HouseBLDetails = group.HouseBLs.Select(h => new HouseBLDetail
                {
                    HouseBL = h.HouseBl ?? "",
                    MasterBL = h.BlNumber ?? "",
                    DeclarationNumber = h.DeclarationNumber ?? "",
                    ConsigneeName = h.ConsigneeName ?? "",
                    ClearanceType = h.ClearanceType ?? "",
                    RotationNumber = h.RotationNumber ?? "",
                    GoodsDescription = h.GoodsDescription ?? "",
                    TotalDutyPaid = h.TotalDutyPaid,
                    DeclarationDate = h.DeclarationDate,
                    BOEId = h.Id,
                    ContainerNumber = h.ContainerNumber ?? "",
                    // ✅ Calculate IsComplete: Check if required fields are present
                    IsComplete = !string.IsNullOrEmpty(h.DeclarationNumber ?? "") &&
                                 !string.IsNullOrEmpty(h.HouseBl ?? "") &&
                                 !string.IsNullOrEmpty(h.ConsigneeName ?? "") &&
                                 !string.IsNullOrEmpty(h.ClearanceType ?? "")
                }).ToList()
            };
        }

        /// <summary>
        /// Get consolidated cargo grouped by Master BL
        /// Returns one record per Master BL with all its House BLs and containers
        /// Note: One Master BL can span multiple containers (rare case)
        /// </summary>
        public async Task<List<ConsolidatedCargoGroup>> GetConsolidatedCargoGroupsAsync(
            string? masterBL = null,
            string? containerNumber = null,
            string? clearanceType = null,
            int? limit = null)
        {
            var query = _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.IsConsolidated)  // Consolidated only
                .Where(b => !string.IsNullOrEmpty(b.BlNumber)); // Must have Master BL

            if (!string.IsNullOrEmpty(masterBL))
            {
                query = query.Where(b => b.BlNumber == masterBL);
            }

            if (!string.IsNullOrEmpty(containerNumber))
            {
                query = query.Where(b => b.ContainerNumber == containerNumber);
            }

            if (!string.IsNullOrEmpty(clearanceType))
            {
                query = query.Where(b => b.ClearanceType == clearanceType);
            }

            // ✅ MEMORY FIX: Reduce multiplier from 10 to 3 to prevent loading excessive data
            // Most consolidated groups have 1-3 containers, so 3x limit should be sufficient while preventing memory bloat
            // If limit is 50, this loads 150 records instead of 500 (70% reduction)
            var allData = limit.HasValue
                ? await query.OrderByDescending(b => b.CreatedAt).Take(limit.Value * 3).ToListAsync() // Reduced from 10 to 3
                : await query.OrderByDescending(b => b.CreatedAt).Take(300).ToListAsync(); // ✅ MEMORY FIX: Add hard limit when no limit specified

            // Group and aggregate in memory
            var groups = allData
                .GroupBy(b => b.BlNumber)
                .Select(g => new
                {
                    MasterBL = g.Key,
                    Containers = g.Select(b => b.ContainerNumber).Distinct().ToList(),
                    HouseBLs = g.Select(b => new
                    {
                        b.HouseBl,
                        b.DeclarationNumber,
                        b.BlNumber,
                        b.ContainerNumber,
                        b.ConsigneeName,
                        b.ClearanceType,
                        b.RotationNumber,
                        b.GoodsDescription,
                        b.TotalDutyPaid,
                        b.DeclarationDate,
                        b.Id
                    }).ToList(),
                    MostRecentDate = g.Max(b => b.CreatedAt)
                })
                .OrderByDescending(g => g.MostRecentDate)
                .ToList();

            // Apply limit after grouping
            if (limit.HasValue)
            {
                groups = groups.Take(limit.Value).ToList();
            }

            return groups.Select(g => new ConsolidatedCargoGroup
            {
                MasterBL = g.MasterBL ?? "",
                // ✅ Filter out invalid container numbers
                ContainerNumbers = ContainerNumberValidator.FilterValidContainerNumbers(g.Containers),
                HouseBLDetails = g.HouseBLs.Select(h => new HouseBLDetail
                {
                    HouseBL = h.HouseBl ?? "",
                    MasterBL = h.BlNumber ?? "",
                    DeclarationNumber = h.DeclarationNumber ?? "",
                    ConsigneeName = h.ConsigneeName ?? "",
                    ClearanceType = h.ClearanceType ?? "",
                    RotationNumber = h.RotationNumber ?? "",
                    GoodsDescription = h.GoodsDescription ?? "",
                    TotalDutyPaid = h.TotalDutyPaid,
                    DeclarationDate = h.DeclarationDate,
                    BOEId = h.Id,
                    ContainerNumber = h.ContainerNumber ?? "",
                    // ✅ Calculate IsComplete: Check if required fields are present
                    IsComplete = !string.IsNullOrEmpty(h.DeclarationNumber ?? "") &&
                                 !string.IsNullOrEmpty(h.HouseBl ?? "") &&
                                 !string.IsNullOrEmpty(h.ConsigneeName ?? "") &&
                                 !string.IsNullOrEmpty(h.ClearanceType ?? "")
                }).ToList()
            }).ToList();
        }

        /// <summary>
        /// Get all containers under a specific Declaration/Master BL (non-consolidated)
        /// </summary>
        public async Task<List<string>> GetContainersByDeclarationAsync(string declarationNumber)
        {
            var containers = await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.DeclarationNumber == declarationNumber && !b.IsConsolidated)
                .Select(b => b.ContainerNumber)
                .Distinct()
                .ToListAsync();

            // ✅ Filter out invalid container numbers
            return ContainerNumberValidator.FilterValidContainerNumbers(containers);
        }

        /// <summary>
        /// Get all House BLs under a specific Master BL (consolidated)
        /// </summary>
        public async Task<List<HouseBLDetail>> GetHouseBLsByMasterBLAsync(string masterBL)
        {
            var houseBLs = await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.BlNumber == masterBL && b.IsConsolidated)
                .Select(b => new HouseBLDetail
                {
                    HouseBL = b.HouseBl ?? "",
                    MasterBL = b.BlNumber ?? "",
                    DeclarationNumber = b.DeclarationNumber ?? "",
                    ConsigneeName = b.ConsigneeName ?? "",
                    ClearanceType = b.ClearanceType ?? "",
                    RotationNumber = b.RotationNumber ?? "",
                    GoodsDescription = b.GoodsDescription ?? "",
                    TotalDutyPaid = b.TotalDutyPaid,
                    DeclarationDate = b.DeclarationDate,
                    BOEId = b.Id,
                    ContainerNumber = b.ContainerNumber ?? "",
                    // ✅ Calculate IsComplete: Check if required fields are present
                    IsComplete = !string.IsNullOrEmpty(b.DeclarationNumber) &&
                                 !string.IsNullOrEmpty(b.HouseBl) &&
                                 !string.IsNullOrEmpty(b.ConsigneeName) &&
                                 !string.IsNullOrEmpty(b.ClearanceType)
                })
                .ToListAsync();

            return houseBLs;
        }

        /// <summary>
        /// Get all containers under a specific Master BL (consolidated)
        /// </summary>
        public async Task<List<string>> GetContainersByMasterBLAsync(string masterBL)
        {
            var containers = await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.BlNumber == masterBL && b.IsConsolidated)
                .Select(b => b.ContainerNumber)
                .Distinct()
                .ToListAsync();

            // ✅ Filter out invalid container numbers
            return ContainerNumberValidator.FilterValidContainerNumbers(containers);
        }

        /// <summary>
        /// Get all House BLs under a specific container (consolidated) - Legacy method for backward compatibility
        /// </summary>
        [Obsolete("Use GetHouseBLsByMasterBLAsync instead. Container-based grouping is deprecated.")]
        public async Task<List<HouseBLDetail>> GetHouseBLsByContainerAsync(string containerNumber)
        {
            var houseBLs = await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.ContainerNumber == containerNumber && b.IsConsolidated)
                .Select(b => new HouseBLDetail
                {
                    HouseBL = b.HouseBl ?? "",
                    MasterBL = b.BlNumber ?? "",
                    DeclarationNumber = b.DeclarationNumber ?? "",
                    ConsigneeName = b.ConsigneeName ?? "",
                    ClearanceType = b.ClearanceType ?? "",
                    RotationNumber = b.RotationNumber ?? "",
                    GoodsDescription = b.GoodsDescription ?? "",
                    TotalDutyPaid = b.TotalDutyPaid,
                    DeclarationDate = b.DeclarationDate,
                    BOEId = b.Id,
                    ContainerNumber = b.ContainerNumber ?? "",
                    // ✅ Calculate IsComplete: Check if required fields are present
                    IsComplete = !string.IsNullOrEmpty(b.DeclarationNumber) &&
                                 !string.IsNullOrEmpty(b.HouseBl) &&
                                 !string.IsNullOrEmpty(b.ConsigneeName) &&
                                 !string.IsNullOrEmpty(b.ClearanceType)
                })
                .ToListAsync();

            return houseBLs;
        }
    }

    /// <summary>
    /// Non-Consolidated Cargo: One Master BL covering multiple containers
    /// </summary>
    public class NonConsolidatedCargoGroup
    {
        public string DeclarationNumber { get; set; } = string.Empty;
        public string MasterBL { get; set; } = string.Empty;
        public List<string> Containers { get; set; } = new();
        public int NoOfContainers { get; set; }
        public string ClearanceType { get; set; } = string.Empty;
        public string ConsigneeName { get; set; } = string.Empty;
        public string RotationNumber { get; set; } = string.Empty;
        public string GoodsDescription { get; set; } = string.Empty;
        public decimal? TotalDutyPaid { get; set; }
        public string? DeclarationDate { get; set; }
        public int SampleBOEId { get; set; }
    }

    /// <summary>
    /// Consolidated Cargo: One Master BL with multiple House BLs (can span one or multiple containers)
    /// </summary>
    public class ConsolidatedCargoGroup
    {
        public string MasterBL { get; set; } = string.Empty; // Unique identifier for consolidated cargo
        public List<string> ContainerNumbers { get; set; } = new(); // All containers under this Master BL (can be 1 or multiple)
        public List<HouseBLDetail> HouseBLDetails { get; set; } = new();
    }

    /// <summary>
    /// House BL detail within a consolidated Master BL
    /// </summary>
    public class HouseBLDetail
    {
        public string HouseBL { get; set; } = string.Empty;
        public string MasterBL { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public string ConsigneeName { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public string RotationNumber { get; set; } = string.Empty;
        public string GoodsDescription { get; set; } = string.Empty;
        public decimal? TotalDutyPaid { get; set; }
        public string? DeclarationDate { get; set; }
        public int BOEId { get; set; }
        public string ContainerNumber { get; set; } = string.Empty; // Container this House BL belongs to
        public bool IsComplete { get; set; } // ✅ Added for ImageAnalysisViewDialog compatibility
    }
}







