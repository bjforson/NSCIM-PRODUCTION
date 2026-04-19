using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.API.Logging
{
    public class LogFilterConfiguration
    {
        public static readonly Dictionary<string, LogLevel> ServiceLogLevels = new()
        {
            // Reduce noise from frequently logging services
            ["ICUMS-BACKGROUND"] = LogLevel.Warning,      // Only warnings and errors
            ["ICUMS-FILE-SCANNER"] = LogLevel.Warning,    // Only warnings and errors
            ["ASE-BACKGROUND"] = LogLevel.Warning,        // Only warnings and errors
            ["DASHBOARD-API"] = LogLevel.Warning,         // Only warnings and errors

            // Keep important services at Info level
            ["CONTAINER-COMPLETENESS"] = LogLevel.Information,
            ["CONTAINER-DATA-MAPPER"] = LogLevel.Information,
            ["MANUAL-BOE-SELECTIVITY"] = LogLevel.Information,
            ["ICUMS-SUBMISSION"] = LogLevel.Information,

            // Keep critical services at Debug level for troubleshooting
            ["FS6000-BACKGROUND"] = LogLevel.Debug,
            ["COMPREHENSIVE-HEALTH"] = LogLevel.Debug
        };

        public static readonly HashSet<string> SpamOperations = new()
        {
            "FetchICUMSChunk",
            "FetchICUMSBatchData",
            "RegisterICUMSFile",
            "ProcessICUMSFile",
            "FetchEnhancedDashboardData",
            "ASE_Sync",
            "CheckContainerCompleteness",
            "ProcessPendingMappings",
            "ProcessBOERequests",
            "ProcessSubmissions",
            "SystemHealthCheck"
        };

        public static readonly Dictionary<string, int> ThrottleConfig = new()
        {
            ["ICUMS-BACKGROUND"] = 60,        // Only log every 60 seconds
            ["ICUMS-FILE-SCANNER"] = 120,     // Only log every 2 minutes
            ["ASE-BACKGROUND"] = 120,         // Only log every 2 minutes
            ["DASHBOARD-API"] = 60,           // Only log every 60 seconds
            ["CONTAINER-COMPLETENESS"] = 300, // Only log every 5 minutes
            ["CONTAINER-DATA-MAPPER"] = 300,  // Only log every 5 minutes
            ["MANUAL-BOE-SELECTIVITY"] = 300, // Only log every 5 minutes
            ["ICUMS-SUBMISSION"] = 300        // Only log every 5 minutes
        };
    }
}
