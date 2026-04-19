namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Loose cargo statistics model
    /// </summary>
    public class LooseCargoStatistics
    {
        public int TotalRecords { get; set; }
        public int Imports { get; set; }
        public int Exports { get; set; }
        public int Transit { get; set; }
        public int HighRisk { get; set; }
        public int MediumRisk { get; set; }
        public int LowRisk { get; set; }
        public int RecentRecords { get; set; } // Last 7 days
        public decimal? TotalDutyPaid { get; set; }
        public DateTime? OldestRecord { get; set; }
        public DateTime? NewestRecord { get; set; }
    }

    /// <summary>
    /// Loose cargo search/filter request
    /// </summary>
    public class LooseCargoSearchRequest
    {
        public string? ClearanceType { get; set; }
        public string? CrmsLevel { get; set; }
        public string? SearchTerm { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 100;
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; } = false;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? RegimeCode { get; set; }
    }

    /// <summary>
    /// Loose cargo search response with pagination
    /// </summary>
    public class LooseCargoSearchResponse
    {
        public List<BOEDocument> Records { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }

    /// <summary>
    /// Loose cargo detail DTO with manifest items
    /// </summary>
    public class LooseCargoDetailDto
    {
        public BOEDocument Document { get; set; } = null!;
        public List<DownloadedManifestItem> ManifestItems { get; set; } = new();
        public DownloadedFile? SourceFile { get; set; }
    }
}

