using Microsoft.Extensions.Logging;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Service for BL (Bill of Lading) review operations
    /// ✅ Unified service for both desktop and mobile applications
    /// Uses shared ApiService for authentication
    /// </summary>
    public class BLReviewService
    {
        private readonly ApiService _apiService;
        private readonly ILogger<BLReviewService> _logger;

        public BLReviewService(
            ApiService apiService,
            ILogger<BLReviewService> logger)
        {
            _apiService = apiService;
            _logger = logger;
        }

        /// <summary>
        /// Get all BL groups with completeness filtering
        /// </summary>
        public async Task<List<BLGroupDto>> GetBLGroupsAsync(string? status = null, int page = 1, int pageSize = 20)
        {
            try
            {
                var endpoint = $"/api/BLReview/groups?page={page}&pageSize={pageSize}";

                if (!string.IsNullOrEmpty(status))
                {
                    endpoint += $"&status={status}";
                }

                _logger.LogInformation("🔍 Fetching BL groups from {Endpoint}", endpoint);

                var groups = await _apiService.GetAsync<List<BLGroupDto>>(endpoint) ?? new List<BLGroupDto>();

                _logger.LogInformation("✅ Fetched {Count} BL groups", groups.Count);

                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching BL groups");
                throw;
            }
        }

        /// <summary>
        /// Get detailed information for a specific BL
        /// </summary>
        public async Task<BLDetailsDto?> GetBLDetailsAsync(string masterBlNumber)
        {
            try
            {
                var endpoint = $"/api/BLReview/details/{Uri.EscapeDataString(masterBlNumber)}";

                _logger.LogInformation("🔍 Fetching BL details for {BLNumber}", masterBlNumber);

                var details = await _apiService.GetAsync<BLDetailsDto>(endpoint);

                if (details == null)
                {
                    _logger.LogWarning("⚠️ BL not found: {BLNumber}", masterBlNumber);
                    return null;
                }

                _logger.LogInformation("✅ Fetched BL details with {Count} containers", details.Containers.Count);

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching BL details for {BLNumber}", masterBlNumber);
                throw;
            }
        }

        /// <summary>
        /// Save or update a BL review
        /// </summary>
        public async Task<BLReviewSaveResult?> SaveReviewAsync(BLReviewSubmission submission)
        {
            try
            {
                var endpoint = "/api/BLReview/save";

                _logger.LogInformation("💾 Saving review for BL {BLNumber}", submission.MasterBlNumber);

                var result = await _apiService.PostAsync<BLReviewSubmission, BLReviewSaveResult>(endpoint, submission);

                _logger.LogInformation("✅ Saved review - Status: {Status}, Decision: {Decision}",
                    result?.ReviewStatus, result?.FinalDecision);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving review for BL {BLNumber}", submission.MasterBlNumber);
                throw;
            }
        }

        /// <summary>
        /// Get review history for a BL
        /// </summary>
        public async Task<List<BLReviewHistoryItem>> GetReviewHistoryAsync(string masterBlNumber)
        {
            try
            {
                var endpoint = $"/api/BLReview/history/{Uri.EscapeDataString(masterBlNumber)}";

                _logger.LogInformation("🔍 Fetching review history for BL {BLNumber}", masterBlNumber);

                var history = await _apiService.GetAsync<List<BLReviewHistoryItem>>(endpoint)
                    ?? new List<BLReviewHistoryItem>();

                _logger.LogInformation("✅ Fetched {Count} history records", history.Count);

                return history;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching review history for BL {BLNumber}", masterBlNumber);
                throw;
            }
        }

        /// <summary>
        /// Get statistics for dashboard
        /// </summary>
        public async Task<BLReviewStatistics?> GetStatisticsAsync()
        {
            try
            {
                var endpoint = "/api/BLReview/statistics";

                _logger.LogInformation("🔍 Fetching BL review statistics");

                var stats = await _apiService.GetAsync<BLReviewStatistics>(endpoint);

                _logger.LogInformation("✅ Fetched statistics - {TotalBLs} total BLs", stats?.TotalBLs ?? 0);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching BL review statistics");
                throw;
            }
        }

        /// <summary>
        /// Check if a container is complete
        /// </summary>
        public async Task<bool> IsContainerCompleteAsync(string containerNumber)
        {
            try
            {
                var endpoint = $"/api/BLReview/container/completeness/{Uri.EscapeDataString(containerNumber)}";

                var result = await _apiService.GetAsync<ContainerCompletenessResult>(endpoint);

                return result?.IsComplete ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking container completeness for {Container}", containerNumber);
                return false;
            }
        }
    }
}

