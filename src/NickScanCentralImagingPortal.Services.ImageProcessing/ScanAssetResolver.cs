using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Utilities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing;

public sealed class ScanAssetResolver : IScanAssetResolver
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ScanAssetResolver> _logger;

    public ScanAssetResolver(ApplicationDbContext db, ILogger<ScanAssetResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<ScanAssetResolution> ResolveAsync(
        ScanAssetResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(
            request.ContainerNumber ?? string.Empty,
            request.GroupIdentifier,
            request.AnalysisRecordId,
            request.SplitJobId,
            cancellationToken);
    }

    public async Task<ScanAssetResolution> ResolveAsync(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedContainer = ContainerNumberListMatcher.Normalize(containerNumber);
        var recordContext = await ResolveAnalysisRecordContextAsync(
            normalizedContainer,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            cancellationToken);

        if (recordContext != null)
        {
            normalizedContainer = ContainerNumberListMatcher.Normalize(recordContext.ContainerNumber);
            splitJobId ??= recordContext.SplitJobId;
        }

        if (string.IsNullOrEmpty(normalizedContainer))
        {
            if (splitJobId.HasValue)
            {
                var splitOnlyResolution = await ResolveSplitJobContextAsync(
                    containerNumber,
                    normalizedContainer,
                    splitJobId.Value,
                    recordContext,
                    cancellationToken);

                if (splitOnlyResolution.Found || splitOnlyResolution.IsAmbiguous)
                    return splitOnlyResolution;
            }

            return ScanAssetResolution.NotFound(containerNumber, "ContainerNumberMissing");
        }

        var exactFs6000 = await ResolveExactFs6000Async(normalizedContainer, cancellationToken);
        if (exactFs6000 != null)
            return ApplyWorkflowContext(exactFs6000, recordContext, splitJobId, "ExactFs6000");

        var exactAse = await ResolveExactAseAsync(normalizedContainer, cancellationToken);
        if (exactAse != null)
            return ApplyWorkflowContext(exactAse, recordContext, splitJobId, "ExactAse");

        var exactEagle = await ResolveExactEagleA25Async(normalizedContainer, cancellationToken);
        if (exactEagle != null)
            return ApplyWorkflowContext(exactEagle, recordContext, splitJobId, "ExactEagleA25");

        var tokenizedCandidates = await ResolveTokenizedCandidatesAsync(normalizedContainer, cancellationToken);
        if (tokenizedCandidates.Count == 1)
            return ApplyWorkflowContext(tokenizedCandidates[0], normalizedContainer, recordContext, splitJobId, "TokenizedSourceContainer");

        if (tokenizedCandidates.Count > 1)
        {
            _logger.LogWarning(
                "Ambiguous scan asset resolution for {Container}: {CandidateCount} candidates",
                normalizedContainer,
                tokenizedCandidates.Count);

            return new ScanAssetResolution
            {
                Status = ScanAssetResolutionStatuses.Ambiguous,
                RequestedContainerNumber = containerNumber,
                NormalizedContainerNumber = normalizedContainer,
                ContainerNumber = normalizedContainer,
                IsAmbiguous = true,
                Reason = "AmbiguousSourceScan",
                ResolutionReason = "AmbiguousSourceScan",
                SplitJobId = splitJobId,
                SplitResultId = recordContext?.SplitResultId,
                SplitPosition = recordContext?.SplitPosition,
                CacheKey = BuildCacheKey(null, null, splitJobId, recordContext?.SplitResultId),
                Candidates = tokenizedCandidates
            };
        }

        if (splitJobId.HasValue)
        {
            var splitResolution = await ResolveSplitJobContextAsync(
                containerNumber,
                normalizedContainer,
                splitJobId.Value,
                recordContext,
                cancellationToken);

            if (splitResolution.Found || splitResolution.IsAmbiguous)
                return splitResolution;
        }

        return new ScanAssetResolution
        {
            Status = ScanAssetResolutionStatuses.NotFound,
            RequestedContainerNumber = containerNumber,
            NormalizedContainerNumber = normalizedContainer,
            ContainerNumber = normalizedContainer,
            Reason = recordContext?.SplitJobId != null
                ? "AnalysisRecordHasSplitJobButNoSourceScan"
                : "NoSourceScanFound",
            ResolutionReason = recordContext?.SplitJobId != null
                ? "AnalysisRecordHasSplitJobButNoSourceScan"
                : "NoSourceScanFound",
            SplitJobId = splitJobId,
            SplitResultId = recordContext?.SplitResultId,
            SplitPosition = recordContext?.SplitPosition,
            CacheKey = BuildCacheKey(null, null, splitJobId, recordContext?.SplitResultId)
        };
    }

    private async Task<AnalysisRecord?> ResolveAnalysisRecordContextAsync(
        string normalizedContainer,
        string? groupIdentifier,
        int? analysisRecordId,
        Guid? splitJobId,
        CancellationToken cancellationToken)
    {
        if (analysisRecordId.HasValue)
        {
            return await _db.AnalysisRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record => record.Id == analysisRecordId.Value, cancellationToken);
        }

        var query = _db.AnalysisRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(normalizedContainer))
        {
            query = query.Where(record => record.ContainerNumber == normalizedContainer);

            if (splitJobId.HasValue)
                query = query.Where(record => record.SplitJobId == splitJobId.Value);
        }
        else if (splitJobId.HasValue)
        {
            query = query.Where(record => record.SplitJobId == splitJobId.Value);
        }
        else
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(groupIdentifier))
        {
            var normalizedGroupIdentifier = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier);
            query =
                from record in query
                join groupRow in _db.AnalysisGroups.AsNoTracking() on record.GroupId equals groupRow.Id
                where groupRow.GroupIdentifier == groupIdentifier
                    || groupRow.NormalizedGroupIdentifier == groupIdentifier
                    || (!string.IsNullOrEmpty(normalizedGroupIdentifier)
                        && (groupRow.GroupIdentifier == normalizedGroupIdentifier
                            || groupRow.NormalizedGroupIdentifier == normalizedGroupIdentifier))
                select record;
        }

        return await query
            .OrderByDescending(record => record.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ScanAssetResolution?> ResolveExactFs6000Async(
        string normalizedContainer,
        CancellationToken cancellationToken)
    {
        return await _db.FS6000Scans
            .AsNoTracking()
            .Where(scan => scan.ContainerNumber == normalizedContainer)
            .OrderByDescending(scan => scan.ScanTime)
            .Select(scan => new
            {
                scan.Id,
                scan.OriginalScanRecordId,
                scan.ContainerNumber,
                scan.ScanTime,
                ImageCount = _db.FS6000Images.Count(image => image.ScanId == scan.Id),
                ImageSizeBytes = _db.FS6000Images
                    .Where(image => image.ScanId == scan.Id)
                    .OrderBy(image => image.ImageType == "Main" ? 0 : 1)
                    .ThenByDescending(image => image.CreatedAt)
                    .Select(image => image.FileSizeBytes)
                    .FirstOrDefault(),
                ImageDisplayName = _db.FS6000Images
                    .Where(image => image.ScanId == scan.Id)
                    .OrderBy(image => image.ImageType == "Main" ? 0 : 1)
                    .ThenByDescending(image => image.CreatedAt)
                    .Select(image => image.FileName)
                    .FirstOrDefault()
            })
            .Select(scan => new ScanAssetResolution
            {
                Status = ScanAssetResolutionStatuses.Resolved,
                Found = true,
                RequestedContainerNumber = normalizedContainer,
                NormalizedContainerNumber = normalizedContainer,
                ContainerNumber = normalizedContainer,
                SourceScannerType = "FS6000",
                SourceScanId = scan.OriginalScanRecordId.HasValue
                    ? scan.OriginalScanRecordId.Value.ToString(CultureInfo.InvariantCulture)
                    : scan.Id.ToString(),
                OriginalScanRecordId = scan.OriginalScanRecordId,
                ScannerScanId = scan.Id,
                SourceContainerNumbers = scan.ContainerNumber,
                ResolvedBy = "ExactFs6000",
                MatchKind = ScanAssetMatchKinds.Exact,
                ResolutionReason = "ExactContainerMatch",
                ScanTime = scan.ScanTime,
                ImageSizeBytes = scan.ImageSizeBytes,
                ImageDisplayName = scan.ImageDisplayName,
                HasImage = scan.ImageCount > 0
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ScanAssetResolution?> ResolveExactAseAsync(
        string normalizedContainer,
        CancellationToken cancellationToken)
    {
        return await _db.AseScans
            .AsNoTracking()
            .Where(scan => scan.ContainerNumber == normalizedContainer)
            .OrderByDescending(scan => scan.ScanTime)
            .Select(scan => new ScanAssetResolution
            {
                Status = ScanAssetResolutionStatuses.Resolved,
                Found = true,
                RequestedContainerNumber = normalizedContainer,
                NormalizedContainerNumber = normalizedContainer,
                ContainerNumber = normalizedContainer,
                SourceScannerType = "ASE",
                SourceScanId = scan.OriginalScanRecordId.HasValue
                    ? scan.OriginalScanRecordId.Value.ToString(CultureInfo.InvariantCulture)
                    : scan.Id.ToString(),
                OriginalScanRecordId = scan.OriginalScanRecordId,
                ScannerScanId = scan.Id,
                SourceContainerNumbers = scan.ContainerNumber,
                ResolvedBy = "ExactAse",
                MatchKind = ScanAssetMatchKinds.Exact,
                ResolutionReason = "ExactContainerMatch",
                ScanTime = scan.ScanTime,
                ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                ImageDisplayName = scan.ImageDisplayName,
                HasImage = scan.ScanImage != null && scan.ScanImage.Length > 0
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ScanAssetResolution?> ResolveExactEagleA25Async(
        string normalizedContainer,
        CancellationToken cancellationToken)
    {
        long? accession = long.TryParse(normalizedContainer, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAccession)
            ? parsedAccession
            : null;

        var scan = await _db.EagleA25Scans
            .Include(scan => scan.Assets)
            .AsNoTracking()
            .Where(scan =>
                (accession.HasValue && (scan.Accession == accession.Value || scan.ScanAccession == accession.Value))
                || scan.CargoIdentifier == normalizedContainer
                || scan.AirWaybill == normalizedContainer)
            .OrderByDescending(scan => scan.ScanDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (scan == null)
            return null;

        var accessionText = scan.Accession.ToString(CultureInfo.InvariantCulture);
        return new ScanAssetResolution
        {
            Status = ScanAssetResolutionStatuses.Resolved,
            Found = true,
            RequestedContainerNumber = normalizedContainer,
            NormalizedContainerNumber = normalizedContainer,
            ContainerNumber = !string.IsNullOrWhiteSpace(scan.CargoIdentifier)
                ? scan.CargoIdentifier!
                : accessionText,
            SourceScannerType = "EAGLE_A25",
            SourceScanId = scan.Id.ToString(),
            ScannerScanId = scan.Id,
            SourceContainerNumbers = accessionText,
            ResolvedBy = "ExactEagleA25",
            MatchKind = ScanAssetMatchKinds.Exact,
            ResolutionReason = "ExactEagleA25Match",
            ScanTime = scan.ScanDateUtc,
            ImageSizeBytes = scan.Assets
                .Where(asset => asset.FileType == "XRAY")
                .Select(asset => asset.FileSizeBytes)
                .FirstOrDefault(),
            ImageDisplayName = accessionText,
            HasImage = scan.Assets.Any(asset =>
                !string.IsNullOrWhiteSpace(asset.LocalPath)
                && (asset.FileType == "XRAY"
                    || asset.FileType == "XRAYJPEG"
                    || asset.FileType == "SCANDOC"))
        };
    }

    private async Task<List<ScanAssetResolutionCandidate>> ResolveTokenizedCandidatesAsync(
        string normalizedContainer,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ScanAssetResolutionCandidate>();
        var aseRows = await _db.AseScans
            .AsNoTracking()
            .Where(scan => scan.ContainerNumber != null
                && scan.ContainerNumber.ToUpper().Contains(normalizedContainer)
                && scan.ContainerNumber != normalizedContainer)
            .OrderByDescending(scan => scan.ScanTime)
            .Take(20)
            .Select(scan => new
            {
                scan.Id,
                scan.OriginalScanRecordId,
                scan.ContainerNumber,
                scan.ScanTime,
                ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                scan.ImageDisplayName
            })
            .ToListAsync(cancellationToken);

        candidates.AddRange(aseRows
            .Where(row => ContainerNumberListMatcher.ContainsContainer(row.ContainerNumber, normalizedContainer))
            .Select(row => ToCandidate(
                "ASE",
                row.Id,
                row.OriginalScanRecordId,
                row.ContainerNumber,
                "TokenizedAseContainer",
                row.ScanTime,
                row.ImageSizeBytes,
                row.ImageDisplayName)));

        var originalRows = await _db.OriginalScanRecords
            .AsNoTracking()
            .Where(original => original.OriginalContainerNumbers != null
                && original.OriginalContainerNumbers.ToUpper().Contains(normalizedContainer))
            .OrderByDescending(original => original.ScanTime)
            .Take(20)
            .Select(original => new
            {
                original.Id,
                original.ScannerType,
                original.OriginalContainerNumbers,
                original.ScanTime
            })
            .ToListAsync(cancellationToken);

        foreach (var original in originalRows
            .Where(row => ContainerNumberListMatcher.ContainsContainer(row.OriginalContainerNumbers, normalizedContainer)))
        {
            if (candidates.Any(candidate => candidate.OriginalScanRecordId == original.Id))
                continue;

            if (string.Equals(original.ScannerType, "ASE", StringComparison.OrdinalIgnoreCase))
            {
                var ase = await _db.AseScans
                    .AsNoTracking()
                    .Where(scan => scan.OriginalScanRecordId == original.Id)
                    .OrderByDescending(scan => scan.ScanTime)
                    .Select(scan => new
                    {
                        scan.Id,
                        scan.ContainerNumber,
                        scan.ScanTime,
                        ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                        scan.ImageDisplayName
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (ase != null)
                {
                    candidates.Add(ToCandidate(
                        "ASE",
                        ase.Id,
                        original.Id,
                        ase.ContainerNumber ?? original.OriginalContainerNumbers,
                        "TokenizedOriginalScan",
                        ase.ScanTime,
                        ase.ImageSizeBytes,
                        ase.ImageDisplayName));
                }
            }
            else if (string.Equals(original.ScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
            {
                var fs = await _db.FS6000Scans
                    .AsNoTracking()
                    .Where(scan => scan.OriginalScanRecordId == original.Id)
                    .OrderByDescending(scan => scan.ScanTime)
                    .Select(scan => new
                    {
                        scan.Id,
                        scan.ContainerNumber,
                        scan.ScanTime,
                        ImageCount = _db.FS6000Images.Count(image => image.ScanId == scan.Id),
                        ImageSizeBytes = _db.FS6000Images
                            .Where(image => image.ScanId == scan.Id)
                            .OrderBy(image => image.ImageType == "Main" ? 0 : 1)
                            .ThenByDescending(image => image.CreatedAt)
                            .Select(image => image.FileSizeBytes)
                            .FirstOrDefault(),
                        ImageDisplayName = _db.FS6000Images
                            .Where(image => image.ScanId == scan.Id)
                            .OrderBy(image => image.ImageType == "Main" ? 0 : 1)
                            .ThenByDescending(image => image.CreatedAt)
                            .Select(image => image.FileName)
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (fs != null)
                {
                    candidates.Add(ToCandidate(
                        "FS6000",
                        fs.Id,
                        original.Id,
                        fs.ContainerNumber,
                        "TokenizedOriginalScan",
                        fs.ScanTime,
                        fs.ImageCount > 0 ? fs.ImageSizeBytes : null,
                        fs.ImageDisplayName));
                }
            }
        }

        return candidates
            .GroupBy(candidate => new { candidate.SourceScannerType, candidate.ScannerScanId, candidate.OriginalScanRecordId })
            .Select(group => group.First())
            .OrderByDescending(candidate => candidate.ScanTime)
            .ToList();
    }

    private async Task<ScanAssetResolution> ResolveSplitJobContextAsync(
        string requestedContainer,
        string normalizedContainer,
        Guid splitJobId,
        AnalysisRecord? recordContext,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CrossRecordScans
            .AsNoTracking()
            .Where(scan => scan.SplitJobId == splitJobId)
            .OrderByDescending(scan => scan.ScanDateTime)
            .Take(20)
            .Select(scan => new
            {
                scan.ScannerRecordId,
                scan.ScannerType,
                scan.OriginalScanRecord,
                scan.Container1,
                scan.Container2,
                scan.ScanDateTime
            })
            .ToListAsync(cancellationToken);

        var candidates = rows
            .Where(row =>
                string.IsNullOrWhiteSpace(normalizedContainer)
                || ContainerNumberListMatcher.ContainsContainer(row.OriginalScanRecord, normalizedContainer)
                || ContainerNumberListMatcher.ContainsContainer(row.Container1, normalizedContainer)
                || ContainerNumberListMatcher.ContainsContainer(row.Container2, normalizedContainer))
            .Select(row => ToCandidate(
                row.ScannerType,
                row.ScannerRecordId,
                originalScanRecordId: null,
                row.OriginalScanRecord,
                "SplitJobContext",
                row.ScanDateTime,
                imageSizeBytes: null,
                imageDisplayName: null))
            .ToList();

        if (candidates.Count == 1)
        {
            var effectiveContainer = recordContext?.ContainerNumber
                ?? (!string.IsNullOrWhiteSpace(normalizedContainer)
                    ? normalizedContainer
                    : candidates[0].SourceContainerNumbers ?? requestedContainer);

            var resolution = ToResolution(candidates[0], requestedContainer, effectiveContainer);
            resolution.ResolvedBy = "SplitJobContext";
            resolution.MatchKind = ScanAssetMatchKinds.SplitContext;
            resolution.ResolutionReason = "SplitJobContextMatch";
            resolution.SplitJobId = splitJobId;
            resolution.SplitResultId = recordContext?.SplitResultId;
            resolution.SplitPosition = recordContext?.SplitPosition;
            resolution.CacheKey = BuildCacheKey(
                resolution.SourceScannerType,
                resolution.SourceScanId,
                resolution.SplitJobId,
                resolution.SplitResultId);
            return resolution;
        }

        if (candidates.Count > 1)
        {
            return new ScanAssetResolution
            {
                Status = ScanAssetResolutionStatuses.Ambiguous,
                RequestedContainerNumber = requestedContainer,
                NormalizedContainerNumber = normalizedContainer,
                ContainerNumber = normalizedContainer,
                IsAmbiguous = true,
                Reason = "AmbiguousSplitJobSourceScan",
                ResolutionReason = "AmbiguousSplitJobSourceScan",
                SplitJobId = splitJobId,
                SplitResultId = recordContext?.SplitResultId,
                SplitPosition = recordContext?.SplitPosition,
                CacheKey = BuildCacheKey(null, null, splitJobId, recordContext?.SplitResultId),
                Candidates = candidates
            };
        }

        return ScanAssetResolution.NotFound(requestedContainer, "SplitJobSourceScanNotFound");
    }

    private static ScanAssetResolution ApplyWorkflowContext(
        ScanAssetResolution resolution,
        AnalysisRecord? recordContext,
        Guid? splitJobId,
        string resolvedBy)
    {
        resolution.Found = true;
        resolution.Status = ScanAssetResolutionStatuses.Resolved;
        resolution.ResolvedBy = resolvedBy;
        resolution.MatchKind = resolvedBy.Contains("Exact", StringComparison.OrdinalIgnoreCase)
            ? ScanAssetMatchKinds.Exact
            : resolution.MatchKind;
        resolution.SplitJobId = splitJobId ?? recordContext?.SplitJobId;
        resolution.SplitResultId = recordContext?.SplitResultId;
        resolution.SplitPosition = recordContext?.SplitPosition;
        resolution.CacheKey = BuildCacheKey(
            resolution.SourceScannerType,
            resolution.SourceScanId,
            resolution.SplitJobId,
            resolution.SplitResultId);
        return resolution;
    }

    private static ScanAssetResolution ToResolution(
        ScanAssetResolutionCandidate candidate,
        string requestedContainer,
        string normalizedContainer)
    {
        return new ScanAssetResolution
        {
            Status = ScanAssetResolutionStatuses.Resolved,
            Found = true,
            RequestedContainerNumber = requestedContainer,
            NormalizedContainerNumber = normalizedContainer,
            ContainerNumber = normalizedContainer,
            SourceScannerType = candidate.SourceScannerType,
            SourceScanId = candidate.SourceScanId,
            OriginalScanRecordId = candidate.OriginalScanRecordId,
            ScannerScanId = candidate.ScannerScanId,
            SourceContainerNumbers = candidate.SourceContainerNumbers,
            ResolvedBy = candidate.ResolvedBy,
            MatchKind = ScanAssetMatchKinds.Tokenized,
            ResolutionReason = "TokenizedContainerMatch",
            ScanTime = candidate.ScanTime,
            ImageSizeBytes = candidate.ImageSizeBytes,
            ImageDisplayName = candidate.ImageDisplayName,
            HasImage = candidate.ImageSizeBytes.GetValueOrDefault() > 0,
            CacheKey = BuildCacheKey(
                candidate.SourceScannerType,
                candidate.SourceScanId,
                splitJobId: null,
                splitResultId: null)
        };
    }

    private static ScanAssetResolution ApplyWorkflowContext(
        ScanAssetResolutionCandidate candidate,
        string normalizedContainer,
        AnalysisRecord? recordContext,
        Guid? splitJobId,
        string resolvedBy)
    {
        var requestedContainer = recordContext?.ContainerNumber ?? normalizedContainer;
        var resolution = ToResolution(candidate, requestedContainer, normalizedContainer);
        resolution.ResolvedBy = resolvedBy;
        resolution.MatchKind = ScanAssetMatchKinds.Tokenized;
        resolution.SplitJobId = splitJobId ?? recordContext?.SplitJobId;
        resolution.SplitResultId = recordContext?.SplitResultId;
        resolution.SplitPosition = recordContext?.SplitPosition;
        resolution.CacheKey = BuildCacheKey(
            resolution.SourceScannerType,
            resolution.SourceScanId,
            resolution.SplitJobId,
            resolution.SplitResultId);
        return resolution;
    }

    private static ScanAssetResolutionCandidate ToCandidate(
        string scannerType,
        Guid scannerScanId,
        int? originalScanRecordId,
        string? sourceContainerNumbers,
        string resolvedBy,
        DateTime? scanTime,
        long? imageSizeBytes,
        string? imageDisplayName)
    {
        return new ScanAssetResolutionCandidate
        {
            SourceScannerType = scannerType,
            SourceScanId = originalScanRecordId.HasValue
                ? originalScanRecordId.Value.ToString(CultureInfo.InvariantCulture)
                : scannerScanId.ToString(),
            OriginalScanRecordId = originalScanRecordId,
            ScannerScanId = scannerScanId,
            SourceContainerNumbers = sourceContainerNumbers,
            ResolvedBy = resolvedBy,
            ScanTime = scanTime,
            ImageSizeBytes = imageSizeBytes,
            ImageDisplayName = imageDisplayName,
            CacheKey = BuildCacheKey(
                scannerType,
                originalScanRecordId.HasValue
                    ? originalScanRecordId.Value.ToString(CultureInfo.InvariantCulture)
                    : scannerScanId.ToString(),
                splitJobId: null,
                splitResultId: null)
        };
    }

    private static ScanAssetCacheKey BuildCacheKey(
        string? sourceScannerType,
        string? sourceScanId,
        Guid? splitJobId,
        Guid? splitResultId)
    {
        return new ScanAssetCacheKey
        {
            SourceScannerType = sourceScannerType,
            SourceScanId = sourceScanId,
            SplitJobId = splitJobId,
            SplitResultId = splitResultId
        };
    }
}
