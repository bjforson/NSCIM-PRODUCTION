using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ReportsClient
{
    public const string BasePath = "/api/Reports";

    private readonly ApiService _apiService;

    public ReportsClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TReport?> GetReportAsync<TReport>(
        string reportType,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return _apiService.GetAsync<TReport>(BuildReportPath(reportType, startDate, endDate));
    }

    public Task<byte[]?> ExportReportAsync(
        string reportType,
        string format,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return _apiService.GetBytesAsync(BuildExportPath(reportType, format, startDate, endDate));
    }

    public static string BuildReportPath(
        string reportType,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return $"{BasePath}/{Uri.EscapeDataString(reportType)}{BuildDateQueryString(startDate, endDate)}";
    }

    public static string BuildExportPath(
        string reportType,
        string format,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var parts = new List<string>
        {
            $"format={Uri.EscapeDataString(format)}"
        };

        AddDate(parts, "startDate", startDate);
        AddDate(parts, "endDate", endDate);

        return $"{BasePath}/{Uri.EscapeDataString(reportType)}/export?{string.Join("&", parts)}";
    }

    private static string BuildDateQueryString(DateTime? startDate, DateTime? endDate)
    {
        var parts = new List<string>();
        AddDate(parts, "startDate", startDate);
        AddDate(parts, "endDate", endDate);

        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }

    private static void AddDate(List<string> parts, string name, DateTime? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{name}={value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }
    }
}
