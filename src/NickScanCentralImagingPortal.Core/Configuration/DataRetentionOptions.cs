namespace NickScanCentralImagingPortal.Core.Configuration;

/// <summary>
/// Data retention cutoff for purge and re-ingestion prevention.
/// When enabled, records before CutoffDate are excluded from ingestion.
/// </summary>
public class DataRetentionOptions
{
    public const string SectionName = "DataRetention";

    /// <summary>
    /// When true, ingestion services skip records with date before CutoffDate.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Cutoff date (UTC). Do not ingest records before this date.
    /// Format: "yyyy-MM-dd". Default: 2026-01-01.
    /// </summary>
    public DateTime? CutoffDate { get; set; }

    /// <summary>
    /// Returns the effective cutoff date, or MinValue if not configured.
    /// </summary>
    public DateTime EffectiveCutoffDate => (Enabled && CutoffDate.HasValue) ? CutoffDate.Value.Date : DateTime.MinValue;
}
