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
        // "Today" row
        int ActiveEmployees,
        int PendingLeave,
        int OnLeaveToday,
        int LoginsToday,
        // "This month" row
        int ClockInsToday,
        int HiresThisMonth,
        int PayrollRunsThisMonth,
        decimal PayrollTotalThisMonth,
        string? Error = null);

    /// <summary>Snapshot of NSCIM scan-portal metrics surfaced in the portal.</summary>
    public record NscimStats(
        bool IsAvailable,
        // "Today" row
        long TotalContainers,
        long ScansToday,
        int ScannersOnline,
        int ScannersTotal,
        int ActiveOperators,
        decimal CompletenessPercent,
        bool SystemsOperational,
        // "By scanner" row
        long TotalScannerScans,
        long AseScans,
        long FS6000Scans,
        DateTime? LastAseScan,
        DateTime? LastFS6000Scan,
        string? Error = null);

    public async Task<HrStats> GetHrStatsAsync(CancellationToken ct = default)
    {
        var conn = _config.GetConnectionString("NickHrDb");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return Empty("NickHrDb connection string not configured.");
        }

        try
        {
            await using var db = new NpgsqlConnection(conn);
            await db.OpenAsync(ct);

            // One round-trip, eight metrics via scalar subqueries.
            const string sql = @"
SELECT
  (SELECT COUNT(*) FROM public.""Employees""
     WHERE ""IsDeleted"" = false) AS active_employees,
  (SELECT COUNT(*) FROM public.""LeaveRequests""
     WHERE ""Status"" = 0) AS pending_leave,
  (SELECT COUNT(DISTINCT ""EmployeeId"") FROM public.""LeaveRequests""
     WHERE ""Status"" = 1
       AND ""StartDate"" <= current_date
       AND ""EndDate""   >= current_date) AS on_leave_today,
  (SELECT COUNT(DISTINCT ""Email"") FROM public.""LoginAudits""
     WHERE ""LoginTime"" >= current_date
       AND ""LoginTime"" <  current_date + interval '1 day'
       AND ""Success"" = true) AS logins_today,
  (SELECT COUNT(*) FROM public.""AttendanceRecords""
     WHERE ""Date"" = current_date) AS clock_ins_today,
  (SELECT COUNT(*) FROM public.""Employees""
     WHERE ""HireDate"" >= date_trunc('month', current_date)
       AND ""IsDeleted"" = false) AS hires_this_month,
  (SELECT COUNT(*) FROM public.""PayrollRuns""
     WHERE ""RunDate"" >= date_trunc('month', current_date)) AS payroll_runs_this_month,
  (SELECT COALESCE(SUM(""TotalNetPay"")::numeric, 0) FROM public.""PayrollRuns""
     WHERE ""RunDate"" >= date_trunc('month', current_date)) AS payroll_total_this_month
";

            await using var cmd = new NpgsqlCommand(sql, db);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Empty("No rows returned");
            }

            return new HrStats(
                IsAvailable:           true,
                ActiveEmployees:       SafeInt(reader, 0),
                PendingLeave:          SafeInt(reader, 1),
                OnLeaveToday:          SafeInt(reader, 2),
                LoginsToday:           SafeInt(reader, 3),
                ClockInsToday:         SafeInt(reader, 4),
                HiresThisMonth:        SafeInt(reader, 5),
                PayrollRunsThisMonth:  SafeInt(reader, 6),
                PayrollTotalThisMonth: SafeDecimal(reader, 7));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetHrStatsAsync failed");
            return Empty(ex.Message);
        }

        static HrStats Empty(string err)
            => new(false, 0, 0, 0, 0, 0, 0, 0, 0m, err);
    }

    public async Task<NscimStats> GetNscimStatsAsync(CancellationToken ct = default)
    {
        var summaryUrl = _config["Stats:NscimStatsUrl"];
        var dashUrl    = _config["Stats:NscimDashboardUrl"];
        if (string.IsNullOrWhiteSpace(summaryUrl) || string.IsNullOrWhiteSpace(dashUrl))
        {
            return Empty("NSCIM stats URLs not configured.");
        }

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(6);

            // Both feeds in parallel.
            var summaryTask = http.GetFromJsonAsync<SummaryPayload>(summaryUrl, ct);
            var dashTask    = http.GetFromJsonAsync<DashboardPayload>(dashUrl, ct);
            await Task.WhenAll(summaryTask, dashTask);
            var s = summaryTask.Result;
            var d = dashTask.Result;
            if (s is null || d is null)
            {
                return Empty("NSCIM returned empty payload.");
            }

            return new NscimStats(
                IsAvailable:          true,
                TotalContainers:      s.TotalContainers,
                ScansToday:           s.TodaysScans,
                ScannersOnline:       s.ScannersOnline,
                ScannersTotal:        s.ScannersTotal,
                ActiveOperators:      s.ActiveOperators,
                CompletenessPercent:  s.CompletenessPercent,
                SystemsOperational:   s.SystemsOperational,
                TotalScannerScans:    d.Scanners.TotalScans,
                AseScans:             d.Scanners.ASEScans,
                FS6000Scans:          d.Scanners.FS6000Scans,
                LastAseScan:          d.Scanners.LastASEScan,
                LastFS6000Scan:       d.Scanners.LastFS6000Scan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetNscimStatsAsync failed");
            return Empty(ex.Message);
        }

        static NscimStats Empty(string err) => new(false, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, null, null, err);
    }

    private static int SafeInt(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    private static decimal SafeDecimal(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));

    // Minimal JSON shape mirrors. Only declare fields we actually consume.
    private class SummaryPayload
    {
        public long TotalContainers { get; set; }
        public long TodaysScans { get; set; }
        public decimal CompletenessPercent { get; set; }
        public int ScannersOnline { get; set; }
        public int ScannersTotal { get; set; }
        public int ActiveOperators { get; set; }
        public bool SystemsOperational { get; set; }
    }

    private class DashboardPayload
    {
        public ScannerBlock Scanners { get; set; } = new();
    }
    private class ScannerBlock
    {
        public long TotalScans { get; set; }
        public long ASEScans { get; set; }
        public long FS6000Scans { get; set; }
        public DateTime? LastASEScan { get; set; }
        public DateTime? LastFS6000Scan { get; set; }
    }
}
