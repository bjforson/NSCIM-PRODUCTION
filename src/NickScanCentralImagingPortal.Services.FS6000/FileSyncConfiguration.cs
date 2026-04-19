using System;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class FileSyncConfiguration
    {
        // Using property names that match appsettings.json
        public string SourceDirectory { get; set; } = @"Z:\23301FS01";
        public string DestinationDirectory { get; set; } = "C:\\NickScan\\FS6000\\Staging";
        public string ProcessedPath { get; set; } = "C:\\NickScan\\FS6000\\Archive";
        public int SyncIntervalMinutes { get; set; } = 5;
        public int MinimumYear { get; set; } = 2025;
        public int MinimumMonthDay { get; set; } = 901; // September 1st
        public bool StrictFolderValidation { get; set; } = true;
        public bool LogSkippedFolders { get; set; } = true;

        // Network share credentials for when the service runs as LocalSystem
        public string? NetworkShareUsername { get; set; }
        public string? NetworkSharePassword { get; set; }

        // Legacy property names for backward compatibility
        public string SourcePath => SourceDirectory;
        public string DestinationPath => DestinationDirectory;
        public TimeSpan SyncInterval => TimeSpan.FromMinutes(SyncIntervalMinutes);

        /// <summary>
        /// Validate configuration and log values
        /// </summary>
        public void Validate(ILogger logger)
        {
            logger.LogInformation("📋 FS6000 Configuration:");
            logger.LogInformation("   SourceDirectory: {SourceDirectory}", SourceDirectory);
            logger.LogInformation("   DestinationDirectory: {DestinationDirectory}", DestinationDirectory);
            logger.LogInformation("   ProcessedPath: {ProcessedPath}", ProcessedPath);
            logger.LogInformation("   SyncIntervalMinutes: {SyncIntervalMinutes}", SyncIntervalMinutes);
            logger.LogInformation("   MinimumYear: {MinimumYear}", MinimumYear);
            logger.LogInformation("   MinimumMonthDay: {MinimumMonthDay}", MinimumMonthDay);
            logger.LogInformation("   StrictFolderValidation: {StrictFolderValidation}", StrictFolderValidation);
            logger.LogInformation("   LogSkippedFolders: {LogSkippedFolders}", LogSkippedFolders);

            // Validate paths
            if (string.IsNullOrWhiteSpace(SourceDirectory))
            {
                throw new InvalidOperationException("SourceDirectory cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(DestinationDirectory))
            {
                throw new InvalidOperationException("DestinationDirectory cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(ProcessedPath))
            {
                throw new InvalidOperationException("ProcessedPath cannot be empty");
            }

            logger.LogInformation("✅ Configuration validation passed");
        }
    }
}
