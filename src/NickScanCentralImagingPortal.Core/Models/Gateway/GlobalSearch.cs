using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Global search request across all data sources
    /// </summary>
    public class GlobalSearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public List<string> EntityTypes { get; set; } = new(); // "Container", "ICUMS", "Vehicle", "Operator"
        public int MaxResults { get; set; } = 100;
        public int Skip { get; set; } = 0;
        public bool IncludeMetadata { get; set; } = true;
        public Dictionary<string, string> Filters { get; set; } = new(); // Custom filters
    }

    /// <summary>
    /// Global search response with results from multiple data sources
    /// </summary>
    public class GlobalSearchResponse
    {
        public string Query { get; set; } = string.Empty;
        public int TotalResults { get; set; }
        public int ResultsReturned { get; set; }
        public int ResponseTimeMs { get; set; }
        public DateTime SearchedAt { get; set; } = DateTime.UtcNow;

        public List<GatewaySearchResult> Results { get; set; } = new();
        public Dictionary<string, int> ResultsByType { get; set; } = new(); // "Container": 50, "ICUMS": 30
        public List<string> Suggestions { get; set; } = new(); // Search suggestions for typos
    }

    /// <summary>
    /// Individual search result for Gateway dashboard (renamed to avoid Swagger schema conflict with Core.Models.SearchResult)
    /// </summary>
    public class GatewaySearchResult
    {
        public string EntityType { get; set; } = string.Empty; // "Container", "ICUMS", "Vehicle", "Operator"
        public string Id { get; set; } = string.Empty; // Unique identifier
        public string PrimaryField { get; set; } = string.Empty; // Main display field (e.g., ContainerNumber)
        public string SecondaryField { get; set; } = string.Empty; // Secondary info (e.g., ScanDate)
        public double RelevanceScore { get; set; } // 0.0-1.0

        public Dictionary<string, object> Metadata { get; set; } = new(); // Additional data
        public List<string> MatchedFields { get; set; } = new(); // Fields that matched the query
        public string Preview { get; set; } = string.Empty; // Text preview with highlighted matches
    }
}

