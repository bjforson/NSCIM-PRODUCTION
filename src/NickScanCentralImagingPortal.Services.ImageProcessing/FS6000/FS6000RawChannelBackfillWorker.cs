using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Background worker that continuously closes the gap between fs6000scans
    /// and their raw .img channels in fs6000images (HighEnergy / LowEnergy /
    /// Material). Runs on a fixed cycle inside the API process.
    ///
    /// Contract ("never abandon"):
    ///
    /// 1.  Every cycle, query fs6000scans in the last <see cref="LookbackWindow"/>
    ///     and select rows that are missing one or more of the three raw
    ///     channels. The query naturally stays up-to-date — as soon as a scan
    ///     gets all three channels ingested, it falls out of the result set.
    /// 2.  For each candidate, delegate to <see cref="FS6000RawChannelIngester"/>
    ///     which has its own retry-on-IOException (file-lock) with exponential
    ///     backoff. If a channel still can't be read this cycle, the scan
    ///     stays in the working set and is retried next cycle. No "give up"
    ///     state.
    /// 3.  The only way a scan leaves the working set without being fully
    ///     ingested is by aging out of the lookback window. Past 7 days,
    ///     historical backfills must be driven manually via
    ///     POST /api/imageprocessing/backfill/fs6000-raw-channels.
    ///
    /// Interaction with the manual backfill endpoint: both paths call the
    /// same idempotent ingester. Concurrent writes to the same (scanid,
    /// imagetype) are caught by the unique index + <c>IsUniqueViolation</c>
    /// handler and treated as AlreadyPresent — safe.
    /// </summary>
    public class FS6000RawChannelBackfillWorker : BackgroundService
    {
        /// <summary>How often a cycle runs.</summary>
        private static readonly TimeSpan CycleInterval = TimeSpan.FromMinutes(5);

        /// <summary>How far back to look for scans that still need channels.</summary>
        private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(7);

        /// <summary>
        /// Delay before the first cycle so we don't fight startup CPU with
        /// every other background service that kicks on API boot.
        /// </summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Safety cap on scans examined per cycle. In normal operation a
        /// 7-day window on a moderately busy line won't hit this, but it
        /// guards against a pathological case where thousands of scans pile
        /// up missing channels (e.g. the ingester broke for a week).
        /// </summary>
        private const int MaxScansPerCycle = 500;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FS6000RawChannelBackfillWorker> _logger;

        public FS6000RawChannelBackfillWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<FS6000RawChannelBackfillWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[FS6000-RAW-WORKER] starting. cycle={Cycle} lookback={Lookback} maxPerCycle={Cap}",
                CycleInterval, LookbackWindow, MaxScansPerCycle);

            try { await Task.Delay(InitialDelay, stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[FS6000-RAW-WORKER] cycle threw — will retry next interval");
                }

                try { await Task.Delay(CycleInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }

            _logger.LogInformation("[FS6000-RAW-WORKER] stopped");
        }

        private async Task RunCycleAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ingester = scope.ServiceProvider.GetRequiredService<FS6000RawChannelIngester>();

            var lookback = DateTime.UtcNow - LookbackWindow;

            // Candidate selection: scans in the window missing AT LEAST ONE of
            // the three raw-channel image types. We re-query every cycle so
            // that progress made in previous cycles is automatically excluded.
            var candidates = await db.FS6000Scans
                .AsNoTracking()
                .Where(s => s.ScanTime >= lookback)
                .Where(s =>
                    !db.FS6000Images.Any(i => i.ScanId == s.Id && i.ImageType == "HighEnergy") ||
                    !db.FS6000Images.Any(i => i.ScanId == s.Id && i.ImageType == "LowEnergy")  ||
                    !db.FS6000Images.Any(i => i.ScanId == s.Id && i.ImageType == "Material"))
                .OrderByDescending(s => s.ScanTime) // newest first — operators watching dashboards care most about recent scans
                .Select(s => new { s.Id, s.FilePath, s.ContainerNumber })
                .Take(MaxScansPerCycle)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                _logger.LogDebug("[FS6000-RAW-WORKER] nothing to ingest this cycle");
                return;
            }

            _logger.LogInformation(
                "[FS6000-RAW-WORKER] cycle starting — {Count} scan(s) with missing channels",
                candidates.Count);

            int ingested = 0, alreadyComplete = 0, pending = 0, invalid = 0, acceptedUnrecoverable = 0, failed = 0;
            long bytes = 0;

            foreach (var scan in candidates)
            {
                if (ct.IsCancellationRequested) break;

                var folder = ResolveStablePath(scan.FilePath);
                try
                {
                    var r = await ingester.IngestAsync(scan.Id, folder, ct);
                    bytes += r.IngestedBytes;

                    if (r.IngestedChannels > 0)
                    {
                        ingested++;
                    }
                    else if (r.AcceptedInvalidChannels > 0)
                    {
                        acceptedUnrecoverable++;
                    }
                    else if (r.InvalidChannels > 0)
                    {
                        // File is readable, but the FS6000 header proves it is
                        // truncated or otherwise structurally incomplete. Keep
                        // it out of the DB, but don't report it as a transient
                        // worker failure every cycle.
                        invalid++;
                    }
                    else if (r.FailedChannels > 0)
                    {
                        // Ingester-level failure (e.g. IOException even after
                        // its own retries). Next cycle will try again.
                        failed++;
                    }
                    else if (r.MissingFiles > 0 || !string.IsNullOrEmpty(r.ErrorMessage))
                    {
                        // .img files aren't in Archive yet (file-sync still
                        // running?) or the folder itself is missing. Not
                        // an ingest failure per se — just nothing to read
                        // right now. Retry next cycle.
                        pending++;
                    }
                    else
                    {
                        // All three channels were already present. This can
                        // happen if the manual backfill or a previous cycle
                        // partially ingested. Candidate query looks at
                        // "missing ANY channel", so even having 2 of 3 puts
                        // a scan on the list.
                        alreadyComplete++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex,
                        "[FS6000-RAW-WORKER] scan {ScanId} ({Container}) — will retry next cycle",
                        scan.Id, scan.ContainerNumber);
                }
            }

            _logger.LogInformation(
                "[FS6000-RAW-WORKER] cycle done. ingested={Ingested} alreadyComplete={Done} pendingNextCycle={Pending} acceptedUnrecoverable={AcceptedUnrecoverable} invalid={Invalid} failed={Failed} bytes={Bytes}",
                ingested, alreadyComplete, pending, acceptedUnrecoverable, invalid, failed, bytes);
        }

        /// <summary>
        /// fs6000scans.filepath frequently still reflects the Staging path
        /// where the scan first landed. By the time the worker touches the
        /// folder, the scan has almost always been archived; rewrite the
        /// path so we read from the stable copy.
        /// </summary>
        private static string ResolveStablePath(string? recordedPath)
        {
            if (string.IsNullOrWhiteSpace(recordedPath)) return string.Empty;
            return recordedPath.Replace(@"\Staging\", @"\Archive\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
