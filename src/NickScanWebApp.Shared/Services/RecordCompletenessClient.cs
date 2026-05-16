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

    public Task<RecordCompletenessOldestInAudit?> GetOldestInAuditAsync()
    {
        return _apiService.GetAsync<RecordCompletenessOldestInAudit>($"{BasePath}/oldest-in-audit");
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
