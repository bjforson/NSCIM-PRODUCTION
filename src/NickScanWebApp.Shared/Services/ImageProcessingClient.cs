namespace NickScanWebApp.Shared.Services;

public class ImageProcessingClient
{
    private const string BasePath = "/api/ImageProcessing";

    private readonly ApiService _apiService;

    public ImageProcessingClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<ImageSearchResultDto?> SearchImagesAsync(string? containerNumber, string? scannerType)
    {
        var queryParams = new List<string>();

        if (!string.IsNullOrWhiteSpace(containerNumber))
        {
            queryParams.Add($"containerNumber={Uri.EscapeDataString(containerNumber)}");
        }

        if (!string.IsNullOrWhiteSpace(scannerType))
        {
            queryParams.Add($"scannerType={Uri.EscapeDataString(scannerType)}");
        }

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        return _apiService.GetAsync<ImageSearchResultDto>($"{BasePath}{query}");
    }

    public Task<ImageProcessingStatsDto?> GetStatisticsAsync()
    {
        return _apiService.GetAsync<ImageProcessingStatsDto>($"{BasePath}/statistics");
    }
}

public class ImageSearchResultDto
{
    public List<ImageDetailDto>? Images { get; set; }
    public int? TotalCount { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class ImageDetailDto
{
    public int Id { get; set; }
    public string? FileName { get; set; }
    public string? ContainerNumber { get; set; }
    public string? ScannerType { get; set; }
    public string? ImageType { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? ProcessingStatus { get; set; }
}

public class ImageProcessingStatsDto
{
    public int? TotalImages { get; set; }
    public int? ProcessedImages { get; set; }
    public int? PendingImages { get; set; }
    public int? FailedImages { get; set; }
    public int? ImagesToday { get; set; }
    public double? AverageProcessingTime { get; set; }
    public double? ProcessingSuccessRate { get; set; }
}
