using Serilog.Events;
using Serilog.Formatting;
using System.Collections.Frozen;

namespace NickScanCentralImagingPortal.API.Logging;

public class ServiceColorFormatter : ITextFormatter
{
    private const string Reset = "\x1b[0m";
    private const string BrightRed = "\x1b[91m";
    private const string BrightYellow = "\x1b[93m";
    private const string DimGray = "\x1b[90m";

    private record ServiceStyle(string Emoji, string AnsiColor);

    private static readonly FrozenDictionary<string, ServiceStyle> ServiceMap = new Dictionary<string, ServiceStyle>
    {
        // ── Scanner services (cyan family) ──
        ["FS6000BackgroundService"]       = new("📡", "\x1b[96m"),  // bright cyan
        ["FileSyncService"]               = new("📂", "\x1b[96m"),
        ["IngestionService"]              = new("📥", "\x1b[36m"),  // cyan
        ["AseDatabaseSyncService"]        = new("🔗", "\x1b[93m"),  // bright yellow
        ["AseBackgroundService"]          = new("🔗", "\x1b[93m"),

        // ── ICUMS pipeline (green family) ──
        ["IcumJsonIngestionService"]      = new("📋", "\x1b[92m"),  // bright green
        ["IcumPipelineOrchestratorService"] = new("🔄", "\x1b[32m"), // green
        ["IcumApiService"]                = new("🌐", "\x1b[32m"),
        ["IcumBackgroundService"]         = new("🌿", "\x1b[32m"),
        ["IcumDataTransferService"]       = new("🚚", "\x1b[92m"),
        ["IcumFileScannerService"]        = new("🔍", "\x1b[92m"),
        ["FailedFileRetryService"]        = new("🔁", "\x1b[32m"),
        ["ICUMSMetricsCollectorService"]  = new("📊", "\x1b[32m"),
        ["IcumFileArchiveService"]        = new("🗄️", "\x1b[32m"),
        ["ICUMSDownloadBackgroundService"] = new("⬇️", "\x1b[92m"),
        ["PostICUMSValidationService"]    = new("✔️", "\x1b[92m"),
        ["ManualBOESelectivityService"]   = new("📑", "\x1b[32m"),

        // ── Container completeness (magenta family) ──
        ["ContainerCompletenessService"]  = new("📦", "\x1b[95m"),  // bright magenta
        ["ContainerCompletenessOrchestratorService"] = new("📦", "\x1b[95m"),
        ["ContainerDataMapperService"]    = new("🗺️", "\x1b[95m"),
        ["QueueRecoveryService"]          = new("🔧", "\x1b[35m"),  // magenta
        ["ICUMSSubmissionService"]        = new("📤", "\x1b[35m"),
        ["ContainerStatusReconciliationService"] = new("⚖️", "\x1b[35m"),

        // ── Image analysis (blue family) ──
        ["ImageAnalysisOrchestratorService"] = new("🖼️", "\x1b[94m"), // bright blue
        ["ImageAnalysisBootstrapper"]     = new("🎬", "\x1b[94m"),
        ["UserReadinessSyncService"]      = new("👤", "\x1b[94m"),

        // ── Dashboard & broadcast (yellow family) ──
        ["ComprehensiveDashboardService"] = new("📈", "\x1b[33m"),  // yellow
        ["DashboardBroadcastService"]     = new("📡", "\x1b[33m"),
        ["ImageAnalysisDashboardBroadcastService"] = new("📺", "\x1b[33m"),
        ["MonitoringBroadcastService"]    = new("📢", "\x1b[33m"),

        // ── Monitoring & health (red family — stands out for alerts) ──
        ["ComprehensiveHealthCheckService"] = new("💓", "\x1b[91m"), // bright red
        ["ErrorMonitoringBackgroundService"] = new("🚨", "\x1b[91m"),
        ["PerformanceMonitoringService"]  = new("⚡", "\x1b[91m"),
        ["DuplicateDownloadMonitoringService"] = new("🛡️", "\x1b[91m"),
        ["EndpointUsageCleanupBackgroundService"] = new("🧹", "\x1b[90m"), // gray
        ["EndpointUsageBufferService"]    = new("📝", "\x1b[90m"),

        // ── Validation (bright white family) ──
        ["CMRValidationService"]          = new("✅", "\x1b[97m"),  // bright white
        ["CMRRedownloadService"]          = new("⏬", "\x1b[97m"),
        ["CMRRedownloadBackgroundService"] = new("⏬", "\x1b[97m"),
        ["CMRMetricsRecorderService"]     = new("📏", "\x1b[97m"),

        // ── Lifecycle & orchestration (blue) ──
        ["ServiceOrchestratorBackgroundService"] = new("🎯", "\x1b[34m"),
        ["ServiceLifecycleStartupService"] = new("🚀", "\x1b[34m"),
        ["MasterOrchestratorService"]     = new("👑", "\x1b[34m"),

        // ── Auth & security ──
        ["PermissionSeeder"]              = new("🔐", "\x1b[33m"),
        ["AccessReviewService"]           = new("🔑", "\x1b[33m"),

        // ── Email & notifications ──
        ["DailyDataQualityReportService"] = new("📧", "\x1b[36m"),

        // ── Workers ──
        ["AssignmentWorker"]              = new("👷", "\x1b[36m"),
        ["IntakeWorker"]                  = new("🏗️", "\x1b[36m"),
        ["SubmissionWorker"]              = new("📮", "\x1b[36m"),
        ["HousekeepingWorker"]            = new("🧽", "\x1b[90m"),

        // ── Middleware / Infra ──
        ["PerformanceLoggingMiddleware"]  = new("⏱️", "\x1b[90m"),
        ["SlowQueryInterceptor"]          = new("🐌", "\x1b[93m"),
    }.ToFrozenDictionary();

