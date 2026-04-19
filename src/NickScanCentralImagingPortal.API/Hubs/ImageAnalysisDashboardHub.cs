using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using Npgsql;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR Hub for Image Analysis Operations Dashboard real-time updates
    /// </summary>
    public class ImageAnalysisDashboardHub : Hub
    {
        private readonly ILogger<ImageAnalysisDashboardHub> _logger;

        public ImageAnalysisDashboardHub(ILogger<ImageAnalysisDashboardHub> logger)
        {
            _logger = logger;
        }

        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected to ImageAnalysisDashboardHub: {ConnectionId}", Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected from ImageAnalysisDashboardHub: {ConnectionId}", Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Join a specific dashboard group (for targeted updates)
        /// </summary>
        public async Task JoinDashboardGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} joined dashboard group: {GroupName}", Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Leave a dashboard group
        /// </summary>
        public async Task LeaveDashboardGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogDebug("Client {ConnectionId} left dashboard group: {GroupName}", Context.ConnectionId, groupName);
        }
    }

    /// <summary>
    /// Background service that broadcasts Image Analysis Dashboard updates via SignalR
    /// Updates clients every 10 seconds with fresh dashboard data
    /// </summary>
    public class ImageAnalysisDashboardBroadcastService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ImageAnalysisDashboardBroadcastService> _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private List<DashboardAlert> _previousAlerts = new();
        private bool _applicationLogsTableExists = true;

        public ImageAnalysisDashboardBroadcastService(
            IServiceScopeFactory scopeFactory,
            ILogger<ImageAnalysisDashboardBroadcastService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Image Analysis Dashboard Broadcast Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ImageAnalysisDashboardHub>>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                    // Get dashboard data
                    var dashboardData = await GetDashboardDataAsync(dbContext, icumsDownloadsDbContext, stoppingToken);

                    // Broadcast to all connected clients
                    await hubContext.Clients.All.SendAsync("DashboardUpdate", dashboardData, stoppingToken);

                    // Check for new alerts and broadcast them
                    var previousAlerts = _previousAlerts;
                    var currentAlerts = await GetCurrentAlertsAsync(dbContext, stoppingToken);

                    // Find new alerts (not in previous list)
                    var newAlerts = currentAlerts
                        .Where(a => !previousAlerts.Any(pa => pa.Id == a.Id))
                        .ToList();

                    foreach (var alert in newAlerts)
                    {
                        await hubContext.Clients.All.SendAsync("NewAlert", alert, stoppingToken);
                        _logger.LogInformation("🚨 New alert broadcasted: {AlertTitle} ({Severity})", alert.Title, alert.Severity);
                    }

                    _previousAlerts = currentAlerts;

                    _logger.LogDebug("✅ Broadcasted dashboard update to {ClientCount} clients",
                        await GetClientCountAsync(hubContext));

                    await Task.Delay(_updateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Check if it's a database connectivity error
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _logger.LogWarning(ex, "❌ Database connectivity issue during dashboard update (This is normal during startup or when SQL Server is unavailable). Retrying in 30 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Wait longer when database is unavailable
                    }
                    else
                    {
                        _logger.LogError(ex, "❌ Error broadcasting dashboard update");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Retry after 5 seconds on error
                    }
                }
            }

            _logger.LogInformation("🛑 Image Analysis Dashboard Broadcast Service stopping...");
        }

        private async Task<ImageAnalysisDashboardData> GetDashboardDataAsync(
            ApplicationDbContext dbContext,
            IcumDownloadsDbContext icumsDownloadsDbContext,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var stages = new Dictionary<string, StageMetrics>();

            // Get counts for each status
            var statusCounts = await dbContext.AnalysisGroups
                .GroupBy(g => g.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

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
                AnalysisStatuses.Completed
            };

            foreach (var stage in workflowStages)
            {
                var count = statusCounts.FirstOrDefault(s => s.Status == stage)?.Count ?? 0;

                var groupsInStage = await dbContext.AnalysisGroups
                    .Where(g => g.Status == stage)
                    .ToListAsync(cancellationToken);

                var averageAge = groupsInStage.Any()
                    ? TimeSpan.FromTicks((long)groupsInStage.Average(g => (now - g.CreatedAtUtc).Ticks))
                    : TimeSpan.Zero;

                var oldestAge = groupsInStage.Any()
                    ? groupsInStage.Min(g => now - g.CreatedAtUtc)
                    : TimeSpan.Zero;

                var oneHourAgo = now.AddHours(-1);
                var recentGroups = groupsInStage.Where(g => g.UpdatedAtUtc.HasValue && g.UpdatedAtUtc.Value >= oneHourAgo).Count();
                var incomingRate = (double)recentGroups;

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
                    OutgoingRate = 0, // Would need historical tracking
                    NetChange = incomingRate,
                    Status = status
                };
            }

            // Get WorkflowStage distribution
            var workflowStageDistribution = await dbContext.ContainerCompletenessStatuses
                .GroupBy(c => c.WorkflowStage ?? "Pending")
                .Select(g => new WorkflowStageDistribution
                {
                    StageName = g.Key,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

            var totalContainers = workflowStageDistribution.Sum(d => d.Count);
            foreach (var dist in workflowStageDistribution)
            {
                dist.Percentage = totalContainers > 0 ? (dist.Count * 100.0 / totalContainers) : 0;
            }

            var throughput = new ImageAnalysisWorkflowThroughput
            {
                ContainersPerHour = 0,
                GroupsPerHour = 0,
                DecisionsPerHour = 0,
                PeakThroughput = 0,
                TargetThroughput = 100,
                PerformanceVsTarget = 0
            };

            var workflowStatus = new WorkflowStatusData
            {
                Stages = stages,
                Throughput = throughput,
                Distribution = workflowStageDistribution
            };

            // Get assignment metrics
            var settings = await dbContext.AnalysisSettings.FirstOrDefaultAsync(cancellationToken) ?? new AnalysisSettings();
            var activeAssignments = await dbContext.AnalysisAssignments
                .Where(a => a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .ToListAsync(cancellationToken);

            var userAssignments = activeAssignments
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
                    AverageAge = TimeSpan.FromTicks((long)g.Average(a => (now - a.CreatedAtUtc).Ticks)),
                    OldestAge = g.Min(a => now - a.CreatedAtUtc)
                })
                .ToList();

            var analystReadyCount = await dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Ready)
                .CountAsync(cancellationToken);

            var auditReadyCount = await dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.AnalystCompleted)
                .CountAsync(cancellationToken);

            var balanceScore = CalculateBalanceScore(userAssignments);

            var assignments = new AssignmentMetricsData
            {
                AssignmentMode = settings.AssignmentMode ?? "Manual",
                ServiceEnabled = settings.Enabled,
                LastCycleTime = null,
                AverageCycleTime = null,
                UserAssignments = userAssignments,
                AnalystQueue = new QueueStatus
                {
                    Role = "Analyst",
                    ReadyForAssignment = analystReadyCount,
                    AssignmentRate = 0,
                    AverageWaitTime = TimeSpan.Zero,
                    LongestWait = TimeSpan.Zero,
                    AssignmentSuccessRate = 0
                },
                AuditQueue = new QueueStatus
                {
                    Role = "Audit",
                    ReadyForAssignment = auditReadyCount,
                    AssignmentRate = 0,
                    AverageWaitTime = TimeSpan.Zero,
                    LongestWait = TimeSpan.Zero,
                    AssignmentSuccessRate = 0
                },
                CurrentStrategy = settings.AutoAssignStrategy ?? "RoundRobin",
                BalanceScore = balanceScore
            };

            // Get performance metrics (simplified)
            var todayStart = now.Date;
            var groupsCompletedToday = await dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Completed
                    && g.UpdatedAtUtc.HasValue
                    && g.UpdatedAtUtc.Value >= todayStart)
                .CountAsync(cancellationToken);

            var performance = new PerformanceMetricsData
            {
                Throughput = new ThroughputMetrics
                {
                    ContainersPerHour = 0,
                    GroupsPerHour = groupsCompletedToday / Math.Max(1, now.Hour),
                    DecisionsPerHour = 0,
                    PeakThroughput = 0,
                    TargetThroughput = 100,
                    PerformanceVsTarget = 0
                },
                ProcessingTimes = new ProcessingTimeMetrics
                {
                    ReadyToAnalystAssigned = TimeSpan.FromMinutes(5),
                    AnalystAssignedToCompleted = TimeSpan.FromMinutes(30),
                    AnalystCompletedToAuditAssigned = TimeSpan.FromMinutes(2),
                    AuditAssignedToCompleted = TimeSpan.FromMinutes(15),
                    TotalEndToEnd = TimeSpan.FromMinutes(52)
                },
                UserProductivity = new List<UserProductivity>(),
                SystemPerformance = new ImageAnalysisSystemPerformanceMetrics
                {
                    AssignmentWorkerCycleTime = TimeSpan.FromSeconds(2),
                    AverageDatabaseQueryTime = TimeSpan.FromMilliseconds(50),
                    AverageApiResponseTime = TimeSpan.FromMilliseconds(100),
                    ErrorRate = 0.1,
                    SystemLoad = new SystemLoad
                    {
                        CpuUsage = 0,
                        MemoryUsage = 0,
                        DatabaseConnections = 0,
                        DiskIoRead = 0,
                        DiskIoWrite = 0,
                        NetworkBandwidth = 0
                    }
                }
            };

            // Get data integrity metrics
            var dataIntegrity = await GetDataIntegrityMetricsAsync(dbContext, icumsDownloadsDbContext, cancellationToken);

            // Get real-time readiness status
            var realTimeReadiness = await GetRealTimeReadinessStatusAsync(dbContext, cancellationToken);

            return new ImageAnalysisDashboardData
            {
                WorkflowStatus = workflowStatus,
                Assignments = assignments,
                Performance = performance,
                DataIntegrity = dataIntegrity,
                RealTimeReadiness = realTimeReadiness,
                Timestamp = now
            };
        }

        private async Task<DataIntegrityMetricsData> GetDataIntegrityMetricsAsync(
            ApplicationDbContext dbContext,
            IcumDownloadsDbContext icumsDownloadsDbContext,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var last24Hours = now.AddHours(-24);
            var lastHour = now.AddHours(-1);

            // Get total containers
            var totalContainers = await dbContext.ContainerCompletenessStatuses
                .CountAsync(cancellationToken);

            // Count containers with NULL GroupIdentifier
            var nullGroupIdentifier = await dbContext.ContainerCompletenessStatuses
                .Where(c => string.IsNullOrEmpty(c.GroupIdentifier))
                .CountAsync(cancellationToken);

            // Count containers with missing BOEDocumentId
            var missingBOEDocumentId = await dbContext.ContainerCompletenessStatuses
                .Where(c => c.BOEDocumentId == null || c.BOEDocumentId == 0)
                .CountAsync(cancellationToken);

            // Count containers with wrong GroupIdentifier (GroupIdentifier doesn't match BOE DeclarationNumber)
            // ✅ FIX: Cannot join across different DbContext instances - query separately and join in memory
            var containersWithBOE = await dbContext.ContainerCompletenessStatuses
                .Where(c => !string.IsNullOrEmpty(c.GroupIdentifier) && c.BOEDocumentId.HasValue)
                .Select(c => new { c.BOEDocumentId, c.GroupIdentifier })
                .ToListAsync(cancellationToken);

            var boeIds = containersWithBOE.Select(c => c.BOEDocumentId!.Value).Distinct().ToList();

            // ✅ SQL Server 2014 FIX: Query all BOEDocuments and filter in memory to avoid CTE generation
            // EF Core's Contains() with lists generates CTEs that require semicolons in SQL Server 2014
            // Querying all documents and filtering in memory avoids CTE generation entirely
            // This is safe if BOEDocuments table is reasonably sized (< 100k records)
            var boeDocuments = new List<(int Id, string? DeclarationNumber)>();

            if (boeIds.Count > 0)
            {
                // Create a HashSet for fast lookup
                var boeIdSet = new HashSet<int>(boeIds);

                // Query all BOEDocuments and filter in memory
                // This avoids CTE generation issues in SQL Server 2014
                var allBoeDocs = await icumsDownloadsDbContext.BOEDocuments
                    .Select(b => new { b.Id, b.DeclarationNumber })
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                // Filter in memory to get only the documents we need
                boeDocuments = allBoeDocs
                    .Where(b => boeIdSet.Contains(b.Id))
                    .Select(b => (b.Id, b.DeclarationNumber))
                    .ToList();
            }

            var boeDict = boeDocuments.ToDictionary(b => b.Id, b => b.DeclarationNumber);
            var wrongGroupIdentifier = containersWithBOE
                .Count(c => boeDict.TryGetValue(c.BOEDocumentId!.Value, out var declaration)
                    && declaration != c.GroupIdentifier);

            // Calculate integrity percentage
            var integrityPercentage = totalContainers > 0
                ? ((totalContainers - nullGroupIdentifier - missingBOEDocumentId - wrongGroupIdentifier) * 100.0 / totalContainers)
                : 100.0;

            // Determine integrity status
            var integrityStatus = integrityPercentage >= 99.5 ? "Healthy"
                : integrityPercentage >= 95.0 ? "Warning"
                : "Critical";

            // Get preventive fixes count from ApplicationLogs table
            var preventiveFixesLast24h = 0;
            var preventiveFixesLastHour = 0;
            var lastPreventiveFixTime = (DateTime?)null;

            if (_applicationLogsTableExists)
            {
                try
                {
                    var connection = dbContext.Database.GetDbConnection();
                    var wasOpen = connection.State == System.Data.ConnectionState.Open;
                    if (!wasOpen) await connection.OpenAsync(cancellationToken);

                    try
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = @"
                            SELECT 
                                COUNT(CASE WHEN timestamp >= @last24h THEN 1 END) as fixeslast24h,
                                COUNT(CASE WHEN timestamp >= @lastHour THEN 1 END) as fixeslasthour,
                                MAX(timestamp) as lastfixtime
                            FROM applicationlogs
                            WHERE (serviceid LIKE '%COMPLETENESS%' OR serviceid LIKE '%DATA-INTEGRITY%')
                              AND (message LIKE '%preventive%' OR message LIKE '%fix%' OR message LIKE '%validated%' 
                                   OR message LIKE '%corrected%' OR message LIKE '%linked%' OR message LIKE '%GroupIdentifier%'
                                   OR message LIKE '%BOEDocumentId%')
                              AND level IN ('Information', 'Warning')
                              AND timestamp >= @last24h";

                        var last24hParam = command.CreateParameter();
                        last24hParam.ParameterName = "@last24h";
                        last24hParam.Value = last24Hours;
                        command.Parameters.Add(last24hParam);

                        var lastHourParam = command.CreateParameter();
                        lastHourParam.ParameterName = "@lastHour";
                        lastHourParam.Value = lastHour;
                        command.Parameters.Add(lastHourParam);

                        using var reader = await command.ExecuteReaderAsync(cancellationToken);
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            preventiveFixesLast24h = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                            preventiveFixesLastHour = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                            lastPreventiveFixTime = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                        }
                    }
                    finally
                    {
                        if (!wasOpen && connection.State == System.Data.ConnectionState.Open)
                        {
                            await connection.CloseAsync();
                        }
                    }
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
                {
                    _applicationLogsTableExists = false;
                    _logger.LogWarning("ApplicationLogs table does not exist - skipping preventive fix queries until restart");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query preventive fixes from ApplicationLogs, using defaults");
                }
            }

            // Use defaults if no fixes found
            var lastPreventiveFixTimeValue = lastPreventiveFixTime ?? DateTime.UtcNow.AddHours(-1);

            // Get recent issues (last 10)
            var recentIssues = await dbContext.ContainerCompletenessStatuses
                .Where(c => (string.IsNullOrEmpty(c.GroupIdentifier) || c.BOEDocumentId == null || c.BOEDocumentId == 0)
                    && c.UpdatedAt >= last24Hours)
                .OrderByDescending(c => c.UpdatedAt)
                .Take(10)
                .Select(c => new DataIntegrityIssue
                {
                    ContainerNumber = c.ContainerNumber,
                    ScannerType = c.ScannerType,
                    IssueType = string.IsNullOrEmpty(c.GroupIdentifier) ? "NullGroupIdentifier"
                        : (c.BOEDocumentId == null || c.BOEDocumentId == 0) ? "MissingBOEDocumentId"
                        : "WrongGroupIdentifier",
                    DetectedAt = c.UpdatedAt,
                    IsFixed = !string.IsNullOrEmpty(c.GroupIdentifier) && c.BOEDocumentId.HasValue && c.BOEDocumentId > 0
                })
                .ToListAsync(cancellationToken);

            return new DataIntegrityMetricsData
            {
                TotalContainers = totalContainers,
                ContainersWithNullGroupIdentifier = nullGroupIdentifier,
                ContainersWithMissingBOEDocumentId = missingBOEDocumentId,
                ContainersWithWrongGroupIdentifier = wrongGroupIdentifier,
                IntegrityPercentage = integrityPercentage,
                IntegrityStatus = integrityStatus,
                PreventiveFixesLast24h = preventiveFixesLast24h,
                PreventiveFixesLastHour = preventiveFixesLastHour,
                LastPreventiveFixTime = lastPreventiveFixTimeValue,
                RecentIssues = recentIssues
            };
        }

        private async Task<RealTimeReadinessData> GetRealTimeReadinessStatusAsync(
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            // ✅ FIX: Reduced heartbeat window from 5 to 2 minutes for faster detection of logged-out users
            var maxIdleMinutes = 2;

            // Get ready users from SignalR state provider (real-time)
            // SignalR users are actively connected, which indicates authentication
            var readyAnalysts = UserReadinessStateProvider.GetReadyUsers("Analyst", TimeSpan.FromMinutes(maxIdleMinutes));
            var readyAuditors = UserReadinessStateProvider.GetReadyUsers("Audit", TimeSpan.FromMinutes(maxIdleMinutes));

            // ✅ FIX: Load roles first, then load all users, then filter in memory to completely avoid CTE generation
            var analystRoleIds = await dbContext.Roles
                .Where(r => r.IsActive && r.Name.ToUpper() == "ANALYST")
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            var auditRoleIds = await dbContext.Roles
                .Where(r => r.IsActive && r.Name.ToUpper() == "AUDIT")
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            // ✅ FIX: Load all active users with roles first, then filter in memory to avoid Contains() generating CTE
            var allActiveUsers = await dbContext.Users
                .Where(u => u.IsActive && u.RoleId != null)
                .Select(u => new { u.Username, u.RoleId })
                .ToListAsync(cancellationToken);

            var analystRoleIdSet = new HashSet<int?>(analystRoleIds.Cast<int?>());
            var auditRoleIdSet = new HashSet<int?>(auditRoleIds.Cast<int?>());

            var totalAnalysts = allActiveUsers
                .Where(u => analystRoleIdSet.Contains(u.RoleId))
                .Select(u => u.Username)
                .Distinct()
                .Count();

            var totalAuditors = allActiveUsers
                .Where(u => auditRoleIdSet.Contains(u.RoleId))
                .Select(u => u.Username)
                .Distinct()
                .Count();

            // Build ready user status list
            var readyUsers = new List<ReadyUserStatus>();

            // Add ready analysts
            foreach (var username in readyAnalysts)
            {
                var lastHeartbeat = UserReadinessStateProvider.GetLastHeartbeat(username, "Analyst");
                readyUsers.Add(new ReadyUserStatus
                {
                    Username = username,
                    Role = "Analyst",
                    IsReady = true,
                    LastHeartbeat = lastHeartbeat,
                    TimeSinceLastHeartbeat = now - lastHeartbeat,
                    Source = "SignalR"
                });
            }

            // Add ready auditors
            foreach (var username in readyAuditors)
            {
                var lastHeartbeat = UserReadinessStateProvider.GetLastHeartbeat(username, "Audit");
                readyUsers.Add(new ReadyUserStatus
                {
                    Username = username,
                    Role = "Audit",
                    IsReady = true,
                    LastHeartbeat = lastHeartbeat,
                    TimeSinceLastHeartbeat = now - lastHeartbeat,
                    Source = "SignalR"
                });
            }

            // Calculate percentages
            var analystReadinessPercentage = totalAnalysts > 0
                ? (readyAnalysts.Count * 100.0 / totalAnalysts)
                : 0;
            var auditorReadinessPercentage = totalAuditors > 0
                ? (readyAuditors.Count * 100.0 / totalAuditors)
                : 0;

            return new RealTimeReadinessData
            {
                ReadyUsers = readyUsers,
                TotalAnalysts = totalAnalysts,
                ReadyAnalysts = readyAnalysts.Count,
                TotalAuditors = totalAuditors,
                ReadyAuditors = readyAuditors.Count,
                AnalystReadinessPercentage = analystReadinessPercentage,
                AuditorReadinessPercentage = auditorReadinessPercentage,
                LastUpdate = now
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

            var variance = assignments.Sum(x => Math.Pow(x - avg, 2)) / assignments.Count;
            var stdDev = Math.Sqrt(variance);
            var cv = avg > 0 ? stdDev / avg : 0;

            return Math.Max(0, 100 - (cv * 100));
        }

        private Task<int> GetClientCountAsync(IHubContext<ImageAnalysisDashboardHub> hubContext)
        {
            // SignalR doesn't provide a direct way to count clients, so we return 0
            // In production, you might want to track this separately
            return Task.FromResult(0);
        }

        private async Task<List<DashboardAlert>> GetCurrentAlertsAsync(
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var alerts = new List<DashboardAlert>();

            // Check for bottlenecks - groups with high queue sizes
            var readyGroups = await dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.Ready)
                .CountAsync(cancellationToken);

            if (readyGroups > 100)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Bottleneck",
                    Severity = "Critical",
                    Title = $"High queue in Ready stage",
                    Message = $"{readyGroups} groups waiting for analyst assignment",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }
            else if (readyGroups > 50)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Bottleneck",
                    Severity = "High",
                    Title = $"Growing queue in Ready stage",
                    Message = $"{readyGroups} groups waiting for analyst assignment",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }

            // Check for audit queue
            var auditReadyGroups = await dbContext.AnalysisGroups
                .Where(g => g.Status == AnalysisStatuses.AnalystCompleted)
                .CountAsync(cancellationToken);

            if (auditReadyGroups > 50)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Bottleneck",
                    Severity = auditReadyGroups > 75 ? "Critical" : "High",
                    Title = $"High queue in Audit stage",
                    Message = $"{auditReadyGroups} groups waiting for audit assignment",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }

            // Check for stale assignments (older than 4 hours)
            var staleAssignments = await dbContext.AnalysisAssignments
                .Where(a => a.State == "Active" && a.CreatedAtUtc < now.AddHours(-4))
                .CountAsync(cancellationToken);

            if (staleAssignments > 0)
            {
                alerts.Add(new DashboardAlert
                {
                    Id = alerts.Count + 1,
                    AlertType = "Performance",
                    Severity = staleAssignments > 10 ? "High" : "Medium",
                    Title = $"Stale assignments detected",
                    Message = $"{staleAssignments} assignments older than 4 hours",
                    CreatedAt = now,
                    IsAcknowledged = false,
                    IsResolved = false
                });
            }

            // Check for data integrity issues
            using var scope = _scopeFactory.CreateScope();
            var icumsDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            var nullGroupIdentifier = await dbContext.ContainerCompletenessStatuses
                .Where(c => string.IsNullOrEmpty(c.GroupIdentifier))
                .CountAsync(cancellationToken);

            var missingBOEDocumentId = await dbContext.ContainerCompletenessStatuses
                .Where(c => c.BOEDocumentId == null || c.BOEDocumentId == 0)
                .CountAsync(cancellationToken);

            var totalContainers = await dbContext.ContainerCompletenessStatuses
                .CountAsync(cancellationToken);

            if (totalContainers > 0)
            {
                var integrityPercentage = ((totalContainers - nullGroupIdentifier - missingBOEDocumentId) * 100.0 / totalContainers);

                if (integrityPercentage < 95.0)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Id = alerts.Count + 1,
                        AlertType = "DataIntegrity",
                        Severity = integrityPercentage < 90.0 ? "Critical" : "High",
                        Title = $"Data integrity issues detected",
                        Message = $"{nullGroupIdentifier} containers with NULL GroupIdentifier, {missingBOEDocumentId} with missing BOEDocumentId. Integrity: {integrityPercentage:F2}%",
                        CreatedAt = now,
                        IsAcknowledged = false,
                        IsResolved = false,
                        Metadata = new Dictionary<string, object>
                        {
                            { "NullGroupIdentifier", nullGroupIdentifier },
                            { "MissingBOEDocumentId", missingBOEDocumentId },
                            { "IntegrityPercentage", integrityPercentage }
                        }
                    });
                }
                else if (nullGroupIdentifier > 50 || missingBOEDocumentId > 50)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Id = alerts.Count + 1,
                        AlertType = "DataIntegrity",
                        Severity = "Medium",
                        Title = $"Data integrity warning",
                        Message = $"{nullGroupIdentifier} containers with NULL GroupIdentifier, {missingBOEDocumentId} with missing BOEDocumentId",
                        CreatedAt = now,
                        IsAcknowledged = false,
                        IsResolved = false
                    });
                }
            }

            return alerts;
        }

        /// <summary>
        /// Check if exception is a database connectivity issue
        /// </summary>
        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is SqlException sqlEx)
            {
                // SQL Server error numbers for connectivity issues
                return sqlEx.Number == 2 || sqlEx.Number == 40 || sqlEx.Number == 53 ||
                       sqlEx.Number == 121 || sqlEx.Number == 10053 || sqlEx.Number == 10054 ||
                       sqlEx.Number == 10060 || sqlEx.Number == 1225 ||
                       sqlEx.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase);
            }

            // Check for common database connectivity error messages
            var errorMessage = ex.Message;
            return errorMessage.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase) ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }
    }
}
