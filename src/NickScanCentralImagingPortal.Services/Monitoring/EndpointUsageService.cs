using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Service for tracking and querying API endpoint usage
    /// </summary>
    public class EndpointUsageService : IEndpointUsageService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EndpointUsageService> _logger;

        public EndpointUsageService(
            IServiceScopeFactory scopeFactory,
            ApplicationDbContext context,
            ILogger<EndpointUsageService> logger)
        {
            _scopeFactory = scopeFactory;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Records endpoint usage in a separate scope to avoid DbContext concurrency with the request pipeline.
        /// When middleware runs after a controller that uses fire-and-forget (e.g. my-assignments cache hit),
        /// both can touch the same request-scoped DbContext concurrently. Using a new scope isolates this.
        /// </summary>
        public async Task RecordEndpointUsageAsync(EndpointUsageRecord record)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var logEntry = new EndpointUsageLog
                {
                    Endpoint = EndpointUsagePathNormalizer.Normalize(record.Endpoint),
                    Method = record.Method,
                    StatusCode = record.StatusCode,
                    ResponseTimeMs = record.ResponseTimeMs,
                    IpAddress = record.IpAddress,
                    UserAgent = record.UserAgent,
                    Timestamp = record.Timestamp,
                    IsDeprecated = record.IsDeprecated,
                    IsPhase3Route = record.IsPhase3Route,
                    CorrelationId = record.CorrelationId
                };

                context.EndpointUsageLogs.Add(logEntry);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording endpoint usage for {Endpoint}", record.Endpoint);
                // Don't throw - logging failures shouldn't break the application
            }
        }

        public async Task<EndpointUsageStats> GetUsageStatsAsync(string endpoint, DateTime? from = null, DateTime? to = null)
        {
            var normalizedEndpoint = EndpointUsagePathNormalizer.Normalize(endpoint);
            var query = _context.EndpointUsageLogs.Where(e => e.Endpoint.ToLower() == normalizedEndpoint);

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            var logs = await query.ToListAsync();

            if (!logs.Any())
            {
                return new EndpointUsageStats { Endpoint = normalizedEndpoint };
            }

            var stats = new EndpointUsageStats
            {
                Endpoint = normalizedEndpoint,
                TotalCalls = logs.Count,
                UniqueCallers = logs.Where(l => !string.IsNullOrEmpty(l.IpAddress))
                    .Select(l => l.IpAddress)
                    .Distinct()
                    .Count(),
                AverageResponseTimeMs = logs.Average(l => l.ResponseTimeMs),
                MinResponseTimeMs = logs.Min(l => l.ResponseTimeMs),
                MaxResponseTimeMs = logs.Max(l => l.ResponseTimeMs),
                SuccessCount = logs.Count(l => l.StatusCode >= 200 && l.StatusCode < 300),
                ErrorCount = logs.Count(l => l.StatusCode >= 400),
                FirstCall = logs.Min(l => l.Timestamp),
                LastCall = logs.Max(l => l.Timestamp),
                StatusCodeCounts = logs.GroupBy(l => l.StatusCode)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return stats;
        }

        public async Task<List<EndpointCaller>> GetCallersAsync(string endpoint, DateTime? from = null, DateTime? to = null)
        {
            var normalizedEndpoint = EndpointUsagePathNormalizer.Normalize(endpoint);
            var query = _context.EndpointUsageLogs
                .Where(e => e.Endpoint.ToLower() == normalizedEndpoint && !string.IsNullOrEmpty(e.IpAddress));

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            // ✅ FIX: Materialize the query first, then apply IsInternalIp check in memory
            // This avoids EF Core translation issues with custom methods
            var logs = await query.ToListAsync();

            var callers = logs
                .GroupBy(e => new { e.IpAddress, e.UserAgent })
                .Select(g => new EndpointCaller
                {
                    IpAddress = g.Key.IpAddress ?? "Unknown",
                    UserAgent = g.Key.UserAgent,
                    CallCount = g.Count(),
                    FirstCall = g.Min(e => e.Timestamp),
                    LastCall = g.Max(e => e.Timestamp),
                    IsInternal = IsInternalIp(g.Key.IpAddress ?? "")
                })
                .OrderByDescending(c => c.CallCount)
                .ToList();

            return callers;
        }

        public async Task<Dictionary<string, int>> GetDeprecatedEndpointUsageAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _context.EndpointUsageLogs.Where(e => e.IsDeprecated);

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            var usage = await query
                .GroupBy(e => e.Endpoint.ToLower())
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            return usage;
        }

        public async Task<List<DeprecatedEndpointSummary>> GetDeprecatedEndpointSummaryAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _context.EndpointUsageLogs.Where(e => e.IsDeprecated);

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            var now = DateTime.UtcNow;
            var summaries = await query
                .GroupBy(e => e.Endpoint.ToLower())
                .Select(g => new DeprecatedEndpointSummary
                {
                    Endpoint = g.Key,
                    TotalCalls = g.Count(),
                    UniqueCallers = g.Where(e => !string.IsNullOrEmpty(e.IpAddress))
                        .Select(e => e.IpAddress)
                        .Distinct()
                        .Count(),
                    LastCall = g.Max(e => e.Timestamp),
                    DaysSinceLastCall = (int)(now - g.Max(e => e.Timestamp)).TotalDays,
                    IsSafeToRemove = (now - g.Max(e => e.Timestamp)).TotalDays >= 30,
                    CallerIps = g.Where(e => !string.IsNullOrEmpty(e.IpAddress))
                        .Select(e => e.IpAddress!)
                        .Distinct()
                        .ToList()
                })
                .OrderByDescending(s => s.TotalCalls)
                .ToListAsync();

            return summaries;
        }

        public async Task<Dictionary<string, int>> GetPhase3RouteUsageAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _context.EndpointUsageLogs.Where(e => e.IsPhase3Route);

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            var usage = await query
                .GroupBy(e => e.Endpoint.ToLower())
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            return usage;
        }

        public async Task<List<Phase3RouteSummary>> GetPhase3RouteSummaryAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _context.EndpointUsageLogs.Where(e => e.IsPhase3Route);

            if (from.HasValue)
                query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.Timestamp <= to.Value);

            // ✅ FIX: Materialize the query first, then apply IsInternalIp check in memory
            // This avoids EF Core translation issues with custom methods
            var logs = await query.ToListAsync();

            var summaries = logs
                .GroupBy(e => EndpointUsagePathNormalizer.Normalize(e.Endpoint))
                .Select(g => new Phase3RouteSummary
                {
                    Endpoint = g.Key,
                    TotalCalls = g.Count(),
                    UniqueCallers = g.Where(e => !string.IsNullOrEmpty(e.IpAddress))
                        .Select(e => e.IpAddress)
                        .Distinct()
                        .Count(),
                    LastCall = g.Max(e => e.Timestamp),
                    Callers = g.Where(e => !string.IsNullOrEmpty(e.IpAddress))
                        .GroupBy(e => new { e.IpAddress, e.UserAgent })
                        .Select(cg => new EndpointCaller
                        {
                            IpAddress = cg.Key.IpAddress ?? "Unknown",
                            UserAgent = cg.Key.UserAgent,
                            CallCount = cg.Count(),
                            FirstCall = cg.Min(e => e.Timestamp),
                            LastCall = cg.Max(e => e.Timestamp),
                            IsInternal = IsInternalIp(cg.Key.IpAddress ?? "")
                        })
                        .ToList(),
                    HasExternalCallers = g.Any(e => !string.IsNullOrEmpty(e.IpAddress) && !IsInternalIp(e.IpAddress))
                })
                .OrderByDescending(s => s.TotalCalls)
                .ToList();

            return summaries;
        }

        public async Task<List<EndpointUsageTrend>> GetUsageTrendsAsync(string endpoint, int days = 30)
        {
            var from = DateTime.UtcNow.AddDays(-days);
            var normalizedEndpoint = EndpointUsagePathNormalizer.Normalize(endpoint);
            var query = _context.EndpointUsageLogs
                .Where(e => e.Endpoint.ToLower() == normalizedEndpoint && e.Timestamp >= from);

            var logs = await query.ToListAsync();

            var trends = logs
                .GroupBy(e => e.Timestamp.Date)
                .Select(g => new EndpointUsageTrend
                {
                    Date = g.Key,
                    CallCount = g.Count(),
                    UniqueCallers = g.Where(e => !string.IsNullOrEmpty(e.IpAddress))
                        .Select(e => e.IpAddress)
                        .Distinct()
                        .Count(),
                    AverageResponseTimeMs = g.Average(e => e.ResponseTimeMs),
                    ErrorCount = g.Count(e => e.StatusCode >= 400)
                })
                .OrderBy(t => t.Date)
                .ToList();

            return trends;
        }

        public async Task<List<string>> GetSafeToRemoveEndpointsAsync(int daysWithZeroUsage = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysWithZeroUsage);

            // Get all deprecated endpoints
            var deprecatedEndpoints = await _context.EndpointUsageLogs
                .Where(e => e.IsDeprecated)
                .Select(e => e.Endpoint.ToLower())
                .Distinct()
                .ToListAsync();

            // Find endpoints with no usage in the last N days
            var safeToRemove = new List<string>();
            foreach (var endpoint in deprecatedEndpoints)
            {
                var hasRecentUsage = await _context.EndpointUsageLogs
                    .AnyAsync(e => e.Endpoint.ToLower() == endpoint && e.Timestamp >= cutoffDate);

                if (!hasRecentUsage)
                {
                    safeToRemove.Add(endpoint);
                }
            }

            return safeToRemove;
        }

        public async Task<List<AllEndpointsSummary>> GetAllEndpointsSummaryAsync(DateTime? from = null, DateTime? to = null)
        {
            // Default to last 30 days if no date range specified
            if (!from.HasValue && !to.HasValue)
                from = DateTime.UtcNow.AddDays(-30);

            var query = _context.EndpointUsageLogs.AsQueryable();
            if (from.HasValue) query = query.Where(e => e.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(e => e.Timestamp <= to.Value);

            var now = DateTime.UtcNow;

            // Push aggregation to SQL — avoid loading all rows into memory
            var summaries = await query
                .GroupBy(e => new { Endpoint = e.Endpoint.ToLower(), e.Method })
                .Select(g => new AllEndpointsSummary
                {
                    Endpoint = g.Key.Endpoint,
                    Method = g.Key.Method,
                    TotalCalls = g.Count(),
                    FirstCall = g.Min(e => e.Timestamp),
                    LastCall = g.Max(e => e.Timestamp),
                    AverageResponseTimeMs = g.Average(e => e.ResponseTimeMs),
                    MinResponseTimeMs = g.Min(e => e.ResponseTimeMs),
                    MaxResponseTimeMs = g.Max(e => e.ResponseTimeMs),
                    SuccessCount = g.Count(e => e.StatusCode >= 200 && e.StatusCode < 300),
                    ErrorCount = g.Count(e => e.StatusCode >= 400),
                    IsDeprecated = g.Any(e => e.IsDeprecated),
                    IsPhase3Route = g.Any(e => e.IsPhase3Route)
                })
                .OrderByDescending(s => s.TotalCalls)
                .ToListAsync();

            // Compute derived fields in-memory (these can't translate to SQL easily)
            foreach (var s in summaries)
            {
                s.DaysSinceLastCall = s.LastCall.HasValue ? (int)(now - s.LastCall.Value).TotalDays : 0;
                s.ErrorRate = s.TotalCalls > 0 ? (double)s.ErrorCount / s.TotalCalls * 100 : 0;
                s.Status = s.IsDeprecated ? "Deprecated"
                    : s.IsPhase3Route ? "Phase3"
                    : s.DaysSinceLastCall > 30 ? "Unused"
                    : "Active";
            }

            return summaries;
        }

        public async Task<int> CleanupOldLogsAsync(int daysToKeep = 90)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

            var oldLogs = _context.EndpointUsageLogs
                .Where(e => e.Timestamp < cutoffDate);

            var count = await oldLogs.CountAsync();
            _context.EndpointUsageLogs.RemoveRange(oldLogs);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} old endpoint usage logs (older than {Days} days)", count, daysToKeep);
            return count;
        }

        private bool IsInternalIp(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            // Check for localhost, loopback, and private IP ranges
            return ipAddress == "127.0.0.1" ||
                   ipAddress == "::1" ||
                   ipAddress.StartsWith("10.") ||
                   ipAddress.StartsWith("192.168.") ||
                   ipAddress.StartsWith("172.16.") ||
                   ipAddress.StartsWith("172.17.") ||
                   ipAddress.StartsWith("172.18.") ||
                   ipAddress.StartsWith("172.19.") ||
                   ipAddress.StartsWith("172.20.") ||
                   ipAddress.StartsWith("172.21.") ||
                   ipAddress.StartsWith("172.22.") ||
                   ipAddress.StartsWith("172.23.") ||
                   ipAddress.StartsWith("172.24.") ||
                   ipAddress.StartsWith("172.25.") ||
                   ipAddress.StartsWith("172.26.") ||
                   ipAddress.StartsWith("172.27.") ||
                   ipAddress.StartsWith("172.28.") ||
                   ipAddress.StartsWith("172.29.") ||
                   ipAddress.StartsWith("172.30.") ||
                   ipAddress.StartsWith("172.31.");
        }
    }
}

