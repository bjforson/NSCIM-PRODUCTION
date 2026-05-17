namespace NickScanWebApp.Shared.Services;

public class ImageProcessingClient
{
    public const string BasePath = "/api/ImageProcessing";

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

    public Task<TComplete?> GetCompleteContainerDataAsync<TComplete>(string containerNumber)
    {
        return _apiService.GetAsync<TComplete>(BuildCompleteContainerDataPath(containerNumber));
    }

    public Task<TCapabilities?> GetModeCapabilitiesAsync<TCapabilities>(string containerNumber)
    {
        return _apiService.GetAsync<TCapabilities>(BuildModeCapabilitiesPath(containerNumber));
    }

    public Task<TPixel?> GetPixelAsync<TPixel>(string containerNumber, int x, int y)
    {
        return _apiService.GetAsync<TPixel>(BuildPixelPath(containerNumber, x, y));
    }

    public Task<TRoi?> GetRoiAsync<TRoi>(string containerNumber, int x, int y, int width, int height)
    {
        return _apiService.GetAsync<TRoi>(BuildRoiPath(containerNumber, x, y, width, height));
    }

    public static string BuildCompleteContainerDataPath(string containerNumber)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/complete";
    }

    public static string BuildCompleteImagePath(string containerNumber, string? size = null, string? imageType = null)
    {
        var path = $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/complete/image";
        var queryParams = new List<string>();

        if (!string.IsNullOrWhiteSpace(size))
        {
            queryParams.Add($"size={Uri.EscapeDataString(size)}");
        }

        if (!string.IsNullOrWhiteSpace(imageType))
        {
            queryParams.Add($"imageType={Uri.EscapeDataString(imageType)}");
        }

        return queryParams.Count == 0
            ? path
            : $"{path}?{string.Join("&", queryParams)}";
    }

    public static string BuildModeCapabilitiesPath(string containerNumber)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/mode-capabilities";
    }

    public static string BuildPixelPath(string containerNumber, int x, int y)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/pixel?x={x}&y={y}";
    }

    public static string BuildRawPlanePath(string containerNumber, string plane)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/raw?plane={Uri.EscapeDataString(plane)}";
    }

    public static string BuildRoiPath(string containerNumber, int x, int y, int width, int height)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/roi?x={x}&y={y}&w={width}&h={height}";
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
