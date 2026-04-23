using System.Net.Http.Json;
using Npgsql;

namespace NickERP.Portal.Services;

/// <summary>
/// Pulls live metrics from NickHR (Postgres) and NSCIM (HTTP API) for the portal dashboard.
/// Each method is self-contained — a failure in one data source never blocks others; the caller
/// gets back a result object with IsAvailable=false and an Error message instead of an exception.
/// </summary>
public class StatsService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StatsService> _logger;

    public StatsService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<StatsService> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Snapshot of HR metrics surfaced in the portal.</summary>
    public record HrStats(
        bool IsAvailable,
        int ActiveEmployees,
        int PendingLeave,
        int OnLeaveToday,
        int LoginsToday,
        string? Error = null);

    /// <summary>Snapshot of NSCIM scan-portal metrics surfaced in the portal.</summary>
    public record NscimStats(
        bool IsAvailable,
        long TotalContainers,
        long ScansToday,
        int ScannersOnline,
        int ScannersTotal,
        int ActiveOperators,
        decimal CompletenessPercent,
        bool SystemsOperational,
        string? Error = null);

    public async Task<HrStats> GetHrStatsAsync(CancellationToken ct = default)
    {
        var conn = _config.GetConnectionString("NickHrDb");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return new HrStats(false, 0, 0, 0, 0, "NickHrDb connection string not configured.");
        }

        try
        {
            await using var db = new NpgsqlConnection(conn);
            await db.OpenAsync(ct);

            // One round-trip, four metrics.
            const string sql = @"
SELECT
  (SELECT COUNT(*) FROM public.""Employees""
     WHERE ""IsDeleted"" = false) AS active_employees,
  (SELECT COUNT(*) FROM public.""LeaveRequests""
     WHERE ""Status"" = 0) AS pending_leave,
  (SELECT COUNT(DISTINCT ""EmployeeId"") FROM public.""LeaveRequests""
     WHERE ""Status"" = 1
       AND ""StartDate"" <= current_date
       AND ""EndDate"" >= current_date) AS on_leave_today,
  (SELECT COUNT(DISTINCT ""Email"") FROM public.""LoginAudits""
     WHERE ""LoginTime"" >= current_date
       AND ""LoginTime"" <  current_date + interval '1 day'
       AND ""Success"" = true) AS logins_today
";

            await using var cmd = new NpgsqlCommand(sql, db);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new HrStats(false, 0, 0, 0, 0, "No rows returned");
            }

            return new HrStats(
                IsAvailable:    true,
                ActiveEmployees: SafeInt(reader, 0),
                PendingLeave:    SafeInt(reader, 1),
                OnLeaveToday:    SafeInt(reader, 2),
                LoginsToday:     SafeInt(reader, 3));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetHrStatsAsync failed");
            return new HrStats(false, 0, 0, 0, 0, ex.Message);
        }
    }

    public async Task<NscimStats> GetNscimStatsAsync(CancellationToken ct = default)
    {
        var url = _config["Stats:NscimStatsUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return Empty("NscimStatsUrl not configured.");
        }

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(6);
            var payload = await http.GetFromJsonAsync<NscimStatsPayload>(url, ct);
            if (payload is null)
            {
                return Empty("NSCIM returned no payload.");
            }

            return new NscimStats(
                IsAvailable:          true,
                TotalContainers:      payload.TotalContainers,
                ScansToday:           payload.TodaysScans,
                ScannersOnline:       payload.ScannersOnline,
                ScannersTotal:        payload.ScannersTotal,
                ActiveOperators:      payload.ActiveOperators,
                CompletenessPercent:  payload.CompletenessPercent,
                SystemsOperational:   payload.SystemsOperational);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetNscimStatsAsync failed");
            return Empty(ex.Message);
        }

        static NscimStats Empty(string err) => new(false, 0, 0, 0, 0, 0, 0, false, err);
    }

    private static int SafeInt(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    // Mirrors the JSON shape of /api/public/system-stats. Only the fields we use are declared.
    private class NscimStatsPayload
    {
        public long TotalContainers { get; set; }
        public long TodaysScans { get; set; }
        public decimal CompletenessPercent { get; set; }
        public int ScannersOnline { get; set; }
        public int ScannersTotal { get; set; }
        public int ActiveOperators { get; set; }
        public bool SystemsOperational { get; set; }
    }
}
