using Microsoft.Extensions.Logging;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Service for container processing operations
    /// ✅ Unified service for both desktop and mobile applications
    /// Uses shared ApiService for authentication
    /// </summary>
    public class ContainerProcessingService
    {
        private readonly ContainerProcessingClient _containerProcessingClient;
        private readonly ILogger<ContainerProcessingService> _logger;

        public ContainerProcessingService(
            ContainerProcessingClient containerProcessingClient,
            ILogger<ContainerProcessingService> logger)
        {
            _containerProcessingClient = containerProcessingClient;
            _logger = logger;
        }

        public async Task<List<ContainerGroupDto>> GetContainerGroupsAsync(string? clearanceType = null, int page = 1, int pageSize = 50)
        {
            try
            {
                var endpoint = ContainerProcessingClient.BuildGroupsPath(clearanceType, page, pageSize);

                _logger.LogInformation("Fetching container groups from {Endpoint}", endpoint);

                var groups = await _containerProcessingClient.GetGroupsAsync<List<ContainerGroupDto>>(
                    clearanceType,
                    page,
                    pageSize) ?? new List<ContainerGroupDto>();

                _logger.LogInformation("Fetched {Count} container groups", groups.Count);

                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching container groups");
                throw;
            }
        }

        public async Task<ContainerProcessingSummaryDto?> GetSummaryStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Fetching container processing summary");

                try
                {
                    var summary = await _containerProcessingClient.GetSummaryAsync<ContainerProcessingSummaryDto>();
                    _logger.LogInformation("Fetched summary - {Total} total containers", summary?.TotalContainers ?? 0);
                    return summary;
                }
                catch (ApiException ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
                {
                    _logger.LogDebug("Container processing summary endpoint not available (404)");
                    return new ContainerProcessingSummaryDto
                    {
                        TotalContainers = 0,
                        PendingReview = 0,
                        CompletedReview = 0,
                        FailedReview = 0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching summary statistics");
                throw;
            }
        }
    }
}

