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
using System.Text.Json;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public sealed class TwoContainerSplitIntakeService : ITwoContainerSplitIntakeService
    {
        private const string ServiceId = "[TWO-CONTAINER-SPLIT]";

        private readonly ApplicationDbContext _db;
        private readonly IImageSplitterService _splitter;
        private readonly IEnumerable<IScanFormatAdapter> _formatAdapters;
        private readonly IASEImageConverterService _aseConverter;
        private readonly IImageProcessingService _imageProcessing;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwoContainerSplitIntakeService> _logger;

        public TwoContainerSplitIntakeService(
            ApplicationDbContext db,
            IImageSplitterService splitter,
            IEnumerable<IScanFormatAdapter> formatAdapters,
            IASEImageConverterService aseConverter,
            IImageProcessingService imageProcessing,
            IConfiguration configuration,
            ILogger<TwoContainerSplitIntakeService> logger)
        {
            _db = db;
            _splitter = splitter;
            _formatAdapters = formatAdapters;
            _aseConverter = aseConverter;
            _imageProcessing = imageProcessing;
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
            var compactPair = string.Join(",", containers);
            var spacedPair = string.Join(", ", containers);
            var originalPair = original.OriginalContainerNumbers?.Trim();
            var records = await _db.AnalysisRecords
                .AsNoTracking()
                .Where(record =>
                    (record.ContainerNumber == containerA
                        || record.ContainerNumber == containerB
                        || record.ContainerNumber == compactPair
                        || record.ContainerNumber == spacedPair
                        || (!string.IsNullOrWhiteSpace(originalPair) && record.ContainerNumber == originalPair))
                    && (record.ScannerType == original.ScannerType || record.ScannerType == null || record.ScannerType == string.Empty))
                .OrderByDescending(record => record.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var latestRecords = records
                .Where(record =>
                    string.Equals(record.ContainerNumber, containerA, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(record.ContainerNumber, containerB, StringComparison.OrdinalIgnoreCase))
                .GroupBy(record => record.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc).First())
                .ToList();

            if (records.Any(record => IsCompositeContainerNumber(record.ContainerNumber, containers))
                && latestRecords.Count < 2)
            {
                return true;
            }

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
                        scan.ContainerNumber,
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

            if (IsSupportedImageBytes(row.ImageData))
            {
                if (IsJpegImageBytes(row.ImageData))
                    return new SplitSourceImage(row.ScanId, row.ImageData);

                var rendered = await TryRenderFs6000SourceImageAsync(
                    row.ScanId,
                    row.ContainerNumber,
                    row.ImageType,
                    row.ImageData.Length);
                if (rendered != null)
                    return new SplitSourceImage(row.ScanId, rendered);

                _logger.LogWarning(
                    "{ServiceId} FS6000 source image for scan {ScanId} ({Container}) is a supported non-JPEG blob ({ImageType}, magic={Magic}) but viewer-rendered fallback was unavailable; submitting stored blob to splitter.",
                    ServiceId,
                    row.ScanId,
                    row.ContainerNumber,
                    row.ImageType,
                    GetMagicHex(row.ImageData));
                return new SplitSourceImage(row.ScanId, row.ImageData);
            }

            var renderedUnsupported = await TryRenderFs6000SourceImageAsync(
                row.ScanId,
                row.ContainerNumber,
                row.ImageType,
                row.ImageData.Length);
            if (renderedUnsupported != null)
                return new SplitSourceImage(row.ScanId, renderedUnsupported);

            _logger.LogWarning(
                "{ServiceId} FS6000 source image for scan {ScanId} ({Container}) is unsupported ({ImageType}, {Bytes} bytes, magic={Magic}) and rendered fallback was unavailable.",
                ServiceId,
                row.ScanId,
                row.ContainerNumber,
                row.ImageType,
                row.ImageData.Length,
                GetMagicHex(row.ImageData));

            return null;
        }

        private async Task<byte[]?> TryRenderFs6000SourceImageAsync(
            Guid scanId,
            string containerNumber,
            string? imageType,
            int storedBytes)
        {
            try
            {
                var rendered = await _imageProcessing.GetCompleteContainerDataAsync(containerNumber, imageType: null);
                if (rendered?.ImageBytes is { Length: > 0 } && IsSupportedImageBytes(rendered.ImageBytes))
                {
                    _logger.LogInformation(
                        "{ServiceId} Rendered FS6000 source image for scan {ScanId} ({Container}) before split submission so splitter previews match the normal viewer image (stored {ImageType}, {Bytes} bytes).",
                        ServiceId,
                        scanId,
                        containerNumber,
                        imageType,
                        storedBytes);
                    return rendered.ImageBytes;
                }

                _logger.LogWarning(
                    "{ServiceId} Rendered FS6000 source image for scan {ScanId} ({Container}) was unavailable or unsupported (stored {ImageType}, {Bytes} bytes).",
                    ServiceId,
                    scanId,
                    containerNumber,
                    imageType,
                    storedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{ServiceId} Failed to render FS6000 source image for scan {ScanId} ({Container}) before split submission.",
                    ServiceId,
                    scanId,
                    containerNumber);
            }

            return null;
        }

        private static bool IsJpegImageBytes(byte[] imageData)
        {
            return imageData.Length >= 3
                && imageData[0] == 0xFF
                && imageData[1] == 0xD8
                && imageData[2] == 0xFF;
        }

        private static bool IsSupportedImageBytes(byte[] imageData)
        {
            if (IsJpegImageBytes(imageData))
            {
                return true;
            }

            if (imageData.Length >= 8
                && imageData[0] == 0x89
                && imageData[1] == 0x50
                && imageData[2] == 0x4E
                && imageData[3] == 0x47
                && imageData[4] == 0x0D
                && imageData[5] == 0x0A
                && imageData[6] == 0x1A
                && imageData[7] == 0x0A)
            {
                return true;
            }

            if (imageData.Length >= 12
                && imageData[0] == 0x52
                && imageData[1] == 0x49
                && imageData[2] == 0x46
                && imageData[3] == 0x46
                && imageData[8] == 0x57
                && imageData[9] == 0x45
                && imageData[10] == 0x42
                && imageData[11] == 0x50)
            {
                return true;
            }

            return false;
        }

        private static string GetMagicHex(byte[] imageData)
        {
            if (imageData.Length == 0)
                return "empty";

            return Convert.ToHexString(imageData.Take(Math.Min(imageData.Length, 8)).ToArray());
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
            var compactPair = string.Join(",", containers);
            var spacedPair = string.Join(", ", containers);
            var originalPair = original.OriginalContainerNumbers?.Trim();
            var records = await _db.AnalysisRecords
                .AsTracking()
                .Where(record =>
                    (record.ContainerNumber == containerA
                        || record.ContainerNumber == containerB
                        || record.ContainerNumber == compactPair
                        || record.ContainerNumber == spacedPair
                        || (!string.IsNullOrWhiteSpace(originalPair) && record.ContainerNumber == originalPair))
                    && (record.ScannerType == original.ScannerType || record.ScannerType == null || record.ScannerType == string.Empty))
                .OrderByDescending(record => record.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var latestRecords = records
                .Where(record =>
                    string.Equals(record.ContainerNumber, containerA, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(record.ContainerNumber, containerB, StringComparison.OrdinalIgnoreCase))
                .GroupBy(record => record.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc).First())
                .ToList();

            var structuralChanges = await PromoteCompositeAnalysisRecordAsync(
                original,
                containers,
                records,
                latestRecords,
                cancellationToken);

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

            var linked = structuralChanges;
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
            {
                await _db.SaveChangesAsync(cancellationToken);
                await RefreshMaterializedAssignmentRowsAsync(latestRecords, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return linked;
        }

        private async Task<int> PromoteCompositeAnalysisRecordAsync(
            OriginalScanRecord original,
            IReadOnlyList<string> containers,
            List<AnalysisRecord> records,
            List<AnalysisRecord> latestRecords,
            CancellationToken cancellationToken)
        {
            var composite = records
                .Where(record => IsCompositeContainerNumber(record.ContainerNumber, containers))
                .OrderByDescending(record => record.CreatedAtUtc)
                .FirstOrDefault();

            if (composite == null)
                return 0;

            if (latestRecords.Count >= 2)
            {
                _db.AnalysisRecords.Remove(composite);
                return 1;
            }

            var changed = 0;
            var containerA = containers[0];
            var containerB = containers[1];
            var hasA = latestRecords.Any(record => string.Equals(record.ContainerNumber, containerA, StringComparison.OrdinalIgnoreCase));
            var hasB = latestRecords.Any(record => string.Equals(record.ContainerNumber, containerB, StringComparison.OrdinalIgnoreCase));

            if (!hasA)
            {
                composite.ContainerNumber = containerA;
                latestRecords.Add(composite);
                hasA = true;
                changed++;
            }
            else if (!hasB)
            {
                composite.ContainerNumber = containerB;
                latestRecords.Add(composite);
                hasB = true;
                changed++;
            }

            if (!hasA || !hasB)
            {
                var missingContainer = hasA ? containerB : containerA;
                var alreadyExists = await _db.AnalysisRecords
                    .AsNoTracking()
                    .AnyAsync(record =>
                        record.GroupId == composite.GroupId
                        && record.ContainerNumber == missingContainer,
                        cancellationToken);

                if (!alreadyExists)
                {
                    var sibling = new AnalysisRecord
                    {
                        GroupId = composite.GroupId,
                        ContainerNumber = missingContainer,
                        ScannerType = composite.ScannerType ?? original.ScannerType,
                        ImageUrl = composite.ImageUrl,
                        MetadataRef = composite.MetadataRef,
                        CompletenessRef = composite.CompletenessRef,
                        ScanImageAssetId = composite.ScanImageAssetId,
                        OriginalScanRecordId = composite.OriginalScanRecordId,
                        SourceContainerLabel = composite.SourceContainerLabel,
                        Status = composite.Status,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _db.AnalysisRecords.Add(sibling);
                    latestRecords.Add(sibling);
                    changed++;
                }
            }

            return changed;
        }

        private async Task RefreshMaterializedAssignmentRowsAsync(
            IReadOnlyCollection<AnalysisRecord> linkedRecords,
            CancellationToken cancellationToken)
        {
            var groupIds = linkedRecords
                .Select(record => record.GroupId)
                .Distinct()
                .ToList();

            foreach (var groupId in groupIds)
            {
                var containers = await _db.AnalysisRecords
                    .AsNoTracking()
                    .Where(record => record.GroupId == groupId)
                    .Select(record => record.ContainerNumber)
                    .Distinct()
                    .OrderBy(container => container)
                    .ToListAsync(cancellationToken);

                var group = await _db.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

                if (group != null && containers.Count > 0)
                {
                    group.TotalContainerCount = containers.Count;
                    group.PendingContainerCount = containers.Count;
                    group.UpdatedAtUtc = DateTime.UtcNow;

                    if (IsCompositeContainerNumber(group.GroupIdentifier, containers))
                    {
                        await RefreshSplitCompletenessRowsAsync(
                            group.GroupIdentifier,
                            containers,
                            cancellationToken);
                    }
                }

                var queueEntry = await _db.AnalysisQueueEntries
                    .AsTracking()
                    .FirstOrDefaultAsync(entry => entry.GroupId == groupId, cancellationToken);

                if (queueEntry != null && containers.Count > 0)
                {
                    queueEntry.ContainerCount = containers.Count;
                    queueEntry.ContainersJson = JsonSerializer.Serialize(containers);
                    queueEntry.ContainersWithImages = containers.Count;
                    queueEntry.ContainersWithoutImages = 0;
                    queueEntry.TotalContainerCount = containers.Count;
                    queueEntry.PendingContainerCount = containers.Count;
                    queueEntry.GroupUpdatedAtUtc = DateTime.UtcNow;
                    queueEntry.LastRefreshedAtUtc = DateTime.UtcNow;
                }
            }
        }

        private async Task RefreshSplitCompletenessRowsAsync(
            string groupIdentifier,
            IReadOnlyCollection<string> containers,
            CancellationToken cancellationToken)
        {
            var containerStatuses = await _db.ContainerCompletenessStatuses
                .AsTracking()
                .Where(status => containers.Contains(status.ContainerNumber))
                .ToListAsync(cancellationToken);

            foreach (var status in containerStatuses)
            {
                status.HasImageData = true;

                if (IsCompositeContainerIdentifier(status.GroupIdentifier))
                {
                    _logger.LogWarning(
                        "{ServiceId} Clearing composite scan-pair GroupIdentifier {GroupIdentifier} from container completeness row {Container}. Cargo grouping must remain container/declaration/CMR keyed.",
                        ServiceId,
                        status.GroupIdentifier,
                        status.ContainerNumber);
                    status.GroupIdentifier = null;
                }

                if (string.IsNullOrWhiteSpace(status.WorkflowStage)
                    || string.Equals(status.WorkflowStage, "Pending", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status.WorkflowStage, "AwaitingScan", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status.WorkflowStage, "SplitPending", StringComparison.OrdinalIgnoreCase))
                {
                    status.WorkflowStage = "ImageAnalysis";
                }

                status.UpdatedAt = DateTime.UtcNow;
            }

            var compactGroupIdentifier = groupIdentifier.Replace(" ", string.Empty);
            var compositeStatuses = await _db.ContainerCompletenessStatuses
                .AsTracking()
                .Where(status =>
                    status.ContainerNumber == groupIdentifier
                    || status.ContainerNumber.Replace(" ", string.Empty) == compactGroupIdentifier)
                .ToListAsync(cancellationToken);

            foreach (var status in compositeStatuses)
            {
                status.GroupIdentifier = string.Empty;
                status.WorkflowStage = "SplitSuperseded";
                status.UpdatedAt = DateTime.UtcNow;
            }
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

        private static bool IsCompositeContainerNumber(
            string? containerNumber,
            IReadOnlyList<string> expectedContainers)
        {
            var parsed = ParseTwoContainerNumbers(containerNumber);
            if (parsed.Count != expectedContainers.Count)
                return false;

            return expectedContainers.All(expected =>
                parsed.Contains(expected, StringComparer.OrdinalIgnoreCase));
        }

        private static bool IsCompositeContainerIdentifier(string? identifier)
        {
            var parsed = ParseTwoContainerNumbers(identifier);
            return parsed.Count >= 2 && parsed.All(IsLikelyIsoContainerNumber);
        }

        private static bool IsLikelyIsoContainerNumber(string value)
        {
            var token = value.Trim();
            return token.Length == 11
                && token.Take(4).All(char.IsLetter)
                && token.Skip(4).All(char.IsDigit);
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
