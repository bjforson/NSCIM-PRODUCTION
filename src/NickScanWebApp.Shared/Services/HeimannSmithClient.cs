using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class HeimannSmithClient
{
    public const string BasePath = "/api/HeimannSmith";
    public const string StatisticsPath = BasePath + "/statistics";
    public const string ScansPath = BasePath + "/scans";

    private readonly ApiService _apiService;

    public HeimannSmithClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<HeimannSmithStatistics?> GetStatisticsAsync()
    {
        return _apiService.GetAsync<HeimannSmithStatistics>(StatisticsPath);
    }

    public Task<HeimannSmithScanResponse?> GetScansAsync(HeimannSmithScanQuery? query = null)
    {
        return _apiService.GetAsync<HeimannSmithScanResponse>(BuildScansPath(query));
    }

    public static string BuildScansPath(HeimannSmithScanQuery? query = null)
    {
        query ??= new HeimannSmithScanQuery();

        var parts = new List<string>();
        Add(parts, "page", query.Page);
        Add(parts, "pageSize", query.PageSize);
        Add(parts, "containerNumber", query.ContainerNumber?.Trim());
        Add(parts, "processingStatus", query.ProcessingStatus?.Trim());
        Add(parts, "startDate", query.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", query.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return ScansPath + ToQueryString(parts);
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static void Add(List<string> parts, string name, int? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{name}={Math.Max(1, value.Value)}");
        }
    }

    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}

public sealed class HeimannSmithScanQuery
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? ContainerNumber { get; set; }
    public string? ProcessingStatus { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public sealed class HeimannSmithScanResponse
{
    public List<HeimannSmithScanDto>? Data { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public sealed class HeimannSmithScanDto
{
    public int Id { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string ScannerId { get; set; } = string.Empty;
    public DateTime ScanDateTime { get; set; }
    public string? ImagePath { get; set; }
    public string ProcessingStatus { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public sealed class HeimannSmithStatistics
{
    public int TotalScans { get; set; }
    public int TodayScans { get; set; }
    public int CompletedScans { get; set; }
    public int PendingScans { get; set; }
    public int FailedScans { get; set; }
    public HeimannSmithLastScan? LastScan { get; set; }
}

public sealed class HeimannSmithLastScan
{
    public int Id { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string ScannerId { get; set; } = string.Empty;
    public DateTime ScanDateTime { get; set; }
    public string ProcessingStatus { get; set; } = "Pending";
}
