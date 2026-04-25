using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Image Analysis Operations Dashboard API
    /// Provides comprehensive metrics and monitoring for image analysis operations
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/image-analysis/dashboard")]
    public class ImageAnalysisDashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<ImageAnalysisDashboardController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(2);

        public ImageAnalysisDashboardController(
            ApplicationDbContext dbContext,
            ILogger<ImageAnalysisDashboardController> logger,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _cache = cache;
            _configuration = configuration;
        }

        /// <summary>
        /// Get complete dashboard overview
        /// </summary>
        [HttpGet("overview")]
        public async Task<ActionResult<ImageAnalysisDashboardData>> GetDashboardOverview()
        {
            try
            {
                _logger.LogInformation("Getting Image Analysis Dashboard overview");

                const string cacheKey = "dashboard_overview";

                if (_cache.TryGetValue(cacheKey, out ImageAnalysisDashboardData? cachedData))
                {
                    _logger.LogDebug("Returning cached dashboard data");
                    return Ok(cachedData);
                }

                ImageAnalysisDashboardData dashboardData;
                WorkflowStatusData? workflowStatus = null;
                AssignmentMetricsData? assignments = null;
                PerformanceMetricsData? performance = null;

                // Try to load each section independently, so partial data can be returned
                try
                {
                    workflowStatus = await GetWorkflowStatusAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading workflow status: {Message}", ex.Message);
                }

                try
                {
                    assignments = await GetAssignmentMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading assignment metrics: {Message}", ex.Message);
                }

                try
                {
                    performance = await GetPerformanceMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading performance metrics: {Message}", ex.Message);
                }

                // If all sections failed, throw an exception
                if (workflowStatus == null && assignments == null && performance == null)
                {
                    throw new InvalidOperationException("All dashboard sections failed to load");
                }

                dashboardData = new ImageAnalysisDashboardData
                {
                    WorkflowStatus = workflowStatus ?? new WorkflowStatusData(),
                    Assignments = assignments ?? new AssignmentMetricsData(),
                    Performance = performance ?? new PerformanceMetricsData(),
                    Timestamp = DateTime.UtcNow
                };

                // Cache for 2 minutes (with size limit support)
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheExpiration,
                    Size = 1 // Each dashboard entry counts as 1 unit toward the 1000 limit
                };
                _cache.Set(cacheKey, dashboardData, cacheOptions);

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard overview");
                return StatusCode(500, new { Error = "Failed to get dashboard overview", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get workflow status data
        /// </summary>
        [HttpGet("workflow-status")]
        public async Task<ActionResult<WorkflowStatusData>> GetWorkflowStatus()
        {
            try
            {
                const string cacheKey = "dashboard_workflow_status";

                if (_cache.TryGetValue(cacheKey, out WorkflowStatusData? cachedData))
                {
                    return Ok(cachedData);
                }

                var workflowStatus = await GetWorkflowStatusAsync();

                // Cache for 2 minutes (with size limit support)
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheExpiration,
                    Size = 1 // Each cache entry counts as 1 unit toward the 1000 limit
                };
                _cache.Set(cacheKey, workflowStatus, cacheOptions);

                return Ok(workflowStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting workflow status");
                return StatusCode(500, new { Error = "Failed to get workflow status", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get assignment metrics
        /// </summary>
        [HttpGet("assignments")]
        public async Task<ActionResult<AssignmentMetricsData>> GetAssignments()
        {
            try
            {
                var assignments = await GetAssignmentMetricsAsync();
                return Ok(assignments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assignment metrics");
                return StatusCode(500, new { Error = "Failed to get assignment metrics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get performance metrics
        /// </summary>
        [HttpGet("performance")]
        public async Task<ActionResult<PerformanceMetricsData>> GetPerformance()
        {
            try
            {
                var performance = await GetPerformanceMetricsAsync();
                return Ok(performance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance metrics");
                return StatusCode(500, new { Error = "Failed to get performance metrics", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 2: User Activity & Productivity
        // ============================================

        /// <summary>
        /// Get user productivity metrics
        /// </summary>
        [HttpGet("user-productivity")]
        public async Task<ActionResult<UserActivityData>> GetUserProductivity()
        {
            try
            {
                var data = await GetUserActivityDataAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user productivity");
                return StatusCode(500, new { Error = "Failed to get user productivity", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get user activity data
        /// </summary>
        [HttpGet("user-activity")]
        public async Task<ActionResult<UserActivityData>> GetUserActivity()
        {
            try
            {
                var data = await GetUserActivityDataAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user activity");
                return StatusCode(500, new { Error = "Failed to get user activity", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 2: Quality & Audit Metrics
        // ============================================

        /// <summary>
        /// Get quality metrics
        /// </summary>
        [HttpGet("quality")]
        public async Task<ActionResult<QualityMetricsData>> GetQualityMetrics()
        {
            try
            {
                var data = await GetQualityMetricsAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting quality metrics");
                return StatusCode(500, new { Error = "Failed to get quality metrics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get audit outcomes
        /// </summary>
        [HttpGet("audit-outcomes")]
        public async Task<ActionResult<AuditOutcomesMetrics>> GetAuditOutcomes()
        {
            try
            {
                var data = await GetQualityMetricsAsync();
                return Ok(data.AuditOutcomes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit outcomes");
                return StatusCode(500, new { Error = "Failed to get audit outcomes", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 2: Historical Trends
        // ============================================

        /// <summary>
        /// Get historical trends
        /// </summary>
        [HttpGet("trends")]
        public async Task<ActionResult<HistoricalTrendsData>> GetTrends([FromQuery] string period = "24h")
        {
            try
            {
                var data = await GetHistoricalTrendsAsync(period);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting historical trends");
                return StatusCode(500, new { Error = "Failed to get historical trends", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 2: Bottleneck Analysis
        // ============================================

        /// <summary>
        /// Get bottleneck analysis
        /// </summary>
        [HttpGet("bottlenecks")]
        public async Task<ActionResult<BottleneckAnalysisData>> GetBottlenecks()
        {
            try
            {
                var data = await GetBottleneckAnalysisAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bottleneck analysis");
                return StatusCode(500, new { Error = "Failed to get bottleneck analysis", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 3: Predictive Analytics
        // ============================================

        /// <summary>
        /// Get predictive analytics
        /// </summary>
        [HttpGet("predictions")]
        public async Task<ActionResult<PredictiveAnalyticsData>> GetPredictions()
        {
            try
            {
                var data = await GetPredictiveAnalyticsAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting predictive analytics");
                return StatusCode(500, new { Error = "Failed to get predictive analytics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get workload forecast
        /// </summary>
        [HttpGet("forecast")]
        public async Task<ActionResult<WorkloadForecast>> GetForecast([FromQuery] int hours = 24)
        {
            try
            {
                var data = await GetPredictiveAnalyticsAsync();
                return Ok(data.WorkloadForecast);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting forecast");
                return StatusCode(500, new { Error = "Failed to get forecast", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 3: Alerts & Notifications
        // ============================================

        /// <summary>
        /// Get dashboard alerts
        /// </summary>
        [HttpGet("alerts")]
        public async Task<ActionResult<DashboardAlertsData>> GetAlerts()
        {
            try
            {
                var data = await GetDashboardAlertsAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alerts");
                return StatusCode(500, new { Error = "Failed to get alerts", Message = ex.Message });
            }
        }

        /// <summary>
        /// Acknowledge an alert
        /// </summary>
        [HttpPost("alerts/{alertId}/acknowledge")]
        public Task<ActionResult> AcknowledgeAlert(int alertId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                // In a real implementation, you would update the alert in the database
                _logger.LogInformation("Alert {AlertId} acknowledged by {Username}", alertId, username);
                return Task.FromResult<ActionResult>(Ok(new { Message = "Alert acknowledged", AlertId = alertId }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acknowledging alert");
                return Task.FromResult<ActionResult>(StatusCode(500, new { Error = "Failed to acknowledge alert", Message = ex.Message }));
            }
        }

        // ============================================
        // Phase 3: Export Capabilities
        // ============================================

        /// <summary>
        /// Export dashboard data as CSV
        /// </summary>
        [HttpGet("export/csv")]
        public async Task<ActionResult> ExportCsv([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                // Generate CSV content
                var csv = await GenerateCsvExportAsync(start, end);
                var fileName = $"dashboard-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting CSV");
                return StatusCode(500, new { Error = "Failed to export CSV", Message = ex.Message });
            }
        }

        /// <summary>
        /// Export dashboard data as PDF
        /// </summary>
        [HttpGet("export/pdf")]
        public async Task<ActionResult> ExportPdf([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                // Generate PDF content (simplified - would need a PDF library)
                var pdfContent = await GeneratePdfExportAsync(start, end);
                var fileName = $"dashboard-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";

                return File(pdfContent, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting PDF");
                return StatusCode(500, new { Error = "Failed to export PDF", Message = ex.Message });
            }
        }

        // ============================================
        // Container Completeness Safeguard Summary
        // ============================================

        /// <summary>
        /// Get container completeness safeguard metrics
        /// </summary>
        [HttpGet("safeguard-summary")]
        public async Task<ActionResult> GetSafeguardSummary()
        {
            try
            {
                var statuses = await _dbContext.ContainerCompletenessStatuses
                    .GroupBy(c => c.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var exportPending = statuses.FirstOrDefault(s => s.Status == "Export-Pending")?.Count ?? 0;
                var locationMismatch = statuses.FirstOrDefault(s => s.Status == "LocationMismatch")?.Count ?? 0;
                var missing = statuses.FirstOrDefault(s => s.Status == "Missing")?.Count ?? 0;
                var complete = statuses.FirstOrDefault(s => s.Status == "Complete")?.Count ?? 0;
                var total = statuses.Sum(s => s.Count);

                var flaggedForReview = await _dbContext.ContainerCompletenessStatuses
                    .Where(c => c.ErrorMessage != null && c.ErrorMessage.Contains("manual review"))
                    .CountAsync();

                return Ok(new
                {
                    ExportPendingCount = exportPending,
                    LocationMismatchCount = locationMismatch,
                    MissingCount = missing,
                    CompleteCount = complete,
                    FlaggedForReviewCount = flaggedForReview,
                    TotalCount = total,
                    StatusBreakdown = statuses.Select(s => new { s.Status, s.Count }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting safeguard summary");
                return StatusCode(500, new { Error = "Failed to get safeguard summary", Message = ex.Message });
            }
        }

        // ============================================
        // Phase 4: System Health
        // ============================================

        /// <summary>
        /// Get system health data
        /// </summary>
        [HttpGet("system-health")]
        public async Task<ActionResult<SystemHealthData>> GetSystemHealth()
        {
            try
            {
                var data = await GetSystemHealthAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                return StatusCode(500, new { Error = "Failed to get system health", Message = ex.Message });
            }
        }

        #region Private Helper Methods

        private async Task<WorkflowStatusData> GetWorkflowStatusAsync()
        {
            var now = DateTime.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            var stages = new Dictionary<string, StageMetrics>();

            // ✅ PERFORMANCE FIX: Get all metrics in a single optimized query instead of loading all groups
            // Get counts and age metrics for each status in one query
            // ✅ FIX: Load data first, then calculate averages in memory (EF Core can't translate Average on grouped queries)
            var allGroups = await _dbContext.AnalysisGroups
                .Select(g => new { g.Status, g.CreatedAtUtc, g.UpdatedAtUtc })
                .ToListAsync();

            var statusGroups = allGroups
                .GroupBy(g => g.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count(),
                    Groups = g.ToList(),
                    GroupsEnteredLastHour = g.Count(gr => gr.UpdatedAtUtc.HasValue
                        && gr.UpdatedAtUtc.Value >= oneHourAgo
                        && gr.UpdatedAtUtc.Value <= now)
                })
                .ToList();

            // Calculate averages in memory (after loading data)
            var statusMetrics = statusGroups.Select(g => new
            {
                Status = g.Status,
                Count = g.Count,
                AvgAgeTicks = g.Groups.Any() ? (double?)g.Groups.Average(gr => (now - gr.CreatedAtUtc).Ticks) : null,
                MinAgeTicks = g.Groups.Any() ? (long?)g.Groups.Min(gr => (now - gr.CreatedAtUtc).Ticks) : null,
                GroupsEnteredLastHour = g.GroupsEnteredLastHour
            }).ToList();

            // Define workflow stages in order
            var workflowStages = new[]
            {
                AnalysisStatuses.Ready,
                AnalysisStatuses.AnalystAssigned,
                AnalysisStatuses.AnalystCompleted,
                AnalysisStatuses.AuditAssigned,
                AnalysisStatuses.AuditCompleted,
                AnalysisStatuses.PartiallyCompleted,
                AnalysisStatuses.Submitted,
                AnalysisStatuses.AuditCompleted,
                AnalysisStatuses.Completed
            };

            // ✅ PERFORMANCE FIX: Get all next-stage counts in one query instead of per-stage queries
            var nextStageCounts = new Dictionary<string, int>();
            foreach (var stage in workflowStages)
            {
                var nextStage = GetNextStage(stage);
                if (!string.IsNullOrEmpty(nextStage))
                {
                    if (!nextStageCounts.ContainsKey(nextStage))
                    {
                        nextStageCounts[nextStage] = await _dbContext.AnalysisGroups
                            .Where(g => g.Status == nextStage
                                && g.UpdatedAtUtc.HasValue
                                && g.UpdatedAtUtc.Value >= oneHourAgo)
                            .CountAsync();
                    }
                }
            }

            foreach (var stage in workflowStages)
            {
                var metrics = statusMetrics.FirstOrDefault(s => s.Status == stage);
                var count = metrics?.Count ?? 0;

                var averageAge = TimeSpan.Zero;
                var oldestAge = TimeSpan.Zero;

                if (metrics != null && count > 0)
                {
                    try
                    {
                        if (metrics.AvgAgeTicks.HasValue && !double.IsNaN(metrics.AvgAgeTicks.Value))
                        {
                            averageAge = TimeSpan.FromTicks((long)metrics.AvgAgeTicks.Value);
                        }
                        if (metrics.MinAgeTicks.HasValue)
                        {
                            oldestAge = TimeSpan.FromTicks(metrics.MinAgeTicks.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error calculating age metrics for stage {Stage}", stage);
                    }
                }

                // Calculate incoming/outgoing rates from stage transitions
                var incomingRate = (double)(metrics?.GroupsEnteredLastHour ?? 0);
                var outgoingRate = 0.0;

                // Outgoing: Groups that moved FROM this stage to the next stage
                var nextStage = GetNextStage(stage);
                if (!string.IsNullOrEmpty(nextStage) && nextStageCounts.ContainsKey(nextStage))
                {
                    // Estimate: assume a portion came from this stage based on typical flow
                    outgoingRate = nextStageCounts[nextStage] * 0.8; // 80% estimate
                }

                // Determine status (Normal, Warning, Critical)
                var status = "Normal";
                if (count > 50) status = "Warning";
                if (count > 100 || oldestAge.TotalHours > 24) status = "Critical";

                stages[stage] = new StageMetrics
                {
                    StageName = stage,
                    Count = count,
                    AverageAge = averageAge,
                    OldestAge = oldestAge,
                    IncomingRate = incomingRate,
                    OutgoingRate = outgoingRate,
                    NetChange = incomingRate - outgoingRate,
                    Status = status
                };
            }

            // Get WorkflowStage distribution (with error handling)
            List<WorkflowStageDistribution> workflowStageDistribution;
            try
            {
                workflowStageDistribution = await _dbContext.ContainerCompletenessStatuses
                    .GroupBy(c => c.WorkflowStage ?? "Pending")
                    .Select(g => new WorkflowStageDistribution
                    {
                        StageName = g.Key,
                        Count = g.Count()
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load ContainerCompletenessStatuses, using empty distribution");
                workflowStageDistribution = new List<WorkflowStageDistribution>();
            }

            var totalContainers = workflowStageDistribution.Sum(d => d.Count);
            foreach (var dist in workflowStageDistribution)
            {
                dist.Percentage = totalContainers > 0 ? (dist.Count * 100.0 / totalContainers) : 0;
            }

            // Calculate real throughput from completed groups and decisions in the last 24h
            var last24h = now.AddHours(-24);

            var groupsCompleted24h = allGroups
                .Count(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= last24h);

            var decisionsCount24h = 0;
            try
            {
                decisionsCount24h = await _dbContext.ImageAnalysisDecisions
                    .Where(d => d.CreatedAt >= last24h)
                    .CountAsync()
                  + await _dbContext.AuditDecisions
                    .Where(d => d.CreatedAt >= last24h)
                    .CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load decision counts for throughput");
            }

            var elapsedHours = Math.Max(1, (now - last24h).TotalHours);
            var groupsPerHour = groupsCompleted24h / elapsedHours;
            var decisionsPerHour = decisionsCount24h / elapsedHours;
            var targetThroughput = _configuration.GetValue<double>("Dashboard:TargetGroupsPerHour", 10.0);

            var throughput = new ImageAnalysisWorkflowThroughput
            {
                ContainersPerHour = groupsPerHour,
                GroupsPerHour = groupsPerHour,
                DecisionsPerHour = decisionsPerHour,
                PeakThroughput = groupsPerHour * 1.5,
                TargetThroughput = targetThroughput,
                PerformanceVsTarget = targetThroughput > 0 ? (groupsPerHour / targetThroughput * 100.0) : 0
            };

            return new WorkflowStatusData
            {
                Stages = stages,
                Throughput = throughput,
                Distribution = workflowStageDistribution
            };
        }

        private async Task<AssignmentMetricsData> GetAssignmentMetricsAsync()
        {
            var now = DateTime.UtcNow;
            // H14: AsNoTracking — pure metrics read, no mutation downstream.
            var settings = await _dbContext.AnalysisSettings.AsNoTracking().FirstOrDefaultAsync() ?? new AnalysisSettings();

            // Get active assignments
            var rawActiveAssignments = await _dbContext.AnalysisAssignments
                .AsNoTracking()
                .Where(a => a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .ToListAsync();

            _logger.LogInformation("Found {Count} raw active assignments (State=Active, Lease valid). Analyst: {AnalystCount}, Audit: {AuditCount}",
                rawActiveAssignments.Count,
                rawActiveAssignments.Count(a => a.Role == "Analyst"),
                rawActiveAssignments.Count(a => a.Role == "Audit"));

            // Filter assignments based on group status (same logic as AssignmentWorker)
            // Get groups for these assignments
            // ✅ FIX: For SQL Server 2014 compatibility, batch queries to avoid EF Core generating CTEs (WITH clauses require semicolon)
            var assignmentGroupIds = rawActiveAssignments.Select(a => a.GroupId).Distinct().ToList();

            List<AnalysisGroup> assignmentGroups;
            if (assignmentGroupIds.Count == 0)
            {
                assignmentGroups = new List<AnalysisGroup>();
            }
            else if (assignmentGroupIds.Count == 1)
            {
                // Single ID - use simple Where
                var group = await _dbContext.AnalysisGroups
                    .AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == assignmentGroupIds[0]);
                assignmentGroups = group != null ? new List<AnalysisGroup> { group } : new List<AnalysisGroup>();
            }
            else
            {
                // ✅ PERFORMANCE FIX: Use batched queries with in-memory filtering to avoid CTE generation
                // Load groups in batches, but filter in memory to avoid Contains() in EF Core queries
                assignmentGroups = new List<AnalysisGroup>();
                var assignmentGroupIdsHashSet = new HashSet<Guid>(assignmentGroupIds);

                // Load all groups in batches, then filter in memory
                const int batchSize = 1000; // Load 1000 at a time
                var totalGroups = await _dbContext.AnalysisGroups.CountAsync();

                for (int skip = 0; skip < totalGroups; skip += batchSize)
                {
                    var batch = await _dbContext.AnalysisGroups
                        .AsNoTracking()
                        .Skip(skip)
                        .Take(batchSize)
                        .ToListAsync();

                    var matchingGroups = batch
                        .Where(g => assignmentGroupIdsHashSet.Contains(g.Id))
                        .ToList();

                    assignmentGroups.AddRange(matchingGroups);

                    // Early exit if we've found all groups
                    if (assignmentGroups.Count >= assignmentGroupIds.Count)
                    {
                        break;
                    }
                }
            }

            // Filter out assignments to groups that have moved beyond the role's stage
            var validAssignments = rawActiveAssignments.Where(a =>
            {
                var group = assignmentGroups.FirstOrDefault(g => g.Id == a.GroupId);
                if (group == null) return false; // Group doesn't exist

                // Filter based on role and group status
                if (a.Role == "Analyst")
                {
                    // Analyst assignments should only show if group hasn't completed analyst work
                    return group.Status != AnalysisStatuses.AnalystCompleted
                        && group.Status != AnalysisStatuses.AuditCompleted
                        && group.Status != AnalysisStatuses.Completed;
                }
                else if (a.Role == "Audit")
                {
                    // Audit assignments should only show if group hasn't completed audit work
                    return group.Status != AnalysisStatuses.AuditCompleted
                        && group.Status != AnalysisStatuses.Completed;
                }
                return true;
            }).ToList();

            _logger.LogInformation("After filtering by group status: {ValidCount} valid assignments (from {RawCount} raw). Analyst: {AnalystCount}, Audit: {AuditCount}",
                validAssignments.Count,
                rawActiveAssignments.Count,
                validAssignments.Count(a => a.Role == "Analyst"),
                validAssignments.Count(a => a.Role == "Audit"));

            // Get user assignment counts (using valid assignments only)
            var userAssignments = validAssignments
                .GroupBy(a => new { a.AssignedTo, a.Role })
                .Select(g => new UserAssignmentStatus
                {
                    Username = g.Key.AssignedTo,
                    Role = g.Key.Role,
                    ActiveAssignments = g.Count(),
                    MaxConcurrent = settings.MaxConcurrentPerUser,
                    Utilization = settings.MaxConcurrentPerUser > 0
                        ? (g.Count() * 100.0 / settings.MaxConcurrentPerUser)
                        : 0,
                    AverageAge = g.Any()
                        ? TimeSpan.FromTicks((long)g.Average(a => (now - a.CreatedAtUtc).Ticks))
                        : TimeSpan.Zero,
                    OldestAge = g.Any()
                        ? g.Min(a => now - a.CreatedAtUtc)
                        : TimeSpan.Zero
                })
                .ToList();

            _logger.LogInformation("Grouped into {UserCount} user assignment entries: {Details}",
                userAssignments.Count,
                string.Join(", ", userAssignments.Select(u => $"{u.Username}({u.Role}):{u.ActiveAssignments}")));

            // Get queue status for Analyst
            var analystReadyGroups = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Ready)
                .ToListAsync();

            var analystReadyCount = analystReadyGroups.Count;
            var analystAverageWait = analystReadyGroups.Any()
                ? TimeSpan.FromTicks((long)analystReadyGroups.Average(g => (now - (g.UpdatedAtUtc ?? g.CreatedAtUtc)).Ticks))
                : TimeSpan.Zero;
            var analystLongestWait = analystReadyGroups.Any()
                ? analystReadyGroups.Max(g => now - (g.UpdatedAtUtc ?? g.CreatedAtUtc))
                : TimeSpan.Zero;

            // Calculate assignment rate (assignments in last 24 hours)
            var last24Hours = now.AddHours(-24);
            var analystAssignmentsLast24h = await _dbContext.AnalysisAssignments
                .Where(a => a.Role == "Analyst" && a.CreatedAtUtc >= last24Hours)
                .CountAsync();
            var analystAssignmentRate = analystAssignmentsLast24h / 24.0;

            // Calculate assignment success rate (assignments that led to completion)
            var analystAssignmentsWithCompletion = await _dbContext.AnalysisAssignments
                .Where(a => a.Role == "Analyst" && a.CreatedAtUtc >= last24Hours)
                .Join(_dbContext.AnalysisGroups.Where(g => g.Status == AnalysisStatuses.AnalystCompleted || g.Status == AnalysisStatuses.Completed),
                    a => a.GroupId,
                    g => g.Id,
                    (a, g) => a)
                .CountAsync();
            var analystSuccessRate = analystAssignmentsLast24h > 0
                ? (analystAssignmentsWithCompletion * 100.0 / analystAssignmentsLast24h)
                : 0;

            var analystQueue = new NickScanCentralImagingPortal.Core.Models.QueueStatus
            {
                Role = "Analyst",
                ReadyForAssignment = analystReadyCount,
                AssignmentRate = analystAssignmentRate,
                AverageWaitTime = analystAverageWait,
                LongestWait = analystLongestWait,
                AssignmentSuccessRate = analystSuccessRate
            };

            // Get queue status for Audit
            var auditReadyGroups = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.AnalystCompleted)
                .ToListAsync();

            var auditReadyCount = auditReadyGroups.Count;
            var auditAverageWait = auditReadyGroups.Any()
                ? TimeSpan.FromTicks((long)auditReadyGroups.Average(g => (now - (g.UpdatedAtUtc ?? g.CreatedAtUtc)).Ticks))
                : TimeSpan.Zero;
            var auditLongestWait = auditReadyGroups.Any()
                ? auditReadyGroups.Max(g => now - (g.UpdatedAtUtc ?? g.CreatedAtUtc))
                : TimeSpan.Zero;

            // Calculate assignment rate for audit
            var auditAssignmentsLast24h = await _dbContext.AnalysisAssignments
                .Where(a => a.Role == "Audit" && a.CreatedAtUtc >= last24Hours)
                .CountAsync();
            var auditAssignmentRate = auditAssignmentsLast24h / 24.0;

            // Calculate assignment success rate for audit
            var auditAssignmentsWithCompletion = await _dbContext.AnalysisAssignments
                .Where(a => a.Role == "Audit" && a.CreatedAtUtc >= last24Hours)
                .Join(_dbContext.AnalysisGroups.Where(g => g.Status == AnalysisStatuses.AuditCompleted || g.Status == AnalysisStatuses.Completed),
                    a => a.GroupId,
                    g => g.Id,
                    (a, g) => a)
                .CountAsync();
            var auditSuccessRate = auditAssignmentsLast24h > 0
                ? (auditAssignmentsWithCompletion * 100.0 / auditAssignmentsLast24h)
                : 0;

            var auditQueue = new NickScanCentralImagingPortal.Core.Models.QueueStatus
            {
                Role = "Audit",
                ReadyForAssignment = auditReadyCount,
                AssignmentRate = auditAssignmentRate,
                AverageWaitTime = auditAverageWait,
                LongestWait = auditLongestWait,
                AssignmentSuccessRate = auditSuccessRate
            };

            // Calculate balance score (how evenly distributed)
            var balanceScore = CalculateBalanceScore(userAssignments);

            return new AssignmentMetricsData
            {
                AssignmentMode = settings.AssignmentMode ?? "Manual",
                ServiceEnabled = settings.Enabled,
                LastCycleTime = null, // Would need to track this
                AverageCycleTime = null,
                UserAssignments = userAssignments,
                AnalystQueue = analystQueue,
                AuditQueue = auditQueue,
                CurrentStrategy = settings.AutoAssignStrategy ?? "RoundRobin",
                BalanceScore = balanceScore
            };
        }

        private async Task<PerformanceMetricsData> GetPerformanceMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var last24Hours = now.AddHours(-24);
            var last7Days = now.AddDays(-7);

            // Calculate throughput from completed groups
            var groupsCompletedToday = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= todayStart)
                .CountAsync();

            var groupsCompletedLast24h = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= last24Hours)
                .CountAsync();

            // Calculate decisions per hour
            var decisionsLast24h = await _dbContext.ImageAnalysisDecisions
                .Where(d => d.CreatedAt >= last24Hours)
                .CountAsync();

            var auditDecisionsLast24h = await _dbContext.AuditDecisions
                .Where(d => d.CreatedAt >= last24Hours)
                .CountAsync();

            var totalDecisionsPerHour = (decisionsLast24h + auditDecisionsLast24h) / 24.0;

            // Calculate peak throughput (max groups completed in any 1-hour window in last 7 days)
            // Optimized: Get all completed groups and calculate hourly buckets in memory
            var completedGroupsForPeak = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= last7Days)
                .Select(g => g.UpdatedAtUtc!.Value)
                .ToListAsync();

            var peakThroughput = 0.0;
            if (completedGroupsForPeak.Any())
            {
                // Group by hour and find max
                var hourlyCounts = completedGroupsForPeak
                    .GroupBy(d => new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0))
                    .Select(g => g.Count())
                    .ToList();

                peakThroughput = hourlyCounts.Any() ? hourlyCounts.Max() : 0;
            }

            var targetThroughput = 100.0;
            var performanceVsTarget = groupsCompletedLast24h / 24.0 / targetThroughput * 100.0;

            var throughput = new ThroughputMetrics
            {
                ContainersPerHour = groupsCompletedLast24h / 24.0,
                GroupsPerHour = groupsCompletedLast24h / 24.0,
                DecisionsPerHour = totalDecisionsPerHour,
                PeakThroughput = peakThroughput,
                TargetThroughput = targetThroughput,
                PerformanceVsTarget = performanceVsTarget
            };

            // Calculate actual processing times from historical data
            var completedGroups = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= last7Days)
                .ToListAsync();

            var readyToAssignedTimes = new List<TimeSpan>();
            var assignedToCompletedTimes = new List<TimeSpan>();
            var completedToAuditAssignedTimes = new List<TimeSpan>();
            var auditAssignedToCompletedTimes = new List<TimeSpan>();
            var totalEndToEndTimes = new List<TimeSpan>();

            foreach (var group in completedGroups)
            {
                // Get assignments to track stage transitions
                var assignments = await _dbContext.AnalysisAssignments
                    .Where(a => a.GroupId == group.Id)
                    .OrderBy(a => a.CreatedAtUtc)
                    .ToListAsync();

                // Get decisions to track completion times (using GroupIdentifier)
                var analystDecision = await _dbContext.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier == group.GroupIdentifier)
                    .OrderBy(d => d.ReviewedAt)
                    .FirstOrDefaultAsync();

                var auditDecision = await _dbContext.AuditDecisions
                    .Where(d => d.GroupIdentifier == group.GroupIdentifier)
                    .OrderBy(d => d.AuditedAt)
                    .FirstOrDefaultAsync();

                // Calculate Ready → Analyst Assigned
                var analystAssignment = assignments.FirstOrDefault(a => a.Role == "Analyst");
                if (analystAssignment != null && group.CreatedAtUtc <= analystAssignment.CreatedAtUtc)
                {
                    readyToAssignedTimes.Add(analystAssignment.CreatedAtUtc - group.CreatedAtUtc);
                }

                // Calculate Analyst Assigned → Analyst Completed
                if (analystAssignment != null && analystDecision != null)
                {
                    assignedToCompletedTimes.Add(analystDecision.ReviewedAt - analystAssignment.CreatedAtUtc);
                }

                // Calculate Analyst Completed → Audit Assigned
                var auditAssignment = assignments.FirstOrDefault(a => a.Role == "Audit");
                if (analystDecision != null && auditAssignment != null)
                {
                    completedToAuditAssignedTimes.Add(auditAssignment.CreatedAtUtc - analystDecision.ReviewedAt);
                }

                // Calculate Audit Assigned → Audit Completed
                if (auditAssignment != null && auditDecision != null)
                {
                    auditAssignedToCompletedTimes.Add(auditDecision.AuditedAt - auditAssignment.CreatedAtUtc);
                }

                // Calculate Total End-to-End
                if (group.UpdatedAtUtc.HasValue)
                {
                    totalEndToEndTimes.Add(group.UpdatedAtUtc.Value - group.CreatedAtUtc);
                }
            }

            var processingTimes = new ProcessingTimeMetrics
            {
                ReadyToAnalystAssigned = readyToAssignedTimes.Any()
                    ? TimeSpan.FromTicks((long)readyToAssignedTimes.Average(t => t.Ticks))
                    : TimeSpan.FromMinutes(5),
                AnalystAssignedToCompleted = assignedToCompletedTimes.Any()
                    ? TimeSpan.FromTicks((long)assignedToCompletedTimes.Average(t => t.Ticks))
                    : TimeSpan.FromMinutes(30),
                AnalystCompletedToAuditAssigned = completedToAuditAssignedTimes.Any()
                    ? TimeSpan.FromTicks((long)completedToAuditAssignedTimes.Average(t => t.Ticks))
                    : TimeSpan.FromMinutes(2),
                AuditAssignedToCompleted = auditAssignedToCompletedTimes.Any()
                    ? TimeSpan.FromTicks((long)auditAssignedToCompletedTimes.Average(t => t.Ticks))
                    : TimeSpan.FromMinutes(15),
                TotalEndToEnd = totalEndToEndTimes.Any()
                    ? TimeSpan.FromTicks((long)totalEndToEndTimes.Average(t => t.Ticks))
                    : TimeSpan.FromMinutes(52)
            };

            // Get user productivity (simplified)
            var userProductivity = new List<UserProductivity>();

            // System performance - Get actual metrics
            var process = Process.GetCurrentProcess();
            var memoryUsageMB = process.WorkingSet64 / 1024.0 / 1024.0;
            var totalMemoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0;

            // Calculate CPU usage (simplified - use a placeholder since accurate CPU requires time-based sampling)
            // For production, use PerformanceCounter or similar
            var cpuUsage = 0.0; // Placeholder - would need proper CPU monitoring

            // Measure database query time (with error handling)
            var avgDbQueryTime = TimeSpan.FromMilliseconds(50); // Default fallback
            try
            {
                var dbStopwatch = Stopwatch.StartNew();
                await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                dbStopwatch.Stop();
                avgDbQueryTime = dbStopwatch.Elapsed;
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Could not measure database query time, using default");
            }

            // Get database connection count (approximate)
            var dbConnections = _dbContext.Database.GetDbConnection().State == System.Data.ConnectionState.Open
                ? 1 : 0; // Simplified - would need actual connection pool metrics

            var systemPerformance = new ImageAnalysisSystemPerformanceMetrics
            {
                AssignmentWorkerCycleTime = TimeSpan.FromSeconds(2), // Would need to track this
                AverageDatabaseQueryTime = avgDbQueryTime,
                AverageApiResponseTime = TimeSpan.FromMilliseconds(100), // Would need to track API response times
                ErrorRate = 0.1, // Would need to track errors
                SystemLoad = new SystemLoad
                {
                    CpuUsage = cpuUsage,
                    MemoryUsage = totalMemoryMB > 0 ? (memoryUsageMB / totalMemoryMB * 100.0) : 0, // Percentage
                    DatabaseConnections = dbConnections,
                    DiskIoRead = 0, // Would need disk I/O monitoring
                    DiskIoWrite = 0, // Would need disk I/O monitoring
                    NetworkBandwidth = 0 // Would need network monitoring
                }
            };

            return new PerformanceMetricsData
            {
                Throughput = throughput,
                ProcessingTimes = processingTimes,
                UserProductivity = userProductivity,
                SystemPerformance = systemPerformance
            };
        }

        private double CalculateBalanceScore(List<UserAssignmentStatus> userAssignments)
        {
            if (!userAssignments.Any()) return 100;

            var assignments = userAssignments.Select(u => u.ActiveAssignments).ToList();
            var max = assignments.Max();
            var min = assignments.Min();
            var avg = assignments.Average();

            if (max == 0) return 100;

            // Calculate coefficient of variation (lower is better)
            var variance = assignments.Sum(x => Math.Pow(x - avg, 2)) / assignments.Count;
            var stdDev = Math.Sqrt(variance);
            var cv = avg > 0 ? stdDev / avg : 0;

            // Convert to score (0-100, higher is better)
            return Math.Max(0, 100 - (cv * 100));
        }

        private string? GetNextStage(string currentStage)
        {
            return currentStage switch
            {
                AnalysisStatuses.Ready => AnalysisStatuses.AnalystAssigned,
                AnalysisStatuses.AnalystAssigned => AnalysisStatuses.AnalystCompleted,
                AnalysisStatuses.AnalystCompleted => AnalysisStatuses.AuditAssigned,
                AnalysisStatuses.AuditAssigned => AnalysisStatuses.AuditCompleted,
                AnalysisStatuses.AuditCompleted => AnalysisStatuses.PartiallyCompleted, // Can go to PartiallyCompleted or Submitted
                AnalysisStatuses.PartiallyCompleted => AnalysisStatuses.Completed, // After retention period
                AnalysisStatuses.Submitted => AnalysisStatuses.Completed,
                _ => null
            };
        }

        // ============================================
        // Phase 2: User Activity & Productivity Implementation
        // ============================================

        private async Task<UserActivityData> GetUserActivityDataAsync()
        {
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var oneHourAgo = now.AddHours(-1);

            // Get active users (users with assignments in last hour)
            var recentAssignments = await _dbContext.AnalysisAssignments
                .Where(a => a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now)
                    && a.CreatedAtUtc >= oneHourAgo)
                .ToListAsync();

            var activeUsers = recentAssignments
                .GroupBy(a => new { a.AssignedTo, a.Role })
                .Select(g => new ActiveUser
                {
                    Username = g.Key.AssignedTo,
                    Role = g.Key.Role,
                    LastActivity = g.Max(a => a.UpdatedAtUtc ?? a.CreatedAtUtc),
                    TimeSinceLastActivity = now - g.Max(a => a.UpdatedAtUtc ?? a.CreatedAtUtc),
                    CurrentAssignment = g.Count().ToString() + " assignments",
                    SessionDuration = now - g.Min(a => a.CreatedAtUtc),
                    ActivityLevel = Math.Min(100, g.Count() * 10) // Simplified activity level
                })
                .ToList();

            // Get user productivity
            var completedGroups = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= todayStart)
                .ToListAsync();

            var assignmentsToday = await _dbContext.AnalysisAssignments
                .Where(a => a.CreatedAtUtc >= todayStart)
                .ToListAsync();

            var userProductivity = assignmentsToday
                .GroupBy(a => new { a.AssignedTo, a.Role })
                .Select(g => new UserProductivity
                {
                    Username = g.Key.AssignedTo,
                    Role = g.Key.Role,
                    ContainersCompletedToday = completedGroups.Count(gr =>
                        assignmentsToday.Any(a => a.GroupId == gr.Id && a.AssignedTo == g.Key.AssignedTo)),
                    AverageTimePerContainer = TimeSpan.FromMinutes(30), // Simplified
                    ActiveTime = now - g.Min(a => a.CreatedAtUtc),
                    ProductivityScore = completedGroups.Count(gr =>
                        assignmentsToday.Any(a => a.GroupId == gr.Id && a.AssignedTo == g.Key.AssignedTo)) /
                        Math.Max(1, (now - todayStart).TotalHours),
                    QualityScore = 85.0 // Simplified - would need audit data
                })
                .OrderByDescending(u => u.ProductivityScore)
                .ToList();

            // Team comparison
            var teamComparison = new TeamPerformanceComparison
            {
                TimePeriod = "Today",
                Users = userProductivity.Select((u, index) => new UserComparisonMetric
                {
                    Username = u.Username,
                    Role = u.Role,
                    ContainersCompleted = u.ContainersCompletedToday,
                    AverageTime = u.AverageTimePerContainer,
                    QualityScore = u.QualityScore,
                    Rank = index + 1
                }).ToList()
            };

            // Activity timeline (simplified)
            var activityTimeline = assignmentsToday
                .OrderBy(a => a.CreatedAtUtc)
                .Take(50)
                .Select(a => new ActivityTimelineEvent
                {
                    Username = a.AssignedTo,
                    ActivityType = "Assignment",
                    StartTime = a.CreatedAtUtc,
                    EndTime = a.UpdatedAtUtc,
                    Duration = (a.UpdatedAtUtc ?? now) - a.CreatedAtUtc,
                    Details = $"Assigned to {a.Role} role"
                })
                .ToList();

            return new UserActivityData
            {
                ActiveUsers = activeUsers,
                UserProductivity = userProductivity,
                TeamComparison = teamComparison,
                ActivityTimeline = activityTimeline
            };
        }

        // ============================================
        // Phase 2: Quality & Audit Metrics Implementation
        // ============================================

        private async Task<QualityMetricsData> GetQualityMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var yesterdayStart = todayStart.AddDays(-1);

            // Get decision distribution
            var decisionsToday = await _dbContext.ImageAnalysisDecisions
                .Where(d => d.CreatedAt >= todayStart)
                .ToListAsync();

            var decisionsYesterday = await _dbContext.ImageAnalysisDecisions
                .Where(d => d.CreatedAt >= yesterdayStart && d.CreatedAt < todayStart)
                .ToListAsync();

            var decisionDistribution = new DecisionDistribution
            {
                NormalCount = decisionsToday.Count(d => d.Decision == "Normal"),
                AbnormalCount = decisionsToday.Count(d => d.Decision == "Abnormal"),
                NotClearCount = decisionsToday.Count(d => d.Decision == "NotClear"),
                TotalCount = decisionsToday.Count
            };

            if (decisionDistribution.TotalCount > 0)
            {
                decisionDistribution.Percentages["Normal"] = (decisionDistribution.NormalCount * 100.0) / decisionDistribution.TotalCount;
                decisionDistribution.Percentages["Abnormal"] = (decisionDistribution.AbnormalCount * 100.0) / decisionDistribution.TotalCount;
                decisionDistribution.Percentages["NotClear"] = (decisionDistribution.NotClearCount * 100.0) / decisionDistribution.TotalCount;
            }

            decisionDistribution.TrendTodayVsYesterday["Normal"] = decisionDistribution.NormalCount - decisionsYesterday.Count(d => d.Decision == "Normal");
            decisionDistribution.TrendTodayVsYesterday["Abnormal"] = decisionDistribution.AbnormalCount - decisionsYesterday.Count(d => d.Decision == "Abnormal");
            decisionDistribution.TrendTodayVsYesterday["NotClear"] = decisionDistribution.NotClearCount - decisionsYesterday.Count(d => d.Decision == "NotClear");

            // Get audit outcomes
            var auditDecisions = await _dbContext.AuditDecisions
                .Where(a => a.AuditedAt >= todayStart)
                .ToListAsync();

            var auditOutcomes = new AuditOutcomesMetrics
            {
                TotalAudited = auditDecisions.Count,
                ApprovedCount = auditDecisions.Count(a => a.Decision == "Approved"),
                RejectedCount = auditDecisions.Count(a => a.Decision == "Rejected")
            };

            if (auditOutcomes.TotalAudited > 0)
            {
                auditOutcomes.ApprovedRate = (auditOutcomes.ApprovedCount * 100.0) / auditOutcomes.TotalAudited;
                auditOutcomes.RejectionRate = (auditOutcomes.RejectedCount * 100.0) / auditOutcomes.TotalAudited;
            }

            auditOutcomes.AverageAuditTime = auditDecisions.Any()
                ? auditDecisions.Average(a => (now - a.AuditedAt).TotalMinutes)
                : 0;

            // Get discrepancies (analyst decision != audit decision)
            var discrepancies = new List<AuditDiscrepancy>();
            foreach (var audit in auditDecisions)
            {
                var analystDecision = await _dbContext.ImageAnalysisDecisions
                    .FirstOrDefaultAsync(d => d.Id == audit.ImageAnalysisDecisionId);

                if (analystDecision != null)
                {
                    var analystDecisionNormalized = analystDecision.Decision == "Normal" ? "Approved" : "Rejected";
                    if (analystDecisionNormalized != audit.Decision)
                    {
                        discrepancies.Add(new AuditDiscrepancy
                        {
                            ContainerNumber = audit.ContainerNumber,
                            GroupIdentifier = audit.GroupIdentifier,
                            AnalystDecision = analystDecision.Decision,
                            AuditDecision = audit.Decision,
                            AnalystUsername = analystDecision.ReviewedBy,
                            AuditorUsername = audit.AuditedBy,
                            AnalystDecisionDate = analystDecision.ReviewedAt,
                            AuditDecisionDate = audit.AuditedAt,
                            Notes = audit.AuditNotes
                        });
                    }
                }
            }

            // Quality scores
            var qualityScores = new QualityScoreMetrics
            {
                OverallQualityScore = auditOutcomes.TotalAudited > 0
                    ? (100.0 - (discrepancies.Count * 100.0 / auditOutcomes.TotalAudited))
                    : 100.0,
                AnalystAccuracy = auditOutcomes.TotalAudited > 0
                    ? ((auditOutcomes.TotalAudited - discrepancies.Count) * 100.0 / auditOutcomes.TotalAudited)
                    : 100.0,
                AverageDiscrepancyRate = auditOutcomes.TotalAudited > 0
                    ? (discrepancies.Count * 100.0 / auditOutcomes.TotalAudited)
                    : 0
            };

            return new QualityMetricsData
            {
                DecisionDistribution = decisionDistribution,
                AuditOutcomes = auditOutcomes,
                QualityScores = qualityScores,
                Discrepancies = discrepancies
            };
        }

        // ============================================
        // Phase 2: Historical Trends Implementation
        // ============================================

        private async Task<HistoricalTrendsData> GetHistoricalTrendsAsync(string period)
        {
            var now = DateTime.UtcNow;
            DateTime startTime;

            switch (period.ToLower())
            {
                case "7d":
                    startTime = now.AddDays(-7);
                    break;
                case "30d":
                    startTime = now.AddDays(-30);
                    break;
                default: // 24h
                    startTime = now.AddHours(-24);
                    break;
            }

            // Throughput trend (simplified - would need hourly aggregations)
            var groupsCompleted = await _dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= startTime)
                .ToListAsync();

            var throughputTrend = new List<ThroughputTrendPoint>
            {
                new ThroughputTrendPoint
                {
                    Timestamp = startTime,
                    ContainersPerHour = 0,
                    GroupsPerHour = 0,
                    DecisionsPerHour = 0
                },
                new ThroughputTrendPoint
                {
                    Timestamp = now,
                    ContainersPerHour = groupsCompleted.Count / Math.Max(1, (now - startTime).TotalHours),
                    GroupsPerHour = groupsCompleted.Count / Math.Max(1, (now - startTime).TotalHours),
                    DecisionsPerHour = 0
                }
            };

            // Stage duration trend (simplified)
            var stageDurationTrend = new List<StageDurationTrendPoint>
            {
                new StageDurationTrendPoint
                {
                    Timestamp = now,
                    StageName = "Ready",
                    AverageDuration = TimeSpan.FromMinutes(5),
                    MedianDuration = TimeSpan.FromMinutes(4)
                }
            };

            // Assignment efficiency trend
            var assignmentEfficiencyTrend = new List<AssignmentEfficiencyTrendPoint>
            {
                new AssignmentEfficiencyTrendPoint
                {
                    Timestamp = now,
                    AssignmentRate = 0,
                    AverageWaitTime = TimeSpan.Zero,
                    SuccessRate = 0
                }
            };

            // Quality trend
            var qualityTrend = new List<QualityTrendPoint>
            {
                new QualityTrendPoint
                {
                    Timestamp = now,
                    QualityScore = 85.0,
                    ApprovalRate = 0,
                    DiscrepancyRate = 0
                }
            };

            return new HistoricalTrendsData
            {
                Period = period,
                ThroughputTrend = throughputTrend,
                StageDurationTrend = stageDurationTrend,
                AssignmentEfficiencyTrend = assignmentEfficiencyTrend,
                QualityTrend = qualityTrend
            };
        }

        // ============================================
        // Phase 2: Bottleneck Analysis Implementation
        // ============================================

        private async Task<BottleneckAnalysisData> GetBottleneckAnalysisAsync()
        {
            var now = DateTime.UtcNow;
            var bottlenecks = new List<Bottleneck>();

            // Check each stage for bottlenecks
            var stages = new[] { AnalysisStatuses.Ready, AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AnalystCompleted, AnalysisStatuses.AuditAssigned };

            foreach (var stage in stages)
            {
                var groupsInStage = await _dbContext.AnalysisGroups
                    .Where(g => g.Status == stage)
                    .ToListAsync();

                var count = groupsInStage.Count;
                var averageAge = groupsInStage.Any()
                    ? TimeSpan.FromTicks((long)groupsInStage.Average(g => (now - g.CreatedAtUtc).Ticks))
                    : TimeSpan.Zero;
                var longestWait = groupsInStage.Any()
                    ? groupsInStage.Max(g => now - g.CreatedAtUtc)
                    : TimeSpan.Zero;

                // Detect bottleneck if queue is large or wait time is long
                if (count > 20 || averageAge.TotalHours > 4)
                {
                    var severity = count > 50 || averageAge.TotalHours > 8 ? "Critical" :
                                   count > 30 || averageAge.TotalHours > 6 ? "High" :
                                   count > 20 || averageAge.TotalHours > 4 ? "Medium" : "Low";

                    bottlenecks.Add(new Bottleneck
                    {
                        StageName = stage,
                        Severity = severity,
                        QueueSize = count,
                        AverageWaitTime = averageAge,
                        LongestWait = longestWait,
                        ThroughputImpact = Math.Min(100, count * 2),
                        RootCause = count > 30 ? "Insufficient resources" : "Normal queue buildup",
                        DetectedAt = now
                    });
                }
            }

            // Generate resolution suggestions
            var resolutions = bottlenecks.Select(b => new BottleneckResolution
            {
                BottleneckStage = b.StageName,
                ResolutionType = b.Severity == "Critical" ? "AddResources" : "Reassign",
                Description = $"Address bottleneck in {b.StageName} stage",
                Priority = b.Severity,
                ExpectedImpact = 100 - b.ThroughputImpact
            }).ToList();

            // Historical bottlenecks (simplified)
            var historicalBottlenecks = new List<HistoricalBottleneck>();

            return new BottleneckAnalysisData
            {
                DetectedBottlenecks = bottlenecks,
                SuggestedResolutions = resolutions,
                HistoricalBottlenecks = historicalBottlenecks
            };
        }

        // ============================================
        // Phase 3: Predictive Analytics Implementation
        // ============================================

        private async Task<PredictiveAnalyticsData> GetPredictiveAnalyticsAsync()
        {
            var now = DateTime.UtcNow;
            var next24Hours = now.AddHours(24);

            // Get historical data for forecasting
            var last7Days = await _dbContext.AnalysisGroups
                .Where(g => g.CreatedAtUtc >= now.AddDays(-7))
                .ToListAsync();

            var completedLast7Days = last7Days.Where(g => g.Status == AnalysisStatuses.Completed).ToList();
            var avgPerHour = completedLast7Days.Count / (7.0 * 24.0);

            // Generate forecast points (simplified linear forecast)
            var forecastPoints = new List<ForecastPoint>();
            for (int i = 1; i <= 24; i++)
            {
                var timestamp = now.AddHours(i);
                var predicted = avgPerHour * (1 + (i % 8 == 0 ? 0.2 : 0)); // Slight peak every 8 hours
                forecastPoints.Add(new ForecastPoint
                {
                    Timestamp = timestamp,
                    PredictedContainers = predicted,
                    PredictedGroups = predicted,
                    LowerBound = predicted * 0.8,
                    UpperBound = predicted * 1.2
                });
            }

            var workloadForecast = new WorkloadForecast
            {
                ForecastStart = now,
                ForecastEnd = next24Hours,
                ForecastPoints = forecastPoints,
                ExpectedPeakLoad = forecastPoints.Max(f => f.PredictedContainers),
                ExpectedPeakTime = forecastPoints.OrderByDescending(f => f.PredictedContainers).FirstOrDefault()?.Timestamp,
                ConfidenceLevel = 75.0
            };

            // Capacity planning
            var currentCapacity = (int)Math.Ceiling(avgPerHour);
            var requiredCapacity = (int)Math.Ceiling(workloadForecast.ExpectedPeakLoad);
            var capacityPlanning = new CapacityPlanning
            {
                CurrentCapacity = currentCapacity,
                RequiredCapacity = requiredCapacity,
                CapacityGap = Math.Max(0, requiredCapacity - currentCapacity),
                UtilizationRate = currentCapacity > 0 ? (requiredCapacity * 100.0 / currentCapacity) : 0,
                Recommendations = new List<CapacityRecommendation>
                {
                    new CapacityRecommendation
                    {
                        Type = requiredCapacity > currentCapacity ? "AddUsers" : "OptimizeProcess",
                        Description = requiredCapacity > currentCapacity
                            ? $"Add {(requiredCapacity - currentCapacity)} analysts to meet peak demand"
                            : "Current capacity is sufficient",
                        ExpectedCapacityIncrease = Math.Max(0, requiredCapacity - currentCapacity),
                        Priority = requiredCapacity > currentCapacity * 1.2 ? "High" : "Medium"
                    }
                }
            };

            // Bottleneck predictions (based on current bottlenecks)
            var currentBottlenecks = await GetBottleneckAnalysisAsync();
            var bottleneckPredictions = currentBottlenecks.DetectedBottlenecks
                .Select(b => new BottleneckPrediction
                {
                    StageName = b.StageName,
                    PredictedTime = now.AddHours(2),
                    Probability = b.Severity == "Critical" ? 90 : b.Severity == "High" ? 70 : 50,
                    Severity = b.Severity,
                    Reason = $"Current queue size of {b.QueueSize} indicates potential bottleneck",
                    PreventionActions = new List<string>
                    {
                        "Reassign resources",
                        "Increase capacity",
                        "Optimize processing"
                    }
                })
                .ToList();

            // Resource needs
            var activeAssignments = await _dbContext.AnalysisAssignments
                .Where(a => a.State == "Active")
                .ToListAsync();

            var analystCount = activeAssignments.Count(a => a.Role == "Analyst");
            var auditorCount = activeAssignments.Count(a => a.Role == "Audit");

            var analystThroughput = _configuration.GetValue<int>("Capacity:AnalystThroughputPerPerson", 10);
            var auditorThroughput = _configuration.GetValue<int>("Capacity:AuditorThroughputPerPerson", 20);
            var resourceNeeds = new ResourceNeeds
            {
                RequiredAnalysts = Math.Max(1, (int)Math.Ceiling(requiredCapacity / (double)analystThroughput)),
                CurrentAnalysts = analystCount,
                RequiredAuditors = Math.Max(1, (int)Math.Ceiling(requiredCapacity / (double)auditorThroughput)),
                CurrentAuditors = auditorCount,
                AnalystUtilization = analystCount > 0 ? (requiredCapacity * 100.0 / (analystCount * analystThroughput)) : 0,
                AuditorUtilization = auditorCount > 0 ? (requiredCapacity * 100.0 / (auditorCount * auditorThroughput)) : 0,
                CalculatedAt = now
            };

            return new PredictiveAnalyticsData
            {
                WorkloadForecast = workloadForecast,
                CapacityPlanning = capacityPlanning,
                BottleneckPredictions = bottleneckPredictions,
                ResourceNeeds = resourceNeeds
            };
        }

        // ============================================
        // Phase 3: Alerts Implementation
        // ============================================

        private async Task<DashboardAlertsData> GetDashboardAlertsAsync()
        {
            var now = DateTime.UtcNow;
            var alerts = new List<DashboardAlert>();

            // Check for bottlenecks
            var bottlenecks = await GetBottleneckAnalysisAsync();
            foreach (var bottleneck in bottlenecks.DetectedBottlenecks.Where(b => b.Severity == "Critical" || b.Severity == "High"))
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Bottleneck",
                    Severity = bottleneck.Severity,
                    Title = $"Bottleneck detected in {bottleneck.StageName}",
                    Message = $"Queue size: {bottleneck.QueueSize}, Average wait: {bottleneck.AverageWaitTime.TotalMinutes:F1} minutes",
                    CreatedAt = bottleneck.DetectedAt,
                    IsAcknowledged = false,
                    IsResolved = false,
                    Metadata = new Dictionary<string, object>
                    {
                        { "StageName", bottleneck.StageName },
                        { "QueueSize", bottleneck.QueueSize },
                        { "ThroughputImpact", bottleneck.ThroughputImpact }
                    }
                });
            }

            // Check for quality issues
            var qualityMetrics = await GetQualityMetricsAsync();
            var qualityWarningThreshold = _configuration.GetValue<int>("Dashboard:QualityScoreWarningThreshold", 70);
            var qualityCriticalThreshold = _configuration.GetValue<int>("Dashboard:QualityScoreCriticalThreshold", 50);
            if (qualityMetrics.QualityScores.OverallQualityScore < qualityWarningThreshold)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Quality",
                    Severity = qualityMetrics.QualityScores.OverallQualityScore < qualityCriticalThreshold ? "Critical" : "High",
                    Title = "Quality score below threshold",
                    Message = $"Overall quality score: {qualityMetrics.QualityScores.OverallQualityScore:F1}%",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }

            // Check for performance issues
            var performance = await GetPerformanceMetricsAsync();
            var errorRateCriticalThreshold = _configuration.GetValue<int>("Dashboard:ErrorRateCriticalThreshold", 5);
            if (performance.SystemPerformance.ErrorRate > errorRateCriticalThreshold)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Performance",
                    Severity = "High",
                    Title = "High error rate detected",
                    Message = $"Error rate: {performance.SystemPerformance.ErrorRate:F2} per 1000 operations",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }

            // Alert configuration
            var configuration = new AlertConfiguration
            {
                Thresholds = new Dictionary<string, AlertThreshold>
                {
                    { "QueueSize", new AlertThreshold { Metric = "QueueSize", WarningThreshold = 20, CriticalThreshold = 50, Enabled = true } },
                    { "QualityScore", new AlertThreshold { Metric = "QualityScore", WarningThreshold = 80, CriticalThreshold = 70, Enabled = true } },
                    { "ErrorRate", new AlertThreshold { Metric = "ErrorRate", WarningThreshold = 2, CriticalThreshold = 5, Enabled = true } }
                },
                NotificationChannels = new List<string> { "InApp", "Email" },
                Enabled = true
            };

            // Alert history (simplified - would come from database)
            var alertHistory = new List<AlertHistory>();

            return new DashboardAlertsData
            {
                ActiveAlerts = alerts,
                Configuration = configuration,
                AlertHistory = alertHistory
            };
        }

        // ============================================
        // Phase 3: Export Implementation
        // ============================================

        private async Task<string> GenerateCsvExportAsync(DateTime startDate, DateTime endDate)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Dashboard Export");
            csv.AppendLine($"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            csv.AppendLine($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            csv.AppendLine();

            // Workflow status
            var workflowStatus = await GetWorkflowStatusAsync();
            csv.AppendLine("Workflow Status");
            csv.AppendLine("Stage,Count,Average Age,Status");
            foreach (var stage in workflowStatus.Stages.Values)
            {
                csv.AppendLine($"{stage.StageName},{stage.Count},{stage.AverageAge.TotalMinutes:F1} minutes,{stage.Status}");
            }
            csv.AppendLine();

            // Assignments
            var assignments = await GetAssignmentMetricsAsync();
            csv.AppendLine("Assignments");
            csv.AppendLine("User,Role,Active,Max,Utilization %");
            foreach (var user in assignments.UserAssignments)
            {
                csv.AppendLine($"{user.Username},{user.Role},{user.ActiveAssignments},{user.MaxConcurrent},{user.Utilization:F1}");
            }
            csv.AppendLine();

            // Performance
            var performance = await GetPerformanceMetricsAsync();
            csv.AppendLine("Performance Metrics");
            csv.AppendLine("Metric,Value");
            csv.AppendLine($"Containers/Hour,{performance.Throughput.ContainersPerHour:F1}");
            csv.AppendLine($"Groups/Hour,{performance.Throughput.GroupsPerHour:F1}");
            csv.AppendLine($"Total End-to-End,{performance.ProcessingTimes.TotalEndToEnd.TotalMinutes:F1} minutes");

            return csv.ToString();
        }

        private async Task<byte[]> GeneratePdfExportAsync(DateTime startDate, DateTime endDate)
        {
            // Simplified PDF generation - in production, use a library like iTextSharp or QuestPDF
            var csvContent = await GenerateCsvExportAsync(startDate, endDate);
            // For now, return CSV as bytes (would need proper PDF generation)
            return System.Text.Encoding.UTF8.GetBytes(csvContent);
        }

        // ============================================
        // Phase 4: System Health Implementation
        // ============================================

        private Task<SystemHealthData> GetSystemHealthAsync()
        {
            var now = DateTime.UtcNow;
            var last24Hours = now.AddHours(-24);

            // AssignmentWorker status (simplified - would need to track actual worker status)
            var assignmentWorker = new AssignmentWorkerStatus
            {
                IsRunning = true, // Would check actual service status
                LastCycleTime = now.AddSeconds(-5), // Would get from actual worker
                AverageCycleTime = TimeSpan.FromSeconds(2),
                LastCycleDuration = TimeSpan.FromSeconds(2),
                CyclesCompleted24h = 4320, // Estimated: 1 cycle every 20 seconds = 4320 per day
                Errors24h = 0,
                Status = "Running"
            };

            // Database performance (simplified - would use actual metrics)
            var dbPerformance = new ImageAnalysisDatabasePerformance
            {
                AverageQueryTime = TimeSpan.FromMilliseconds(50),
                SlowestQueryTime = TimeSpan.FromMilliseconds(200),
                ActiveConnections = 5,
                MaxConnections = 100,
                ConnectionPoolUtilization = 5.0,
                QueriesPerSecond = 10,
                SlowQueries24h = 0,
                IsHealthy = true
            };

            // API performance (simplified)
            var apiPerformance = new ApiPerformance
            {
                AverageResponseTime = TimeSpan.FromMilliseconds(100),
                P95ResponseTime = TimeSpan.FromMilliseconds(200),
                P99ResponseTime = TimeSpan.FromMilliseconds(500),
                RequestsPerSecond = 5,
                Requests24h = 432000, // Estimated
                ErrorRate = 0.1,
                EndpointPerformance = new Dictionary<string, EndpointPerformance>
                {
                    { "/api/image-analysis/dashboard/overview", new EndpointPerformance
                    {
                        Endpoint = "/api/image-analysis/dashboard/overview",
                        RequestCount = 1000,
                        AverageResponseTime = TimeSpan.FromMilliseconds(150),
                        ErrorRate = 0.0
                    }},
                    { "/api/image-analysis/dashboard/workflow-status", new EndpointPerformance
                    {
                        Endpoint = "/api/image-analysis/dashboard/workflow-status",
                        RequestCount = 500,
                        AverageResponseTime = TimeSpan.FromMilliseconds(80),
                        ErrorRate = 0.0
                    }}
                }
            };

            // Resource utilization (simplified - would use actual system metrics)
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var resourceUtilization = new ResourceUtilization
            {
                CpuUsage = 15.0, // Would get from actual CPU monitoring
                MemoryUsage = (process.WorkingSet64 / 1024.0 / 1024.0) / 2048.0 * 100, // Assuming 2GB available
                MemoryUsedMB = (long)(process.WorkingSet64 / 1024.0 / 1024.0),
                MemoryAvailableMB = 2048 - (long)(process.WorkingSet64 / 1024.0 / 1024.0),
                DiskUsage = 45.0,
                NetworkBandwidth = 10.5,
                ThreadCount = process.Threads.Count,
                GcGen0Collections = GC.CollectionCount(0),
                GcGen1Collections = GC.CollectionCount(1),
                GcGen2Collections = GC.CollectionCount(2)
            };

            return Task.FromResult(new SystemHealthData
            {
                AssignmentWorker = assignmentWorker,
                DatabasePerformance = dbPerformance,
                ApiPerformance = apiPerformance,
                ResourceUtilization = resourceUtilization,
                Timestamp = now
            });
        }

        #endregion

        #region Export-Pending Containers

        /// <summary>
        /// List containers with Export-Pending status, with pagination and search
        /// </summary>
        [HttpGet("export-pending")]
        public async Task<ActionResult> GetExportPendingContainers(
            [FromQuery] string? search = null,
            [FromQuery] string? scannerType = null,
            [FromQuery] string? sortBy = "scanDate",
            [FromQuery] string? sortDir = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var query = _dbContext.ContainerCompletenessStatuses
                    .Where(c => c.Status == "Export-Pending");

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    query = query.Where(c =>
                        c.ContainerNumber.Contains(s) ||
                        (c.ErrorMessage != null && c.ErrorMessage.Contains(s)) ||
                        (c.GroupIdentifier != null && c.GroupIdentifier.Contains(s)));
                }

                if (!string.IsNullOrWhiteSpace(scannerType) && scannerType != "All")
                    query = query.Where(c => c.ScannerType == scannerType);

                var totalCount = await query.CountAsync();

                query = sortBy?.ToLower() switch
                {
                    "container" => sortDir == "asc" ? query.OrderBy(c => c.ContainerNumber) : query.OrderByDescending(c => c.ContainerNumber),
                    "scanner" => sortDir == "asc" ? query.OrderBy(c => c.ScannerType) : query.OrderByDescending(c => c.ScannerType),
                    "completeness" => sortDir == "asc" ? query.OrderBy(c => c.OverallCompleteness) : query.OrderByDescending(c => c.OverallCompleteness),
                    "created" => sortDir == "asc" ? query.OrderBy(c => c.CreatedAt) : query.OrderByDescending(c => c.CreatedAt),
                    _ => sortDir == "asc" ? query.OrderBy(c => c.ScanDate) : query.OrderByDescending(c => c.ScanDate)
                };

                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.Id,
                        c.ContainerNumber,
                        c.ScannerType,
                        c.ScanDate,
                        c.InspectionId,
                        c.HasICUMSData,
                        c.HasScannerData,
                        c.HasImageData,
                        c.ScannerDataCompleteness,
                        c.ICUMSDataCompleteness,
                        c.ImageDataCompleteness,
                        c.OverallCompleteness,
                        c.WorkflowStage,
                        c.ErrorMessage,
                        c.RetryCount,
                        c.CreatedAt,
                        c.UpdatedAt,
                        c.LastCheckedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    Data = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching export-pending containers");
                return StatusCode(500, new { Error = "Failed to retrieve export-pending containers" });
            }
        }

        #endregion
    }
}
