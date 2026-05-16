namespace NickScanWebApp.Shared.Services;

public class RecordCompletenessClient
{
    private const string BasePath = "/api/recordcompleteness";

    private readonly ApiService _apiService;

    public RecordCompletenessClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<RecordCompletenessSummaryCounts?> GetSummaryAsync()
    {
        return _apiService.GetAsync<RecordCompletenessSummaryCounts>($"{BasePath}/summary");
    }

    public Task<TSummary?> GetSummaryAsync<TSummary>()
    {
        return _apiService.GetAsync<TSummary>($"{BasePath}/summary");
    }

    public Task<RecordCompletenessOldestInAudit?> GetOldestInAuditAsync()
    {
        return _apiService.GetAsync<RecordCompletenessOldestInAudit>($"{BasePath}/oldest-in-audit");
    }

    public Task<TResponse?> GetRecordsAsync<TResponse>(
        string status,
        string search,
        bool onlyMultiContainer,
        int page,
        int pageSize)
    {
        var endpoint = $"{BasePath}?status={Uri.EscapeDataString(status)}" +
            $"&search={Uri.EscapeDataString(search)}" +
            $"&onlyMultiContainer={onlyMultiContainer}" +
            $"&page={page}" +
            $"&pageSize={pageSize}";

        return _apiService.GetAsync<TResponse>(endpoint);
    }

    public Task<TDetail?> GetDetailAsync<TDetail>(int id)
    {
        return _apiService.GetAsync<TDetail>($"{BasePath}/{id}");
    }

    public Task<TDetail?> GetByDeclarationAsync<TDetail>(string declarationNumber)
    {
        return _apiService.GetAsync<TDetail>(
            $"{BasePath}/by-declaration/{Uri.EscapeDataString(declarationNumber)}");
    }
}

public class RecordCompletenessSummaryCounts
{
    public int TotalRecords { get; set; }
    public int TotalMultiContainer { get; set; }
    public int IntegrityGapContainers { get; set; }
    public int Pending { get; set; }
    public int PartiallyReady { get; set; }
    public int Ready { get; set; }
    public int InAnalysis { get; set; }
    public int InAudit { get; set; }
    public int Submitted { get; set; }
    public int Completed { get; set; }
    public int Archived { get; set; }
}

public class RecordCompletenessOldestInAudit
{
    public DateTime? OldestCreatedAtUtc { get; set; }
    public int TotalInAudit { get; set; }
}
