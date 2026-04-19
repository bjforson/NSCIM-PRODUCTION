using Microsoft.AspNetCore.Components.Authorization;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Service for container processing operations
    /// </summary>
    public class ContainerProcessingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ContainerProcessingService> _logger;
        private readonly AuthenticationStateProvider _authStateProvider;
        private const string API_CLIENT_NAME = "NickScanAPI";

        public ContainerProcessingService(
            IHttpClientFactory httpClientFactory,
            ILogger<ContainerProcessingService> logger,
            AuthenticationStateProvider authStateProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _authStateProvider = authStateProvider;
        }

        private async Task<HttpClient> GetAuthenticatedClientAsync()
        {
            var client = _httpClientFactory.CreateClient(API_CLIENT_NAME);

            if (_authStateProvider is SimpleAuthStateProvider simpleAuth)
            {
                var token = await simpleAuth.GetTokenAsync();
                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            }

            return client;
        }

        public async Task<List<ContainerGroupDto>> GetContainerGroupsAsync(string? clearanceType = null, int page = 1, int pageSize = 50)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var endpoint = $"/api/ContainerProcessing/groups?page={page}&pageSize={pageSize}";

                if (!string.IsNullOrEmpty(clearanceType))
                {
                    endpoint += $"&clearanceType={clearanceType}";
                }

                _logger.LogInformation("Fetching container groups from {Endpoint}", endpoint);

                var response = await client.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();

                var groups = await response.Content.ReadFromJsonAsync<List<ContainerGroupDto>>() ?? new List<ContainerGroupDto>();

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
                var client = await GetAuthenticatedClientAsync();
                var endpoint = "/api/ContainerProcessing/summary";

                _logger.LogInformation("Fetching container processing summary");

                var response = await client.GetAsync(endpoint);
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
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
                
                response.EnsureSuccessStatusCode();

                var summary = await response.Content.ReadFromJsonAsync<ContainerProcessingSummaryDto>();

                _logger.LogInformation("Fetched summary - {Total} total containers", summary?.TotalContainers ?? 0);

                return summary;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching summary statistics");
                throw;
            }
        }
    }

    #region DTOs

    public class ContainerGroupDto
    {
        public string ClearanceType { get; set; } = string.Empty;
        public string GroupingKey { get; set; } = string.Empty;
        public string GroupingValue { get; set; } = string.Empty;
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public List<ContainerProcessingItemDto> Containers { get; set; } = new();
        public DateTime? LatestScanDate { get; set; }
        public string PrimaryScannerType { get; set; } = string.Empty;
    }

    public class ContainerProcessingItemDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string? BlNumber { get; set; }
        public string? BoeNumber { get; set; }
        public string? RotationNumber { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public bool HasScannerData { get; set; }
        public bool HasICUMSData { get; set; }
        public bool HasImages { get; set; }
        public bool HasBOE { get; set; }
        public int ImageCount { get; set; }
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public DateTime? ScanDate { get; set; }
        public int CompletenessScore { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class ContainerProcessingSummaryDto
    {
        public int TotalGroups { get; set; }
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public int IncompleteContainers { get; set; }
        public double CompletionRate { get; set; }
        public int IMGroups { get; set; }
        public int EXGroups { get; set; }
        public int CMRGroups { get; set; }
        public int FS6000Containers { get; set; }
        public int ASEContainers { get; set; }
        public int HeimannContainers { get; set; }
        
        // Review status properties (added for error handling)
        public int PendingReview { get; set; }
        public int CompletedReview { get; set; }
        public int FailedReview { get; set; }
    }

    #endregion
}

