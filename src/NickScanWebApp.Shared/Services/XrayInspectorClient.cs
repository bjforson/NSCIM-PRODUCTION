namespace NickScanWebApp.Shared.Services;

public sealed class XrayInspectorClient
{
    private const string BasePath = "/api/xray-inspector";
    private readonly ApiService _apiService;

    public XrayInspectorClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> SearchAsync<TResponse>(string query, string scanner, int limit)
    {
        return _apiService.GetAsync<TResponse>(
            $"{BasePath}/search?q={Uri.EscapeDataString(query)}&scanner={Uri.EscapeDataString(scanner)}&limit={limit}");
    }

    public Task<TDetails?> GetScanAsync<TDetails>(string scanner, string id)
    {
        return _apiService.GetAsync<TDetails>(
            $"{BasePath}/scan/{Uri.EscapeDataString(scanner)}/{Uri.EscapeDataString(id)}");
    }

    public Task<TResponse?> AnalyzeLineProfileAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/analyze/line-profile", request);
    }

    public Task<TResponse?> AnalyzeRoiStatsAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/analyze/roi-stats", request);
    }

    public Task<TResponse?> AnalyzeObjectsAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/analyze/objects", request);
    }

    public Task<byte[]?> AnalyzeEdgeBytesAsync(object request)
    {
        return _apiService.PostForBytesAsync($"{BasePath}/analyze/edge", request);
    }

    public Task<byte[]?> AnalyzeThresholdBytesAsync(object request)
    {
        return _apiService.PostForBytesAsync($"{BasePath}/analyze/threshold", request);
    }

    public Task<byte[]?> AnalyzeDualEnergyDiffBytesAsync(string scanner, string id)
    {
        return _apiService.PostEmptyForBytesAsync(
            $"{BasePath}/analyze/dual-energy-diff?scanner={Uri.EscapeDataString(scanner)}&id={Uri.EscapeDataString(id)}");
    }

    public Task<byte[]?> GetVendorJpegBytesAsync(string scanner, string id)
    {
        return _apiService.GetBytesAsync(
            $"{BasePath}/vendor-jpeg/{Uri.EscapeDataString(scanner)}/{Uri.EscapeDataString(id)}");
    }

    public Task<byte[]?> GetImageBytesAsync(string scanner, string id, string queryString)
    {
        return _apiService.GetBytesAsync(BuildImagePath(scanner, id, queryString));
    }

    public Task<byte[]?> ExportRoiCsvAsync(object request)
    {
        return _apiService.PostForBytesAsync($"{BasePath}/export/roi-csv", request);
    }

    public Task<byte[]?> ExportPdfReportAsync(object request)
    {
        return _apiService.PostForBytesAsync($"{BasePath}/export/pdf-report", request);
    }

    public static string BuildImagePath(string scanner, string id, string queryString)
    {
        return $"{BasePath}/image/{Uri.EscapeDataString(scanner)}/{Uri.EscapeDataString(id)}{queryString}";
    }

    public static string BuildCompositePath(string scanner, string id)
    {
        return $"{BasePath}/composite/{Uri.EscapeDataString(scanner)}/{Uri.EscapeDataString(id)}";
    }
}
