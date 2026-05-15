namespace NickScanCentralImagingPortal.Services.EagleA25
{
    public class EagleA25Configuration
    {
        public bool Enabled { get; set; } = false;

        public string ConnectionString { get; set; } = string.Empty;

        public string SourceShareRoot { get; set; } = @"\\10.0.5.163\e\Xray\Data";

        public string SourceShareUsername { get; set; } = string.Empty;

        public string SourceSharePassword { get; set; } = string.Empty;

        public string SourceDbPathRoot { get; set; } = @"\\Server.xray.local\XRAYDATA";

        public DateTime MinimumScanDateUtc { get; set; } = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        public int SyncIntervalMinutes { get; set; } = 5;

        public int BatchSize { get; set; } = 100;

        public int MaxCatchUpBatchesPerCycle { get; set; } = 25;

        public bool CopyAssetsToLocalStorage { get; set; } = false;

        public string LocalAssetRoot { get; set; } = @"C:\Shared\NSCIM_PRODUCTION\Data\EagleA25";
    }
}
