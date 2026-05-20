namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisManagementClient
{
    public const string BasePath = "/api/image-analysis-management";
    public const string ServiceStatePath = BasePath + "/service-state";
    public const string WaveMonitorPath = BasePath + "/wave-monitor";
    public const string ReadyGroupsPath = BasePath + "/groups/ready";
    public const string AssignmentsPath = BasePath + "/assignments";
    public const string StatsPath = BasePath + "/stats";
    public const string AnalystsPath = BasePath + "/analysts";
    public const string AgentSettingsPath = BasePath + "/agent/settings";
    public const string AgentConditionsPath = BasePath + "/agent/conditions";
    public const string AgentStatsPath = BasePath + "/agent/stats";

    private readonly ApiService _apiService;

    public ImageAnalysisManagementClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TState?> GetServiceStateAsync<TState>()
    {
        return _apiService.GetAsync<TState>(ServiceStatePath);
    }

    public Task<TState?> TryGetServiceStateAsync<TState>()
    {
        return _apiService.TryGetAsync<TState>(ServiceStatePath);
    }

    public Task<TResponse?> UpdateServiceStateAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(ServiceStatePath, request);
    }

    public Task<TWaveMonitor?> GetWaveMonitorAsync<TWaveMonitor>()
    {
        return _apiService.GetAsync<TWaveMonitor>(WaveMonitorPath);
    }

    public Task<List<TGroup>?> GetReadyGroupsAsync<TGroup>()
    {
        return _apiService.GetAsync<List<TGroup>>(ReadyGroupsPath);
    }

    public Task<List<TAssignment>?> GetAssignmentsAsync<TAssignment>()
    {
        return _apiService.GetAsync<List<TAssignment>>(AssignmentsPath);
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(StatsPath);
    }

    public Task<List<string>?> GetAnalystsAsync()
    {
        return _apiService.GetAsync<List<string>>(AnalystsPath);
    }

    public Task<object?> SyncStagesAsync()
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/sync-stages", new { });
    }

    public Task<object?> RebuildIntakeAsync()
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/rebuild-intake", new { });
    }

    public Task<object?> AssignGroupToSelfAsync(string groupId)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/{Uri.EscapeDataString(groupId)}/assign",
            new { });
    }

    public Task<object?> AssignGroupAsync(string groupId, string user)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/{Uri.EscapeDataString(groupId)}/assign",
            new { user });
    }

    public Task<object?> ReassignGroupAsync(string groupId, string user)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/{Uri.EscapeDataString(groupId)}/reassign",
            new { user });
    }

    public Task<object?> ReleaseGroupAsync(string groupId)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/{Uri.EscapeDataString(groupId)}/release",
            new { });
    }

    public Task<TSettings?> GetAgentSettingsAsync<TSettings>()
    {
        return _apiService.GetAsync<TSettings>(AgentSettingsPath);
    }

    public Task<TAuditLog?> GetAgentAuditLogAsync<TAuditLog>(int pageSize = 20)
    {
        return _apiService.GetAsync<TAuditLog>(BuildAgentAuditLogPath(pageSize));
    }

    public Task<TStats?> GetAgentStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(AgentStatsPath);
    }

    public Task<object?> SaveAgentSettingsAsync<TRequest>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, object>(AgentSettingsPath, request);
    }

    public Task<object?> SaveAgentConditionAsync<TRequest>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, object>(AgentConditionsPath, request);
    }

    public Task<object?> GetAgentConditionAsync(int conditionId)
    {
        return _apiService.GetAsync<object>($"{AgentConditionsPath}/{conditionId}");
    }

    public Task<object?> ReverseAgentDecisionAsync(long auditLogId, string reason)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/agent/reverse/{auditLogId}",
            new { reason });
    }

    public static string BuildAgentAuditLogPath(int pageSize = 20)
    {
        return $"{BasePath}/agent/audit-log?pageSize={pageSize}";
    }
}
