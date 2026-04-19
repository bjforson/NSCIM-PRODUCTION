using System;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class IngestionConfiguration
    {
        // Using property names that match appsettings.json
        public string ProcessingDirectory { get; set; } = "C:\\tadi_mirror";
        public string ArchiveDirectory { get; set; } = "C:\\tadi_processed";
        public string FailedDirectory { get; set; } = "C:\\tadi_failed";
        public int ProcessingIntervalMinutes { get; set; } = 2;

        // Legacy property names for backward compatibility
        public string DestinationPath => ProcessingDirectory;
        public string ProcessedPath => ArchiveDirectory;
        public TimeSpan IngestionInterval => TimeSpan.FromMinutes(ProcessingIntervalMinutes);
    }
}
