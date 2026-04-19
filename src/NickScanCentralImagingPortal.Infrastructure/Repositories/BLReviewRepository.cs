using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.BLReview;
using NickScanCentralImagingPortal.Core.Entities.Review;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class BLReviewRepository : IBLReviewRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IcumDownloadsDbContext _icumDownloadsContext;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly ILogger<BLReviewRepository> _logger;

        public BLReviewRepository(
            ApplicationDbContext context,
            IcumDownloadsDbContext icumDownloadsContext,
            IImageProcessingService imageProcessingService,
            ILogger<BLReviewRepository> logger)
        {
            _context = context;
            _icumDownloadsContext = icumDownloadsContext;
            _imageProcessingService = imageProcessingService;
            _logger = logger;
        }

        public async Task<List<BLGroupDto>> GetBLGroupsAsync(string? status = null, int page = 1, int pageSize = 20)
        {
            try
            {
                _logger.LogInformation("Getting BL groups from complete containers - Status: {Status}, Page: {Page}, PageSize: {PageSize}", status, page, pageSize);

                // ✅ FIXED: Get complete containers first (from NS_CIS)
                var completeContainerNumbers = await _context.ContainerCompletenessStatuses
                    .Where(c => c.Status == "Complete" && c.HasICUMSData)
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Found {Count} complete containers", completeContainerNumbers.Count);

                // ✅ FIXED: Get BL numbers from ICUMS_Downloads for those containers
                // Split into smaller batches to avoid query timeout
                var batchSize = 1000;
                var containersWithBL = new List<(string MasterBlNumber, string ContainerNumber)>();

                _logger.LogInformation("Processing {TotalBatches} batches of {BatchSize} containers",
                    (int)Math.Ceiling(completeContainerNumbers.Count / (double)batchSize), batchSize);

                for (int i = 0; i < completeContainerNumbers.Count; i += batchSize)
                {
                    var batch = completeContainerNumbers.Skip(i).Take(batchSize).ToList();

                    var placeholders = string.Join(",", batch.Select((_, idx) => $"'{batch[idx].Replace("'", "''")}'")); 
                    var sql = $"SELECT * FROM boedocuments WHERE containernumber IN ({placeholders}) AND blnumber IS NOT NULL AND blnumber <> ''";
                    var batchDocuments = await _icumDownloadsContext.BOEDocuments
                        .FromSqlRaw(sql)
                        .AsNoTracking()
                        .ToListAsync();

                    // ✅ Project in memory after loading to avoid CTE generation
                    var batchResults = batchDocuments
                        .Select(b => new { MasterBlNumber = b.BlNumber, b.ContainerNumber })
                        .ToList();

                    containersWithBL.AddRange(batchResults.Select(b => (b.MasterBlNumber!, b.ContainerNumber!)));

                    _logger.LogDebug("Batch {BatchNum}: Found {Count} containers with BL numbers",
                        (i / batchSize) + 1, batchResults.Count);
                }

                _logger.LogInformation("Found {Count} complete containers with BL numbers", containersWithBL.Count);

                // Group by MasterBlNumber
                var blGroups = containersWithBL
                    .GroupBy(c => c.MasterBlNumber)
                    .Select(g => new
                    {
                        MasterBlNumber = g.Key,
                        ContainerNumbers = g.Select(c => c.ContainerNumber).ToList()
                    })
                    .ToList();

                _logger.LogInformation("Grouped into {Count} BL numbers", blGroups.Count);

                // Build BL groups from containers that are already complete
                var result = new List<BLGroupDto>();

                foreach (var blGroup in blGroups)
                {
                    var blContainers = blGroup.ContainerNumbers.ToList();

                    // All containers in blGroups are already complete (filtered earlier)
                    if (blContainers.Any())
                    {
                        // Get existing review if any
                        var existingReview = await _context.BLReviewRecords
                            .Where(r => r.MasterBlNumber == blGroup.MasterBlNumber)
                            .OrderByDescending(r => r.CreatedAt)
                            .FirstOrDefaultAsync();

                        var blDto = new BLGroupDto
                        {
                            MasterBlNumber = blGroup.MasterBlNumber!,
                            TotalContainers = blGroup.ContainerNumbers.Count,
                            CompleteContainers = blContainers.Count,
                            ContainerNumbers = blContainers,
                            ReviewStatus = existingReview?.ReviewStatus ?? "Pending",
                            FinalDecision = existingReview?.FinalDecision ?? "Pending",
                            LastReviewedAt = existingReview?.ReviewCompletedAt ?? existingReview?.UpdatedAt,
                            ReviewedBy = existingReview?.ReviewedBy,
                            ReviewedContainers = existingReview?.ReviewedContainers ?? 0,
                            NormalContainers = existingReview?.NormalContainers ?? 0,
                            AbnormalContainers = existingReview?.AbnormalContainers ?? 0
                        };

                        result.Add(blDto);
                    }
                }

                // Apply status filter
                if (!string.IsNullOrEmpty(status) && status != "All")
                {
                    result = result.Where(r => r.ReviewStatus == status).ToList();
                }

                // Apply pagination
                var totalBLs = result.Count;
                var totalPages = (int)Math.Ceiling(totalBLs / (double)pageSize);

                var paginatedResult = result
                    .OrderByDescending(r => r.LastReviewedAt ?? DateTime.MinValue)
                    .ThenBy(r => r.MasterBlNumber)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                _logger.LogInformation("✅ Returning {Count} BL groups on page {Page} of {TotalPages} (Total BLs: {TotalBLs})",
                    paginatedResult.Count, page, totalPages, totalBLs);

                return paginatedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL groups");
                throw;
            }
        }

        public async Task<BLDetailsDto?> GetBLDetailsAsync(string masterBlNumber)
        {
            try
            {
                _logger.LogInformation("Getting BL details for {BLNumber}", masterBlNumber);

                // ✅ FIXED: Get all complete containers for this BL from ICUMS_Downloads
                var containerNumbers = await _icumDownloadsContext.BOEDocuments
                    .Where(b => b.BlNumber == masterBlNumber)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                if (!containerNumbers.Any())
                {
                    _logger.LogWarning("No containers found for BL {BLNumber}", masterBlNumber);
                    return null;
                }

                // ✅ FIXED: Filter to only include complete containers
                var completeContainerNumbers = await _context.ContainerCompletenessStatuses
                    .Where(c => containerNumbers.Contains(c.ContainerNumber)
                             && c.Status == "Complete"
                             && c.HasICUMSData)
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                if (!completeContainerNumbers.Any())
                {
                    _logger.LogWarning("No complete containers found for BL {BLNumber}", masterBlNumber);
                    return null;
                }

                // Build container details for complete containers only
                var containers = new List<ContainerInBLDto>();

                foreach (var containerNumber in completeContainerNumbers)
                {
                    var containerDto = await BuildContainerDto(containerNumber);
                    if (containerDto != null)
                    {
                        containers.Add(containerDto);
                    }
                }

                // Get current review and history
                var reviews = await _context.BLReviewRecords
                    .Include(r => r.ContainerDecisions)
                    .Where(r => r.MasterBlNumber == masterBlNumber)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                var currentReview = reviews.FirstOrDefault();
                BLReviewSummary? currentReviewSummary = null;

                if (currentReview != null)
                {
                    currentReviewSummary = new BLReviewSummary
                    {
                        Id = currentReview.Id,
                        ReviewStatus = currentReview.ReviewStatus,
                        FinalDecision = currentReview.FinalDecision,
                        BLComments = currentReview.BLComments,
                        ReviewedBy = currentReview.ReviewedBy,
                        ReviewStartedAt = currentReview.ReviewStartedAt,
                        ReviewCompletedAt = currentReview.ReviewCompletedAt,
                        TotalContainers = currentReview.TotalContainers,
                        ReviewedContainers = currentReview.ReviewedContainers,
                        NormalContainers = currentReview.NormalContainers,
                        AbnormalContainers = currentReview.AbnormalContainers
                    };

                    // Apply existing decisions to containers
                    foreach (var container in containers)
                    {
                        var decision = currentReview.ContainerDecisions
                            .FirstOrDefault(d => d.ContainerNumber == container.ContainerNumber);

                        if (decision != null)
                        {
                            container.CurrentDecision = decision.Decision;
                            container.CurrentComments = decision.Comments;
                        }
                    }
                }

                var details = new BLDetailsDto
                {
                    MasterBlNumber = masterBlNumber,
                    Containers = containers,
                    CurrentReview = currentReviewSummary,
                    ReviewHistory = reviews.Skip(1).Select(r => new BLReviewSummary
                    {
                        Id = r.Id,
                        ReviewStatus = r.ReviewStatus,
                        FinalDecision = r.FinalDecision,
                        BLComments = r.BLComments,
                        ReviewedBy = r.ReviewedBy,
                        ReviewStartedAt = r.ReviewStartedAt,
                        ReviewCompletedAt = r.ReviewCompletedAt,
                        TotalContainers = r.TotalContainers,
                        ReviewedContainers = r.ReviewedContainers,
                        NormalContainers = r.NormalContainers,
                        AbnormalContainers = r.AbnormalContainers
                    }).ToList()
                };

                _logger.LogInformation("Built BL details with {Count} containers", containers.Count);

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL details for {BLNumber}", masterBlNumber);
                throw;
            }
        }

        public async Task<BLReviewRecord> SaveReviewAsync(BLReviewSubmission submission)
        {
            try
            {
                _logger.LogInformation("Saving review for BL {BLNumber}", submission.MasterBlNumber);

                BLReviewRecord review;

                if (submission.Id.HasValue)
                {
                    // Update existing review
                    review = await _context.BLReviewRecords
                        .Include(r => r.ContainerDecisions)
                        .FirstOrDefaultAsync(r => r.Id == submission.Id.Value)
                        ?? throw new Exception($"Review {submission.Id} not found");

                    review.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Create new review
                    review = new BLReviewRecord
                    {
                        MasterBlNumber = submission.MasterBlNumber,
                        ReviewedBy = submission.ReviewedBy,
                        ReviewStartedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.BLReviewRecords.Add(review);
                }

                // Update or add container decisions
                foreach (var containerDecision in submission.ContainerDecisions)
                {
                    var existing = review.ContainerDecisions
                        .FirstOrDefault(d => d.ContainerNumber == containerDecision.ContainerNumber);

                    if (existing != null)
                    {
                        // Update existing
                        existing.Decision = containerDecision.Decision;
                        existing.Comments = containerDecision.Comments;
                        existing.ReviewedBy = submission.ReviewedBy;
                        existing.ReviewedAt = DateTime.UtcNow;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Add new
                        var newDecision = new ContainerReviewDecision
                        {
                            BLReviewRecordId = review.Id,
                            ContainerNumber = containerDecision.ContainerNumber,
                            Decision = containerDecision.Decision,
                            Comments = containerDecision.Comments,
                            ReviewedBy = submission.ReviewedBy,
                            ReviewedAt = containerDecision.Decision != "Pending" ? DateTime.UtcNow : null,
                            CreatedAt = DateTime.UtcNow
                        };

                        // Get container metadata for flags
                        var containerMeta = await GetContainerMetadata(containerDecision.ContainerNumber);
                        newDecision.HasScanner = containerMeta.HasScanner;
                        newDecision.HasICUMS = containerMeta.HasICUMS;
                        newDecision.HasImages = containerMeta.HasImages;
                        newDecision.ScannerType = containerMeta.ScannerType;

                        review.ContainerDecisions.Add(newDecision);
                    }
                }

                // Calculate counts and final decision
                var reviewedDecisions = review.ContainerDecisions.Where(d => d.Decision != "Pending").ToList();
                review.ReviewedContainers = reviewedDecisions.Count;
                review.NormalContainers = reviewedDecisions.Count(d => d.Decision == "Normal");
                review.AbnormalContainers = reviewedDecisions.Count(d => d.Decision == "Abnormal");
                review.TotalContainers = review.ContainerDecisions.Count;

                // Auto-calculate final decision
                review.FinalDecision = CalculateFinalDecision(review.ContainerDecisions.ToList());

                // Update status
                if (submission.IsComplete && review.ReviewedContainers == review.TotalContainers)
                {
                    review.ReviewStatus = "Completed";
                    review.ReviewCompletedAt = DateTime.UtcNow;
                }
                else if (review.ReviewedContainers > 0)
                {
                    review.ReviewStatus = "InProgress";
                }
                else
                {
                    review.ReviewStatus = "Pending";
                }

                review.BLComments = submission.BLComments;

                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Saved review for BL {BLNumber} - Status: {Status}, Decision: {Decision}",
                    submission.MasterBlNumber, review.ReviewStatus, review.FinalDecision);

                return review;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving review for BL {BLNumber}", submission.MasterBlNumber);
                throw;
            }
        }

        public async Task<List<BLReviewRecord>> GetReviewHistoryAsync(string masterBlNumber)
        {
            return await _context.BLReviewRecords
                .Include(r => r.ContainerDecisions)
                .Where(r => r.MasterBlNumber == masterBlNumber)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<BLReviewStatistics> GetStatisticsAsync()
        {
            try
            {
                // ✅ MEMORY OPTIMIZATION: Filter to last 30 days to reduce buffer pool usage
                // Only load recent data into SQL Server memory instead of entire table
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
                var baseQuery = _context.BLReviewRecords
                    .Where(r => r.CreatedAt >= thirtyDaysAgo)
                    .AsNoTracking(); // Read-only query, no change tracking

                // ✅ Use database aggregation instead of loading all data into memory
                // For statistics, we show last 30 days for operational data
                // Total counts use database aggregation
                var stats = new BLReviewStatistics
                {
                    // Recent statistics (last 30 days) using database aggregation
                    TotalBLs = await baseQuery.CountAsync(),
                    PendingBLs = await baseQuery.CountAsync(r => r.ReviewStatus == "Pending"),
                    InProgressBLs = await baseQuery.CountAsync(r => r.ReviewStatus == "InProgress"),
                    CompletedBLs = await baseQuery.CountAsync(r => r.ReviewStatus == "Completed"),

                    // Sum operations using database aggregation
                    TotalContainers = await baseQuery.SumAsync(r => (int?)r.TotalContainers) ?? 0,
                    CompleteContainers = await baseQuery.SumAsync(r => (int?)r.ReviewedContainers) ?? 0
                };

                _logger.LogDebug("BL Review statistics retrieved: Total={Total}, Pending={Pending}, InProgress={InProgress}, Completed={Completed}",
                    stats.TotalBLs, stats.PendingBLs, stats.InProgressBLs, stats.CompletedBLs);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL review statistics");
                return new BLReviewStatistics(); // Return empty stats on error
            }
        }

        public async Task<bool> IsContainerCompleteAsync(string containerNumber)
        {
            try
            {
                // ✅ FIXED: Use actual Container Completeness Status from the database
                var completenessStatus = await _context.ContainerCompletenessStatuses
                    .FirstOrDefaultAsync(c => c.ContainerNumber == containerNumber
                                            && c.Status == "Complete"
                                            && c.HasICUMSData);

                var isComplete = completenessStatus != null;

                _logger.LogDebug("Container {Container} completeness check: {Complete}",
                    containerNumber, isComplete);

                return isComplete;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking completeness for container {Container}", containerNumber);
                return false;
            }
        }

        #region Private Helper Methods

        private async Task<ContainerInBLDto?> BuildContainerDto(string containerNumber)
        {
            try
            {
                var meta = await GetContainerMetadata(containerNumber);

                // Only include complete containers
                if (!meta.HasScanner || !meta.HasICUMS || !meta.HasImages)
                {
                    return null;
                }

                return new ContainerInBLDto
                {
                    ContainerNumber = containerNumber,
                    ScannerType = meta.ScannerType,
                    HasScanner = meta.HasScanner,
                    HasICUMS = meta.HasICUMS,
                    HasImages = meta.HasImages,
                    ImageCount = meta.ImageCount,
                    ScannerRecordCount = meta.ScannerRecordCount,
                    ICUMSRecordCount = meta.ICUMSRecordCount,
                    ScanDate = meta.ScanDate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building container DTO for {Container}", containerNumber);
                return null;
            }
        }

        private async Task<ContainerMetadata> GetContainerMetadata(string containerNumber)
        {
            var meta = new ContainerMetadata { ContainerNumber = containerNumber };

            // ✅ FIXED: Get completeness status from ContainerCompletenessStatuses
            var completenessStatus = await _context.ContainerCompletenessStatuses
                .FirstOrDefaultAsync(c => c.ContainerNumber == containerNumber);

            if (completenessStatus != null)
            {
                meta.ScannerType = completenessStatus.ScannerType;
                meta.ScanDate = completenessStatus.ScanDate;
                meta.HasScanner = true; // Status is Complete means it has scanner data
                meta.HasICUMS = completenessStatus.HasICUMSData;
                meta.HasImages = true; // Complete status implies images exist
            }

            // Get scanner record count
            if (meta.ScannerType == "FS6000")
            {
                var fs6000Scan = await _context.FS6000Scans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);
                if (fs6000Scan != null)
                {
                    meta.ScannerRecordCount = await _context.FS6000Images
                        .CountAsync(i => i.ScanId == fs6000Scan.Id);
                    meta.ImageCount = meta.ScannerRecordCount;
                }
            }
            else if (meta.ScannerType == "ASE")
            {
                meta.ScannerRecordCount = 1;
                meta.ImageCount = 1;
            }

            // ✅ FIXED: Get ICUMS data count from BOEDocuments
            var icumsCount = await _icumDownloadsContext.BOEDocuments
                .CountAsync(b => b.ContainerNumber == containerNumber);
            meta.ICUMSRecordCount = icumsCount;

            return meta;
        }

        private string CalculateFinalDecision(List<ContainerReviewDecision> decisions)
        {
            var reviewedDecisions = decisions.Where(d => d.Decision != "Pending").ToList();

            // If no containers reviewed yet
            if (!reviewedDecisions.Any())
                return "Pending";

            // If ANY container is Abnormal → BL is Abnormal
            if (reviewedDecisions.Any(d => d.Decision == "Abnormal"))
                return "Abnormal";

            // If ALL reviewed containers are Normal
            if (reviewedDecisions.All(d => d.Decision == "Normal"))
            {
                // If not all containers reviewed yet, mark as In Progress
                if (reviewedDecisions.Count < decisions.Count)
                    return "Pending"; // Still pending overall

                return "Normal"; // All reviewed and all normal
            }

            return "Pending";
        }

        private class ContainerMetadata
        {
            public string ContainerNumber { get; set; } = string.Empty;
            public bool HasScanner { get; set; }
            public bool HasICUMS { get; set; }
            public bool HasImages { get; set; }
            public string ScannerType { get; set; } = string.Empty;
            public int ImageCount { get; set; }
            public int ScannerRecordCount { get; set; }
            public int ICUMSRecordCount { get; set; }
            public DateTime? ScanDate { get; set; }
        }

        #endregion
    }
}

