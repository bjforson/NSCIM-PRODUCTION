using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ASE;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Background service that recovers missed scans from scanner tables
    /// This is the ultimate safety net - automatically discovers scans that weren't queued
    /// Runs every 2 hours to scan recent scans and ensure they're in the queue
    /// </summary>
    public class QueueRecoveryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<QueueRecoveryService> _logger;
        private readonly GoLiveOptions _goLiveOptions;
        private const string SERVICE_ID = "[QUEUE-RECOVERY]";

        // Configuration
        private static readonly TimeSpan RecoveryInterval = TimeSpan.FromHours(2); // Run every 2 hours
        private static readonly TimeSpan ScanLookbackWindow = TimeSpan.FromHours(24); // Check last 24 hours
        private const int BatchSize = 100; // Process 100 scans at a time

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. PerformRecoveryAsync
        // writes these and ExecuteAsync reads them for the per-iteration summary.
        private int _cycleCount = 0;
        private int _lastQueued = 0;
        private int _lastSkipped = 0;
        private int _lastFound = 0;

        public QueueRecoveryService(
            IServiceProvider serviceProvider,
            ILogger<QueueRecoveryService> logger,
            IOptions<GoLiveOptions> goLiveOptions)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} 🔄 Queue Recovery Service started. Will run every {Interval} hours",
                SERVICE_ID, RecoveryInterval.TotalHours);

            // Wait a bit before first run to let application fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(QueueRecoveryService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastQueued = 0;
                _lastSkipped = 0;
                _lastFound = 0;
                int _failedThisCycle = 0;
                try
                {
                    await PerformRecoveryAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "{ServiceId} ❌ Error during recovery cycle", SERVICE_ID);
                    AlertRecoveryServiceFailure(ex.Message);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = scans queued for recovery; skipped = duplicates
                // already in queue; failed = loop-level exceptions.
                _logger.LogIterationSummary(
                    "QUEUE-RECOVERY",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastQueued,
                    itemsSkipped: _lastSkipped,
                    itemsFailed: _failedThisCycle);

                // Wait for next recovery cycle
                await Task.Delay(RecoveryInterval, stoppingToken);
            }
        }

        private async Task PerformRecoveryAsync(CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("{ServiceId} 🔍 Starting recovery cycle (scanning last {Hours} hours)",
                SERVICE_ID, ScanLookbackWindow.TotalHours);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IContainerScanQueueRepository>();
            var queuePublisher = scope.ServiceProvider.GetRequiredService<IContainerScanQueuePublisher>();

            var recoveryStats = new RecoveryStatistics();

            try
            {
                // Recover FS6000 scans
                await RecoverFS6000ScansAsync(dbContext, queueRepository, queuePublisher, recoveryStats, cancellationToken);

                // Recover ASE scans
                await RecoverAseScansAsync(dbContext, queueRepository, queuePublisher, recoveryStats, cancellationToken);

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "{ServiceId} ✅ Recovery cycle completed in {Duration:F1}s. Found {FoundCount} missed scans, queued {QueuedCount} scans, skipped {SkippedCount} duplicates",
                    SERVICE_ID, duration.TotalSeconds, recoveryStats.TotalFound, recoveryStats.TotalQueued, recoveryStats.TotalSkipped);

                // Audit 8.13 (Sprint 5G2 follow-up): publish per-cycle counts
                // to ExecuteAsync's heartbeat emitter.
                _lastFound = recoveryStats.TotalFound;
                _lastQueued = recoveryStats.TotalQueued;
                _lastSkipped = recoveryStats.TotalSkipped;

                // Log warning if missed scans were found (indicates previous queue publishing failures)
                if (recoveryStats.TotalFound > 0)
                {
                    _logger.LogWarning(
                        "{ServiceId} ⚠️ Found {FoundCount} missed scans during recovery. This indicates previous queue publishing failures. All scans have been queued.",
                        SERVICE_ID, recoveryStats.TotalFound);

                    // Alert when missed scans are found
                    AlertRecoveryFoundMissedScans(recoveryStats.TotalFound);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} ❌ Error during recovery cycle", SERVICE_ID);

                // Alert on recovery service failure
                AlertRecoveryServiceFailure(ex.Message);

                throw;
            }
        }

        private async Task RecoverFS6000ScansAsync(
            ApplicationDbContext dbContext,
            IContainerScanQueueRepository queueRepository,
            IContainerScanQueuePublisher queuePublisher,
            RecoveryStatistics stats,
            CancellationToken cancellationToken)
        {
            var cutoffDate = DateTime.UtcNow.Subtract(ScanLookbackWindow);
            var goLiveDate = DateTime.SpecifyKind(_goLiveOptions.EffectiveGoLiveDate, DateTimeKind.Utc);
            var effectiveCutoff = goLiveDate > DateTime.MinValue && goLiveDate > cutoffDate ? goLiveDate : cutoffDate;

            _logger.LogDebug("{ServiceId} 🔍 Scanning FS6000Scans for scans after {CutoffDate}",
                SERVICE_ID, effectiveCutoff);

            // Query FS6000 scans from the last 24 hours (read-only, no tracking)
            // Go-live: use ScanTime for cutoff so we don't recover pre-GoLive scans
            var fs6000Scans = await dbContext.FS6000Scans
                .AsNoTracking()
                .Where(s => s.ScanTime >= effectiveCutoff && s.CreatedAt >= cutoffDate && !string.IsNullOrWhiteSpace(s.ContainerNumber))
                .OrderBy(s => s.CreatedAt)
                .Select(s => new FS6000ScanInfo
                {
                    Id = s.Id,
                    ContainerNumber = s.ContainerNumber,
                    ScanTime = s.ScanTime,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync(cancellationToken);

            stats.FS6000Found = fs6000Scans.Count;

            if (fs6000Scans.Count == 0)
            {
                _logger.LogDebug("{ServiceId} No FS6000 scans found in last {Hours} hours",
                    SERVICE_ID, ScanLookbackWindow.TotalHours);
                return;
            }

            _logger.LogInformation("{ServiceId} 🔍 Found {Count} FS6000 scans to check (last {Hours} hours)",
                SERVICE_ID, fs6000Scans.Count, ScanLookbackWindow.TotalHours);

            // Process in batches to avoid memory issues
            for (int i = 0; i < fs6000Scans.Count; i += BatchSize)
            {
                var batch = fs6000Scans.Skip(i).Take(BatchSize).ToList();
                await ProcessFS6000BatchAsync(batch, queueRepository, queuePublisher, stats, cancellationToken);
            }
        }

        private async Task ProcessFS6000BatchAsync(
            List<FS6000ScanInfo> batch,
            IContainerScanQueueRepository queueRepository,
            IContainerScanQueuePublisher queuePublisher,
            RecoveryStatistics stats,
            CancellationToken cancellationToken)
        {
            var scansToQueue = new List<ContainerScanInfo>();

            foreach (var scan in batch)
            {
                try
                {
                    var containerNumber = scan.ContainerNumber?.Trim();
                    var inspectionId = scan.Id.ToString(); // FS6000 uses Guid Id as inspection ID
                    var scanDate = scan.ScanTime;

                    if (string.IsNullOrWhiteSpace(containerNumber))
                    {
                        stats.TotalSkipped++;
                        continue;
                    }

                    // Check if already in queue
                    var isQueued = await queueRepository.IsInQueueAsync(
                        containerNumber,
                        CommonScannerTypes.FS6000,
                        inspectionId);

                    if (!isQueued)
                    {
                        // Scan not in queue - add to recovery list
                        scansToQueue.Add(new ContainerScanInfo
                        {
                            ContainerNumber = containerNumber,
                            ScannerType = CommonScannerTypes.FS6000,
                            InspectionId = inspectionId,
                            ScanDate = scanDate,
                            Priority = 1, // Higher priority for recovered scans
                            Metadata = $"{{\"Recovered\": true, \"RecoveryDate\": \"{DateTime.UtcNow:O}\"}}"
                        });
                        stats.TotalFound++;
                    }
                    else
                    {
                        stats.TotalSkipped++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{ServiceId} ⚠️ Error processing FS6000 scan {ScanId}",
                        SERVICE_ID, scan.Id);
                    stats.TotalSkipped++;
                }
            }

            // Publish recovered scans to queue
            if (scansToQueue.Any())
            {
                try
                {
                    var queuedCount = await queuePublisher.PublishScansBatchAsync(scansToQueue);
                    stats.TotalQueued += queuedCount;
                    stats.FS6000Queued += queuedCount;

                    _logger.LogInformation("{ServiceId} ✅ Queued {QueuedCount} FS6000 scans from batch of {BatchCount} (found {FoundCount} missed, {SkippedCount} already queued)",
                        SERVICE_ID, queuedCount, batch.Count, scansToQueue.Count, batch.Count - scansToQueue.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} ❌ Error queueing {Count} FS6000 scans from batch",
                        SERVICE_ID, scansToQueue.Count);
                    // Continue processing - will retry in next cycle
                }
            }
        }

        private async Task RecoverAseScansAsync(
            ApplicationDbContext dbContext,
            IContainerScanQueueRepository queueRepository,
            IContainerScanQueuePublisher queuePublisher,
            RecoveryStatistics stats,
            CancellationToken cancellationToken)
        {
            var cutoffDate = DateTime.UtcNow.Subtract(ScanLookbackWindow);
            var goLiveDate = DateTime.SpecifyKind(_goLiveOptions.EffectiveGoLiveDate, DateTimeKind.Utc);
            var effectiveCutoff = goLiveDate > DateTime.MinValue && goLiveDate > cutoffDate ? goLiveDate : cutoffDate;

            _logger.LogDebug("{ServiceId} 🔍 Scanning AseScans for scans after {CutoffDate}",
                SERVICE_ID, effectiveCutoff);

            // Query ASE scans from the last 24 hours (read-only, no tracking)
            // Go-live: use ScanTime for cutoff so we don't recover pre-GoLive scans
            var aseScans = await dbContext.AseScans
                .AsNoTracking()
                .Where(s => s.ScanTime >= effectiveCutoff && s.CreatedAt >= cutoffDate && !string.IsNullOrWhiteSpace(s.ContainerNumber))
                .OrderBy(s => s.CreatedAt)
                .Select(s => new AseScanInfo
                {
                    InspectionId = s.InspectionId,
                    ContainerNumber = s.ContainerNumber,
                    ScanTime = s.ScanTime,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync(cancellationToken);

            stats.AseFound = aseScans.Count;

            if (aseScans.Count == 0)
            {
                _logger.LogDebug("{ServiceId} No ASE scans found in last {Hours} hours",
                    SERVICE_ID, ScanLookbackWindow.TotalHours);
                return;
            }

            _logger.LogInformation("{ServiceId} 🔍 Found {Count} ASE scans to check (last {Hours} hours)",
                SERVICE_ID, aseScans.Count, ScanLookbackWindow.TotalHours);

            // Process in batches to avoid memory issues
            for (int i = 0; i < aseScans.Count; i += BatchSize)
            {
                var batch = aseScans.Skip(i).Take(BatchSize).ToList();
                await ProcessAseBatchAsync(batch, queueRepository, queuePublisher, stats, cancellationToken);
            }
        }

        private async Task ProcessAseBatchAsync(
            List<AseScanInfo> batch,
            IContainerScanQueueRepository queueRepository,
            IContainerScanQueuePublisher queuePublisher,
            RecoveryStatistics stats,
            CancellationToken cancellationToken)
        {
            var scansToQueue = new List<ContainerScanInfo>();

            foreach (var scan in batch)
            {
                try
                {
                    var queueItems = AseScanQueueItemFactory.Create(
                        scan.InspectionId,
                        scan.ContainerNumber,
                        scan.ScanTime,
                        priority: 1,
                        recovered: true,
                        recoveryDateUtc: DateTime.UtcNow);

                    if (queueItems.Count == 0)
                    {
                        stats.TotalSkipped++;
                        continue;
                    }

                    if (queueItems.Count > 1)
                    {
                        _logger.LogInformation(
                            "{ServiceId} Splitting recovered ASE inspection {InspectionId} from source container label {SourceContainer} into {Count} queue item(s)",
                            SERVICE_ID,
                            scan.InspectionId,
                            scan.ContainerNumber,
                            queueItems.Count);
                    }

                    foreach (var queueItem in queueItems)
                    {
                        var isQueued = await queueRepository.IsInQueueAsync(
                            queueItem.ContainerNumber,
                            queueItem.ScannerType,
                            queueItem.InspectionId);

                        if (!isQueued)
                        {
                            scansToQueue.Add(queueItem);
                            stats.TotalFound++;
                        }
                        else
                        {
                            stats.TotalSkipped++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{ServiceId} ⚠️ Error processing ASE scan InspectionId {InspectionId}",
                        SERVICE_ID, scan.InspectionId);
                    stats.TotalSkipped++;
                }
            }

            // Publish recovered scans to queue
            if (scansToQueue.Any())
            {
                try
                {
                    var queuedCount = await queuePublisher.PublishScansBatchAsync(scansToQueue);
                    stats.TotalQueued += queuedCount;
                    stats.AseQueued += queuedCount;

                    _logger.LogInformation("{ServiceId} ✅ Queued {QueuedCount} ASE scans from batch of {BatchCount} (found {FoundCount} missed, {SkippedCount} already queued)",
                        SERVICE_ID, queuedCount, batch.Count, scansToQueue.Count, batch.Count - scansToQueue.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} ❌ Error queueing {Count} ASE scans from batch",
                        SERVICE_ID, scansToQueue.Count);
                    // Continue processing - will retry in next cycle
                }
            }
        }

        private class RecoveryStatistics
        {
            public int TotalFound { get; set; }
            public int TotalQueued { get; set; }
            public int TotalSkipped { get; set; }
            public int FS6000Found { get; set; }
            public int FS6000Queued { get; set; }
            public int AseFound { get; set; }
            public int AseQueued { get; set; }
        }

        private class FS6000ScanInfo
        {
            public Guid Id { get; set; }
            public string ContainerNumber { get; set; } = string.Empty;
            public DateTime ScanTime { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        /// <summary>
        /// Helper method to get alert service from service provider (scoped service)
        /// </summary>
        private void AlertRecoveryFoundMissedScans(int count)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertService = scope.ServiceProvider.GetService<QueuePublishingAlertService>();
                alertService?.AlertRecoveryFoundMissedScans(count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Failed to send alert for missed scans", SERVICE_ID);
            }
        }

        /// <summary>
        /// Helper method to get alert service from service provider (scoped service)
        /// </summary>
        private void AlertRecoveryServiceFailure(string errorMessage)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertService = scope.ServiceProvider.GetService<QueuePublishingAlertService>();
                alertService?.AlertRecoveryServiceFailure(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Failed to send alert for recovery service failure", SERVICE_ID);
            }
        }

        private class AseScanInfo
        {
            public int InspectionId { get; set; }
            public string? ContainerNumber { get; set; }
            public DateTime ScanTime { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
