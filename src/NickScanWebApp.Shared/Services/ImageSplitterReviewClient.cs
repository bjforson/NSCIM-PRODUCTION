using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ImageSplitterReviewClient
{
    private const string BasePath = "/api/image-splitter";
    private readonly ApiService _apiService;

    public ImageSplitterReviewClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<THealth?> GetHealthAsync<THealth>()
    {
        return _apiService.GetAsync<THealth>($"{BasePath}/health");
    }

    public Task<TSummary?> GetReviewSummaryAsync<TSummary>(int? sinceHours = null)
    {
        var query = sinceHours.HasValue
            ? $"?since_hours={sinceHours.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        return _apiService.GetAsync<TSummary>($"{BasePath}/jobs/review-summary{query}");
    }

    public Task<List<TJob>?> GetPendingJobsAsync<TJob>(SplitReviewQueueQuery query)
    {
        return _apiService.GetAsync<List<TJob>>($"{BasePath}/jobs/pending{query.ToQueryString()}");
    }

    public Task<List<TResult>?> GetJobResultsAsync<TResult>(Guid jobId)
    {
        return _apiService.GetAsync<List<TResult>>($"{BasePath}/jobs/{jobId}/results");
    }

    public Task<object?> ApproveAsync(Guid jobId, object payload)
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/jobs/{jobId}/approve", payload);
    }

    public Task<object?> SubmitManualAsync(Guid jobId, object payload)
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/jobs/{jobId}/manual", payload);
    }

    public Task<object?> RejectAsync(Guid jobId, object payload)
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/jobs/{jobId}/reject", payload);
    }

    public static string BuildOriginalImagePath(Guid jobId)
    {
        return $"{BasePath}/jobs/{jobId}/original";
    }

    public static string BuildResultImagePath(Guid jobId, Guid resultId, string side, bool lossless = false)
    {
        var kind = lossless ? "lossless" : "image";
        return $"{BasePath}/jobs/{jobId}/results/{resultId}/{kind}/{Uri.EscapeDataString(side)}";
    }
}

public sealed class SplitReviewQueueQuery
{
    public string Mode { get; set; } = "backlog";
    public int Limit { get; set; } = 500;
    public int? SinceHours { get; set; }
    public string? ScannerType { get; set; }

    internal string ToQueryString()
    {
        var parts = new List<string>
        {
            $"mode={Uri.EscapeDataString(Mode)}",
            $"limit={Limit.ToString(CultureInfo.InvariantCulture)}"
        };

        if (SinceHours.HasValue)
        {
            parts.Add($"since_hours={SinceHours.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(ScannerType))
        {
            parts.Add($"scanner_type={Uri.EscapeDataString(ScannerType)}");
        }

        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}
