namespace NickScanCentralImagingPortal.Core.Configuration;

/// <summary>
/// Runtime configuration for go-live date cutoff.
/// When set, only data from this date onward is processed (no retrospective/backfill).
/// Leave null/empty in development to process all data.
/// </summary>
public class GoLiveOptions
{
    /// <summary>
    /// Go-live date (UTC). Only process data where ScanTime/DownloadDate &gt;= this date.
    /// Format: "yyyy-MM-dd" or ISO 8601. When null or MinValue, no cutoff is applied.
    /// </summary>
    public DateTime? GoLiveDate { get; set; }

    /// <summary>
    /// Returns the effective go-live date, or MinValue if not configured (process all).
    /// </summary>
    public DateTime EffectiveGoLiveDate => GoLiveDate ?? DateTime.MinValue;
}
