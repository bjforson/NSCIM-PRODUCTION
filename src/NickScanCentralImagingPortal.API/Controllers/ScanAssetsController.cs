using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers;

[Authorize]
[ApiController]
[Route("api/scan-assets")]
public sealed class ScanAssetsController : ControllerBase
{
    private readonly IScanAssetResolver _resolver;
    private readonly IImageProcessingService _imageProcessingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ScanAssetsController> _logger;

    public ScanAssetsController(
        IScanAssetResolver resolver,
        IImageProcessingService imageProcessingService,
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext db,
        ILogger<ScanAssetsController> logger)
    {
        _resolver = resolver;
        _imageProcessingService = imageProcessingService;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
    }

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve(
        [FromQuery] string containerNumber,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _resolver.ResolveAsync(
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            cancellationToken);

        if (resolution.IsAmbiguous)
            return Conflict(resolution);

        return resolution.Found ? Ok(resolution) : NotFound(resolution);
    }

    [HttpGet("{sourceScanId}/image")]
    public async Task<IActionResult> GetSourceImage(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? imageType = null,
        [FromQuery] string size = "full",
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? splitResultId = null,
        [FromQuery] string? side = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = !string.IsNullOrWhiteSpace(containerNumber)
            ? await _resolver.ResolveAsync(containerNumber, splitJobId: splitJobId, cancellationToken: cancellationToken)
            : await ResolveBySourceScanIdAsync(sourceScanId, cancellationToken);

        if (!resolution.Found || resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
            return NotFound(resolution);

        if (!SourceIdMatches(sourceScanId, resolution))
            return NotFound(new
            {
                error = "Source scan id does not match resolved container source",
                sourceScanId,
                resolution
            });

        if (splitJobId.HasValue && splitResultId.HasValue)
        {
            var splitSide = NormalizeSplitSide(side ?? resolution.SplitPosition);
            if (string.IsNullOrWhiteSpace(splitSide))
            {
                return BadRequest(new
                {
                    error = "Split crop side is required",
                    sourceScanId,
                    splitJobId,
                    splitResultId,
                    resolution
                });
            }

            var crop = await GetSplitCropAsync(splitJobId.Value, splitResultId.Value, splitSide, cancellationToken);
            if (crop != null)
            {
                Response.Headers.CacheControl = string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase)
                    ? "private, max-age=300"
                    : "private, max-age=3600";
                Response.ContentLength = crop.Value.Bytes.Length;
                return File(crop.Value.Bytes, crop.Value.ContentType);
            }

            return NotFound(new
            {
                error = "Split crop image not found",
                sourceScanId,
                splitJobId,
                splitResultId,
                side = splitSide,
                resolution
            });
        }

        if (IsEagleA25(resolution.SourceScannerType))
        {
            var eagleLookup = !string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers)
                ? resolution.SourceContainerNumbers
                : resolution.SourceScanId ?? sourceScanId;

            var rendered = await _imageProcessingService.GetRenderedImageBytesAsync(
                eagleLookup,
                "bw",
                ct: cancellationToken);

            if (rendered is { Length: > 0 })
            {
                Response.Headers.CacheControl = string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase)
                    ? "private, max-age=300"
                    : "private, max-age=3600";

                if (string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase))
                {
                    return File(rendered, "image/jpeg");
                }