    private static readonly string[] LevelLabels = ["VRB", "DBG", "INF", "WRN", "ERR", "FTL"];
    private static readonly string[] LevelColors =
    [
        "\x1b[90m",   // Verbose - gray
        "\x1b[90m",   // Debug - gray
        "\x1b[92m",   // Info - bright green
        "\x1b[93m",   // Warning - bright yellow
        "\x1b[91m",   // Error - bright red
        "\x1b[91;1m", // Fatal - bold bright red
    ];

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var levelIndex = (int)logEvent.Level;
        if (levelIndex < 0 || levelIndex >= LevelLabels.Length) levelIndex = 2;

        var levelLabel = LevelLabels[levelIndex];
        var levelColor = LevelColors[levelIndex];

        var timestamp = logEvent.Timestamp.ToString("HH:mm:ss");

        var sourceContext = "";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sc))
            sourceContext = sc.ToString().Trim('"');

        var shortName = sourceContext;
        var lastDot = sourceContext.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < sourceContext.Length - 1)
            shortName = sourceContext[(lastDot + 1)..];

        var emoji = "";
        var serviceColor = "\x1b[37m"; // default white

        if (ServiceMap.TryGetValue(shortName, out var style))
        {
            emoji = style.Emoji + " ";
            serviceColor = style.AnsiColor;
        }

        var isError = logEvent.Level >= LogEventLevel.Error;
        var msgColor = isError ? BrightRed : serviceColor;

        output.Write(DimGray);
        output.Write('[');
        output.Write(timestamp);
        output.Write(' ');
        output.Write(levelColor);
        output.Write(levelLabel);
        output.Write(DimGray);
        output.Write("] ");

        output.Write(emoji);
        output.Write(serviceColor);

        if (!string.IsNullOrEmpty(shortName))
        {
            output.Write(shortName);
            output.Write(Reset);
            output.Write(DimGray);
            output.Write(": ");
        }

        output.Write(msgColor);
        output.Write(logEvent.RenderMessage());
        output.Write(Reset);

        if (logEvent.Properties.Count > 0)
        {
            var hasNonSourceProps = false;
            foreach (var prop in logEvent.Properties)
            {
                if (prop.Key == "SourceContext") continue;
                if (!hasNonSourceProps) { output.Write(DimGray); output.Write(" {"); hasNonSourceProps = true; }
                else output.Write(", ");
                output.Write(prop.Key);
                output.Write(": ");
                prop.Value.Render(output);
            }
            if (hasNonSourceProps) { output.Write('}'); output.Write(Reset); }
        }

        output.WriteLine();

        if (logEvent.Exception != null)
        {
            output.Write(BrightRed);
            output.WriteLine(logEvent.Exception);
            output.Write(Reset);
        }
    }
}
