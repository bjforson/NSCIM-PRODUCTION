using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ContainerValidation;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for managing container data completeness between scanners and ICUMS
    /// </summary>
    public class ContainerCompletenessService : BackgroundService, IContainerCompletenessService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly EnhancedColorCodedLogger _logger; // ✅ Using Enhanced Color-Coded Logger
        // Audit 8.10/8.13 (Sprint 5G2): keep the raw MEL ILogger alongside the
        // colour-coded wrapper so we can call BeginCycle / LogIterationSummary
        // (extension methods on ILogger) without modifying the wrapper.
        private readonly ILogger<ContainerCompletenessService> _rawLogger;
        // Audit 8.13 (Sprint 5G2): monotonic iteration counter for heartbeat.
        private int _cycleCount = 0;
        private readonly GoLiveOptions _goLiveOptions;
        private readonly bool _cmrCompositeProgressionEnabled;
        private const string SERVICE_ID = "CONTAINER-COMPLETENESS";

        public ContainerCompletenessService(
            IServiceProvider serviceProvider,
            ILogger<ContainerCompletenessService> logger,
            IOptions<GoLiveOptions> goLiveOptions,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = new EnhancedColorCodedLogger(logger, "CONTAINER-COMPLETENESS", SERVICE_ID);
            _rawLogger = logger;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
            _cmrCompositeProgressionEnabled = configuration.GetValue<bool>("CmrCompositeProgression:Enabled", false);
        }

        /// <summary>
        /// Converts ClearanceType enum to string for storage (CMR, IMEX, or null)
        /// </summary>
        private static string? ClearanceTypeToString(ClearanceType? clearanceType)
        {
            return clearanceType switch
            {
                ClearanceType.CMR => "CMR",
                ClearanceType.IMEX => "IMEX",
                _ => null
            };
        }

        private bool TryBuildCmrCompositeKey(
            string? clearanceType,
            NickScanCentralImagingPortal.Core.Models.BOEDocument? primaryBOE,
            string containerNumber,
            out CmrCompositeKey compositeKey)
        {
            compositeKey = CmrCompositeKey.Empty;

            if (!_cmrCompositeProgressionEnabled
                || primaryBOE == null
                || !string.Equals(clearanceType, "CMR", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return CmrCompositeKeyHelper.TryCreate(
                primaryBOE.RotationNumber,
                containerNumber,
                primaryBOE.BlNumber,
                out compositeKey);
        }

        private static bool ShouldPreserveCmrCompositeGroupIdentifier(
            string? existingGroupIdentifier,
            string? proposedGroupIdentifier)
        {
            return CmrCompositeKeyHelper.IsOperationalKey(existingGroupIdentifier)
                && !string.Equals(
                    existingGroupIdentifier?.Trim(),
                    proposedGroupIdentifier?.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }

        // ─── Match Correction Tool: anomaly flag helper ─────────────────────────
        // Persists a MatchQualityFlag row capturing a matching anomaly so the
        // admin /validation/match-corrections page can list it. Idempotent for
        // unresolved flags of the same (container, type) — re-running the
        // matching pipeline against the same problem container will not flood
        // the table with duplicates.
        private async Task WriteMatchQualityFlagAsync(
            ApplicationDbContext dbContext,
            string containerNumber,
            string? scannerType,
            int? boeDocumentId,
            string flagType,
            string severity,
            string description,
            CancellationToken cancellationToken)
        {
            try
            {
                var existing = await dbContext.MatchQualityFlags
                    .Where(f => f.ContainerNumber == containerNumber
                                && f.FlagType == flagType
                                && !f.IsResolved)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existing != null)
                {
                    // Refresh description so the admin sees the latest values without
                    // creating a duplicate row. Don't touch CreatedAtUtc — first-seen
                    // time is more useful for triage than last-seen.
                    existing.Description = description;
                    existing.Severity = severity;
                    existing.BOEDocumentId = boeDocumentId;
                    existing.ScannerType = scannerType;
                    return;
                }

                dbContext.MatchQualityFlags.Add(new MatchQualityFlag
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType,
                    BOEDocumentId = boeDocumentId,
                    FlagType = flagType,
                    Severity = severity,
                    Description = description,
                    IsResolved = false,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                // Best-effort: never let flag persistence break the matching pipeline.
                _logger.LogWarning(ex,
                    "{ServiceId} Failed to persist MatchQualityFlag for {Container} ({Type}) — continuing",
                    SERVICE_ID, containerNumber, flagType);
            }
        }

        // ─── Audit 3.03 (2026-05-05): single fyco-rule entry point ─────────────
        // Hoisted out of Step 1 (which had it inline) and Step 2 (which had no
        // fyco rule at all — a previously-Complete container could keep
        // hasICUMSData=true after a CMR→IM upgrade landed mid-flight). The pure
        // rule logic lives in Core (FycoRuleEvaluator); this method does the
        // surrounding I/O — fetch FS6000 scan, dispatch on outcome, write the
        // MatchQualityFlag, log appropriately. Returns true when the rule blocked
        // the match (caller must reset hasICUMSData / primaryBOE / boeRecords).
        // ContainerDataMapperService has its own variant inlined for the same
        // rule (different flag-writer signature) — they evaluate via the same
        // FycoRuleEvaluator.
        private async Task<bool> EvaluateAndApplyFycoRuleAsync(
            ApplicationDbContext dbContext,
            string containerNumber,
            string scannerType,
            NickScanCentralImagingPortal.Core.Models.BOEDocument primaryBOE,
            CancellationToken cancellationToken)
        {
            string? fycoPresent = null;
            if (string.Equals(scannerType, CommonScannerTypes.FS6000, StringComparison.OrdinalIgnoreCase))
            {
                var fs6000Scan = await dbContext.FS6000Scans
                    .AsNoTracking()
                    .Where(s => s.ContainerNumber == containerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => new { s.FycoPresent })
                    .FirstOrDefaultAsync(cancellationToken);
                fycoPresent = fs6000Scan?.FycoPresent;
            }

            var result = FycoRuleEvaluator.Evaluate(
                scannerType,
                fycoPresent,
                primaryBOE.ClearanceType,
                primaryBOE.RegimeCode);

            switch (result.Outcome)
            {
                case FycoRuleOutcome.FailLayer2_ClearanceTypeImport:
                    _logger.LogError(
                        "{ServiceId} FYCO MISMATCH (CRITICAL, layer 2): {Container} scan FycoPresent='{Fyco}' (Export) but BOE.ClearanceType='{Clearance}' (Import). Blocking match.",
                        SERVICE_ID, containerNumber, fycoPresent, primaryBOE.ClearanceType);
                    await WriteMatchQualityFlagAsync(
                        dbContext,
                        containerNumber,
                        scannerType,
                        primaryBOE.Id,
                        flagType: "FycoMismatch",
                        severity: "Critical",
                        description: result.FlagDescription!,
                        cancellationToken);
                    return true;

                case FycoRuleOutcome.FailLayer3_NonExportRegime:
                    _logger.LogError(
                        "{ServiceId} FYCO MISMATCH (CRITICAL, layer 3): {Container} scan FycoPresent='{Fyco}' (Export) but BOE.RegimeCode='{Regime}' is not an export regime (clearance={Clearance}). Blocking match.",
                        SERVICE_ID, containerNumber, fycoPresent, primaryBOE.RegimeCode, primaryBOE.ClearanceType);
                    await WriteMatchQualityFlagAsync(
                        dbContext,
                        containerNumber,
                        scannerType,
                        primaryBOE.Id,
                        flagType: "FycoMismatch",
                        severity: "Critical",
                        description: result.FlagDescription!,
                        cancellationToken);
                    return true;

                case FycoRuleOutcome.WarningSuspicious_UnknownFycoVsExportBoe:
                    _logger.LogWarning(
                        "{ServiceId} FYCO SUSPICIOUS: {Container} BOE.ClearanceType='{Clearance}' (Export) but scan FycoPresent='{Fyco}' (Unknown). Allowing with flag.",
                        SERVICE_ID, containerNumber, primaryBOE.ClearanceType, fycoPresent ?? "(empty)");
                    await WriteMatchQualityFlagAsync(
                        dbContext,
                        containerNumber,
                        scannerType,
                        primaryBOE.Id,
                        flagType: "FycoMismatch",
                        severity: "Warning",
                        description: result.FlagDescription!,
                        cancellationToken);
                    return false;

                case FycoRuleOutcome.NotApplicable:
                case FycoRuleOutcome.Pass:
                default:
                    return false;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Add random startup delay to prevent all services from starting simultaneously
            var randomDelay = Random.Shared.Next(1000, 5000);
            await Task.Delay(randomDelay, stoppingToken);

            _logger.LogInformation("{ServiceId} Container Completeness Service started", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2): mint per-cycle CorrelationId so every
                // log line emitted during this iteration carries the same key.
                using var _cycleScope = _rawLogger.BeginCycle(nameof(ContainerCompletenessService));
                // Audit 8.13 (Sprint 5G2): track elapsed for the heartbeat below.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                int _failedThisCycle = 0;
                try
                {
                    await CheckContainerCompletenessAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service cancelled - exit gracefully
                    _logger.LogInformation("{ServiceId} Service cancelled", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    // Check if it's a database connectivity error
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _logger.LogWarning(ex, "{ServiceId} Database connectivity issue during completeness check (This is normal during startup)", SERVICE_ID);
                    }
                    else
                    {
                        _logger.LogError(ex, "{ServiceId} Error during container completeness check", SERVICE_ID);
                    }
                }

                // Audit 8.13 (Sprint 5G2): per-iteration heartbeat. Items are
                // hard to count from here (CheckContainerCompletenessAsync is
                // queue-drain shaped and doesn't return a count), so processed=0
                // for an idle cycle and failed=1 if the body threw — operators
                // get a continuous "I'm alive" signal regardless.
                _rawLogger.LogIterationSummary(
                    SERVICE_ID,
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: 0,
                    itemsSkipped: 0,
                    itemsFailed: _failedThisCycle);

                // ✅ QUEUE ARCHITECTURE: Reduced delay since we're event-driven (queue-based)
                // Process queue items more frequently for better responsiveness
                // If queue is empty, we can wait longer, but if items exist, process them quickly
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // 30 seconds for event-driven processing
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task CheckContainerCompletenessAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IContainerScanQueueRepository>();

            _logger.LogInformation("{ServiceId} Starting container completeness check (Queue-Based Architecture)", SERVICE_ID);

            try
            {
                int newRecords = 0;
                int updatedRecords = 0;
                int unchangedRecords = 0;
                int queueItemsProcessed = 0;

                // Safeguard anomaly counters (aggregated across Step 1 + Step 2)
                int exportDetectedCount = 0;
                int locationMismatchCount = 0;
                int nullDeliveryPlaceCount = 0;
                int emptyFycoCount = 0;
                var readyDeclarations = new HashSet<(string Declaration, string Container)>();

                // Check if Export-Pending re-evaluation is enabled via system settings
                var reEvalExports = await IsExportReEvalEnabled(dbContext);
                if (reEvalExports)
                {
                    _logger.LogInformation("{ServiceId} 🔄 Export re-evaluation is ENABLED — Export-Pending records will be re-checked for BOE matches", SERVICE_ID);
                }

                // ✅ QUEUE ARCHITECTURE STEP 1: CONSUME FROM QUEUE
                // Scanner services push scans to queue, we consume them here
                // This is event-driven and works with ANY scanner type dynamically (future-proof!)
                _logger.LogInformation("{ServiceId} 📥 STEP 1: Consuming scans from ContainerScanQueue...", SERVICE_ID);

                // ✅ CRITICAL FIX: Recover stuck items BEFORE getting next batch
                // Items stuck in "Processing" status (from crashes/restarts) won't be picked up
                try
                {
                    var recoveredCount = await queueRepository.RecoverStuckProcessingItemsAsync(timeoutMinutes: 30);
                    if (recoveredCount > 0)
                    {
                        _logger.LogInformation("{ServiceId} 🔄 Recovered {Count} stuck queue items (were processing for >30 minutes)",
                            SERVICE_ID, recoveredCount);
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogWarning(recoveryEx, "{ServiceId} Error recovering stuck queue items", SERVICE_ID);
                }

                // Get next batch of queue items ready for processing (ordered by priority, then oldest first)
                var queueItems = await queueRepository.GetNextBatchAsync(batchSize: 100);

                if (!queueItems.Any())
                {
                    // ✅ ENHANCED LOGGING: Check why no items were retrieved
                    var totalPending = await dbContext.ContainerScanQueues
                        .CountAsync(q => q.Status == ContainerScanQueueStatus.Pending, stoppingToken);
                    var stuckProcessing = await dbContext.ContainerScanQueues
                        .CountAsync(q => q.Status == ContainerScanQueueStatus.Processing, stoppingToken);
                    var exceededRetries = await dbContext.ContainerScanQueues
                        .CountAsync(q => q.Status == ContainerScanQueueStatus.Pending && q.RetryCount >= q.MaxRetries, stoppingToken);

                    _logger.LogInformation("{ServiceId} ⚠️ No items retrieved from queue. Stats: {TotalPending} pending, {StuckProcessing} stuck in Processing, {ExceededRetries} exceeded max retries",
                        SERVICE_ID, totalPending, stuckProcessing, exceededRetries);
                }
                else
                {
                    _logger.LogInformation("{ServiceId} 📋 Retrieved {Count} items from queue for processing",
                        SERVICE_ID, queueItems.Count);

                    // Process each queue item
                    var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
                    var queueItemsToComplete = new List<int>();
                    foreach (var queueItem in queueItems)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        // Go-live cutoff: skip items before GoLiveDate (mark as completed without processing)
                        if (goLiveDate > DateTime.MinValue && queueItem.ScanDate < goLiveDate)
                        {
                            try
                            {
                                await queueRepository.MarkAsCompletedAsync(queueItem.Id);
                                _logger.LogDebug("{ServiceId} ⏭️ Skipped {Container} (ScanDate {ScanDate} before GoLiveDate {GoLiveDate})",
                                    SERVICE_ID, queueItem.ContainerNumber, queueItem.ScanDate.ToString("yyyy-MM-dd"), goLiveDate.ToString("yyyy-MM-dd"));
                            }
                            catch (Exception skipEx)
                            {
                                _logger.LogWarning(skipEx, "{ServiceId} Error marking pre-GoLive queue item {QueueId} as completed", SERVICE_ID, queueItem.Id);
                            }
                            continue;
                        }

                        try
                        {
                            // ✅ ENHANCED LOGGING: Track MarkAsProcessingAsync execution
                            _logger.LogInformation("{ServiceId} 🔄 Attempting to mark queue item {QueueId} ({Container}, {ScannerType}, InspectionId: {InspectionId}) as Processing...",
                                SERVICE_ID, queueItem.Id, queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId);

                            // Mark as processing to prevent other instances from picking it up
                            await queueRepository.MarkAsProcessingAsync(queueItem.Id);

                            _logger.LogInformation("{ServiceId} ✅ Successfully marked queue item {QueueId} ({Container}) as Processing",
                                SERVICE_ID, queueItem.Id, queueItem.ContainerNumber);

                            // Check if completeness record already exists (by ContainerNumber + ScannerType + InspectionId)
                            // AsTracking required: default is NoTracking, but we need to persist changes via SaveChangesAsync
                            var existingStatus = await dbContext.ContainerCompletenessStatuses
                                .AsTracking()
                                .FirstOrDefaultAsync(c =>
                                    c.ContainerNumber == queueItem.ContainerNumber &&
                                    c.ScannerType == queueItem.ScannerType &&
                                    c.InspectionId == queueItem.InspectionId,
                                    stoppingToken);

                            if (existingStatus != null)
                            {
                                // Already processed - just update LastCheckedAt and mark queue item as completed
                                existingStatus.LastCheckedAt = DateTime.UtcNow;

                                queueItemsToComplete.Add(queueItem.Id);
                                unchangedRecords++;
                                _logger.LogDebug("{ServiceId} ⏭️ Skipped {Container} ({ScannerType}) - already processed (InspectionId: {InspectionId}), queued completion after save",
                                    SERVICE_ID, queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId);

                                continue;
                            }

                            // ═══════════════════════════════════════════════════════════════
                            // SAFEGUARD: Export detection for FS6000 (Takoradi) scans
                            // Exports don't have matching ICUMS import data yet
                            // ═══════════════════════════════════════════════════════════════
                            FycoCategory fycoCategory = FycoCategory.Unknown;
                            if (queueItem.ScannerType.Equals(CommonScannerTypes.FS6000, StringComparison.OrdinalIgnoreCase))
                            {
                                var fs6000Scan = await dbContext.FS6000Scans
                                    .AsNoTracking()
                                    .Where(s => s.ContainerNumber == queueItem.ContainerNumber)
                                    .OrderByDescending(s => s.ScanTime)
                                    .Select(s => new { s.FycoPresent })
                                    .FirstOrDefaultAsync(stoppingToken);

                                fycoCategory = FycoClassifier.Classify(fs6000Scan?.FycoPresent);

                                if (fycoCategory == FycoCategory.Export)
                                {
                                    exportDetectedCount++;
                                    _logger.LogInformation("{ServiceId} EXPORT detected: {Container} (FycoPresent: {Fyco}) — holding as Export-Pending",
                                        SERVICE_ID, queueItem.ContainerNumber, fs6000Scan?.FycoPresent);

                                    var (expHasImages, expImageCount) = await CheckImagesExistAsync(queueItem.ContainerNumber, queueItem.ScannerType, dbContext);

                                    var exportStatus = new ContainerCompletenessStatus
                                    {
                                        ContainerNumber = queueItem.ContainerNumber,
                                        ScannerType = queueItem.ScannerType,
                                        InspectionId = queueItem.InspectionId,
                                        ScanDate = queueItem.ScanDate,
                                        HasScannerData = true,
                                        HasICUMSData = false,
                                        HasImageData = expHasImages,
                                        ScannerDataCompleteness = 100,
                                        ICUMSDataCompleteness = 0,
                                        ImageDataCompleteness = expImageCount > 0 ? 100 : 0,
                                        OverallCompleteness = (100 + 0 + (expImageCount > 0 ? 100 : 0)) / 3,
                                        Status = "Export-Pending",
                                        WorkflowStage = "Export-Hold",
                                        ErrorMessage = $"Export cargo (FycoPresent: {fs6000Scan?.FycoPresent}) — ICUMS export data not yet available",
                                        CreatedAt = DateTime.UtcNow,
                                        UpdatedAt = DateTime.UtcNow,
                                        LastCheckedAt = DateTime.UtcNow,
                                        RetryCount = 0
                                    };

                                    dbContext.ContainerCompletenessStatuses.Add(exportStatus);
                                    newRecords++;
                                    queueItemsProcessed++;
                                    queueItemsToComplete.Add(queueItem.Id);
                                    continue;
                                }

                                if (fs6000Scan != null && string.IsNullOrWhiteSpace(fs6000Scan.FycoPresent))
                                {
                                    emptyFycoCount++;
                                }
                            }

                            // ✅ MULTI-CONTAINER FIX: Split container string if it contains multiple containers
                            var individualContainers = queueItem.ContainerNumber
                                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(c => c.Trim())
                                .ToList();

                            bool hasICUMSData = false;
                            var boeRecords = new List<NickScanCentralImagingPortal.Core.Models.BOEDocument>();

                            if (individualContainers.Count > 1)
                            {
                                // Multi-container: Check EACH container individually
                                _logger.LogInformation("{ServiceId} Multi-container detected: {Containers}",
                                    SERVICE_ID, string.Join(", ", individualContainers));

                                foreach (var singleContainer in individualContainers)
                                {
                                    var containerBoeRecords = await icumsDownloadsDbContext.BOEDocuments
                                        .Where(b => b.ContainerNumber == singleContainer)
                                        .ToListAsync(stoppingToken);

                                    boeRecords.AddRange(containerBoeRecords);
                                }

                                hasICUMSData = individualContainers.All(c =>
                                    boeRecords.Any(b => b.ContainerNumber == c));
                            }
                            else
                            {
                                // Single container: Normal check
                                hasICUMSData = await icumsDownloadsDbContext.BOEDocuments
                                    .AnyAsync(b => b.ContainerNumber == queueItem.ContainerNumber, stoppingToken);

                                // Keep boeRecords as the FULL set (any ProcessingStatus) — it
                                // feeds House-BL consolidation accounting downstream which must
                                // count all BOE rows. The canonical helper is applied below for
                                // primaryBOE selection only (audit 3.06).
                                boeRecords = await icumsDownloadsDbContext.BOEDocuments
                                    .Where(b => b.ContainerNumber == queueItem.ContainerNumber)
                                    .ToListAsync(stoppingToken);
                            }

                            // ✅ Store the primary BOE document (canonical selection — audit 3.06)
                            // Filter to ProcessingStatus="Transferred" + OrderByDescending Id so
                            // the gate sees the same row Step 2, the mapper, and the validation
                            // service do.
                            var primaryBOE = boeRecords.CanonicalBoeQuery().FirstOrDefault();
                            int? primaryBOEId = primaryBOE?.Id;

                            // ═══════════════════════════════════════════════════════════════
                            // SAFEGUARD: Location gate — scanner port must match BOE DeliveryPlace
                            // FS6000 (Takoradi/TKD) must not match Tema (TMA) BOE data, and vice versa
                            // ═══════════════════════════════════════════════════════════════
                            if (hasICUMSData && primaryBOE != null)
                            {
                                var expectedPort = ScannerLocationMap.GetExpectedPortCode(queueItem.ScannerType);
                                if (expectedPort != null && !string.IsNullOrWhiteSpace(primaryBOE.DeliveryPlace))
                                {
                                    if (!ScannerLocationMap.IsLocationMatch(queueItem.ScannerType, primaryBOE.DeliveryPlace))
                                    {
                                        locationMismatchCount++;
                                        var actualPort = ScannerLocationMap.ExtractPortCode(primaryBOE.DeliveryPlace) ?? "UNKNOWN";
                                        _logger.LogWarning("{ServiceId} LOCATION MISMATCH: {Container} scanned at {ScannerType} (expected {Expected}) but BOE DeliveryPlace={DeliveryPlace} (port={Actual}). Blocking match.",
                                            SERVICE_ID, queueItem.ContainerNumber, queueItem.ScannerType, expectedPort, primaryBOE.DeliveryPlace, actualPort);

                                        await WriteMatchQualityFlagAsync(
                                            dbContext,
                                            queueItem.ContainerNumber,
                                            queueItem.ScannerType,
                                            primaryBOE.Id,
                                            flagType: "PortMismatch",
                                            severity: "Critical",
                                            description: $"Scanned at {queueItem.ScannerType} (expected port {expectedPort}) but BOE.DeliveryPlace='{primaryBOE.DeliveryPlace}' (port {actualPort}). Match blocked pending admin review.",
                                            stoppingToken);

                                        hasICUMSData = false;
                                        primaryBOEId = null;
                                        primaryBOE = null;
                                        boeRecords.Clear();
                                    }
                                }
                                else if (expectedPort != null && string.IsNullOrWhiteSpace(primaryBOE.DeliveryPlace))
                                {
                                    // ─── PREVENTION HARDENING (Match Correction Tool) ───
                                    // Previously: allow the match through and just log a warning.
                                    // Now: block the match outright and persist a Critical flag so
                                    // the admin tool can surface it. The location gate cannot
                                    // verify the match without DeliveryPlace, and we have a
                                    // documented case of wrong matches slipping through this
                                    // branch (record 80126035944 — see plan).
                                    nullDeliveryPlaceCount++;
                                    _logger.LogWarning("{ServiceId} NULL DELIVERY PLACE BLOCK: {Container} BOEDocumentId={BoeId} has no DeliveryPlace — blocking match and flagging for admin review",
                                        SERVICE_ID, queueItem.ContainerNumber, primaryBOE.Id);

                                    await WriteMatchQualityFlagAsync(
                                        dbContext,
                                        queueItem.ContainerNumber,
                                        queueItem.ScannerType,
                                        primaryBOE.Id,
                                        flagType: "NullDeliveryPlace",
                                        severity: "Critical",
                                        description: $"BOEDocumentId={primaryBOE.Id} has no DeliveryPlace; location gate cannot verify scanner '{queueItem.ScannerType}' (expected port '{expectedPort}'). Match blocked pending admin review.",
                                        stoppingToken);

                                    hasICUMSData = false;
                                    primaryBOEId = null;
                                    primaryBOE = null;
                                    boeRecords.Clear();
                                }
                            }

                            // ─── PREVENTION HARDENING: Fyco / ClearanceType cross-check ───
                            // 3-layer fyco rule. Trigger record 80126035944 — export scans
                            // that landed against an import BOE because FycoPresent was
                            // empty. Defence in depth: even if location and date proximity
                            // allow the match, cross-check the scanner's fyco signal against
                            // the BOE's clearance type and regime.
                            //
                            // Audit 3.03 (2026-05-05): hoisted into the shared
                            // EvaluateAndApplyFycoRuleAsync helper above so Step 1, Step 2,
                            // and the mapper all reach the same verdict via FycoRuleEvaluator.
                            if (hasICUMSData && primaryBOE != null)
                            {
                                var fycoBlocked = await EvaluateAndApplyFycoRuleAsync(
                                    dbContext,
                                    queueItem.ContainerNumber,
                                    queueItem.ScannerType,
                                    primaryBOE,
                                    stoppingToken);

                                if (fycoBlocked)
                                {
                                    hasICUMSData = false;
                                    primaryBOEId = null;
                                    primaryBOE = null;
                                    boeRecords.Clear();
                                }
                            }

                            // DATE PROXIMITY: Detect possible container number reuse (>90 days apart = likely different shipment)
                            if (hasICUMSData && primaryBOE != null && queueItem.ScanDate != default)
                            {
                                DateTime? boeDate = null;
                                if (!string.IsNullOrWhiteSpace(primaryBOE.DeclarationDate) &&
                                    DateTime.TryParse(primaryBOE.DeclarationDate, out var parsedBoeDate))
                                {
                                    boeDate = parsedBoeDate;
                                }

                                if (boeDate.HasValue && boeDate.Value != default &&
                                    Math.Abs((queueItem.ScanDate - boeDate.Value).TotalDays) > 90)
                                {
                                    _logger.LogWarning("{ServiceId} DATE PROXIMITY: {Container} scan date {ScanDate} is {Days:F0} days from BOE date {BoeDate}. Possible container reuse.",
                                        SERVICE_ID, queueItem.ContainerNumber, queueItem.ScanDate, Math.Abs((queueItem.ScanDate - boeDate.Value).TotalDays), boeDate.Value);
                                    hasICUMSData = false;
                                    primaryBOEId = null;
                                    primaryBOE = null;
                                    boeRecords.Clear();
                                }
                            }

                            // ✅ Detect and store ClearanceType
                            string? clearanceType = null;
                            if (primaryBOE != null)
                            {
                                var clearanceTypeDetectionService = scope.ServiceProvider.GetRequiredService<IClearanceTypeDetectionService>();
                                var detectedClearanceType = clearanceTypeDetectionService.DetectClearanceTypeFromBOE(primaryBOE);
                                clearanceType = ClearanceTypeToString(detectedClearanceType);
                            }

                            // ✅ Check if images exist
                            var (hasImages, imageCount) = await CheckImagesExistAsync(queueItem.ContainerNumber, queueItem.ScannerType, dbContext);

                            // ─── PREVENTION HARDENING: Duplicate image filename detection ───
                            // The trigger record (80126035944) had two distinct containers
                            // (DFSU1568154, MSMU1095136) sharing the same FS6000Image filename
                            // 23301FS01202603270005.jpg. This is a strong signal of a
                            // cross-record scan that needs human review. Surface it as a flag.
                            if (hasImages && queueItem.ScannerType == "FS6000")
                            {
                                try
                                {
                                    var ownScanIds = await dbContext.FS6000Scans
                                        .AsNoTracking()
                                        .Where(s => s.ContainerNumber == queueItem.ContainerNumber)
                                        .Select(s => s.Id)
                                        .ToListAsync(stoppingToken);

                                    if (ownScanIds.Any())
                                    {
                                        var sharedFileNames = await dbContext.FS6000Images
                                            .AsNoTracking()
                                            .Where(i => ownScanIds.Contains(i.ScanId)
                                                        && !string.IsNullOrEmpty(i.FileName))
                                            .Select(i => i.FileName)
                                            .Distinct()
                                            .ToListAsync(stoppingToken);

                                        foreach (var fileName in sharedFileNames)
                                        {
                                            var sharedWith = await dbContext.FS6000Images
                                                .AsNoTracking()
                                                .Where(i => i.FileName == fileName && !ownScanIds.Contains(i.ScanId))
                                                .Join(dbContext.FS6000Scans.AsNoTracking(),
                                                      img => img.ScanId,
                                                      scan => scan.Id,
                                                      (img, scan) => scan.ContainerNumber)
                                                .Where(c => c != null && c != queueItem.ContainerNumber)
                                                .Distinct()
                                                .Take(5)
                                                .ToListAsync(stoppingToken);

                                            if (sharedWith.Any())
                                            {
                                                _logger.LogWarning("{ServiceId} DUPLICATE IMAGE: {Container} shares filename '{File}' with {OtherCount} other container(s): {Others}",
                                                    SERVICE_ID, queueItem.ContainerNumber, fileName, sharedWith.Count, string.Join(", ", sharedWith));

                                                await WriteMatchQualityFlagAsync(
                                                    dbContext,
                                                    queueItem.ContainerNumber,
                                                    queueItem.ScannerType,
                                                    primaryBOEId,
                                                    flagType: "DuplicateImage",
                                                    severity: "Warning",
                                                    description: $"FS6000 image '{fileName}' is shared with other container(s): {string.Join(", ", sharedWith)}. Possible cross-record scan needing human review.",
                                                    stoppingToken);

                                                // One flag per container is enough — no need to spam.
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch (Exception dupEx)
                                {
                                    _logger.LogWarning(dupEx,
                                        "{ServiceId} Duplicate-image check failed for {Container} — continuing",
                                        SERVICE_ID, queueItem.ContainerNumber);
                                }
                            }

                            // Get consolidation info
                            bool isConsolidated = boeRecords.Any(b => b.IsConsolidated);
                            int? totalHouseBLs = null;
                            int? completeHouseBLs = null;
                            string? consolidationDetails = null;
                            string? groupIdentifier = null;

                            if (isConsolidated && boeRecords.Any())
                            {
                                // CONSOLIDATED: Group by Container, build House BL details
                                totalHouseBLs = boeRecords.Count;
                                completeHouseBLs = boeRecords.Count(b =>
                                    !string.IsNullOrEmpty(b.DeclarationNumber) &&
                                    !string.IsNullOrEmpty(b.RotationNumber));

                                var houseBLDetails = boeRecords.Select(b => new
                                {
                                    HouseBL = b.HouseBl ?? "N/A",
                                    HasData = !string.IsNullOrEmpty(b.DeclarationNumber),
                                    ICUMSData = new
                                    {
                                        b.ContainerNumber,
                                        b.DeclarationNumber,
                                        HouseBL = b.HouseBl,
                                        MasterBL = b.BlNumber,
                                        Consignee = b.ConsigneeName,
                                        b.GoodsDescription,
                                        b.RotationNumber,
                                        TransitType = b.ClearanceType,
                                        RiskLevel = b.CrmsLevel,
                                        ScannerRequired = b.CrmsLevel == "H" || b.CrmsLevel == "High" ? "Yes" : "No"
                                    }
                                }).ToList();

                                consolidationDetails = System.Text.Json.JsonSerializer.Serialize(houseBLDetails);
                                groupIdentifier = queueItem.ContainerNumber;
                            }
                            else if (boeRecords.Any())
                            {
                                // NON-CONSOLIDATED: Group by BOE
                                var declaration = boeRecords.First().DeclarationNumber;
                                groupIdentifier = declaration;

                                var containersUnderBOE = await icumsDownloadsDbContext.BOEDocuments
                                    .Where(b => b.DeclarationNumber == declaration)
                                    .Select(b => b.ContainerNumber)
                                    .Distinct()
                                    .CountAsync(stoppingToken);

                                consolidationDetails = $"{containersUnderBOE} container(s)";
                            }

                            if (TryBuildCmrCompositeKey(clearanceType, primaryBOE, queueItem.ContainerNumber, out var cmrCompositeKey))
                            {
                                groupIdentifier = cmrCompositeKey.OperationalKey;
                                consolidationDetails = cmrCompositeKey.DisplayLabel;
                            }

                            // 1.19.0 — CMR clearance type special case.
                            // CMR = "Cargo Movement Record" in ICUMS terminology: manifest-only
                            // data, no declaration filed yet. A CMR row has NO DeclarationNumber
                            // by definition, so the group-by-BOE logic above produces an empty
                            // groupIdentifier. Before this fix, CMR rows were being marked as
                            // Status=Complete + WorkflowStage=ImageAnalysis anyway, which made
                            // them invisible to IntakeWorker (which filters on non-empty
                            // groupIdentifier) and stuck them in a never-processed state. 650
                            // such rows existed in prod at release time.
                            //
                            // The correct behaviour: don't mark CMR rows as Complete at all.
                            // They should stay Pending until the declaration arrives and the
                            // 1.13.0 CMR-to-IM/EX lifecycle service upgrades them — at which
                            // point the completeness service will re-process them with a
                            // proper declaration number and valid groupIdentifier.
                            var completenessDecision = ContainerCompletenessPolicy.Evaluate(
                                hasScannerData: true,
                                hasICUMSData,
                                hasImageData: hasImages,
                                clearanceType,
                                groupIdentifier,
                                cmrCompositeProgressionEnabled: _cmrCompositeProgressionEnabled,
                                cmrRotationNumber: primaryBOE?.RotationNumber,
                                cmrContainerNumber: queueItem.ContainerNumber,
                                cmrBlNumber: primaryBOE?.BlNumber);

                            // Create completeness status record
                            var newStatus = new ContainerCompletenessStatus
                            {
                                ContainerNumber = queueItem.ContainerNumber,
                                ScannerType = queueItem.ScannerType, // ✅ Dynamic - works with ANY scanner type!
                                InspectionId = queueItem.InspectionId,
                                ScanDate = queueItem.ScanDate,
                                HasScannerData = true,
                                HasICUMSData = hasICUMSData,
                                HasImageData = hasImages,
                                ScannerDataCompleteness = 100,
                                ICUMSDataCompleteness = hasICUMSData ? 100 : 0,
                                ImageDataCompleteness = imageCount > 0 ? 100 : 0,
                                OverallCompleteness = (100 + (hasICUMSData ? 100 : 0) + (imageCount > 0 ? 100 : 0)) / 3,
                                IsConsolidated = isConsolidated,
                                TotalHouseBLs = totalHouseBLs,
                                CompleteHouseBLs = completeHouseBLs,
                                ConsolidationDetails = consolidationDetails,
                                GroupIdentifier = groupIdentifier,
                                BOEDocumentId = primaryBOEId,
                                ClearanceType = clearanceType,
                                ICUMSDataDate = hasICUMSData ? DateTime.UtcNow : null,
                                Status = completenessDecision.Status,
                                WorkflowStage = completenessDecision.WorkflowStage,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                                LastCheckedAt = DateTime.UtcNow,
                                RetryCount = 0
                            };

                            dbContext.ContainerCompletenessStatuses.Add(newStatus);
                            newRecords++;
                            queueItemsProcessed++;

                            // Track containers with images for event-driven record promotion
                            if (hasImages && !string.IsNullOrWhiteSpace(groupIdentifier))
                                readyDeclarations.Add((groupIdentifier, queueItem.ContainerNumber));

                            queueItemsToComplete.Add(queueItem.Id);

                            _logger.LogInformation("{ServiceId} ✅ Processed from queue: {Container} ({ScannerType}, InspectionId: {InspectionId}) - Status: {Status}, BOEId: {BOEId}",
                                SERVICE_ID, queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId, newStatus.Status, primaryBOEId);

                            if (!hasICUMSData && newStatus.RetryCount < 3)
                            {
                                foreach (var singleContainer in individualContainers)
                                {
                                    var containerHasBoe = await icumsDownloadsDbContext.BOEDocuments
                                        .AnyAsync(b => b.ContainerNumber == singleContainer, stoppingToken);

                                    if (!containerHasBoe)
                                    {
                                        var existingRequest = await dbContext.ManualBOERequests
                                            .FirstOrDefaultAsync(r => r.ContainerNumber == singleContainer &&
                                                                     r.Status != "Completed" && r.Status != "Failed",
                                                                stoppingToken);

                                        if (existingRequest == null)
                                        {
                                            var boeRequest = new ManualBOERequest
                                            {
                                                ContainerNumber = singleContainer,
                                                RequestedBy = "System",
                                                RequestDate = DateTime.UtcNow,
                                                Status = "Pending",
                                                CreatedAt = DateTime.UtcNow,
                                                UpdatedAt = DateTime.UtcNow
                                            };
                                            dbContext.ManualBOERequests.Add(boeRequest);

                                            _logger.LogInformation("{ServiceId} 📋 Auto-queued {Container} for ICUMS download (missing data)",
                                                SERVICE_ID, singleContainer);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{ServiceId} ❌ Error processing queue item {QueueId} ({Container}, {ScannerType})",
                                SERVICE_ID, queueItem.Id, queueItem.ContainerNumber, queueItem.ScannerType);

                            // Update retry info - if max retries reached, mark as failed
                            await queueRepository.UpdateRetryInfoAsync(queueItem.Id, ex.Message);
                        }
                    }

                    // Save all changes (completeness records + BOE requests)
                    if (newRecords > 0 || unchangedRecords > 0)
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                        foreach (var queueItemId in queueItemsToComplete.Distinct())
                        {
                            try
                            {
                                await queueRepository.MarkAsCompletedAsync(queueItemId);
                            }
                            catch (Exception markCompletedEx)
                            {
                                _logger.LogError(markCompletedEx, "{ServiceId} ❌ Error marking queue item {QueueId} as completed after successful completeness save",
                                    SERVICE_ID, queueItemId);
                            }
                        }

                        // ✅ MEMORY FIX: Clear change tracker to release tracked entities
                        dbContext.ChangeTracker.Clear();
                        _logger.LogInformation("{ServiceId} 💾 Saved {NewRecords} new completeness records (processed {QueueItemsProcessed} queue items)",
                            SERVICE_ID, newRecords, queueItemsProcessed);
                    }

                    // Event-driven record promotion: promote containers that just got images
                    if (readyDeclarations.Count > 0)
                    {
                        try
                        {
                            var recordBuilder = scope.ServiceProvider.GetService<RecordCompleteness.IRecordBuildingService>();
                            if (recordBuilder != null)
                            {
                                foreach (var (declaration, container) in readyDeclarations)
                                {
                                    await recordBuilder.PromoteContainerAndRecomputeAsync(declaration, container, stoppingToken);
                                }
                                _logger.LogInformation("{ServiceId} Promoted {Count} containers in records via event-driven path",
                                    SERVICE_ID, readyDeclarations.Count);
                            }
                        }
                        catch (Exception rbEx)
                        {
                            _logger.LogError(rbEx, "{ServiceId} Error in event-driven record promotion", SERVICE_ID);
                        }
                        readyDeclarations.Clear();
                    }
                }

                // ✅ STEP 2: Process EXISTING containers that need re-checking
                // Complete containers: re-check every 24 hours
                // Missing (new): re-check every 1 hour (may get ICUMS data soon)
                // Missing (confirmed no data, retryCount >= 3): re-check every 12 hours
                var completeStaleDate = DateTime.UtcNow.AddHours(-24);
                var missingStaleDate = DateTime.UtcNow.AddHours(-1);
                var missingConfirmedStaleDate = DateTime.UtcNow.AddHours(-12);
                var maxAgeDate = DateTime.UtcNow.AddDays(-90);

                _logger.LogInformation("{ServiceId} 🔄 STEP 2: Re-checking EXISTING containers (Complete: >24h, Missing: >1h, MissingConfirmed: >12h, MaxAge: 90 days)", SERVICE_ID);

                var recheckBaseQuery = dbContext.ContainerCompletenessStatuses
                    .AsNoTracking()
                    .Where(c => c.ScanDate >= maxAgeDate);

                IQueryable<ContainerCompletenessStatus> recheckFilteredQuery;
                if (reEvalExports)
                {
                    recheckFilteredQuery = recheckBaseQuery.Where(c =>
                        (c.Status == "Complete" && (c.LastCheckedAt == null || c.LastCheckedAt < completeStaleDate)) ||
                        (c.Status == "Export-Pending" && (c.LastCheckedAt == null || c.LastCheckedAt < missingStaleDate)) ||
                        (c.Status != "Complete" && c.Status != "Export-Pending" && c.RetryCount >= 3 && (c.LastCheckedAt == null || c.LastCheckedAt < missingConfirmedStaleDate)) ||
                        (c.Status != "Complete" && c.Status != "Export-Pending" && c.RetryCount < 3 && (c.LastCheckedAt == null || c.LastCheckedAt < missingStaleDate))
                    );
                }
                else
                {
                    recheckFilteredQuery = recheckBaseQuery.Where(c =>
                        c.Status != "Export-Pending" &&
                        (
                            (c.Status == "Complete" && (c.LastCheckedAt == null || c.LastCheckedAt < completeStaleDate)) ||
                            (c.Status != "Complete" && c.RetryCount >= 3 && (c.LastCheckedAt == null || c.LastCheckedAt < missingConfirmedStaleDate)) ||
                            (c.Status != "Complete" && c.RetryCount < 3 && (c.LastCheckedAt == null || c.LastCheckedAt < missingStaleDate))
                        )
                    );
                }

                var containersNeedingCheck = await recheckFilteredQuery
                    .OrderBy(c => c.LastCheckedAt == null ? DateTime.MinValue : c.LastCheckedAt) // ✅ Prioritize nulls (treated as oldest), then oldest LastCheckedAt
                    .ThenByDescending(c => c.ScanDate) // Then by scan date (newest first) for tie-breaking
                    .Take(100) // Process 100 per cycle
                    .Select(c => new { c.Id, c.ContainerNumber, c.ScannerType, c.ScanDate, c.InspectionId })
                    .ToListAsync(stoppingToken);

                var allContainers = containersNeedingCheck
                    .Select(c => (c.Id, c.ContainerNumber, c.ScannerType, c.ScanDate, c.InspectionId))
                    .ToList();

                _logger.LogInformation("{ServiceId} Found {Count} EXISTING containers needing re-check",
                    SERVICE_ID, allContainers.Count);

                // Process each container
                foreach (var container in allContainers)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        // AsTracking required: default is NoTracking, but we need to persist changes via SaveChangesAsync
                        var existingStatus = await dbContext.ContainerCompletenessStatuses
                            .AsTracking()
                            .FirstOrDefaultAsync(c => c.Id == container.Id, stoppingToken);

                        if (existingStatus == null)
                        {
                            _logger.LogWarning("{ServiceId} ⚠️ Record with Id {RecordId} not found - may have been deleted. Skipping.",
                                SERVICE_ID, container.Id);
                            continue;
                        }

                        // SAFEGUARD: Skip Export-Pending records unless re-evaluation is enabled
                        if (existingStatus.Status == "Export-Pending" && !reEvalExports)
                        {
                            existingStatus.LastCheckedAt = DateTime.UtcNow;
                            unchangedRecords++;
                            continue;
                        }

                        // ✅ MULTI-CONTAINER FIX: Split container string if it contains multiple containers
                        var individualContainers = container.ContainerNumber
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(c => c.Trim())
                            .ToList();

                        bool hasICUMSData = false;
                        var boeRecords = new List<NickScanCentralImagingPortal.Core.Models.BOEDocument>();

                        if (individualContainers.Count > 1)
                        {
                            _logger.LogInformation("{ServiceId} Multi-container detected: {Containers}",
                                SERVICE_ID, string.Join(", ", individualContainers));

                            foreach (var singleContainer in individualContainers)
                            {
                                var containerBoeRecords = await icumsDownloadsDbContext.BOEDocuments
                                    .Where(b => b.ContainerNumber == singleContainer)
                                    .ToListAsync(stoppingToken);

                                boeRecords.AddRange(containerBoeRecords);
                            }

                            hasICUMSData = individualContainers.All(c =>
                                boeRecords.Any(b => b.ContainerNumber == c));
                        }
                        else
                        {
                            hasICUMSData = await icumsDownloadsDbContext.BOEDocuments
                                .AnyAsync(b => b.ContainerNumber == container.ContainerNumber, stoppingToken);

                            // Full BOE set retained for House-BL consolidation; canonical
                            // selection happens via CanonicalBoeQuery below (audit 3.06).
                            boeRecords = await icumsDownloadsDbContext.BOEDocuments
                                .Where(b => b.ContainerNumber == container.ContainerNumber)
                                .ToListAsync(stoppingToken);
                        }

                        // ✅ Store the primary BOE document (canonical selection — audit 3.06)
                        var primaryBOE = boeRecords.CanonicalBoeQuery().FirstOrDefault();
                        int? primaryBOEId = primaryBOE?.Id;

                        // SAFEGUARD: Location gate on re-check (same logic as Step 1).
                        // Audit 3.02 (2026-05-05): Step 2 now writes MatchQualityFlag on
                        // both branches mirroring Step 1's contract — previously every
                        // re-check mismatch was silently re-blocked with no admin trail
                        // (24 such cases in 7 days had no MQF coverage). Null-DP branch
                        // also brought into parity: Step 1 blocks + flags, Step 2 used
                        // to log-and-allow; now Step 2 blocks + flags too so the gate
                        // is symmetric and re-evaluation can't promote a null-DP match
                        // that Step 1 already rejected.
                        if (hasICUMSData && primaryBOE != null)
                        {
                            var expectedPort = ScannerLocationMap.GetExpectedPortCode(container.ScannerType);
                            if (expectedPort != null && !string.IsNullOrWhiteSpace(primaryBOE.DeliveryPlace))
                            {
                                if (!ScannerLocationMap.IsLocationMatch(container.ScannerType, primaryBOE.DeliveryPlace))
                                {
                                    locationMismatchCount++;
                                    var actualPort = ScannerLocationMap.ExtractPortCode(primaryBOE.DeliveryPlace) ?? "UNKNOWN";
                                    _logger.LogWarning("{ServiceId} LOCATION MISMATCH (re-check): {Container} scanned at {ScannerType} (expected {Expected}) but BOE DeliveryPlace={DeliveryPlace} (port={Actual}). Blocking match.",
                                        SERVICE_ID, container.ContainerNumber, container.ScannerType, expectedPort, primaryBOE.DeliveryPlace, actualPort);

                                    await WriteMatchQualityFlagAsync(
                                        dbContext,
                                        container.ContainerNumber,
                                        container.ScannerType,
                                        primaryBOE.Id,
                                        flagType: "PortMismatch",
                                        severity: "Critical",
                                        description: $"Re-check blocked match: scanned at {container.ScannerType} (expected port {expectedPort}) but BOE.DeliveryPlace='{primaryBOE.DeliveryPlace}' (port {actualPort}). Match blocked pending admin review.",
                                        stoppingToken);

                                    hasICUMSData = false;
                                    primaryBOEId = null;
                                    primaryBOE = null;
                                    boeRecords.Clear();
                                }
                            }
                            else if (expectedPort != null && string.IsNullOrWhiteSpace(primaryBOE.DeliveryPlace))
                            {
                                nullDeliveryPlaceCount++;
                                _logger.LogWarning("{ServiceId} NULL DELIVERY PLACE BLOCK (re-check): {Container} BOEDocumentId={BoeId} has no DeliveryPlace — blocking match and flagging for admin review",
                                    SERVICE_ID, container.ContainerNumber, primaryBOE.Id);

                                await WriteMatchQualityFlagAsync(
                                    dbContext,
                                    container.ContainerNumber,
                                    container.ScannerType,
                                    primaryBOE.Id,
                                    flagType: "NullDeliveryPlace",
                                    severity: "Critical",
                                    description: $"Re-check: BOEDocumentId={primaryBOE.Id} has no DeliveryPlace; location gate cannot verify scanner '{container.ScannerType}' (expected port '{expectedPort}'). Match blocked pending admin review.",
                                    stoppingToken);

                                hasICUMSData = false;
                                primaryBOEId = null;
                                primaryBOE = null;
                                boeRecords.Clear();
                            }
                        }

                        // ─── FYCO RULE (re-check) — audit 3.03, 2026-05-05 ───
                        // Step 2 previously had no fyco gate at all; a previously-Complete
                        // container that got re-checked after BOE clearancetype changed
                        // (e.g. CMR→IM upgrade landed mid-flight) kept its stale
                        // hasICUMSData=true even when direction now disagreed. Mirror
                        // Step 1's contract via the shared helper so the rule fires the
                        // same way at queue-time and at re-check.
                        if (hasICUMSData && primaryBOE != null)
                        {
                            var fycoBlocked = await EvaluateAndApplyFycoRuleAsync(
                                dbContext,
                                container.ContainerNumber,
                                container.ScannerType,
                                primaryBOE,
                                stoppingToken);

                            if (fycoBlocked)
                            {
                                hasICUMSData = false;
                                primaryBOEId = null;
                                primaryBOE = null;
                                boeRecords.Clear();
                            }
                        }

                        // DATE PROXIMITY (re-check): Detect possible container number reuse (>90 days apart)
                        if (hasICUMSData && primaryBOE != null && container.ScanDate != default)
                        {
                            DateTime? boeDate = null;
                            if (!string.IsNullOrWhiteSpace(primaryBOE.DeclarationDate) &&
                                DateTime.TryParse(primaryBOE.DeclarationDate, out var parsedBoeDate))
                            {
                                boeDate = parsedBoeDate;
                            }

                            if (boeDate.HasValue && boeDate.Value != default &&
                                Math.Abs((container.ScanDate - boeDate.Value).TotalDays) > 90)
                            {
                                _logger.LogWarning("{ServiceId} DATE PROXIMITY (re-check): {Container} scan date {ScanDate} is {Days:F0} days from BOE date {BoeDate}. Possible container reuse.",
                                    SERVICE_ID, container.ContainerNumber, container.ScanDate, Math.Abs((container.ScanDate - boeDate.Value).TotalDays), boeDate.Value);
                                hasICUMSData = false;
                                primaryBOEId = null;
                                primaryBOE = null;
                                boeRecords.Clear();
                            }
                        }

                        // Export-Pending re-evaluation: if BOE still not found, keep as Export-Pending
                        if (existingStatus.Status == "Export-Pending" && !hasICUMSData)
                        {
                            existingStatus.LastCheckedAt = DateTime.UtcNow;
                            unchangedRecords++;
                            _logger.LogDebug("{ServiceId} Export-Pending re-eval: {Container} still no BOE match, keeping as Export-Pending",
                                SERVICE_ID, container.ContainerNumber);
                            continue;
                        }

                        // ✅ Detect and store ClearanceType
                        string? clearanceType = null;
                        if (primaryBOE != null)
                        {
                            var clearanceTypeDetectionService = scope.ServiceProvider.GetRequiredService<IClearanceTypeDetectionService>();
                            var detectedClearanceType = clearanceTypeDetectionService.DetectClearanceTypeFromBOE(primaryBOE);
                            clearanceType = ClearanceTypeToString(detectedClearanceType);
                        }

                        // ✅ NEW: Check if images exist
                        var (hasImages, imageCount) = await CheckImagesExistAsync(container.ContainerNumber, container.ScannerType, dbContext);

                        bool isConsolidated = boeRecords.Any(b => b.IsConsolidated);
                        int? totalHouseBLs = null;
                        int? completeHouseBLs = null;
                        string? consolidationDetails = null;
                        string? groupIdentifier = null;

                        if (isConsolidated && boeRecords.Any())
                        {
                            // CONSOLIDATED: Group by Container, build House BL details with ICUMS data
                            totalHouseBLs = boeRecords.Count;
                            completeHouseBLs = boeRecords.Count(b =>
                                !string.IsNullOrEmpty(b.DeclarationNumber) &&
                                !string.IsNullOrEmpty(b.RotationNumber));

                            // Build JSON with House BL details
                            var houseBLDetails = boeRecords.Select(b => new
                            {
                                HouseBL = b.HouseBl ?? "N/A",
                                HasData = !string.IsNullOrEmpty(b.DeclarationNumber),
                                ICUMSData = new
                                {
                                    b.ContainerNumber,
                                    b.DeclarationNumber,
                                    HouseBL = b.HouseBl,
                                    MasterBL = b.BlNumber,
                                    Consignee = b.ConsigneeName,
                                    b.GoodsDescription,
                                    b.RotationNumber,
                                    TransitType = b.ClearanceType,
                                    RiskLevel = b.CrmsLevel,
                                    ScannerRequired = b.CrmsLevel == "H" || b.CrmsLevel == "High" ? "Yes" : "No"
                                }
                            }).ToList();

                            consolidationDetails = System.Text.Json.JsonSerializer.Serialize(houseBLDetails);
                            groupIdentifier = container.ContainerNumber; // Container is the primary ID
                        }
                        else if (boeRecords.Any())
                        {
                            // NON-CONSOLIDATED: Group by BOE, count containers
                            var declaration = boeRecords.First().DeclarationNumber;
                            groupIdentifier = declaration; // BOE/Declaration is the primary ID

                            // Count total containers under this BOE (we'll need to query this later)
                            var containersUnderBOE = await icumsDownloadsDbContext.BOEDocuments
                                .Where(b => b.DeclarationNumber == declaration)
                                .Select(b => b.ContainerNumber)
                                .Distinct()
                                .CountAsync(stoppingToken);

                            consolidationDetails = $"{containersUnderBOE} container(s)";
                        }

                        if (TryBuildCmrCompositeKey(clearanceType, primaryBOE, container.ContainerNumber, out var cmrCompositeKey))
                        {
                            groupIdentifier = cmrCompositeKey.OperationalKey;
                            consolidationDetails = cmrCompositeKey.DisplayLabel;
                        }

                        // ✅ FIX: existingStatus is already loaded above using the exact Id (guaranteed to exist, or skipped if null)
                        // This is STEP 2 (existing containers), so we should always have an existing record
                        if (ShouldPreserveCmrCompositeGroupIdentifier(existingStatus.GroupIdentifier, groupIdentifier))
                        {
                            _logger.LogDebug("{ServiceId} Preserving CMR composite GroupIdentifier for {Container} (existing: {ExistingGroup}, proposed: {ProposedGroup})",
                                SERVICE_ID, container.ContainerNumber, existingStatus.GroupIdentifier ?? "NULL", groupIdentifier ?? "NULL");
                            groupIdentifier = existingStatus.GroupIdentifier;
                        }

                        // ✅ PREVENTIVE FIX: Ensure data integrity - validate GroupIdentifier matches BOE
                        if (primaryBOEId.HasValue && !string.IsNullOrEmpty(groupIdentifier))
                        {
                            // If BOEDocumentId exists but GroupIdentifier is wrong or NULL, fix it
                            if (existingStatus.BOEDocumentId == primaryBOEId &&
                                existingStatus.GroupIdentifier != groupIdentifier)
                            {
                                _logger.LogWarning("{ServiceId} 🔧 PREVENTIVE FIX: Correcting GroupIdentifier mismatch for {Container} (was: {OldGroup}, should be: {NewGroup})",
                                    SERVICE_ID, container.ContainerNumber, existingStatus.GroupIdentifier ?? "NULL", groupIdentifier);
                                existingStatus.GroupIdentifier = groupIdentifier;
                            }

                            // If GroupIdentifier exists but BOEDocumentId is missing or wrong, fix it
                            if (existingStatus.GroupIdentifier == groupIdentifier &&
                                existingStatus.BOEDocumentId != primaryBOEId)
                            {
                                _logger.LogWarning("{ServiceId} 🔧 PREVENTIVE FIX: Linking missing BOEDocumentId for {Container} (GroupIdentifier: {Group}, BOEId: {BOEId})",
                                    SERVICE_ID, container.ContainerNumber, groupIdentifier, primaryBOEId);
                                existingStatus.BOEDocumentId = primaryBOEId;
                            }

                            // If both are NULL but we have BOE data, set them
                            if (existingStatus.BOEDocumentId == null && existingStatus.GroupIdentifier == null && primaryBOEId.HasValue)
                            {
                                _logger.LogInformation("{ServiceId} 🔧 PREVENTIVE FIX: Setting GroupIdentifier and BOEDocumentId for {Container} (GroupIdentifier: {Group}, BOEId: {BOEId})",
                                    SERVICE_ID, container.ContainerNumber, groupIdentifier, primaryBOEId);
                                existingStatus.GroupIdentifier = groupIdentifier;
                                existingStatus.BOEDocumentId = primaryBOEId;
                            }
                        }

                        // Update existing record if data changed
                        bool dataChanged = existingStatus.HasICUMSData != hasICUMSData ||
                                         existingStatus.HasImageData != hasImages ||
                                         existingStatus.IsConsolidated != isConsolidated ||
                                         existingStatus.BOEDocumentId != primaryBOEId ||
                                         existingStatus.ClearanceType != clearanceType ||
                                         existingStatus.GroupIdentifier != groupIdentifier; // ✅ Also check GroupIdentifier

                        if (dataChanged)
                        {
                            existingStatus.HasICUMSData = hasICUMSData;
                            existingStatus.HasImageData = hasImages;
                            existingStatus.ICUMSDataCompleteness = hasICUMSData ? 100 : 0;
                            existingStatus.ImageDataCompleteness = imageCount > 0 ? 100 : 0;
                            existingStatus.ScannerDataCompleteness = 100;
                            existingStatus.OverallCompleteness = (100 + (hasICUMSData ? 100 : 0) + (imageCount > 0 ? 100 : 0)) / 3;
                            existingStatus.IsConsolidated = isConsolidated;
                            existingStatus.TotalHouseBLs = totalHouseBLs;
                            existingStatus.CompleteHouseBLs = completeHouseBLs;
                            existingStatus.ConsolidationDetails = consolidationDetails;
                            existingStatus.GroupIdentifier = groupIdentifier;
                            existingStatus.BOEDocumentId = primaryBOEId;
                            existingStatus.ClearanceType = clearanceType;
                            existingStatus.ICUMSDataDate = hasICUMSData ? DateTime.UtcNow : null;

                            var completenessDecision = ContainerCompletenessPolicy.Evaluate(
                                existingStatus.HasScannerData,
                                hasICUMSData,
                                hasImages,
                                clearanceType,
                                groupIdentifier,
                                cmrCompositeProgressionEnabled: _cmrCompositeProgressionEnabled,
                                cmrRotationNumber: primaryBOE?.RotationNumber,
                                cmrContainerNumber: container.ContainerNumber,
                                cmrBlNumber: primaryBOE?.BlNumber);
                            existingStatus.Status = completenessDecision.Status;

                            // ✅ FIX: Update WorkflowStage if null or if status changed to Complete
                            if (string.IsNullOrEmpty(existingStatus.WorkflowStage))
                            {
                                existingStatus.WorkflowStage = completenessDecision.WorkflowStage;
                            }
                            else if (completenessDecision.IsComplete && (existingStatus.WorkflowStage == "Pending" || existingStatus.WorkflowStage == "Export-Hold"))
                            {
                                existingStatus.WorkflowStage = "ImageAnalysis";
                            }

                            existingStatus.UpdatedAt = DateTime.UtcNow;
                            existingStatus.LastCheckedAt = DateTime.UtcNow; // ✅ CRITICAL: Update LastCheckedAt to prevent reprocessing
                            updatedRecords++;

                            _logger.LogInformation("{ServiceId} ✅ Updated {Container} (Id: {RecordId}) - HasICUMS: {HasICUMS}, BOEId: {BOEId}, ClearanceType: {ClearanceType}, Status: {Status}",
                                SERVICE_ID, container.ContainerNumber, container.Id, hasICUMSData, primaryBOEId, clearanceType, existingStatus.Status);
                        }
                        else
                        {
                            // ✅ CRITICAL FIX: Always update LastCheckedAt even if data hasn't changed to prevent endless reprocessing
                            existingStatus.LastCheckedAt = DateTime.UtcNow;
                            unchangedRecords++;

                            _logger.LogDebug("{ServiceId} ✓ Checked {Container} (Id: {RecordId}) - No changes, LastCheckedAt updated",
                                SERVICE_ID, container.ContainerNumber, container.Id);
                        }

                        // 🔥 AUTO-QUEUE: If container is missing ICUMS data, create a ManualBOERequest
                        // Only auto-queue if retry count hasn't exceeded limit
                        // ✅ FIX: existingStatus is already loaded and guaranteed to exist at this point

                        // ✅ MULTI-CONTAINER FIX: Queue EACH container individually if missing BOE data
                        if (!hasICUMSData && existingStatus.RetryCount < 3)
                        {
                            foreach (var singleContainer in individualContainers)
                            {
                                var containerHasBoe = await icumsDownloadsDbContext.BOEDocuments
                                    .AnyAsync(b => b.ContainerNumber == singleContainer, stoppingToken);

                                if (!containerHasBoe)
                                {
                                    var existingRequest = await dbContext.ManualBOERequests
                                        .FirstOrDefaultAsync(r => r.ContainerNumber == singleContainer &&
                                                                 r.Status != "Completed" && r.Status != "Failed",
                                                            stoppingToken);

                                    if (existingRequest == null)
                                    {
                                        var boeRequest = new ManualBOERequest
                                        {
                                            ContainerNumber = singleContainer,
                                            RequestedBy = "System",
                                            RequestDate = DateTime.UtcNow,
                                            Status = "Pending",
                                            CreatedAt = DateTime.UtcNow,
                                            UpdatedAt = DateTime.UtcNow
                                        };
                                        dbContext.ManualBOERequests.Add(boeRequest);

                                        _logger.LogInformation("{ServiceId} 📋 Auto-queued {Container} for ICUMS download (missing data)",
                                            SERVICE_ID, singleContainer);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{ServiceId} Error processing container {Container}",
                            SERVICE_ID, container.ContainerNumber);
                    }
                }

                // Save all changes with explicit error handling
                // ✅ CRITICAL: This saves LastCheckedAt updates for all processed containers (both updated and unchanged)
                // This prevents endless reprocessing by ensuring LastCheckedAt is persisted even if no data changed
                try
                {
                    var changeCount = await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("{ServiceId} 💾 Database saved successfully: {ChangeCount} changes committed (New: {New}, Updated: {Updated}, Unchanged: {Unchanged})",
                        SERVICE_ID, changeCount, newRecords, updatedRecords, unchangedRecords);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "{ServiceId} ❌ CRITICAL: Failed to save changes to database! New: {New}, Updated: {Updated}, Unchanged: {Unchanged}. " +
                        "⚠️ LastCheckedAt updates may not be persisted, causing containers to be reprocessed!",
                        SERVICE_ID, newRecords, updatedRecords, unchangedRecords);
                    // Don't throw - let the service continue running, but containers will be reprocessed next cycle
                }

                // ✅ QUEUE ARCHITECTURE: Recover stuck processing items (from service crashes/restarts)
                try
                {
                    var recoveredCount = await queueRepository.RecoverStuckProcessingItemsAsync(timeoutMinutes: 30);
                    if (recoveredCount > 0)
                    {
                        _logger.LogInformation("{ServiceId} 🔄 Recovered {Count} stuck queue items (were processing for >30 minutes)",
                            SERVICE_ID, recoveredCount);
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogWarning(recoveryEx, "{ServiceId} Error recovering stuck queue items", SERVICE_ID);
                }

                // ✅ QUEUE ARCHITECTURE: Cleanup old completed items (once per day, at midnight)
                var now = DateTime.UtcNow;
                if (now.Hour == 0 && now.Minute < 5) // Run cleanup once per day around midnight
                {
                    try
                    {
                        var cleanedCount = await queueRepository.CleanupOldItemsAsync(daysToKeep: 7);
                        if (cleanedCount > 0)
                        {
                            _logger.LogInformation("{ServiceId} 🧹 Cleaned up {Count} old queue items (older than 7 days)",
                                SERVICE_ID, cleanedCount);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "{ServiceId} Error cleaning up old queue items", SERVICE_ID);
                    }
                }

                _logger.LogInformation("{ServiceId} ✅ Completeness check completed | Queue: {QueueProcessed}, New: {New}, Updated: {Updated}, Unchanged: {Unchanged}",
                    SERVICE_ID, queueItemsProcessed, newRecords, updatedRecords, unchangedRecords);

                // Safeguard anomaly summary (always log for structured observability)
                _logger.LogInformation("{ServiceId} SAFEGUARD SUMMARY: Exports={Exports}, LocationMismatches={Mismatches}, NullDeliveryPlace={NullDP}, EmptyFyco={EmptyFyco}",
                    SERVICE_ID, exportDetectedCount, locationMismatchCount, nullDeliveryPlaceCount, emptyFycoCount);

                var totalAnomalies = exportDetectedCount + locationMismatchCount + nullDeliveryPlaceCount + emptyFycoCount;
                if (totalAnomalies > 0)
                {
                    _logger.LogWarning("{ServiceId} SAFEGUARD ALERTS: {Total} anomalies detected this cycle (Exports={Exports}, LocationMismatches={Mismatches}, NullDeliveryPlace={NullDP}, EmptyFyco={EmptyFyco})",
                        SERVICE_ID, totalAnomalies, exportDetectedCount, locationMismatchCount, nullDeliveryPlaceCount, emptyFycoCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error during completeness check", SERVICE_ID);
                // Don't throw - let the service continue running on next cycle
            }
        }

        public async Task<ContainerCompletenessStatus?> GetContainerCompletenessStatusAsync(string containerNumber, string scannerType)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // ✅ IMPORTANT: Get MOST RECENT scan for this container (in case of multiple scans)
            var allScans = await dbContext.ContainerCompletenessStatuses
                .Where(c => c.ContainerNumber == containerNumber && c.ScannerType == scannerType)
                .ToListAsync();

            return allScans
                .OrderByDescending(s => s.ScanDate)
                .FirstOrDefault();
        }

        public async Task<ContainerCompletenessStatus> UpdateContainerCompletenessStatusAsync(
            string containerNumber,
            string scannerType,
            bool hasICUMSData,
            string status,
            string? errorMessage = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existingStatus = await dbContext.ContainerCompletenessStatuses
                .FirstOrDefaultAsync(c => c.ContainerNumber == containerNumber && c.ScannerType == scannerType);

            if (existingStatus != null)
            {
                existingStatus.HasICUMSData = hasICUMSData;
                existingStatus.Status = status;
                existingStatus.ErrorMessage = errorMessage;

                // ✅ FIX: Ensure WorkflowStage is set if null
                if (string.IsNullOrEmpty(existingStatus.WorkflowStage))
                {
                    existingStatus.WorkflowStage = "Pending";
                }

                existingStatus.UpdatedAt = DateTime.UtcNow;
                existingStatus.LastCheckedAt = DateTime.UtcNow;
            }
            else
            {
                existingStatus = new ContainerCompletenessStatus
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType,
                    ScanDate = DateTime.UtcNow,
                    HasICUMSData = hasICUMSData,
                    Status = status,
                    WorkflowStage = "Pending", // ✅ Set WorkflowStage
                    ErrorMessage = errorMessage,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastCheckedAt = DateTime.UtcNow
                };
                dbContext.ContainerCompletenessStatuses.Add(existingStatus);
            }

            await dbContext.SaveChangesAsync();
            // ✅ MEMORY FIX: Clear change tracker to release tracked entities
            dbContext.ChangeTracker.Clear();
            return existingStatus;
        }

        public async Task<List<ContainerCompletenessStatus>> GetContainersWithMissingICUMSDataAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var allMissing = await dbContext.ContainerCompletenessStatuses
                .Where(c => c.Status == "Missing" || c.Status == "Requested")
                .ToListAsync();

            // ✅ IMPORTANT: Return MOST RECENT scan per container only
            // ⚠️ CRITICAL FIX: Secondary order by CreatedAt to handle re-scans with same ScanDate
            return allMissing
                .GroupBy(s => new { s.ContainerNumber, s.ScannerType })
                .Select(g => g.OrderByDescending(s => s.ScanDate)
                              .ThenByDescending(s => s.CreatedAt) // ✅ Prioritize newest created record
                              .First())
                .ToList();
        }

        public async Task<List<ContainerCompletenessStatus>> GetContainersNeedingManualBOERequestsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var allNeedingRequests = await dbContext.ContainerCompletenessStatuses
                .Where(c => c.Status == "Missing" && c.RetryCount < 3)
                .ToListAsync();

            // ✅ IMPORTANT: Return MOST RECENT scan per container only
            // ⚠️ CRITICAL FIX: Secondary order by CreatedAt to handle re-scans with same ScanDate
            return allNeedingRequests
                .GroupBy(s => new { s.ContainerNumber, s.ScannerType })
                .Select(g => g.OrderByDescending(s => s.ScanDate)
                              .ThenByDescending(s => s.CreatedAt) // ✅ Prioritize newest created record
                              .First())
                .ToList();
        }

        /// <summary>
        /// Get pre-computed completeness data with server-side pagination.
        /// Uses raw SQL with ROW_NUMBER() for efficient "most recent per Container+Scanner" semantics without loading 10k rows.
        /// </summary>
        public async Task<(List<ContainerCompletenessStatus> Data, int TotalCount)> GetPreComputedCompletenessDataAsync(
            int page = 1,
            int pageSize = 50,
            string? search = null,
            string? scannerType = null,
            string? status = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Build WHERE conditions for valid container numbers (SQL-translatable)
            // ContainerNumber: 8+ chars, no spaces, 4 letters at start (LIKE '[A-Za-z][A-Za-z][A-Za-z][A-Za-z]%')
            var extWhere = "";
            var extParams = new List<object>();
            if (!string.IsNullOrEmpty(search)) { extWhere += " AND LOWER(s.containernumber) LIKE {" + extParams.Count + "}"; extParams.Add("%" + search.ToLowerInvariant() + "%"); }
            if (!string.IsNullOrEmpty(scannerType) && scannerType!.ToLower() != "all") { extWhere += " AND s.scannertype = {" + extParams.Count + "}"; extParams.Add(scannerType); }
            if (!string.IsNullOrEmpty(status) && status!.ToLower() != "all") { extWhere += " AND s.status = {" + extParams.Count + "}"; extParams.Add(status); }

            var baseWhere = "s.containernumber IS NOT NULL AND LENGTH(s.containernumber) >= 8 AND s.containernumber NOT LIKE '% %' AND s.containernumber NOT IN ('XXXX','SSSS','Unknown','PLACEHOLDER','CONTAINER') AND s.containernumber ~ '^[A-Za-z][A-Za-z][A-Za-z][A-Za-z]'";
            var fullWhere = baseWhere + extWhere;

            var countSql = $@"
WITH ranked AS (
    SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.containernumber, s.scannertype ORDER BY s.scandate DESC, s.createdat DESC) AS rn
    FROM containercompletenessstatuses s
    WHERE {fullWhere}
)
SELECT COUNT(*)::int FROM ranked WHERE rn = 1";

            int totalCount;
            if (extParams.Count > 0)
            {
                var countResult = await dbContext.Database.SqlQueryRaw<int>(countSql, extParams.ToArray()).ToListAsync();
                totalCount = countResult.FirstOrDefault();
            }
            else
            {
                var countResult = await dbContext.Database.SqlQueryRaw<int>(countSql).ToListAsync();
                totalCount = countResult.FirstOrDefault();
            }

            var skip = Math.Max(0, (page - 1) * pageSize);
            var take = Math.Min(100, Math.Max(1, pageSize));

            var dataSql = $@"
WITH ranked AS (
    SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.containernumber, s.scannertype ORDER BY s.scandate DESC, s.createdat DESC) AS rn
    FROM containercompletenessstatuses s
    WHERE {fullWhere}
)
SELECT * FROM ranked WHERE rn = 1
ORDER BY scandate DESC, createdat DESC
LIMIT {take} OFFSET {skip}";

            var completenessData = extParams.Count > 0
                ? await dbContext.ContainerCompletenessStatuses.FromSqlRaw(dataSql, extParams.ToArray()).AsNoTracking().ToListAsync()
                : await dbContext.ContainerCompletenessStatuses.FromSqlRaw(dataSql).AsNoTracking().ToListAsync();

            return (completenessData, totalCount);
        }

        private async Task<ContainerCompletenessStatus> CalculateContainerCompletenessAsync(string containerNumber, string scannerType, ApplicationDbContext dbContext)
        {
            var completeness = new ContainerCompletenessStatus
            {
                ContainerNumber = containerNumber,
                ScannerType = scannerType,
                ScanDate = DateTime.UtcNow,
                HasICUMSData = false,
                HasScannerData = false,
                HasImageData = false,
                Status = "Missing", // ✅ FIX: Default to Missing - will be updated when all data is available
                WorkflowStage = "Pending", // ✅ Set WorkflowStage
                ScannerDataCompleteness = 0,
                ICUMSDataCompleteness = 0,
                ImageDataCompleteness = 0,
                OverallCompleteness = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                // Calculate Scanner Data Completeness
                if (scannerType.ToUpper() == "FS6000")
                {
                    var fs6000Scan = await dbContext.FS6000Scans
                        .Include(s => s.Images)
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (fs6000Scan != null)
                    {
                        completeness.HasScannerData = true;
                        completeness.ScanDate = fs6000Scan.ScanTime;

                        int scannerScore = 0;
                        if (!string.IsNullOrEmpty(fs6000Scan.ContainerNumber)) scannerScore += 33;
                        if (fs6000Scan.ScanTime != default) scannerScore += 33;
                        if (fs6000Scan.Images?.Any() == true)
                        {
                            scannerScore += 34;
                            completeness.HasImageData = true;
                        }

                        completeness.ScannerDataCompleteness = scannerScore;
                        completeness.ImageDataCompleteness = fs6000Scan.Images?.Any() == true ? 100 : 0;
                    }
                }
                else if (scannerType.ToUpper() == "ASE")
                {
                    var aseScan = await dbContext.AseScans
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (aseScan != null)
                    {
                        completeness.HasScannerData = true;
                        completeness.ScanDate = aseScan.ScanTime;

                        int scannerScore = 0;
                        if (!string.IsNullOrEmpty(aseScan.ContainerNumber)) scannerScore += 40;
                        if (aseScan.ScanTime != default) scannerScore += 40;
                        if (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0)
                        {
                            scannerScore += 20;
                            completeness.HasImageData = true;
                        }

                        completeness.ScannerDataCompleteness = scannerScore;
                        completeness.ImageDataCompleteness = (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0) ? 100 : 0;
                    }
                }

                // Calculate ICUMS Data Completeness
                // Check if this container has ICUMS data by looking for BOE documents
                var hasBOEData = await dbContext.ContainerBOERelations
                    .Where(r => r.ContainerNumber == containerNumber && r.IsActive)
                    .AnyAsync();

                if (hasBOEData)
                {
                    completeness.HasICUMSData = true;
                    completeness.ICUMSDataCompleteness = 100; // If BOE relation exists, assume complete
                }
                else
                {
                    completeness.ICUMSDataCompleteness = 0;
                }

                // Calculate Overall Completeness
                completeness.OverallCompleteness = (completeness.ScannerDataCompleteness +
                                                  completeness.ICUMSDataCompleteness +
                                                  completeness.ImageDataCompleteness) / 3;

                // Set status based on completeness
                // ✅ FIX: Status = Complete only when ALL three conditions are met: Scanner + ICUMS + Images
                var allDataAvailable = completeness.HasScannerData && completeness.HasICUMSData && completeness.HasImageData;
                if (allDataAvailable && completeness.OverallCompleteness >= 90)
                {
                    completeness.Status = "Complete";
                    // ✅ FIX: Update WorkflowStage when status becomes Complete
                    if (string.IsNullOrEmpty(completeness.WorkflowStage))
                    {
                        completeness.WorkflowStage = "ImageAnalysis";
                    }
                }
                else if (completeness.OverallCompleteness >= 50)
                {
                    completeness.Status = "Partial";
                    // ✅ FIX: Ensure WorkflowStage is set
                    if (string.IsNullOrEmpty(completeness.WorkflowStage))
                    {
                        completeness.WorkflowStage = "Pending";
                    }
                }
                else
                {
                    completeness.Status = "Missing";
                    // ✅ FIX: Ensure WorkflowStage is set
                    if (string.IsNullOrEmpty(completeness.WorkflowStage))
                    {
                        completeness.WorkflowStage = "Pending";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating completeness for container {ContainerNumber}", containerNumber);
                completeness.Status = "Error";
                completeness.ErrorMessage = ex.Message;
            }

            return completeness;
        }

        /// <summary>
        /// Check if container has actual image files
        /// Images are linked via ScanId, so we check if scan has related images
        /// </summary>
        private async Task<(bool HasImages, int ImageCount)> CheckImagesExistAsync(
            string containerNumber,
            string scannerType,
            ApplicationDbContext dbContext)
        {
            try
            {
                int imageCount = 0;

                if (scannerType == "FS6000")
                {
                    // Get scan ID for this container
                    var scan = await dbContext.FS6000Scans
                        .Where(s => s.ContainerNumber == containerNumber)
                        .FirstOrDefaultAsync();

                    if (scan != null)
                    {
                        imageCount = await dbContext.FS6000Images
                            .Where(i => i.ScanId == scan.Id)
                            .CountAsync();
                    }
                }
                else if (scannerType == "ASE")
                {
                    // ✅ FIX: Check ImageDisplayName instead of just record existence
                    // ImageDisplayName is indexed and indicates image likely exists
                    // This aligns with GetImageMetadata endpoint logic (ContainerDetailsController.cs line 2053)
                    // Previous logic only checked if record exists, causing 15,434+ containers to show HasImageData=false
                    // when images were actually available (ImageDisplayName exists)
                    var hasAseScan = await dbContext.AseScans
                        .AnyAsync(s => s.ContainerNumber == containerNumber &&
                                      !string.IsNullOrEmpty(s.ImageDisplayName));
                    imageCount = hasAseScan ? 1 : 0; // Binary: has images or not
                }

                return (imageCount > 0, imageCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error checking images for {Container}",
                    SERVICE_ID, containerNumber);
                return (false, 0);
            }
        }

        /// <summary>
        /// ✅ PREVENTIVE FIX: Validates and fixes data integrity issues for a specific container
        /// Called when BOE is ingested to ensure proper linking
        /// </summary>
        public async Task<bool> ValidateAndFixContainerDataIntegrityAsync(string containerNumber, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumsDownloadsDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            try
            {
                // Get all completeness records for this container
                var completenessRecords = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.ContainerNumber == containerNumber)
                    .ToListAsync(cancellationToken);

                if (!completenessRecords.Any())
                    return false; // No records to fix

                bool anyFixed = false;

                foreach (var record in completenessRecords)
                {
                    if (CmrCompositeKeyHelper.IsOperationalKey(record.GroupIdentifier))
                    {
                        _logger.LogDebug("{ServiceId} Preserving CMR composite GroupIdentifier for {Container} during data-integrity repair: {GroupIdentifier}",
                            SERVICE_ID, containerNumber, record.GroupIdentifier ?? "NULL");
                        continue;
                    }

                    // Fix 1: If BOEDocumentId exists but GroupIdentifier is NULL or wrong
                    if (record.BOEDocumentId.HasValue)
                    {
                        var boe = await icumsDownloadsDbContext.BOEDocuments
                            .FirstOrDefaultAsync(b => b.Id == record.BOEDocumentId.Value, cancellationToken);

                        if (boe != null && !string.IsNullOrEmpty(boe.DeclarationNumber))
                        {
                            if (record.GroupIdentifier != boe.DeclarationNumber)
                            {
                                _logger.LogInformation("{ServiceId} 🔧 PREVENTIVE FIX: Setting GroupIdentifier for {Container} from BOEDocumentId {BOEId} (was: {OldGroup}, now: {NewGroup})",
                                    SERVICE_ID, containerNumber, record.BOEDocumentId, record.GroupIdentifier ?? "NULL", boe.DeclarationNumber);
                                record.GroupIdentifier = boe.DeclarationNumber;
                                record.UpdatedAt = DateTime.UtcNow;
                                anyFixed = true;
                            }
                        }
                    }

                    // Fix 2: If GroupIdentifier exists but BOEDocumentId is NULL
                    if (!string.IsNullOrEmpty(record.GroupIdentifier) && !record.BOEDocumentId.HasValue)
                    {
                        var matchingBOE = await icumsDownloadsDbContext.BOEDocuments
                            .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber
                                && b.DeclarationNumber == record.GroupIdentifier, cancellationToken);

                        if (matchingBOE != null)
                        {
                            _logger.LogInformation("{ServiceId} 🔧 PREVENTIVE FIX: Linking BOEDocumentId for {Container} with GroupIdentifier {Group} (BOEId: {BOEId})",
                                SERVICE_ID, containerNumber, record.GroupIdentifier, matchingBOE.Id);
                            record.BOEDocumentId = matchingBOE.Id;
                            record.UpdatedAt = DateTime.UtcNow;
                            anyFixed = true;
                        }
                    }

                    // Fix 3: If GroupIdentifier exists but BOEDocumentId points to wrong BOE
                    if (!string.IsNullOrEmpty(record.GroupIdentifier) && record.BOEDocumentId.HasValue)
                    {
                        var currentBOE = await icumsDownloadsDbContext.BOEDocuments
                            .FirstOrDefaultAsync(b => b.Id == record.BOEDocumentId.Value, cancellationToken);

                        if (currentBOE != null && currentBOE.DeclarationNumber != record.GroupIdentifier)
                        {
                            // Find correct BOE
                            var correctBOE = await icumsDownloadsDbContext.BOEDocuments
                                .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber
                                    && b.DeclarationNumber == record.GroupIdentifier, cancellationToken);

                            if (correctBOE != null)
                            {
                                _logger.LogInformation("{ServiceId} 🔧 PREVENTIVE FIX: Correcting BOEDocumentId for {Container} (was: {OldBOEId}, now: {NewBOEId}, GroupIdentifier: {Group})",
                                    SERVICE_ID, containerNumber, record.BOEDocumentId, correctBOE.Id, record.GroupIdentifier);
                                record.BOEDocumentId = correctBOE.Id;
                                record.UpdatedAt = DateTime.UtcNow;
                                anyFixed = true;
                            }
                        }
                    }
                }

                if (anyFixed)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("{ServiceId} ✅ PREVENTIVE FIX: Fixed data integrity issues for container {Container}",
                        SERVICE_ID, containerNumber);
                }

                return anyFixed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} ❌ Error validating data integrity for container {Container}",
                    SERVICE_ID, containerNumber);
                return false;
            }
        }

        /// <summary>
        /// Check if exception is a database connectivity issue
        /// </summary>
        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is SqlException sqlEx)
            {
                // SQL Server error numbers for connectivity issues
                return sqlEx.Number == 2 || sqlEx.Number == 40 || sqlEx.Number == 53 ||
                       sqlEx.Number == 121 || sqlEx.Number == 10053 || sqlEx.Number == 10054 ||
                       sqlEx.Number == 10060 || sqlEx.Number == 1225 ||
                       sqlEx.Message.Contains("network-related") ||
                       sqlEx.Message.Contains("instance-specific error") ||
                       sqlEx.Message.Contains("cannot find the file specified") ||
                       sqlEx.Message.Contains("refused the network connection");
            }

            // Also check for TaskCanceledException from database timeouts
            if (ex is TaskCanceledException)
            {
                // Check if it's from a database operation (indicated by inner exception or stack trace)
                var message = ex.Message.ToLowerInvariant();
                var stackTrace = ex.StackTrace?.ToLowerInvariant() ?? "";
                return stackTrace.Contains("relationalconnection") ||
                       stackTrace.Contains("sqlconnection") ||
                       message.Contains("timeout");
            }

            var errorMessage = ex.Message.ToLowerInvariant();
            return errorMessage.Contains("network-related") ||
                   errorMessage.Contains("instance-specific error") ||
                   errorMessage.Contains("cannot find the file specified") ||
                   errorMessage.Contains("could not open a connection") ||
                   errorMessage.Contains("refused the network connection") ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        private async Task<bool> IsExportReEvalEnabled(ApplicationDbContext db)
        {
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT settingvalue FROM systemsettings WHERE settingkey = 'Completeness.ReEvaluateExports' AND isactive = true LIMIT 1";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return result.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[COMPLETENESS] Could not read ReEvaluateExports setting: {Error}", ex.Message);
            }
            return false;
        }
    }
}