                return File(rendered, "image/jpeg");
            }

            var fallback = await _imageProcessingService.GetCompleteContainerDataAsync(eagleLookup, imageType);
            if (fallback?.ImageBytes is { Length: > 0 })
            {
                Response.Headers.CacheControl = string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase)
                    ? "private, max-age=300"
                    : "private, max-age=3600";
                return File(fallback.ImageBytes, fallback.MimeType ?? "image/jpeg");
            }
        }

        var data = await _imageProcessingService.GetCompleteContainerDataAsync(
            resolution.SourceContainerNumbers,
            imageType);

        if (data?.ImageBytes == null || data.ImageBytes.Length == 0)
        {
            _logger.LogWarning(
                "Source image not available for source {SourceScanId} ({Container})",
                sourceScanId,
                resolution.SourceContainerNumbers);
            return NotFound(new { error = "Source image not found", sourceScanId, resolution });
        }

        Response.Headers.CacheControl = string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase)
            ? "private, max-age=300"
            : "private, max-age=3600";

        return File(data.ImageBytes, data.MimeType ?? "image/jpeg");
    }

    [HttpGet("{sourceScanId}/images")]
    public async Task<IActionResult> GetSourceImages(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? splitResultId = null,
        [FromQuery] string? side = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _resolver.ResolveAsync(
            containerNumber ?? string.Empty,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            cancellationToken);

        if (!resolution.Found || resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
            return resolution.IsAmbiguous ? Conflict(resolution) : NotFound(resolution);

        if (!SourceIdMatches(sourceScanId, resolution))
            return NotFound(new
            {
                error = "Source scan id does not match resolved container source",
                sourceScanId,
                resolution
            });

        var effectiveSplitJobId = splitJobId ?? resolution.SplitJobId;
        var effectiveSplitResultId = splitResultId ?? resolution.SplitResultId;
        var effectiveSide = side ?? resolution.SplitPosition;
        var imagePath = BuildSourceImagePath(sourceScanId, containerNumber, "full", effectiveSplitJobId, effectiveSplitResultId, effectiveSide);
        var thumbnailPath = BuildSourceImagePath(sourceScanId, containerNumber, "thumbnail", effectiveSplitJobId, effectiveSplitResultId, effectiveSide);
        var imageHash = HashCode.Combine(sourceScanId, effectiveSplitJobId, effectiveSplitResultId, effectiveSide);
        var imageId = imageHash == int.MinValue ? int.MaxValue : Math.Abs(imageHash);

        return Ok(new[]
        {
            new
            {
                Id = imageId == int.MinValue ? int.MaxValue : imageId,
                ImageType = resolution.SourceScannerType ?? "Source",
                FileName = $"{resolution.ContainerNumber ?? containerNumber ?? resolution.SourceContainerNumbers}_{sourceScanId}.jpg",
                FileSizeBytes = resolution.ImageSizeBytes.GetValueOrDefault(),
                CreatedAt = resolution.ScanTime ?? DateTime.UtcNow,
                ThumbnailUrl = thumbnailPath,
                FullImageUrl = imagePath,
                SourceScanId = resolution.SourceScanId,
                OriginalScanRecordId = resolution.OriginalScanRecordId,
                ScannerScanId = resolution.ScannerScanId,
                SplitJobId = effectiveSplitJobId,
                SplitResultId = effectiveSplitResultId,
                SplitSide = effectiveSide,
                ResolutionReason = resolution.ResolutionReason,
                Resolution = resolution
            }
        });
    }

    private static string BuildSourceImagePath(
        string sourceScanId,
        string? containerNumber,
        string size,
        Guid? splitJobId,
        Guid? splitResultId,
        string? side)
    {
        var parts = new List<string>
        {
            $"size={Uri.EscapeDataString(size)}"
        };

        if (!string.IsNullOrWhiteSpace(containerNumber))
        {
            parts.Add($"containerNumber={Uri.EscapeDataString(containerNumber)}");
        }

        if (splitJobId.HasValue)
        {
            parts.Add($"splitJobId={splitJobId.Value}");
        }

        if (splitResultId.HasValue)
        {
            parts.Add($"splitResultId={splitResultId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            parts.Add($"side={Uri.EscapeDataString(side)}");
        }

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/image?{string.Join("&", parts)}";
    }

    private async Task<SplitCropAsset?> GetSplitCropAsync(
        Guid splitJobId,
        Guid splitResultId,
        string side,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("RawImageEngine");
            var response = await client.GetAsync(
                $"/api/split/{splitJobId}/results/{splitResultId}/lossless/{Uri.EscapeDataString(side)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Split crop lookup failed for job {SplitJobId}, result {SplitResultId}, side {Side}: {StatusCode}",
                    splitJobId,
                    splitResultId,
                    side,
                    response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                _logger.LogWarning(
                    "Split crop lookup returned empty body for job {SplitJobId}, result {SplitResultId}, side {Side}",
                    splitJobId,
                    splitResultId,
                    side);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType)
                || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "image/png";
            }

            return new SplitCropAsset(bytes, contentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to retrieve split crop for job {SplitJobId}, result {SplitResultId}, side {Side}",
                splitJobId,
                splitResultId,
                side);
            return null;
        }
    }

    private async Task<ScanAssetResolution> ResolveBySourceScanIdAsync(
        string sourceScanId,
        CancellationToken cancellationToken)
    {
        var lookupId = NormalizeSourceScanIdForLookup(sourceScanId);

        if (int.TryParse(lookupId, out var originalScanRecordId))
        {
            var ase = await _db.AseScans
                .AsNoTracking()
                .Where(scan => scan.OriginalScanRecordId == originalScanRecordId)
                .OrderByDescending(scan => scan.ScanTime)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber ?? string.Empty,
                    ContainerNumber = scan.ContainerNumber ?? string.Empty,
                    SourceScannerType = "ASE",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = scan.OriginalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber,
                    ResolvedBy = "SourceScanId",
                    ResolutionReason = "OriginalScanRecordId",
                    ScanTime = scan.ScanTime,
                    ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                    ImageDisplayName = scan.ImageDisplayName
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (ase != null)
                return ase;

            var fs = await _db.FS6000Scans
                .AsNoTracking()
                .Where(scan => scan.OriginalScanRecordId == originalScanRecordId)
                .OrderByDescending(scan => scan.ScanTime)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber,
                    ContainerNumber = scan.ContainerNumber,
                    SourceScannerType = "FS6000",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = scan.OriginalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber,
                    ResolvedBy = "SourceScanId",
                    ResolutionReason = "OriginalScanRecordId",
                    ScanTime = scan.ScanTime
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (fs != null)
                return fs;

            var eagleBySourceScanId = await _db.EagleA25Scans
                .Include(scan => scan.Assets)
                .AsNoTracking()
                .Where(scan => scan.SourceScanId == originalScanRecordId)
                .OrderByDescending(scan => scan.ScanDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (eagleBySourceScanId != null)
                return ToEagleA25Resolution(eagleBySourceScanId, sourceScanId, "EagleA25SourceScanId");

            var original = await _db.OriginalScanRecords
                .AsNoTracking()
                .Where(record => record.Id == originalScanRecordId)
                .Select(record => new
                {
                    record.Id,
                    record.ScannerType,
                    record.OriginalContainerNumbers,
                    record.InspectionId,
                    record.ScanTime
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (original != null)
            {
                var fallback = await ResolveOriginalScanRecordFallbackAsync(
                    sourceScanId,
                    original.Id,
                    original.ScannerType,
                    original.OriginalContainerNumbers,
                    original.InspectionId,
                    original.ScanTime,
                    cancellationToken);

                if (fallback != null)
                    return fallback;
            }
        }

        if (Guid.TryParse(lookupId, out var scannerScanId))
        {
            var ase = await _db.AseScans
                .AsNoTracking()
                .Where(scan => scan.Id == scannerScanId)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber ?? string.Empty,
                    ContainerNumber = scan.ContainerNumber ?? string.Empty,
                    SourceScannerType = "ASE",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = scan.OriginalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber,
                    ResolvedBy = "SourceScanId",
                    ResolutionReason = "AseScanId",
                    ScanTime = scan.ScanTime,
                    ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                    ImageDisplayName = scan.ImageDisplayName
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (ase != null)
                return ase;

            var fs = await _db.FS6000Scans
                .AsNoTracking()
                .Where(scan => scan.Id == scannerScanId)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber,
                    ContainerNumber = scan.ContainerNumber,
                    SourceScannerType = "FS6000",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = scan.OriginalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber,
                    ResolvedBy = "SourceScanId",
                    ResolutionReason = "Fs6000ScanId",
                    ScanTime = scan.ScanTime
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (fs != null)
                return fs;

            var eagle = await _db.EagleA25Scans
                .Include(scan => scan.Assets)
                .AsNoTracking()
                .Where(scan => scan.Id == scannerScanId)
                .FirstOrDefaultAsync(cancellationToken);

            if (eagle != null)
                return ToEagleA25Resolution(eagle, sourceScanId, "EagleA25ScanId");
        }

        return ScanAssetResolution.NotFound(sourceScanId, "SourceScanNotFound");
    }

    private async Task<ScanAssetResolution?> ResolveOriginalScanRecordFallbackAsync(
        string sourceScanId,
        int originalScanRecordId,
        string scannerType,
        string originalContainerNumbers,
        string? inspectionId,
        DateTime scanTime,
        CancellationToken cancellationToken)
    {
        if (string.Equals(scannerType, "ASE", StringComparison.OrdinalIgnoreCase))
        {
            var ase = await _db.AseScans
                .AsNoTracking()
                .Where(scan =>
                    scan.ContainerNumber == originalContainerNumbers
                    || (!string.IsNullOrWhiteSpace(inspectionId)
                        && (scan.InspectionUuid == inspectionId
                            || (scan.ImageDisplayName != null && scan.ImageDisplayName.StartsWith(inspectionId)))))
                .OrderByDescending(scan => scan.ScanTime)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber ?? originalContainerNumbers,
                    ContainerNumber = scan.ContainerNumber ?? originalContainerNumbers,
                    SourceScannerType = "ASE",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = originalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber ?? originalContainerNumbers,
                    ResolvedBy = "SourceScanIdFallback",
                    ResolutionReason = "OriginalScanRecordFallback",
                    ScanTime = scan.ScanTime,
                    ImageSizeBytes = scan.ScanImage != null ? scan.ScanImage.Length : 0,
                    ImageDisplayName = scan.ImageDisplayName,
                    HasImage = scan.ScanImage != null && scan.ScanImage.Length > 0
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (ase != null)
                return ase;
        }

        if (string.Equals(scannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
        {
            var fs = await _db.FS6000Scans
                .AsNoTracking()
                .Where(scan => originalContainerNumbers.Contains(scan.ContainerNumber))
                .OrderByDescending(scan => scan.ScanTime)
                .Select(scan => new ScanAssetResolution
                {
                    Found = true,
                    RequestedContainerNumber = scan.ContainerNumber,
                    ContainerNumber = scan.ContainerNumber,
                    SourceScannerType = "FS6000",
                    SourceScanId = sourceScanId,
                    OriginalScanRecordId = originalScanRecordId,
                    ScannerScanId = scan.Id,
                    SourceContainerNumbers = scan.ContainerNumber,
                    ResolvedBy = "SourceScanIdFallback",
                    ResolutionReason = "OriginalScanRecordFallback",
                    ScanTime = scan.ScanTime,
                    HasImage = scan.HasImage
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (fs != null)
                return fs;
        }

        return new ScanAssetResolution
        {
            Status = ScanAssetResolutionStatuses.Resolved,
            Found = true,
            RequestedContainerNumber = originalContainerNumbers,
            ContainerNumber = originalContainerNumbers,
            SourceScannerType = scannerType,
            SourceScanId = sourceScanId,
            OriginalScanRecordId = originalScanRecordId,
            SourceContainerNumbers = originalContainerNumbers,
            ResolvedBy = "OriginalScanRecord",
            ResolutionReason = "OriginalScanRecordOnly",
            ScanTime = scanTime
        };
    }

    private static bool SourceIdMatches(string sourceScanId, ScanAssetResolution resolution)
    {
        var lookupId = NormalizeSourceScanIdForLookup(sourceScanId);

        return string.Equals(NormalizeSourceScanIdForLookup(resolution.SourceScanId), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.OriginalScanRecordId?.ToString(), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.ScannerScanId?.ToString(), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.ScannerScanId?.ToString("N"), lookupId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEagleA25(string? scannerType)
        => string.Equals(scannerType, "EAGLE_A25", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scannerType, "EagleA25", StringComparison.OrdinalIgnoreCase);

    private static ScanAssetResolution ToEagleA25Resolution(
        NickScanCentralImagingPortal.Core.Entities.EagleA25.EagleA25Scan scan,
        string sourceScanId,
        string reason)
    {
        var accessionText = scan.Accession.ToString(CultureInfo.InvariantCulture);
        var display = string.IsNullOrWhiteSpace(scan.CargoIdentifier) ? accessionText : scan.CargoIdentifier!;

        return new ScanAssetResolution
        {
            Found = true,
            RequestedContainerNumber = display,
            ContainerNumber = display,
            SourceScannerType = "EAGLE_A25",
            SourceScanId = sourceScanId,
            ScannerScanId = scan.Id,
            SourceContainerNumbers = accessionText,
            ResolvedBy = "SourceScanId",
            ResolutionReason = reason,
            ScanTime = scan.ScanDateUtc,
            ImageSizeBytes = scan.Assets
                .Where(asset => asset.FileType == "XRAY")
                .Select(asset => asset.FileSizeBytes)
                .FirstOrDefault(),
            HasImage = scan.Assets.Any(asset =>
                !string.IsNullOrWhiteSpace(asset.LocalPath)
                && (asset.FileType == "XRAY"
                    || asset.FileType == "XRAYJPEG"
                    || asset.FileType == "SCANDOC"))
        };
    }

    private static string NormalizeSourceScanIdForLookup(string? sourceScanId)
    {
        if (string.IsNullOrWhiteSpace(sourceScanId))
            return string.Empty;

        var trimmed = sourceScanId.Trim();
        var separatorIndex = trimmed.IndexOf(':');
        return separatorIndex >= 0 && separatorIndex < trimmed.Length - 1
            ? trimmed[(separatorIndex + 1)..]
            : trimmed;
    }

    private static string? NormalizeSplitSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
            return null;

        var normalized = side.Trim().ToLowerInvariant();
        return normalized is "left" or "right" ? normalized : normalized;
    }

    private readonly record struct SplitCropAsset(byte[] Bytes, string ContentType);
}
