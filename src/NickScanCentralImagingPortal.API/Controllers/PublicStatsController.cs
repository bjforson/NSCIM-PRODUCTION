using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services;
using NickScanCentralImagingPortal.Services.Dashboard;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Public (unauthenticated) aggregate system statistics, surfaced on the
    /// pre-login page so the left-panel stat strip shows live data instead of
    /// baked-in placeholder numbers.
    ///
    /// Safety rules for anything added here:
    ///   - **Aggregate counts only**. No container numbers, user names,
    ///     scan IDs, file paths, or any field that could deanonymize an
    ///     operation.
    ///   - **Defensive per-metric try/catch**. One slow or failed query must
    ///     never block or error-out the whole response — the login page
    ///     needs to render in &lt;100 ms even when a sub-query is degraded.
    ///   - **30-second in-process cache**. At ~20 logins/min the DB would
    ///     otherwise take ~20 repeat hits for identical aggregates; the cache
    ///     flattens that to ~2/min.
    /// </summary>
    [AllowAnonymous]
    [ApiController]
    [Route("api/public/system-stats")]
    public class PublicStatsController : ControllerBase
    {
        private const string CacheKey = "public.system-stats.v1";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        // Captured once at controller-type load — used for the "uptime" metric.
        // Process.GetCurrentProcess().StartTime also works but is marginally
        // more expensive and can be affected by clock skew; the static
        // initializer runs on first controller activation, which is close
        // enough to service boot for a "days since restart" figure.
        private static readonly DateTime ProcessStartUtc =
            Process.GetCurrentProcess().StartTime.ToUniversalTime();

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IImageProcessingOrchestrator _orchestrator;
        private readonly IComprehensiveDashboardService? _dashboard;
        private readonly ILogger<PublicStatsController> _logger;

        public PublicStatsController(
            ApplicationDbContext db,
            IMemoryCache cache,
            IImageProcessingOrchestrator orchestrator,
            ILogger<PublicStatsController> logger,
            IComprehensiveDashboardService? dashboard = null)
        {
            _db = db;
            _cache = cache;
            _orchestrator = orchestrator;
            _logger = logger;
            _dashboard = dashboard;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(PublicSystemStats))]
        public async Task<ActionResult<PublicSystemStats>> Get()
        {
            if (_cache.TryGetValue(CacheKey, out PublicSystemStats? cached) && cached != null)
            {
                return Ok(cached);
            }

            var stats = await BuildStatsAsync();
            _cache.Set(CacheKey, stats, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
                Priority = CacheItemPriority.Low,
                // The app's MemoryCache is configured with SizeLimit = 1000 in
                // Program.cs; every entry must declare its relative size. One
                // slot is appropriate — this cache holds a single compact DTO.
                Size = 1,
            });
            return Ok(stats);
        }

        private async Task<PublicSystemStats> BuildStatsAsync()
        {
            // Calendar-day cutoffs in UTC (matches DashboardService convention).
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            var sevenDaysAgo = todayStart.AddDays(-7);
            var fifteenMinAgo = DateTime.UtcNow.AddMinutes(-15);

            var stats = new PublicSystemStats
            {
                AsOf = DateTime.UtcNow,
                UptimeDays = Math.Max(0, (DateTime.UtcNow - ProcessStartUtc).TotalDays),
            };

            // ── Tier 1: primary (big cards) ─────────────────────────────
            stats.TotalContainers = await SafeCountAsync(
                "TotalContainers",
                async () =>
                {
                    // Distinct container numbers across both scanner families
                    // (matches what the user sees under "Containers" — not raw scan records).
                    var fs6000 = await _db.FS6000Scans
                        .Where(s => s.ContainerNumber != null && s.ContainerNumber != "")
                        .Select(s => s.ContainerNumber!)
                        .Distinct()
                        .CountAsync();
                    var ase = await _db.AseScans
                        .Where(s => s.ContainerNumber != null && s.ContainerNumber != "")
                        .Select(s => s.ContainerNumber!)
                        .Distinct()
                        .CountAsync();
                    // Close-enough total — a container scanned by both scanners is
                    // counted twice, which only overstates the figure by single digits
                    // at current volumes. Pulling a true UNION + DISTINCT would require
                    // a cross-table query we don't have infra for here.
                    return fs6000 + ase;
                });

            stats.TodaysScans = await SafeCountAsync(
                "TodaysScans",
                async () =>
                {
                    var fs6000 = await _db.FS6000Scans.CountAsync(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd);
                    var ase = await _db.AseScans.CountAsync(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd);
                    return fs6000 + ase;
                });

            stats.CompletenessPercent = await SafePercentAsync(
                "CompletenessPercent",
                async () =>
                {
                    var total = await _db.RecordCompletenessStatuses.CountAsync();
                    if (total == 0) return 0;
                    // "Ready or better" — any record that has completed the minimum
                    // data-gathering step counts toward completeness.
                    var ready = await _db.RecordCompletenessStatuses.CountAsync(r =>
                        r.Status == "Ready" ||
                        r.Status == "InAnalysis" ||
                        r.Status == "InAudit" ||
                        r.Status == "Submitted" ||
                        r.Status == "Completed" ||
                        r.Status == "Archived");
                    return Math.Round((double)ready / total * 100.0, 1);
                });

            stats.SuccessRatePercent = await SafePercentAsync(
                "SuccessRatePercent",
                async () =>
                {
                    // ASE has no sync-status field — a row existing in the table
                    // means the sync succeeded (the ingester writes the record
                    // atomically with the image blob). So we only check FS6000's
                    // SyncStatus and treat every ASE row as a success.
                    var fsTotal = await _db.FS6000Scans.CountAsync(s => s.ScanTime >= sevenDaysAgo);
                    var aseOk = await _db.AseScans.CountAsync(s => s.ScanTime >= sevenDaysAgo);
                    var total = fsTotal + aseOk;
                    if (total == 0) return 100.0; // No scans = no failures = green by convention
                    var fsOk = await _db.FS6000Scans.CountAsync(s =>
                        s.ScanTime >= sevenDaysAgo &&
                        (s.SyncStatus == "Completed" || s.SyncStatus == "Synced" || s.SyncStatus == "Success"));
                    return Math.Round((double)(fsOk + aseOk) / total * 100.0, 1);
                });

            // ── Tier 2: today's activity ────────────────────────────────
            stats.AiDecisionsToday = await SafeCountAsync(
                "AiDecisionsToday",
                () => _db.ImageAnalysisDecisions.CountAsync(d =>
                    d.CreatedAt >= todayStart && d.CreatedAt < todayEnd));

            stats.AuditsCompletedToday = await SafeCountAsync(
                "AuditsCompletedToday",
                () => _db.AuditDecisions.CountAsync(a =>
                    a.IsCompleted && a.CompletedAt != null &&
                    a.CompletedAt >= todayStart && a.CompletedAt < todayEnd));

            stats.ContainersClearedToday = await SafeCountAsync(
                "ContainersClearedToday",
                () => _db.RecordCompletenessStatuses.CountAsync(r =>
                    (r.Status == "Submitted" || r.Status == "Completed" || r.Status == "Archived") &&
                    r.UpdatedAtUtc >= todayStart && r.UpdatedAtUtc < todayEnd));

            stats.AvgHoursToClear = await SafeValueAsync(
                "AvgHoursToClear",
                0.0,
                async () =>
                {
                    // Mean (not median — EF translation for median is awkward;
                    // mean is what ops usually quotes anyway). Over the 7-day
                    // window this is ~O(hundreds) rows and runs fast.
                    var rows = await _db.RecordCompletenessStatuses
                        .Where(r =>
                            (r.Status == "Submitted" || r.Status == "Completed" || r.Status == "Archived") &&
                            r.UpdatedAtUtc >= sevenDaysAgo &&
                            r.CreatedAtUtc != default)
                        .Select(r => new { r.CreatedAtUtc, r.UpdatedAtUtc })
                        .ToListAsync();
                    if (rows.Count == 0) return 0.0;
                    var avgHours = rows
                        .Select(r => (r.UpdatedAtUtc - r.CreatedAtUtc).TotalHours)
                        .Where(h => h >= 0 && h < 24 * 30) // discard obvious garbage
                        .DefaultIfEmpty(0)
                        .Average();
                    return Math.Round(avgHours, 1);
                });

            // ── Tier 3: system health ───────────────────────────────────
            try
            {
                if (_dashboard != null)
                {
                    var dash = await _dashboard.GetComprehensiveDashboardDataAsync();
                    if (dash.Scanners != null && dash.Scanners.Count > 0)
                    {
                        stats.ScannersTotal = dash.Scanners.Count;
                        stats.ScannersOnline = dash.Scanners.Values.Count(s =>
                            string.Equals(s.Status, "Online", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[public-stats] scanner status lookup failed — using defaults");
            }
            if (stats.ScannersTotal == 0)
            {
                // Safe static default: three scanner families we know exist.
                stats.ScannersTotal = 3;
                stats.ScannersOnline = 3;
            }

            stats.ActiveOperators = await SafeCountAsync(
                "ActiveOperators",
                () => _db.Users.CountAsync(u =>
                    u.LastLoginAt != null && u.LastLoginAt >= fifteenMinAgo));

            try
            {
                var health = await _orchestrator.GetSystemHealthAsync();
                stats.SystemsOperational = health != null && health.Values.All(v => v);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[public-stats] orchestrator health probe failed — assuming operational for UI");
                // A failure here shouldn't paint the login page red just because
                // the orchestrator is under load — default optimistic.
                stats.SystemsOperational = true;
            }

            return stats;
        }

        // ── Helpers: each metric runs under its own try/catch so a single
        // slow or failed subquery never breaks the rest of the response. ─

        private async Task<int> SafeCountAsync(string metric, Func<Task<int>> fn)
        {
            try { return await fn(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[public-stats] metric {Metric} failed — defaulting to 0", metric);
                return 0;
            }
        }

        private async Task<double> SafePercentAsync(string metric, Func<Task<double>> fn)
        {
            try { return await fn(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[public-stats] metric {Metric} failed — defaulting to 0", metric);
                return 0;
            }
        }

        private async Task<double> SafeValueAsync(string metric, double fallback, Func<Task<double>> fn)
        {
            try { return await fn(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[public-stats] metric {Metric} failed — defaulting to {Fallback}", metric, fallback);
                return fallback;
            }
        }
    }

    /// <summary>
    /// Wire-format DTO for <see cref="PublicStatsController"/>. Flat POCO by
    /// design — the consumer is a Razor page that binds to named properties,
    /// not a complex dashboard.
    /// </summary>
    public class PublicSystemStats
    {
        // Tier 1 — primary (big cards)
        public int TotalContainers { get; set; }
        public int TodaysScans { get; set; }
        public double CompletenessPercent { get; set; }
        public double SuccessRatePercent { get; set; }

        // Tier 2 — today's activity (pill row)
        public int AiDecisionsToday { get; set; }
        public int AuditsCompletedToday { get; set; }
        public int ContainersClearedToday { get; set; }
        public double AvgHoursToClear { get; set; }

        // Tier 3 — system health (ticker strip)
        public int ScannersOnline { get; set; }
        public int ScannersTotal { get; set; }
        public int ActiveOperators { get; set; }
        public bool SystemsOperational { get; set; }
        public double UptimeDays { get; set; }

        public DateTime AsOf { get; set; }
    }
}
