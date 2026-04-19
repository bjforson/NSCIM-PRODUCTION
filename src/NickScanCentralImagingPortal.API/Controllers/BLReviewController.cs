using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.Core.DTOs.BLReview;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require authentication for all endpoints
    public class BLReviewController : ControllerBase
    {
        private readonly IBLReviewRepository _blReviewRepository;
        private readonly ILogger<BLReviewController> _logger;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan BLGroupsCacheExpiration = TimeSpan.FromSeconds(60); // 60 seconds cache

        public BLReviewController(
            IBLReviewRepository blReviewRepository,
            ILogger<BLReviewController> logger,
            IMemoryCache cache)
        {
            _blReviewRepository = blReviewRepository;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Get all BL groups with completeness filtering
        /// </summary>
        [HttpGet("groups")]
        public async Task<ActionResult<List<BLGroupDto>>> GetBLGroups(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                // ✅ PERFORMANCE: Create cache key based on query parameters
                var cacheKey = $"bl-groups-{status ?? "all"}-{page}-{pageSize}";

                // Check cache first
                if (_cache.TryGetValue(cacheKey, out List<BLGroupDto>? cachedGroups))
                {
                    _logger.LogDebug("✅ [CACHE HIT] Returning cached BL groups ({Count} groups)", cachedGroups?.Count ?? 0);
                    return Ok(cachedGroups);
                }

                _logger.LogInformation("⏳ [CACHE MISS] Getting BL groups - Status: {Status}, Page: {Page}, PageSize: {PageSize}",
                    status, page, pageSize);

                var groups = await _blReviewRepository.GetBLGroupsAsync(status, page, pageSize);

                // ✅ PERFORMANCE: Cache the result for 60 seconds
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = BLGroupsCacheExpiration,
                    Size = 1, // Each cache entry counts as 1 unit toward the 1000 limit
                    Priority = CacheItemPriority.Normal
                };
                _cache.Set(cacheKey, groups, cacheOptions);

                _logger.LogInformation("✅ [CACHE SET] Returned and cached {Count} BL groups for {Seconds} seconds",
                    groups.Count, BLGroupsCacheExpiration.TotalSeconds);

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL groups - Status: {Status}, Page: {Page}", status, page);

                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get detailed information for a specific BL
        /// </summary>
        [HttpGet("details/{masterBlNumber}")]
        public async Task<ActionResult<BLDetailsDto>> GetBLDetails(string masterBlNumber)
        {
            try
            {
                _logger.LogInformation("Getting BL details for {BLNumber}", masterBlNumber);

                var details = await _blReviewRepository.GetBLDetailsAsync(masterBlNumber);

                if (details == null)
                {
                    _logger.LogWarning("BL not found: {BLNumber}", masterBlNumber);

                    return NotFound(new { error = "BL not found" });
                }

                _logger.LogInformation("✅ Returned BL details with {Count} containers", details.Containers.Count);

                return Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL details for {BLNumber}", masterBlNumber);

                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Save or update a BL review (supports partial reviews)
        /// </summary>
        [HttpPost("save")]
        public async Task<ActionResult> SaveReview([FromBody] BLReviewSubmission submission)
        {
            try
            {
                // Get username from claims
                var username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
                submission.ReviewedBy = username;

                _logger.LogInformation("Saving review for BL {BLNumber} by {User}", submission.MasterBlNumber, username);

                var review = await _blReviewRepository.SaveReviewAsync(submission);

                _logger.LogInformation("✅ Saved review for BL {BLNumber} - Status: {Status}, Decision: {Decision}",
                    submission.MasterBlNumber, review.ReviewStatus, review.FinalDecision);

                return Ok(new
                {
                    id = review.Id,
                    masterBlNumber = review.MasterBlNumber,
                    reviewStatus = review.ReviewStatus,
                    finalDecision = review.FinalDecision,
                    reviewedContainers = review.ReviewedContainers,
                    totalContainers = review.TotalContainers,
                    message = "Review saved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving review for BL {BLNumber}", submission.MasterBlNumber);

                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get review history for a BL
        /// </summary>
        [HttpGet("history/{masterBlNumber}")]
        public async Task<ActionResult> GetReviewHistory(string masterBlNumber)
        {
            try
            {
                _logger.LogInformation("Getting review history for BL {BLNumber}", masterBlNumber);

                var history = await _blReviewRepository.GetReviewHistoryAsync(masterBlNumber);

                _logger.LogInformation("✅ Returned {Count} review records", history.Count);

                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting review history for BL {BLNumber}", masterBlNumber);

                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get statistics for dashboard
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult> GetStatistics()
        {
            try
            {
                _logger.LogInformation("Getting BL review statistics");

                var stats = await _blReviewRepository.GetStatisticsAsync();

                _logger.LogInformation("✅ Statistics: {TotalBLs} total BLs, {CompletedBLs} completed",
                    stats.TotalBLs, stats.CompletedBLs);

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting BL review statistics");

                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Check if a container is complete (has scanner + ICUMS + images)
        /// </summary>
        [HttpGet("container/completeness/{containerNumber}")]
        public async Task<ActionResult> CheckContainerCompleteness(string containerNumber)
        {
            try
            {
                _logger.LogDebug("Checking completeness for container {Container}", containerNumber);

                var isComplete = await _blReviewRepository.IsContainerCompleteAsync(containerNumber);

                return Ok(new { containerNumber, isComplete });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking completeness for container {Container}", containerNumber);

                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}

