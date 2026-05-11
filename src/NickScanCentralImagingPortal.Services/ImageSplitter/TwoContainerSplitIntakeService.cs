using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public sealed class TwoContainerSplitIntakeService : ITwoContainerSplitIntakeService
    {
        private const string ServiceId = "[TWO-CONTAINER-SPLIT]";

        private readonly ApplicationDbContext _db;
        private readonly IImageSplitterService _splitter;
        private readonly IEnumerable<IScanFormatAdapter> _formatAdapters;
        private readonly IASEImageConverterService _aseConverter;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwoContainerSplitIntakeService> _logger;

        public TwoContainerSplitIntakeService(
            ApplicationDbContext db,
            IImageSplitterService splitter,
            IEnumerable<IScanFormatAdapter> formatAdapters,
            IASEImageConverterService aseConverter,
            IConfiguration configuration,
            ILogger<TwoContainerSplitIntakeService> logger)
        {
            _db = db;
            _splitter = splitter;
            _formatAdapters = formatAdapters;
            _aseConverter = aseConverter;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<TwoContainerSplitEnsureResult> EnsureSplitJobForOriginalAsync(
            int originalScanRecordId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var original = await _db.OriginalScanRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == originalScanRecordId, cancellationToken);

                if (original == null)
                {
                    return NotApplicable(originalScanRecordId, "OriginalScanRecordNotFound");
                }

                var containers = ParseTwoContainerNumbers(original.OriginalContainerNumbers);
                if (original.DerivedRecordCount != 2 || containers.Count != 2)
                {
                    return NotApplicable(original.Id, "NotTwoContainers");
                }

                var containerCsv = string.Join(",", containers);
                var job = await _splitter.FindLatestJobByContainersAsync(containerCsv, cancellationToken);
                var jobFound = job != null;
                var jobCreated = false;

                if (job == null)
                {
                    var sourceImage = await LoadSourceImageAsync(original, containerCsv, cancellationToken);
                    if (sourceImage == null)
                    {
                        return new TwoContainerSplitEnsureResult(
                            original.Id,
                            IsApplicable: true,
                            SplitJobCreated: false,
                            SplitJobFound: false,
                            LinkedAnalysisRecords: 0,
                            Status: "NoSourceImage");
                    }

                    job = await _splitter.SubmitSplitJobAsync(
                        containerCsv,
                        sourceImage.ImageBytes,
                        sourceImage.SourceImageId,
                        original.ScannerType,
                        cancellationToken);

                    if (job == null)
                    {
                        return new TwoContainerSplitEnsureResult(
                            original.Id,
                            IsApplicable: true,
                            SplitJobCreated: false,
                            SplitJobFound: false,
                            LinkedAnalysisRecords: 0,
                            Status: "SplitSubmissionFailed");
                    }

                    jobCreated = true;
                    _logger.LogInformation(
                        "{ServiceId} Submitted split job {JobId} for {ScannerType} original {OriginalId} ({Containers})",
                        ServiceId,
                        job.JobId,
                        original.ScannerType,
                        original.Id,
                        containerCsv);
                }

                var linked = await LinkAnalysisRecordsAsync(original, containers, job, cancellationToken);

                return new TwoContainerSplitEnsureResult(
                    original.Id,
                    IsApplicable: true,
                    SplitJobCreated: jobCreated,
                    SplitJobFound: jobFound,
                    LinkedAnalysisRecords: linked,
                    Status: job.Status);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Failed to ensure split job for original {OriginalId}", ServiceId, originalScanRecordId);
                return new TwoContainerSplitEnsureResult(
                    originalScanRecordId,
                    IsApplicable: false,
                    SplitJobCreated: false,
                    SplitJobFound: false,
                    LinkedAnalysisRecords: 0,
                    Status: "Exception");
            }
        }

        public async Task<TwoContainerSplitSweepResult> SweepAsync(
            int submitLimit = 25,
            int linkLimit = 100,
            CancellationToken cancellationToken = default)
        {
            var scanLimit = Math.Max(
                _configuration.GetValue("ImageSplitter:TwoContainerIntake:ScanLimit", 1000),
                submitLimit + linkLimit);

            var originals = await _db.OriginalScanRecords
                .AsNoTracking()
                .Where(o => o.DerivedRecordCount == 2)
                .OrderByDescending(o => o.IngestedAt)
                .Take(scanLimit)
                .ToListAsync(cancellationToken);

            var applicable = 0;
            var jobsCreated = 0;
            var jobsFound = 0;
            var linked = 0;

            foreach (var original in originals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (jobsCreated >= submitLimit && linked >= linkLimit)
                    break;

                var containers = ParseTwoContainerNumbers(original.OriginalContainerNumbers);
                if (containers.Count != 2)
                    continue;

                if (!await NeedsLocalUpdateAsync(original, containers, cancellationToken))
                    continue;

                var result = await EnsureSplitJobForOriginalAsync(original.Id, cancellationToken);
                if (!result.IsApplicable)
                    continue;

                applicable++;
                if (result.SplitJobCreated) jobsCreated++;
                if (result.SplitJobFound) jobsFound++;
                linked += result.LinkedAnalysisRecords;

                if (jobsCreated >= submitLimit)
                    break;
            }

            return new TwoContainerSplitSweepResult(
                OriginalsScanned: originals.Count,
                ApplicableOriginals: applicable,
                JobsCreated: jobsCreated,
                JobsFound: jobsFound,
                AnalysisRecordsLinked: linked);
        }

        private async Task<bool> NeedsLocalUpdateAsync(
            OriginalScanRecord original,
            IReadOnlyList<string> containers,
            CancellationToken cancellationToken)
        {
            var containerA = containers[0];
            var containerB = containers[1];
            var records = await _db.AnalysisRecords
                .AsNoTracking()
                .Where(record =>
                    (record.ContainerNumber == containerA || record.ContainerNumber == containerB)
                    && (record.ScannerType == original.ScannerType || record.ScannerType == null || record.ScannerType == string.Empty))
                .OrderByDescending(record => record.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var latestRecords = records
                .GroupBy(record => record.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc).First())
                .ToList();

            if (latestRecords.Count == 0)
            {
                var existingJob = await _splitter.FindLatestJobByContainersAsync(
                    string.Join(",", containers),
                    cancellationToken);

                return existingJob == null;
            }

            if (latestRecords.Count < 2)
                return true;

            return latestRecords.Any(record =>
                !record.IsMultiContainerScan
                || record.SplitJobId == null
                || string.IsNullOrWhiteSpace(record.SplitStatus)
                || string.Equals(record.SplitStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(record.SplitStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                    && (record.SplitOptionA_ResultId == null || record.SplitOptionB_ResultId == null)));
        }

        private async Task<SplitSourceImage?> LoadSourceImageAsync(
            OriginalScanRecord original,
            string containerCsv,
            CancellationToken cancellationToken)
        {
            if (string.Equals(original.ScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
                return await LoadFs6000ImageAsync(original.Id, cancellationToken);

            if (string.Equals(original.ScannerType, "ASE", StringComparison.OrdinalIgnoreCase))
                return await LoadAseImageAsync(original.Id, containerCsv, cancellationToken);

            _logger.LogDebug("{ServiceId} Scanner type {ScannerType} is not split-intake supported", ServiceId, original.ScannerType);
            return null;
        }

        private async Task<SplitSourceImage?> LoadFs6000ImageAsync(int originalId, CancellationToken cancellationToken)
        {
            var row = await _db.FS6000Scans
                .AsNoTracking()
                .Where(scan => scan.OriginalScanRecordId == originalId)
                .Join(
                    _db.FS6000Images.AsNoTracking().Where(image => image.ImageData != null),
                    scan => scan.Id,
                    image => image.ScanId,
                    (scan, image) => new
                    {
                        ScanId = scan.Id,
                        image.ImageData,
                        image.ImageType,
                        image.FileSizeBytes,
                        image.CreatedAt
                    })
                .OrderByDescending(row => row.ImageType == "Main")
                .ThenByDescending(row => row.FileSizeBytes ?? 0)
                .ThenByDescending(row => row.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (row?.ImageData == null || row.ImageData.Length == 0)
                return null;

            return new SplitSourceImage(row.ScanId, row.ImageData);
        }

        private async Task<SplitSourceImage?> LoadAseImageAsync(
            int originalId,
            string containerCsv,
            CancellationToken cancellationToken)
        {
            var row = await _db.AseScans
                .AsNoTracking()
                .Where(scan => scan.OriginalScanRecordId == originalId && scan.ScanImage != null)
                .OrderByDescending(scan => scan.ScanTime)
                .Select(scan => new
                {
                    scan.Id,
                    scan.ScanImage,
                    scan.ScanTime
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (row?.ScanImage == null || row.ScanImage.Length < 16)
                return null;

            var imageBytes = await RenderAseForSplitterAsync(row.Id, containerCsv, row.ScanTime, row.ScanImage, cancellationToken);
            return imageBytes == null || imageBytes.Length == 0
                ? null
                : new SplitSourceImage(row.Id, imageBytes);
        }

        private async Task<byte[]?> RenderAseForSplitterAsync(
            Guid scanId,
            string containerCsv,
            DateTime scanTime,
            byte[] scanImage,
            CancellationToken cancellationToken)
        {
            var adapter = _formatAdapters.FirstOrDefault(a => a.SourceFormatTag == ASEFormatAdapter.FormatTag);
            if (adapter != null)
            {
                var source = new ScanSourceBytes
                {
                    ScanId = scanId.ToString(),
                    ContainerNumber = containerCsv,
                    SourceFormatTag = ASEFormatAdapter.FormatTag,
                    Blobs = new Dictionary<string, byte[]> { ["ScanImage"] = scanImage },
                    Metadata = new Dictionary<string, string>
                    {
                        ["ScanTime"] = scanTime.ToString("O")
                    }
                };

                var decoded = await adapter.DecodeAsync(source, cancellationToken);
                if (decoded != null)
                {
                    var rendered = ScanRenderer.Render(decoded, RenderMode.Composite, 1.0f, 99.5f, 1.0f)
                        ?? ScanRenderer.Render(decoded, RenderMode.BlackWhite, 1.0f, 99.5f, 1.0f)
                        ?? ScanRenderer.Render(decoded, RenderMode.Edge, 1.0f, 99.5f, 1.0f);

                    if (rendered != null && rendered.Length > 0)
                        return rendered;
                }
            }

            var fallback = await _aseConverter.ConvertAseImageToJpegAsync(scanImage);
            if (fallback.Success && fallback.ImageData != null && fallback.ImageData.Length > 0)
                return fallback.ImageData;

            _logger.LogWarning("{ServiceId} ASE render failed for scan {ScanId}: {Error}", ServiceId, scanId, fallback.ErrorMessage);
            return null;
        }

        private async Task<int> LinkAnalysisRecordsAsync(
            OriginalScanRecord original,
            IReadOnlyList<string> containers,
            SplitJobReference job,
            CancellationToken cancellationToken)
        {
            var containerA = containers[0];
            var containerB = containers[1];
            var records = await _db.AnalysisRecords
                .AsTracking()
                .Where(record =>
                    (record.ContainerNumber == containerA || record.ContainerNumber == containerB)
                    && (record.ScannerType == original.ScannerType || record.ScannerType == null || record.ScannerType == string.Empty))
                .OrderByDescending(record => record.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var latestRecords = records
                .GroupBy(record => record.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc).First())
                .ToList();

            if (latestRecords.Count == 0)
                return 0;

            var liveStatus = await _splitter.GetJobStatusAsync(job.JobId, cancellationToken);
            var statusForFetch = liveStatus?.Status ?? job.Status;
            var explicitNonChoice = SplitAnalysisStatus.TryMapNonChoiceOutcome(new[]
            {
                liveStatus?.SplitOutcome,
                statusForFetch,
                liveStatus?.BestStrategy,
                liveStatus?.ErrorMessage
            });
            var shouldFetchCandidates = explicitNonChoice == null
                && SplitAnalysisStatus.IsCompletedJobStatus(statusForFetch);

            var topResults = shouldFetchCandidates
                ? await _splitter.GetTopSplitResultsAsync(job.JobId, 2, cancellationToken)
                : Array.Empty<SplitResultReference>();

            var effectiveStatus = liveStatus ?? new SplitJobStatus(
                job.JobId,
                job.Status,
                BestStrategy: null,
                BestConfidence: null,
                SplitX: null,
                ResultCount: shouldFetchCandidates && topResults.Count == 0 ? 1 : topResults.Count);

            var targetStatus = SplitAnalysisStatus.ResolveForAnalysisRecord(
                effectiveStatus,
                fetchedCandidateCount: topResults.Count,
                candidateFetchAttempted: shouldFetchCandidates,
                candidateOutcomes: topResults.Select(result => result.SplitOutcome));

            var linked = 0;
            foreach (var record in latestRecords)
            {
                var changed = false;
                changed |= SetIfChanged(record, r => r.IsMultiContainerScan, true, value => record.IsMultiContainerScan = value);
                changed |= SetIfChanged(record, r => r.SplitJobId, (Guid?)job.JobId, value => record.SplitJobId = value);

                var position = string.Equals(record.ContainerNumber, containerA, StringComparison.OrdinalIgnoreCase)
                    ? "left"
                    : "right";
                changed |= SetIfChanged(record, r => r.SplitPosition, position, value => record.SplitPosition = value);

                if (!SplitAnalysisStatus.ShouldPreserveExistingResolution(record.SplitStatus, record.SplitJobId, job.JobId))
                {
                    if (string.Equals(targetStatus, SplitAnalysisStatus.Ready, StringComparison.OrdinalIgnoreCase))
                    {
                        if (topResults.Count > 0)
                            changed |= SetIfChanged(record, r => r.SplitOptionA_ResultId, (Guid?)topResults[0].ResultId, value => record.SplitOptionA_ResultId = value);
                        if (topResults.Count > 1)
                            changed |= SetIfChanged(record, r => r.SplitOptionB_ResultId, (Guid?)topResults[1].ResultId, value => record.SplitOptionB_ResultId = value);
                    }
                    else
                    {
                        changed |= SetIfChanged(record, r => r.SplitOptionA_ResultId, (Guid?)null, value => record.SplitOptionA_ResultId = value);
                        changed |= SetIfChanged(record, r => r.SplitOptionB_ResultId, (Guid?)null, value => record.SplitOptionB_ResultId = value);
                    }

                    changed |= SetIfChanged(record, r => r.SplitStatus, targetStatus, value => record.SplitStatus = value);
                }

                if (changed)
                    linked++;
            }

            if (linked > 0)
                await _db.SaveChangesAsync(cancellationToken);

            return linked;
        }

        private static IReadOnlyList<string> ParseTwoContainerNumbers(string? raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Where(token => !string.Equals(token, "Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        private static bool IsCompleted(string? status) =>
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

        private static bool IsFailed(string? status) =>
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

        private static TwoContainerSplitEnsureResult NotApplicable(int originalId, string status) =>
            new(originalId, IsApplicable: false, SplitJobCreated: false, SplitJobFound: false, LinkedAnalysisRecords: 0, Status: status);

        private static bool SetIfChanged<T>(
            AnalysisRecord record,
            Func<AnalysisRecord, T> getter,
            T value,
            Action<T> setter)
        {
            if (EqualityComparer<T>.Default.Equals(getter(record), value))
                return false;

            setter(value);
            return true;
        }

        private sealed record SplitSourceImage(Guid SourceImageId, byte[] ImageBytes);
    }

    public sealed class TwoContainerSplitIntakeWorker : BackgroundService
    {
        private const string ServiceId = "[TWO-CONTAINER-SPLIT-WORKER]";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TwoContainerSplitIntakeWorker> _logger;
        private readonly bool _enabled;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _startupDelay;
        private readonly int _submitLimit;
        private readonly int _linkLimit;

        public TwoContainerSplitIntakeWorker(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<TwoContainerSplitIntakeWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _enabled = configuration.GetValue("ImageSplitter:TwoContainerIntake:Enabled", true);
            _interval = TimeSpan.FromSeconds(Math.Max(15, configuration.GetValue("ImageSplitter:TwoContainerIntake:IntervalSeconds", 60)));
            _startupDelay = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("ImageSplitter:TwoContainerIntake:StartupDelaySeconds", 30)));
            _submitLimit = Math.Max(1, configuration.GetValue("ImageSplitter:TwoContainerIntake:SubmitLimit", 25));
            _linkLimit = Math.Max(1, configuration.GetValue("ImageSplitter:TwoContainerIntake:LinkLimit", 100));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("{ServiceId} Disabled by ImageSplitter:TwoContainerIntake:Enabled=false", ServiceId);
                return;
            }

            if (_startupDelay > TimeSpan.Zero)
                await Task.Delay(_startupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var intake = scope.ServiceProvider.GetRequiredService<ITwoContainerSplitIntakeService>();
                    var result = await intake.SweepAsync(_submitLimit, _linkLimit, stoppingToken);

                    if (result.JobsCreated > 0 || result.AnalysisRecordsLinked > 0)
                    {
                        _logger.LogInformation(
                            "{ServiceId} Sweep scanned {Scanned}, applicable {Applicable}, created {Created}, found {Found}, linked {Linked}",
                            ServiceId,
                            result.OriginalsScanned,
                            result.ApplicableOriginals,
                            result.JobsCreated,
                            result.JobsFound,
                            result.AnalysisRecordsLinked);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{ServiceId} Sweep failed", ServiceId);
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
