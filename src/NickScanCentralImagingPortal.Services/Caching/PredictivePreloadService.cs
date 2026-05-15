using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictivePreloadService : IPredictivePreloadService
{
    private readonly ReadyGroupsCacheService _readyGroupsCache;
    private readonly ApplicationDbContext _dbContext;
    private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
    private readonly ICacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredictivePreloadService> _logger;
    private readonly PredictivePreloadOptions _options;
    private readonly PredictivePreloadState _state;
    private readonly SemaphoreSlim _concurrencyGate;

    public PredictivePreloadService(
        ReadyGroupsCacheService readyGroupsCache,
        ApplicationDbContext dbContext,
        IcumDownloadsDbContext icumDownloadsDbContext,
        ICacheService cache,
        IServiceScopeFactory scopeFactory,
        IOptions<PredictivePreloadOptions> options,
        PredictivePreloadState state,
        ILogger<PredictivePreloadService> logger)
    {
        _readyGroupsCache = readyGroupsCache;
        _dbContext = dbContext;
        _icumDownloadsDbContext = icumDownloadsDbContext;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _state = state;
        _concurrencyGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentPreloads));
    }

    public async Task<PredictivePreloadRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var result = new PredictivePreloadRunResult { Enabled = _options.Enabled, StartedAtUtc = startedAt };
        _state.MarkStarted();

        if (!_options.Enabled)
        {
            result.SkippedReason = "Predictive preload disabled";
            result.FinishedAtUtc = DateTime.UtcNow;
            _state.MarkCompleted(result);
            return result;
        }

        try
        {
            var candidates = new List<(AnalysisGroup Group, string Role, string EligibleStatus)>();
            foreach (var role in _options.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eligibleStatus = GetEligibleStatus(role);
                var readyGroups = await _readyGroupsCache.GetReadyGroupsForRoleAsync(role, eligibleStatus, cancellationToken);
                var selected = readyGroups
                    .OrderByDescending(g => g.Priority)
                    .ThenBy(g => g.CreatedAtUtc)
                    .Take(Math.Max(0, _options.MaxAssignmentsPerRole))
                    .ToList();

                candidates.AddRange(selected.Select(g => (g, role, eligibleStatus)));
                await CacheRoleAssignmentsAsync(role, eligibleStatus, selected, cancellationToken);
            }

            result.CandidateCount = candidates.Count;
            if (_options.SkipWhenQueueDepthBelow > 0 && candidates.Count < _options.SkipWhenQueueDepthBelow)
            {
                result.SkippedCount = candidates.Count;
                result.SkippedReason = $"Candidate count below threshold {_options.SkipWhenQueueDepthBelow}";
                result.FinishedAtUtc = DateTime.UtcNow;
                _state.MarkCompleted(result);
                return result;
            }

            var tasks = candidates.Select(candidate =>
                PreloadAssignmentScopedBoundedAsync(candidate.Group.Id, candidate.Role, candidate.EligibleStatus, cancellationToken));
            var assignmentResults = await Task.WhenAll(tasks);

            result.Assignments.AddRange(assignmentResults);
            result.SuccessCount = assignmentResults.Count(r => r.Success);
            result.FailureCount = assignmentResults.Count(r => !r.Success);
            result.FinishedAtUtc = DateTime.UtcNow;
            _state.MarkCompleted(result);

            _logger.LogInformation(
                "Predictive preload pass complete: Candidates={Candidates}, Success={Success}, Failed={Failed}, DurationMs={DurationMs}",
                result.CandidateCount,
                result.SuccessCount,
                result.FailureCount,
                (result.FinishedAtUtc - result.StartedAtUtc).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            result.FinishedAtUtc = DateTime.UtcNow;
            result.FailureCount++;
            _state.MarkFailed(ex);
            throw;
        }
    }

    public async Task<PredictivePreloadAssignmentResult> PreloadAssignmentAsync(
        Guid groupId,
        string role,
        string eligibleStatus,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var group = await _dbContext.AnalysisGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
            if (group == null)
            {
                return new PredictivePreloadAssignmentResult
                {
                    GroupId = groupId,
                    Role = role,
                    Success = false,
                    Error = "Analysis group not found",
                    CompletedAtUtc = DateTime.UtcNow
                };
            }

            var containerNumbers = await _dbContext.AnalysisRecords
                .AsNoTracking()
                .Where(r => r.GroupId == groupId)
                .OrderBy(r => r.ContainerNumber)
                .Select(r => r.ContainerNumber)
                .Distinct()
                .Take(Math.Max(1, _options.MaxContainersPerGroup))
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds));
            var context = new PredictiveAssignmentContext
            {
                GroupId = group.Id,
                Role = role,
                EligibleStatus = eligibleStatus,
                GroupIdentifier = group.GroupIdentifier,
                NormalizedGroupIdentifier = group.NormalizedGroupIdentifier,
                GroupType = group.GroupType,
                ScannerType = group.ScannerType,
                Status = group.Status,
                Priority = group.Priority,
                CreatedAtUtc = group.CreatedAtUtc,
                UpdatedAtUtc = group.UpdatedAtUtc,
                ContainerNumbers = containerNumbers,
                TotalContainerCount = containerNumbers.Count,
                CachedAtUtc = now,
                ExpiresAtUtc = now.Add(ttl)
            };

            await _cache.SetAsync(PredictivePreloadKeys.Assignment(group.Id), context, ttl, cancellationToken);
            await _cache.SetAsync(PredictivePreloadKeys.AssignmentContainers(group.Id), containerNumbers, ttl, cancellationToken);

            var containerResults = new List<PredictivePreloadContainerResult>();
            if (ShouldPreloadContainerContext())
            {
                foreach (var containerNumber in containerNumbers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    containerResults.Add(await PreloadContainerContextAsync(containerNumber, cancellationToken));
                }
            }

            return new PredictivePreloadAssignmentResult
            {
                GroupId = group.Id,
                Role = role,
                GroupIdentifier = group.GroupIdentifier,
                Success = true,
                ContainerCount = containerNumbers.Count,
                ContainerPreloadSuccessCount = containerResults.Count(r => r.Success),
                ContainerPreloadFailureCount = containerResults.Count(r => !r.Success),
                Containers = containerResults,
                CompletedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Predictive preload failed for assignment {GroupId} ({Role})", groupId, role);
            return new PredictivePreloadAssignmentResult
            {
                GroupId = groupId,
                Role = role,
                Success = false,
                Error = ex.Message,
                CompletedAtUtc = DateTime.UtcNow
            };
        }
    }

    public async Task<PredictivePreloadContainerResult> PreloadContainerContextAsync(
        string containerNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeContainerNumber(containerNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new PredictivePreloadContainerResult
            {
                ContainerNumber = containerNumber,
                Success = false,
                Error = "Container number is required",
                CompletedAtUtc = DateTime.UtcNow
            };
        }

        try
        {
            var now = DateTime.UtcNow;
            var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds));
            var pageSize = Math.Max(1, _options.FirstPageSize);

            PredictiveContainerSummary? summary = null;
            PredictiveScannerDataPage? scannerFirstPage = null;
            PredictiveIcumDataPage? icumsFirstPage = null;
            PredictiveBoeSummary? boeSummary = null;
            IReadOnlyList<PredictiveImageMetadata> imageMetadata = Array.Empty<PredictiveImageMetadata>();

            if (_options.PreloadContainerSummary)
            {
                summary = await BuildContainerSummaryAsync(normalized, cancellationToken);
                await _cache.SetAsync(PredictivePreloadKeys.ContainerSummary(normalized), summary, ttl, cancellationToken);
            }

            if (_options.PreloadScannerFirstPage)
            {
                scannerFirstPage = await BuildScannerFirstPageAsync(normalized, pageSize, cancellationToken);
                await _cache.SetAsync(
                    PredictivePreloadKeys.ContainerScannerPage(normalized, scannerFirstPage.Page, scannerFirstPage.PageSize),
                    scannerFirstPage,
                    ttl,
                    cancellationToken);
            }

            if (_options.PreloadIcumsFirstPage)
            {
                icumsFirstPage = await BuildIcumFirstPageAsync(normalized, pageSize, cancellationToken);
                await _cache.SetAsync(
                    PredictivePreloadKeys.ContainerIcumsPage(normalized, icumsFirstPage.Page, icumsFirstPage.PageSize),
                    icumsFirstPage,
                    ttl,
                    cancellationToken);
            }

            if (_options.PreloadBoeSummary)
            {
                boeSummary = await BuildBoeSummaryAsync(normalized, cancellationToken);
                await _cache.SetAsync(PredictivePreloadKeys.ContainerBoe(normalized), boeSummary, ttl, cancellationToken);
            }

            if (_options.PreloadImageMetadata)
            {
                imageMetadata = await BuildImageMetadataAsync(normalized, cancellationToken);
                await _cache.SetAsync(PredictivePreloadKeys.ContainerImageMetadata(normalized), imageMetadata.ToList(), ttl, cancellationToken);
            }

            var context = new PredictiveContainerContext
            {
                ContainerNumber = normalized,
                Summary = summary,
                ScannerFirstPage = scannerFirstPage,
                IcumsFirstPage = icumsFirstPage,
                BoeSummary = boeSummary,
                ImageMetadata = imageMetadata,
                FullImagesPreloaded = false,
                CachedAtUtc = now,
                ExpiresAtUtc = now.Add(ttl)
            };

            await _cache.SetAsync(PredictivePreloadKeys.ContainerContext(normalized), context, ttl, cancellationToken);

            return new PredictivePreloadContainerResult
            {
                ContainerNumber = normalized,
                Success = true,
                ScannerFieldCount = scannerFirstPage?.TotalCount ?? 0,
                IcumFieldCount = icumsFirstPage?.TotalCount ?? 0,
                ImageMetadataCount = imageMetadata.Count,
                CompletedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Predictive preload failed for container {ContainerNumber}", normalized);
            return new PredictivePreloadContainerResult
            {
                ContainerNumber = normalized,
                Success = false,
                Error = ex.Message,
                CompletedAtUtc = DateTime.UtcNow
            };
        }
    }

    public async Task InvalidateAssignmentAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var containerNumbers = await _cache.GetAsync<List<string>>(
            PredictivePreloadKeys.AssignmentContainers(groupId),
            cancellationToken);

        await _cache.RemoveAsync(PredictivePreloadKeys.Assignment(groupId), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.AssignmentContainers(groupId), cancellationToken);

        if (containerNumbers == null)
            return;

        foreach (var containerNumber in containerNumbers)
        {
            await InvalidateContainerContextAsync(containerNumber, cancellationToken);
        }
    }

    public async Task InvalidateContainerContextAsync(string containerNumber, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeContainerNumber(containerNumber);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var pageSize = Math.Max(1, _options.FirstPageSize);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerContext(normalized), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerSummary(normalized), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerScannerPage(normalized, 1, pageSize), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerIcumsPage(normalized, 1, pageSize), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerBoe(normalized), cancellationToken);
        await _cache.RemoveAsync(PredictivePreloadKeys.ContainerImageMetadata(normalized), cancellationToken);
    }

    public Task InvalidateRoleAssignmentsAsync(string role, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(PredictivePreloadKeys.RoleAssignments(role), cancellationToken);
    }

    public Task<PredictiveAssignmentContext?> GetAssignmentContextAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        return _cache.GetAsync<PredictiveAssignmentContext>(
            PredictivePreloadKeys.Assignment(groupId),
            cancellationToken);
    }

    public Task<PredictiveContainerContext?> GetContainerContextAsync(
        string containerNumber,
        CancellationToken cancellationToken = default)
    {
        return _cache.GetAsync<PredictiveContainerContext>(
            PredictivePreloadKeys.ContainerContext(NormalizeContainerNumber(containerNumber)),
            cancellationToken);
    }

    private async Task<PredictiveContainerSummary> BuildContainerSummaryAsync(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var completeness = await _dbContext.ContainerCompletenessStatuses
            .AsNoTracking()
            .Where(c => c.ContainerNumber == containerNumber)
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.ScanDate)
            .FirstOrDefaultAsync(cancellationToken);

        var fs6000Count = await _dbContext.FS6000Scans
            .AsNoTracking()
            .CountAsync(s => s.ContainerNumber == containerNumber, cancellationToken);
        var aseCount = await _dbContext.AseScans
            .AsNoTracking()
            .CountAsync(s => s.ContainerNumber == containerNumber, cancellationToken);
        var icumsCount = await CountRelatedBoeDocumentsAsync(containerNumber, cancellationToken);
        var imageCount = await CountImageMetadataAsync(containerNumber, cancellationToken);

        var latestFsScan = await _dbContext.FS6000Scans
            .AsNoTracking()
            .Where(s => s.ContainerNumber == containerNumber)
            .OrderByDescending(s => s.ScanTime)
            .Select(s => (DateTime?)s.ScanTime)
            .FirstOrDefaultAsync(cancellationToken);
        var latestAseScan = await _dbContext.AseScans
            .AsNoTracking()
            .Where(s => s.ContainerNumber == containerNumber)
            .OrderByDescending(s => s.ScanTime)
            .Select(s => (DateTime?)s.ScanTime)
            .FirstOrDefaultAsync(cancellationToken);
        var latestBoe = await _icumDownloadsDbContext.BOEDocuments
            .AsNoTracking()
            .Where(b => b.ContainerNumber == containerNumber)
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => (DateTime?)b.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var hasScannerData = completeness?.HasScannerData ?? fs6000Count + aseCount > 0;
        var hasIcumsData = completeness?.HasICUMSData ?? icumsCount > 0;
        var hasImageData = completeness?.HasImageData ?? imageCount > 0;
        var completenessScore = completeness?.OverallCompleteness
            ?? CalculateCompletenessScore(hasScannerData, hasIcumsData, hasImageData);

        return new PredictiveContainerSummary
        {
            ContainerNumber = containerNumber,
            ScannerType = completeness?.ScannerType ?? (fs6000Count > 0 ? "FS6000" : aseCount > 0 ? "ASE" : null),
            Status = completeness?.Status,
            WorkflowStage = completeness?.WorkflowStage,
            ClearanceType = completeness?.ClearanceType,
            GroupIdentifier = completeness?.GroupIdentifier,
            BoeDocumentId = completeness?.BOEDocumentId,
            HasScannerData = hasScannerData,
            HasIcumsData = hasIcumsData,
            HasImageData = hasImageData,
            IsConsolidated = completeness?.IsConsolidated ?? false,
            TotalHouseBLs = completeness?.TotalHouseBLs,
            CompleteHouseBLs = completeness?.CompleteHouseBLs,
            ScannerRecordCount = fs6000Count + aseCount,
            IcumsRecordCount = icumsCount,
            ImageCount = imageCount,
            CompletenessScore = completenessScore,
            LatestScanDateUtc = new[] { latestFsScan, latestAseScan }
                .Where(d => d.HasValue)
                .Max(),
            LastUpdatedUtc = new[] { completeness?.UpdatedAt, latestFsScan, latestAseScan, latestBoe }
                .Where(d => d.HasValue)
                .Max()
        };
    }

    private async Task<PredictiveScannerDataPage> BuildScannerFirstPageAsync(
        string containerNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var fields = new List<PredictiveFieldValue>();

        var fs6000 = await _dbContext.FS6000Scans
            .AsNoTracking()
            .Where(s => s.ContainerNumber == containerNumber)
            .OrderByDescending(s => s.ScanTime)
            .Select(s => new
            {
                s.ContainerNumber,
                s.ScanTime,
                s.PicNumber,
                s.FycoPresent,
                s.VesselName,
                s.TruckPlate,
                s.OperatorId,
                s.ScanResult,
                s.GoodsDescription,
                s.ShippingCompany,
                s.Consignee,
                s.SyncStatus,
                s.HasImage,
                s.ImageCount,
                s.ImageIngestedAt,
                s.OriginalScanRecordId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (fs6000 != null)
        {
            AddField(fields, "Scanner Type", "FS6000", "Scanner", fs6000.ScanTime);
            AddField(fields, "Container Number", fs6000.ContainerNumber, "Scanner", fs6000.ScanTime);
            AddField(fields, "Scan Time", fs6000.ScanTime.ToString("O"), "Scanner", fs6000.ScanTime);
            AddField(fields, "Picture Number", fs6000.PicNumber, "Scanner", fs6000.ScanTime);
            AddField(fields, "Truck Plate", fs6000.TruckPlate, "Scanner", fs6000.ScanTime);
            AddField(fields, "Operator", fs6000.OperatorId, "Scanner", fs6000.ScanTime);
            AddField(fields, "Scan Result", fs6000.ScanResult, "Scanner", fs6000.ScanTime);
            AddField(fields, "Goods Description", fs6000.GoodsDescription, "Scanner", fs6000.ScanTime);
            AddField(fields, "Vessel Name", fs6000.VesselName, "Scanner", fs6000.ScanTime);
            AddField(fields, "Shipping Company", fs6000.ShippingCompany, "Scanner", fs6000.ScanTime);
            AddField(fields, "Consignee", fs6000.Consignee, "Scanner", fs6000.ScanTime);
            AddField(fields, "FYCO Present", fs6000.FycoPresent, "Scanner", fs6000.ScanTime);
            AddField(fields, "Sync Status", fs6000.SyncStatus, "Scanner", fs6000.ScanTime);
            AddField(fields, "Has Image", fs6000.HasImage.ToString(), "Image", fs6000.ImageIngestedAt);
            AddField(fields, "Image Count", fs6000.ImageCount.ToString(), "Image", fs6000.ImageIngestedAt);
            AddField(fields, "Original Scan Record ID", fs6000.OriginalScanRecordId?.ToString(), "Scanner", fs6000.ScanTime);
        }

        var ase = await _dbContext.AseScans
            .AsNoTracking()
            .Where(s => s.ContainerNumber == containerNumber)
            .OrderByDescending(s => s.ScanTime)
            .Select(s => new
            {
                s.Id,
                s.InspectionId,
                s.InspectionUuid,
                s.ContainerNumber,
                s.TruckPlate,
                s.ScanTime,
                s.ImageDisplayName,
                HasImage = s.ScanImage != null,
                s.SyncedAt,
                s.OriginalScanRecordId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ase != null)
        {
            AddField(fields, "Scanner Type", "ASE", "Scanner", ase.ScanTime);
            AddField(fields, "Container Number", ase.ContainerNumber, "Scanner", ase.ScanTime);
            AddField(fields, "Inspection ID", ase.InspectionId.ToString(), "Scanner", ase.ScanTime);
            AddField(fields, "Inspection UUID", ase.InspectionUuid, "Scanner", ase.ScanTime);
            AddField(fields, "Truck Plate", ase.TruckPlate, "Scanner", ase.ScanTime);
            AddField(fields, "Scan Time", ase.ScanTime.ToString("O"), "Scanner", ase.ScanTime);
            AddField(fields, "Image Display Name", ase.ImageDisplayName, "Image", ase.ScanTime);
            AddField(fields, "Has Image", ase.HasImage.ToString(), "Image", ase.ScanTime);
            AddField(fields, "Synced At", ase.SyncedAt.ToString("O"), "Scanner", ase.SyncedAt);
            AddField(fields, "Original Scan Record ID", ase.OriginalScanRecordId?.ToString(), "Scanner", ase.ScanTime);
        }

        var originals = await _dbContext.OriginalScanRecords
            .AsNoTracking()
            .Where(r => r.OriginalContainerNumbers == containerNumber || r.OriginalContainerNumbers.Contains(containerNumber))
            .OrderByDescending(r => r.ScanTime)
            .Take(2)
            .Select(r => new
            {
                r.Id,
                r.ScannerType,
                r.OriginalContainerNumbers,
                r.DerivedRecordCount,
                r.PicNumber,
                r.InspectionId,
                r.SourceFilePath,
                r.ScanTime,
                r.IngestedAt
            })
            .ToListAsync(cancellationToken);

        foreach (var original in originals)
        {
            AddField(fields, $"Original Scan {original.Id} Scanner", original.ScannerType, "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Containers", original.OriginalContainerNumbers, "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Derived Count", original.DerivedRecordCount.ToString(), "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Picture Number", original.PicNumber, "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Inspection ID", original.InspectionId, "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Source File", original.SourceFilePath, "Original Scan", original.ScanTime);
            AddField(fields, $"Original Scan {original.Id} Ingested At", original.IngestedAt.ToString("O"), "Original Scan", original.IngestedAt);
        }

        return ToScannerPage(containerNumber, fields, pageSize);
    }

    private async Task<PredictiveIcumDataPage> BuildIcumFirstPageAsync(
        string containerNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var documents = await GetRelatedBoeDocumentsAsync(containerNumber, maxDocuments: 5, cancellationToken);
        var primary = documents.FirstOrDefault();
        var fields = new List<PredictiveFieldValue>();

        if (primary != null)
        {
            AddField(fields, "BOE Document ID", primary.Id.ToString(), "ICUMS", primary.UpdatedAt);
            AddField(fields, "Container Number", primary.ContainerNumber, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Declaration Number", primary.DeclarationNumber, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Declaration Date", primary.DeclarationDate, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Clearance Type", primary.ClearanceType, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Document Type", primary.DocumentType, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Regime Code", primary.RegimeCode, "ICUMS", primary.UpdatedAt);
            AddField(fields, "CRMS Level", primary.CrmsLevel, "ICUMS", primary.UpdatedAt);
            AddField(fields, "BL Number", primary.BlNumber, "Manifest", primary.UpdatedAt);
            AddField(fields, "Master BL Number", primary.MasterBlNumber, "Manifest", primary.UpdatedAt);
            AddField(fields, "House BL", primary.HouseBl, "Manifest", primary.UpdatedAt);
            AddField(fields, "Consignee Name", primary.ConsigneeName, "Manifest", primary.UpdatedAt);
            AddField(fields, "Shipper Name", primary.ShipperName, "Manifest", primary.UpdatedAt);
            AddField(fields, "Country Of Origin", primary.CountryOfOrigin, "Manifest", primary.UpdatedAt);
            AddField(fields, "Goods Description", primary.GoodsDescription, "Manifest", primary.UpdatedAt);
            AddField(fields, "Importer", primary.ImpName ?? primary.ImpExpName, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Exporter", primary.ExpName, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Declarant", primary.DeclarantName, "ICUMS", primary.UpdatedAt);
            AddField(fields, "Total Duty Paid", primary.TotalDutyPaid?.ToString(), "ICUMS", primary.UpdatedAt);
            AddField(fields, "Container ISO", primary.ContainerISO, "Container", primary.UpdatedAt);
            AddField(fields, "Container Size", primary.ContainerSize, "Container", primary.UpdatedAt);
            AddField(fields, "Container Weight", primary.ContainerWeight?.ToString(), "Container", primary.UpdatedAt);
            AddField(fields, "Seal Number", primary.SealNumber, "Container", primary.UpdatedAt);
            AddField(fields, "Truck Plate", primary.TruckPlateNumber, "Container", primary.UpdatedAt);
            AddField(fields, "Document Count", documents.Count.ToString(), "ICUMS", primary.UpdatedAt);
        }

        var manifestPreview = await GetManifestPreviewAsync(documents.Select(d => d.Id).ToList(), cancellationToken);
        return ToIcumPage(containerNumber, fields, manifestPreview, pageSize);
    }

    private async Task<PredictiveBoeSummary> BuildBoeSummaryAsync(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var documents = await GetRelatedBoeDocumentsAsync(containerNumber, maxDocuments: 25, cancellationToken);
        var primary = documents.FirstOrDefault();
        var documentIds = documents.Select(d => d.Id).ToList();
        var manifestItemCount = documentIds.Count == 0
            ? 0
            : await _icumDownloadsDbContext.ManifestItems
                .AsNoTracking()
                .CountAsync(m => documentIds.Contains(m.BOEDocumentId), cancellationToken);

        return new PredictiveBoeSummary
        {
            ContainerNumber = containerNumber,
            DocumentCount = documents.Count,
            ManifestItemCount = manifestItemCount,
            PrimaryBoeDocumentId = primary?.Id,
            DeclarationNumber = primary?.DeclarationNumber,
            BlNumber = primary?.BlNumber ?? primary?.MasterBlNumber,
            HouseBl = primary?.HouseBl,
            ClearanceType = primary?.ClearanceType,
            DocumentType = primary?.DocumentType,
            CrmsLevel = primary?.CrmsLevel,
            ConsigneeName = primary?.ConsigneeName,
            ShipperName = primary?.ShipperName,
            GoodsDescription = primary?.GoodsDescription,
            TotalDutyPaid = documents.Sum(d => d.TotalDutyPaid ?? 0),
            HasIngestionWarnings = documents.Any(d => d.HasIngestionWarnings)
        };
    }

    private async Task<IReadOnlyList<PredictiveImageMetadata>> BuildImageMetadataAsync(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var fs6000Images = await _dbContext.FS6000Images
            .AsNoTracking()
            .Where(i => _dbContext.FS6000Scans.Any(s => s.Id == i.ScanId && s.ContainerNumber == containerNumber))
            .OrderByDescending(i => i.CreatedAt)
            .Take(20)
            .Select(i => new PredictiveImageMetadata
            {
                ImageId = i.Id.ToString(),
                ScannerType = "FS6000",
                ImageType = i.ImageType,
                FileName = i.FileName,
                FileSizeBytes = i.FileSizeBytes ?? 0,
                CreatedAtUtc = i.CreatedAt,
                HasBinaryData = i.ImageData != null,
                ThumbnailUrl = $"/api/ContainerDetails/image/fs6000/{i.Id}/thumbnail",
                FullImageUrl = $"/api/ContainerDetails/image/fs6000/{i.Id}"
            })
            .ToListAsync(cancellationToken);

        var aseImages = await _dbContext.AseScans
            .AsNoTracking()
            .Where(s => s.ContainerNumber == containerNumber && s.ScanImage != null)
            .OrderByDescending(s => s.ScanTime)
            .Take(20)
            .Select(s => new PredictiveImageMetadata
            {
                ImageId = s.Id.ToString(),
                ScannerType = "ASE",
                ImageType = "Scan",
                FileName = s.ImageDisplayName ?? $"{s.ContainerNumber}_{s.InspectionId}.jpg",
                FileSizeBytes = 0,
                CreatedAtUtc = s.ScanTime,
                HasBinaryData = s.ScanImage != null,
                ThumbnailUrl = $"/api/ContainerDetails/image/ase/{s.Id}/thumbnail",
                FullImageUrl = $"/api/ContainerDetails/image/ase/{s.Id}"
            })
            .ToListAsync(cancellationToken);

        return fs6000Images
            .Concat(aseImages)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(25)
            .ToList();
    }

    private async Task<PredictivePreloadAssignmentResult> PreloadAssignmentScopedBoundedAsync(
        Guid groupId,
        string role,
        string eligibleStatus,
        CancellationToken cancellationToken)
    {
        await _concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPredictivePreloadService>();
            return await service.PreloadAssignmentAsync(groupId, role, eligibleStatus, cancellationToken);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task CacheRoleAssignmentsAsync(
        string role,
        string eligibleStatus,
        IReadOnlyList<AnalysisGroup> groups,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds));
        var roleAssignments = new PredictiveRoleAssignments
        {
            Role = role,
            EligibleStatus = eligibleStatus,
            GroupIds = groups.Select(g => g.Id).ToArray(),
            CachedAtUtc = now,
            ExpiresAtUtc = now.Add(ttl)
        };

        await _cache.SetAsync(PredictivePreloadKeys.RoleAssignments(role), roleAssignments, ttl, cancellationToken);
    }

    private async Task<int> CountRelatedBoeDocumentsAsync(string containerNumber, CancellationToken cancellationToken)
    {
        var relatedIds = await GetRelatedBoeDocumentIdsAsync(containerNumber, cancellationToken);
        return await _icumDownloadsDbContext.BOEDocuments
            .AsNoTracking()
            .CountAsync(b => b.ContainerNumber == containerNumber || relatedIds.Contains(b.Id), cancellationToken);
    }

    private async Task<List<BOEDocument>> GetRelatedBoeDocumentsAsync(
        string containerNumber,
        int maxDocuments,
        CancellationToken cancellationToken)
    {
        var relatedIds = await GetRelatedBoeDocumentIdsAsync(containerNumber, cancellationToken);
        return await _icumDownloadsDbContext.BOEDocuments
            .AsNoTracking()
            .Where(b => b.ContainerNumber == containerNumber || relatedIds.Contains(b.Id))
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .Take(Math.Max(1, maxDocuments))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<int>> GetRelatedBoeDocumentIdsAsync(
        string containerNumber,
        CancellationToken cancellationToken)
    {
        var ids = await _dbContext.ContainerCompletenessStatuses
            .AsNoTracking()
            .Where(c => c.ContainerNumber == containerNumber && c.BOEDocumentId.HasValue)
            .Select(c => c.BOEDocumentId!.Value)
            .ToListAsync(cancellationToken);

        var relationIds = await _dbContext.ContainerBOERelations
            .AsNoTracking()
            .Where(r => r.ContainerNumber == containerNumber && r.IsActive)
            .Select(r => r.ICUMSBOEId)
            .ToListAsync(cancellationToken);

        ids.AddRange(relationIds);
        return ids.Distinct().ToList();
    }

    private async Task<IReadOnlyList<PredictiveManifestPreview>> GetManifestPreviewAsync(
        IReadOnlyCollection<int> boeDocumentIds,
        CancellationToken cancellationToken)
    {
        if (boeDocumentIds.Count == 0)
            return Array.Empty<PredictiveManifestPreview>();

        return await _icumDownloadsDbContext.ManifestItems
            .AsNoTracking()
            .Where(m => boeDocumentIds.Contains(m.BOEDocumentId))
            .OrderBy(m => m.BOEDocumentId)
            .ThenBy(m => m.ItemIndex)
            .Take(5)
            .Select(m => new PredictiveManifestPreview
            {
                ManifestItemId = m.Id,
                ItemIndex = m.ItemIndex,
                HsCode = m.HsCode,
                Description = m.Description,
                Quantity = m.Quantity,
                Unit = m.Unit,
                Weight = m.Weight,
                ItemDutyPaid = m.ItemDutyPaid,
                CountryOfOrigin = m.CountryOfOrigin
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<int> CountImageMetadataAsync(string containerNumber, CancellationToken cancellationToken)
    {
        var fs6000ImageCount = await _dbContext.FS6000Images
            .AsNoTracking()
            .CountAsync(i => _dbContext.FS6000Scans.Any(s => s.Id == i.ScanId && s.ContainerNumber == containerNumber), cancellationToken);
        var aseImageCount = await _dbContext.AseScans
            .AsNoTracking()
            .CountAsync(s => s.ContainerNumber == containerNumber && s.ScanImage != null, cancellationToken);

        return fs6000ImageCount + aseImageCount;
    }

    private static PredictiveScannerDataPage ToScannerPage(
        string containerNumber,
        IReadOnlyList<PredictiveFieldValue> fields,
        int pageSize)
    {
        return new PredictiveScannerDataPage
        {
            ContainerNumber = containerNumber,
            Page = 1,
            PageSize = pageSize,
            TotalCount = fields.Count,
            Status = fields.Count > 0 ? "Found" : "NoData",
            Data = fields.Take(pageSize).ToList()
        };
    }

    private static PredictiveIcumDataPage ToIcumPage(
        string containerNumber,
        IReadOnlyList<PredictiveFieldValue> fields,
        IReadOnlyList<PredictiveManifestPreview> manifestPreview,
        int pageSize)
    {
        return new PredictiveIcumDataPage
        {
            ContainerNumber = containerNumber,
            Page = 1,
            PageSize = pageSize,
            TotalCount = fields.Count,
            Status = fields.Count > 0 ? "Found" : "NoData",
            Data = fields.Take(pageSize).ToList(),
            ManifestPreview = manifestPreview
        };
    }

    private static void AddField(
        ICollection<PredictiveFieldValue> fields,
        string field,
        string? value,
        string category,
        DateTime? timestamp)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        fields.Add(new PredictiveFieldValue
        {
            Field = field,
            Value = value,
            Category = category,
            Timestamp = timestamp
        });
    }

    private static bool ShouldPreloadContainerContext(PredictivePreloadOptions options)
    {
        return options.PreloadContainerSummary
            || options.PreloadScannerFirstPage
            || options.PreloadIcumsFirstPage
            || options.PreloadBoeSummary
            || options.PreloadImageMetadata
            || options.PreloadFullImages;
    }

    private bool ShouldPreloadContainerContext()
    {
        return ShouldPreloadContainerContext(_options);
    }

    private static int CalculateCompletenessScore(bool hasScannerData, bool hasIcumsData, bool hasImageData)
    {
        var score = 0;
        if (hasScannerData) score += 33;
        if (hasIcumsData) score += 34;
        if (hasImageData) score += 33;
        return score;
    }

    private static string NormalizeContainerNumber(string? containerNumber)
    {
        return string.IsNullOrWhiteSpace(containerNumber)
            ? string.Empty
            : containerNumber.Trim().ToUpperInvariant();
    }

    private static string GetEligibleStatus(string role)
    {
        return string.Equals(role, "Audit", StringComparison.OrdinalIgnoreCase)
            ? AnalysisStatuses.AnalystCompleted
            : AnalysisStatuses.Ready;
    }
}
