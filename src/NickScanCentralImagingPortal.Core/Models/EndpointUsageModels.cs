using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Record for endpoint usage tracking
    /// </summary>
    public class EndpointUsageRecord
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public double ResponseTimeMs { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsDeprecated { get; set; }
        public bool IsPhase3Route { get; set; }
        public Guid? CorrelationId { get; set; }
    }

    /// <summary>
    /// Statistics for endpoint usage
    /// </summary>
    public class EndpointUsageStats
    {
        public string Endpoint { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int UniqueCallers { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public double MinResponseTimeMs { get; set; }
        public double MaxResponseTimeMs { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public DateTime? FirstCall { get; set; }
        public DateTime? LastCall { get; set; }
        public Dictionary<int, int> StatusCodeCounts { get; set; } = new();
    }

    /// <summary>
    /// Information about an endpoint caller
    /// </summary>
    public class EndpointCaller
    {
        public string IpAddress { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
        public int CallCount { get; set; }
        public DateTime? FirstCall { get; set; }
        public DateTime? LastCall { get; set; }
        public bool IsInternal { get; set; }
    }

    /// <summary>
    /// Usage trend data for an endpoint
    /// </summary>
    public class EndpointUsageTrend
    {
        public DateTime Date { get; set; }
        public int CallCount { get; set; }
        public int UniqueCallers { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// Summary of deprecated endpoint usage
    /// </summary>
    public class DeprecatedEndpointSummary
    {
        public string Endpoint { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int UniqueCallers { get; set; }
        public DateTime? LastCall { get; set; }
        public int DaysSinceLastCall { get; set; }
        public bool IsSafeToRemove { get; set; }
        public List<string> CallerIps { get; set; } = new();
        public string? CanonicalReplacement { get; set; }
        public string? Owner { get; set; }
        public string? Reason { get; set; }
        public DateTime? DeprecatedOnUtc { get; set; }
        public int SafeRemovalAfterDays { get; set; } = 30;
    }

    /// <summary>
    /// Summary of Phase 3 route usage
    /// </summary>
    public class Phase3RouteSummary
    {
        public string Endpoint { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int UniqueCallers { get; set; }
        public DateTime? LastCall { get; set; }
        public List<EndpointCaller> Callers { get; set; } = new();
        public bool HasExternalCallers { get; set; }
    }

    /// <summary>
    /// Summary of all endpoints with usage statistics
    /// </summary>
    public class AllEndpointsSummary
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int UniqueCallers { get; set; }
        public DateTime? FirstCall { get; set; }
        public DateTime? LastCall { get; set; }
        public int DaysSinceLastCall { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public double MinResponseTimeMs { get; set; }
        public double MaxResponseTimeMs { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        public bool IsDeprecated { get; set; }
        public bool IsPhase3Route { get; set; }
        public string Status { get; set; } = "Active"; // Active, Deprecated, Phase3, Unused
        public string? CanonicalReplacement { get; set; }
        public string? Owner { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Source-of-truth metadata used to classify route usage during endpoint consolidation.
    /// </summary>
    public class EndpointRouteUsageDefinition
    {
        public string Pattern { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? CanonicalReplacement { get; set; }
        public string Owner { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime? DeprecatedOnUtc { get; set; }
        public int SafeRemovalAfterDays { get; set; } = 30;
    }
}

