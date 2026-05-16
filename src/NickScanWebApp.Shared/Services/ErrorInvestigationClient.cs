namespace NickScanWebApp.Shared.Services;

public sealed class ErrorInvestigationClient
{
    public const string BasePath = "/api/ErrorInvestigation";
    public const string StatisticsPath = BasePath + "/statistics";

    private readonly ApiService _apiService;

    public ErrorInvestigationClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>(StatisticsPath);
    }

    public Task<TPage?> GetInvestigationsAsync<TPage>(
        int page,
        int pageSize,
        string? status = null,
        string? priority = null,
        string? search = null)
    {
        return _apiService.GetAsync<TPage>(BuildListPath(page, pageSize, status, priority, search));
    }

    public Task<TDetail?> GetInvestigationAsync<TDetail>(long investigationId)
    {
        return _apiService.GetAsync<TDetail>(BuildDetailPath(investigationId));
    }

    public Task<TResponse?> ApproveProposalAsync<TRequest, TResponse>(
        long investigationId,
        long proposalId,
        TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(
            BuildApproveProposalPath(investigationId, proposalId),
            request);
    }

    public Task<TResponse?> RejectProposalAsync<TRequest, TResponse>(
        long investigationId,
        long proposalId,
        TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(
            BuildRejectProposalPath(investigationId, proposalId),
            request);
    }

    public Task<TResponse?> IgnoreInvestigationAsync<TRequest, TResponse>(
        long investigationId,
        TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(
            BuildIgnorePath(investigationId),
            request);
    }

    public static string BuildListPath(
        int page,
        int pageSize,
        string? status = null,
        string? priority = null,
        string? search = null)
    {
        var parts = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrEmpty(status))
        {
            parts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrEmpty(priority))
        {
            parts.Add($"priority={Uri.EscapeDataString(priority)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        return $"{BasePath}?{string.Join("&", parts)}";
    }

    public static string BuildDetailPath(long investigationId)
    {
        return $"{BasePath}/{investigationId}";
    }

    public static string BuildApproveProposalPath(long investigationId, long proposalId)
    {
        return $"{BasePath}/{investigationId}/proposals/{proposalId}/approve";
    }

    public static string BuildRejectProposalPath(long investigationId, long proposalId)
    {
        return $"{BasePath}/{investigationId}/proposals/{proposalId}/reject";
    }

    public static string BuildIgnorePath(long investigationId)
    {
        return $"{BasePath}/{investigationId}/ignore";
    }
}
