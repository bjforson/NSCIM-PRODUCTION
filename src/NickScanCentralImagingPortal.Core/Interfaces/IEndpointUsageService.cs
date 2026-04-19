using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for tracking and querying API endpoint usage
    /// </summary>
    public interface IEndpointUsageService
    {
        /// <summary>
        /// Record an endpoint usage event
        /// </summary>
        Task RecordEndpointUsageAsync(EndpointUsageRecord record);

        /// <summary>
        /// Get usage statistics for a specific endpoint
        /// </summary>
        Task<EndpointUsageStats> GetUsageStatsAsync(string endpoint, DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get list of callers for a specific endpoint
        /// </summary>
        Task<List<EndpointCaller>> GetCallersAsync(string endpoint, DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get usage statistics for all deprecated endpoints
        /// </summary>
        Task<Dictionary<string, int>> GetDeprecatedEndpointUsageAsync(DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get detailed summary of deprecated endpoints
        /// </summary>
        Task<List<DeprecatedEndpointSummary>> GetDeprecatedEndpointSummaryAsync(DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get usage statistics for all Phase 3 routes
        /// </summary>
        Task<Dictionary<string, int>> GetPhase3RouteUsageAsync(DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get detailed summary of Phase 3 routes
        /// </summary>
        Task<List<Phase3RouteSummary>> GetPhase3RouteSummaryAsync(DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Get usage trends for an endpoint over time
        /// </summary>
        Task<List<EndpointUsageTrend>> GetUsageTrendsAsync(string endpoint, int days = 30);

        /// <summary>
        /// Get list of endpoints that are safe to remove (zero usage for specified days)
        /// </summary>
        Task<List<string>> GetSafeToRemoveEndpointsAsync(int daysWithZeroUsage = 30);

        /// <summary>
        /// Clean up old usage logs (older than specified days)
        /// </summary>
        Task<int> CleanupOldLogsAsync(int daysToKeep = 90);

        /// <summary>
        /// Get summary of all endpoints with usage statistics
        /// </summary>
        Task<List<AllEndpointsSummary>> GetAllEndpointsSummaryAsync(DateTime? from = null, DateTime? to = null);
    }
}

