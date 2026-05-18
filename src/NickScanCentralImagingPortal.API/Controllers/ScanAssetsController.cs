using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.EagleA25;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
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
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _resolver.ResolveAsync(
            new ScanAssetResolutionRequest
            {
                ContainerNumber = containerNumber,
                GroupIdentifier = groupIdentifier,
                AnalysisRecordId = analysisRecordId,
                SplitJobId = splitJobId,
                ScanImageAssetId = scanImageAssetId
            },
            cancellationToken);

        if (resolution.IsAmbiguous)
            return Conflict(resolution);

        return resolution.Found ? Ok(resolution) : NotFound(resolution);
    }

    [HttpGet("{sourceScanId}/image")]
    public async Task<IActionResult> GetSourceImage(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] string? imageType = null,
        [FromQuery] string size = "full",
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? splitResultId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] string? side = null,
        [FromQuery] bool annotations = false,
        [FromQuery] string? mode = null,
        [FromQuery] float? loPct = null,
        [FromQuery] float? hiPct = null,
        [FromQuery] float? gamma = null,
        CancellationToken cancellationToken = default)
    {
        var hasResolverContext = !string.IsNullOrWhiteSpace(containerNumber)
            || !string.IsNullOrWhiteSpace(groupIdentifier)
            || analysisRecordId.HasValue
            || scanImageAssetId.HasValue;
        var resolution = hasResolverContext
            ? await _resolver.ResolveAsync(
                new ScanAssetResolutionRequest
                {
                    ContainerNumber = containerNumber,
                    GroupIdentifier = groupIdentifier,
                    AnalysisRecordId = analysisRecordId,
                    SplitJobId = splitJobId,
                    ScanImageAssetId = scanImageAssetId
                },
                cancellationToken)
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

        var sourceContainer = GetPipelineContainerNumber(resolution);
        if (string.IsNullOrWhiteSpace(sourceContainer))
            return NotFound(new { error = "Source container identity not available", sourceScanId, resolution });

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

        if (!string.IsNullOrWhiteSpace(mode))
        {
            var modeBytes = await _imageProcessingService.GetRenderedImageBytesAsync(
                sourceContainer,
                mode,
                loPct ?? 1.0f,
                hiPct ?? 99.5f,
                gamma ?? 1.0f,
                cancellationToken);
            if (modeBytes is { Length: > 0 })
            {
                Response.Headers.CacheControl = "private, max-age=3600";
                return File(modeBytes, "image/jpeg");
            }

            var caps = await _imageProcessingService.GetScanModeCapabilitiesAsync(sourceContainer, cancellationToken);
            if (caps == null)
                return NotFound(new { error = "No scan found for source", sourceScanId, resolution });

            var normalized = mode.Trim().ToLowerInvariant();
            var claimedSupported = caps.SupportedModes != null
                && caps.SupportedModes.Any(m => string.Equals(m, normalized, StringComparison.OrdinalIgnoreCase));
            if (claimedSupported)
            {
                return StatusCode(500, new
                {
                    error = $"Render failed for mode '{mode}' even though capabilities claim support.",
                    sourceScanId,
                    scanner = caps.Scanner,
                    variant = caps.Variant,
                    resolution
                });
            }

            return UnprocessableEntity(new
            {
                error = $"Mode '{mode}' not supported for this scan variant",
                sourceScanId,
                scanner = caps.Scanner,
                variant = caps.Variant,
                supportedModes = caps.SupportedModes,
                resolution
            });
        }

        if (IsAse(resolution.SourceScannerType))
        {
            var caps = await _imageProcessingService.GetScanModeCapabilitiesAsync(sourceContainer, cancellationToken);
            var compositeAvailable = caps?.SupportedModes != null
                && caps.SupportedModes.Any(m => string.Equals(m, "composite", StringComparison.OrdinalIgnoreCase));
            if (compositeAvailable)
            {
                var autoBytes = await _imageProcessingService.GetRenderedImageBytesAsync(
                    sourceContainer,
                    "composite",
                    loPct ?? 1.0f,
                    hiPct ?? 99.5f,
                    gamma ?? 1.0f,
                    cancellationToken);
                if (autoBytes is { Length: > 0 })
                {
                    Response.Headers.CacheControl = "private, max-age=3600";
                    return File(autoBytes, "image/jpeg");
                }
            }
        }

        if (IsEagleA25(resolution.SourceScannerType))
        {
            var rendered = await _imageProcessingService.GetRenderedImageBytesAsync(
                sourceContainer,
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

            var fallback = await _imageProcessingService.GetCompleteContainerDataAsync(sourceContainer, imageType);
            if (fallback?.ImageBytes is { Length: > 0 })
            {
                Response.Headers.CacheControl = string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase)
                    ? "private, max-age=300"
                    : "private, max-age=3600";
                return File(fallback.ImageBytes, fallback.MimeType ?? "image/jpeg");
            }
        }

        var data = await _imageProcessingService.GetCompleteContainerDataAsync(
            sourceContainer,
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

    [HttpGet("{sourceScanId}/mode-capabilities")]
    [ProducesResponseType(200, Type = typeof(ScanModeCapabilities))]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSourceModeCapabilities(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        CancellationToken cancellationToken = default)
    {
        var (resolution, error) = await ResolveSourceForRequestAsync(
            sourceScanId,
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            scanImageAssetId,
            cancellationToken);
        if (error != null)
            return error;

        var sourceContainer = GetPipelineContainerNumber(resolution!);
        if (string.IsNullOrWhiteSpace(sourceContainer))
            return NotFound(new { error = "Source container identity not available", sourceScanId, resolution });

        var caps = await _imageProcessingService.GetScanModeCapabilitiesAsync(sourceContainer, cancellationToken);
        if (caps == null)
            return NotFound(new { error = "No scan found or scanner type not resolvable", sourceScanId, resolution });

        Response.Headers.CacheControl = "private, max-age=60";
        return Ok(caps);
    }

    [HttpGet("{sourceScanId}/roi")]
    [ProducesResponseType(200, Type = typeof(RoiInspectorResult))]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSourceRoiInspector(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] int x = 0,
        [FromQuery] int y = 0,
        [FromQuery] int w = 100,
        [FromQuery] int h = 100,
        CancellationToken cancellationToken = default)
    {
        if (w <= 0 || h <= 0)
            return BadRequest(new { error = "w and h must be positive" });

        var (resolution, error) = await ResolveSourceForRequestAsync(
            sourceScanId,
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            scanImageAssetId,
            cancellationToken);
        if (error != null)
            return error;

        var sourceContainer = GetPipelineContainerNumber(resolution!);
        if (string.IsNullOrWhiteSpace(sourceContainer))
            return NotFound(new { error = "Source container identity not available", sourceScanId, resolution });

        var result = await _imageProcessingService.GetRoiInspectorAsync(sourceContainer, x, y, w, h, cancellationToken);
        if (result == null)
            return NotFound(new { error = "ROI inspector unavailable for source scan", sourceScanId, resolution });

        Response.Headers.CacheControl = "private, max-age=300";
        return Ok(result);
    }

    [HttpGet("{sourceScanId}/pixel")]
    [ProducesResponseType(200, Type = typeof(PixelValueResult))]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSourcePixelValue(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] int x = 0,
        [FromQuery] int y = 0,
        CancellationToken cancellationToken = default)
    {
        var (resolution, error) = await ResolveSourceForRequestAsync(
            sourceScanId,
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            scanImageAssetId,
            cancellationToken);
        if (error != null)
            return error;

        var sourceContainer = GetPipelineContainerNumber(resolution!);
        if (string.IsNullOrWhiteSpace(sourceContainer))
            return NotFound(new { error = "Source container identity not available", sourceScanId, resolution });

        var result = await _imageProcessingService.GetPixelValueAsync(sourceContainer, x, y, cancellationToken);
        if (result == null)
            return NotFound(new { error = "Pixel probe unavailable for source scan", sourceScanId, resolution });

        Response.Headers.CacheControl = "private, max-age=10";
        return Ok(result);
    }

    [HttpGet("{sourceScanId}/raw")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSourceRawPlane(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] string plane = "he",
        CancellationToken cancellationToken = default)
    {
        var (resolution, error) = await ResolveSourceForRequestAsync(
            sourceScanId,
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            scanImageAssetId,
            cancellationToken);
        if (error != null)
            return error;

        var sourceContainer = GetPipelineContainerNumber(resolution!);
        if (string.IsNullOrWhiteSpace(sourceContainer))
            return NotFound(new { error = "Source container identity not available", sourceScanId, resolution });

        var result = await _imageProcessingService.GetRawPlaneAsync(sourceContainer, plane, cancellationToken);
        if (result == null)
        {
            return NotFound(new
            {
                error = $"Raw plane '{plane}' not available for this source scan",
                sourceScanId,
                resolution
            });
        }

        Response.Headers["X-Width"] = result.Width.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Height"] = result.Height.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-BitDepth"] = result.BitDepth.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Plane"] = result.Plane;
        Response.Headers["X-Source-Format"] = result.SourceFormat;
        Response.Headers["Access-Control-Expose-Headers"] =
            "X-Width, X-Height, X-BitDepth, X-Plane, X-Source-Format";
        Response.Headers.CacheControl = "private, max-age=60";
        return File(result.Bytes, "application/octet-stream");
    }

    [HttpGet("{sourceScanId}/images")]
    public async Task<IActionResult> GetSourceImages(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? splitResultId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] string? side = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _resolver.ResolveAsync(
            new ScanAssetResolutionRequest
            {
                ContainerNumber = containerNumber,
                GroupIdentifier = groupIdentifier,
                AnalysisRecordId = analysisRecordId,
                SplitJobId = splitJobId,
                ScanImageAssetId = scanImageAssetId
            },
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
        var imagePath = BuildSourceImagePath(sourceScanId, containerNumber, "full", effectiveSplitJobId, effectiveSplitResultId, resolution.ScanImageAssetId, effectiveSide);
        var thumbnailPath = BuildSourceImagePath(sourceScanId, containerNumber, "thumbnail", effectiveSplitJobId, effectiveSplitResultId, resolution.ScanImageAssetId, effectiveSide);
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
                ScanImageAssetId = resolution.ScanImageAssetId,
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

    [HttpGet("{sourceScanId}/scanner-data")]
    public async Task<IActionResult> GetSourceScannerData(
        string sourceScanId,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? groupIdentifier = null,
        [FromQuery] int? analysisRecordId = null,
        [FromQuery] Guid? splitJobId = null,
        [FromQuery] Guid? scanImageAssetId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool full = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var hasResolverContext = !string.IsNullOrWhiteSpace(containerNumber)
            || !string.IsNullOrWhiteSpace(groupIdentifier)
            || analysisRecordId.HasValue
            || scanImageAssetId.HasValue;
        var resolution = hasResolverContext
            ? await _resolver.ResolveAsync(
                new ScanAssetResolutionRequest
                {
                    ContainerNumber = containerNumber,
                    GroupIdentifier = groupIdentifier,
                    AnalysisRecordId = analysisRecordId,
                    SplitJobId = splitJobId,
                    ScanImageAssetId = scanImageAssetId
                },
                cancellationToken)
            : await ResolveBySourceScanIdAsync(sourceScanId, cancellationToken);

        if (resolution.IsAmbiguous)
            return Conflict(resolution);

        if (!resolution.Found || string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
            return NotFound(resolution);

        if (!SourceIdMatches(sourceScanId, resolution))
            return NotFound(new
            {
                error = "Source scan id does not match resolved container source",
                sourceScanId,
                resolution
            });

        var scannerRecords = await BuildSourceScannerRecordsAsync(resolution, cancellationToken);

        if (full)
        {
            return Ok(BuildFullScannerDataResponse(resolution, scannerRecords));
        }

        var totalCount = scannerRecords.Count;
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResult<ScannerDataRecord>
        {
            Data = scannerRecords
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Status = totalCount > 0 ? "Found" : "NoData"
        });
    }

    private static string BuildSourceImagePath(
        string sourceScanId,
        string? containerNumber,
        string size,
        Guid? splitJobId,
        Guid? splitResultId,
        Guid? scanImageAssetId,
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

        if (scanImageAssetId.HasValue)
        {
            parts.Add($"scanImageAssetId={scanImageAssetId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            parts.Add($"side={Uri.EscapeDataString(side)}");
        }

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/image?{string.Join("&", parts)}";
    }

    private async Task<(ScanAssetResolution? Resolution, IActionResult? Error)> ResolveSourceForRequestAsync(
        string sourceScanId,
        string? containerNumber,
        string? groupIdentifier,
        int? analysisRecordId,
        Guid? splitJobId,
        Guid? scanImageAssetId,
        CancellationToken cancellationToken)
    {
        var hasResolverContext = !string.IsNullOrWhiteSpace(containerNumber)
            || !string.IsNullOrWhiteSpace(groupIdentifier)
            || analysisRecordId.HasValue
            || scanImageAssetId.HasValue;
        var resolution = hasResolverContext
            ? await _resolver.ResolveAsync(
                new ScanAssetResolutionRequest
                {
                    ContainerNumber = containerNumber,
                    GroupIdentifier = groupIdentifier,
                    AnalysisRecordId = analysisRecordId,
                    SplitJobId = splitJobId,
                    ScanImageAssetId = scanImageAssetId
                },
                cancellationToken)
            : await ResolveBySourceScanIdAsync(sourceScanId, cancellationToken);

        if (resolution.IsAmbiguous)
            return (resolution, Conflict(resolution));

        if (!resolution.Found)
            return (resolution, NotFound(resolution));

        if (!SourceIdMatches(sourceScanId, resolution))
        {
            return (resolution, NotFound(new
            {
                error = "Source scan id does not match resolved container source",
                sourceScanId,
                resolution
            }));
        }

        return (resolution, null);
    }

    private static string? GetPipelineContainerNumber(ScanAssetResolution resolution)
    {
        if (!string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
            return resolution.SourceContainerNumbers;

        return GetDisplayContainerNumber(resolution);
    }

    private async Task<List<ScannerDataRecord>> BuildSourceScannerRecordsAsync(
        ScanAssetResolution resolution,
        CancellationToken cancellationToken)
    {
        if (IsFs6000(resolution.SourceScannerType))
        {
            var scan = await FindFs6000ScanAsync(resolution, cancellationToken);
            if (scan != null)
                return BuildFs6000ScannerRecords(scan, resolution);
        }

        if (IsAse(resolution.SourceScannerType))
        {
            var scan = await FindAseScanAsync(resolution, cancellationToken);
            if (scan != null)
                return BuildAseScannerRecords(scan, resolution);
        }

        if (IsEagleA25(resolution.SourceScannerType))
        {
            var scan = await FindEagleA25ScanAsync(resolution, cancellationToken);
            if (scan != null)
                return BuildEagleA25ScannerRecords(scan, resolution);
        }

        var ase = await FindAseScanAsync(resolution, cancellationToken);
        if (ase != null)
            return BuildAseScannerRecords(ase, resolution);

        var fs6000 = await FindFs6000ScanAsync(resolution, cancellationToken);
        if (fs6000 != null)
            return BuildFs6000ScannerRecords(fs6000, resolution);

        var eagle = await FindEagleA25ScanAsync(resolution, cancellationToken);
        return eagle != null
            ? BuildEagleA25ScannerRecords(eagle, resolution)
            : new List<ScannerDataRecord>();
    }

    private async Task<AseScan?> FindAseScanAsync(
        ScanAssetResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.ScannerScanId.HasValue)
        {
            var scan = await _db.AseScans
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == resolution.ScannerScanId.Value, cancellationToken);

            if (scan != null)
                return scan;
        }

        if (resolution.OriginalScanRecordId.HasValue)
        {
            var scan = await _db.AseScans
                .AsNoTracking()
                .Where(s => s.OriginalScanRecordId == resolution.OriginalScanRecordId.Value)
                .OrderByDescending(s => s.ScanTime)
                .FirstOrDefaultAsync(cancellationToken);

            if (scan != null)
                return scan;
        }

        var candidates = GetCandidateContainerNumbers(resolution);
        return candidates.Count == 0
            ? null
            : await _db.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber != null && candidates.Contains(s.ContainerNumber))
                .OrderByDescending(s => s.ScanTime)
                .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<FS6000Scan?> FindFs6000ScanAsync(
        ScanAssetResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.ScannerScanId.HasValue)
        {
            var scan = await _db.FS6000Scans
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == resolution.ScannerScanId.Value, cancellationToken);

            if (scan != null)
                return scan;
        }

        if (resolution.OriginalScanRecordId.HasValue)
        {
            var scan = await _db.FS6000Scans
                .AsNoTracking()
                .Where(s => s.OriginalScanRecordId == resolution.OriginalScanRecordId.Value)
                .OrderByDescending(s => s.ScanTime)
                .FirstOrDefaultAsync(cancellationToken);

            if (scan != null)
                return scan;
        }

        var candidates = GetCandidateContainerNumbers(resolution);
        return candidates.Count == 0
            ? null
            : await _db.FS6000Scans
                .AsNoTracking()
                .Where(s => candidates.Contains(s.ContainerNumber))
                .OrderByDescending(s => s.ScanTime)
                .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<EagleA25Scan?> FindEagleA25ScanAsync(
        ScanAssetResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.ScannerScanId.HasValue)
        {
            var scan = await _db.EagleA25Scans
                .Include(s => s.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == resolution.ScannerScanId.Value, cancellationToken);

            if (scan != null)
                return scan;
        }

        var lookupId = NormalizeSourceScanIdForLookup(resolution.SourceScanId);
        if (int.TryParse(lookupId, out var sourceScanId))
        {
            var scan = await _db.EagleA25Scans
                .Include(s => s.Assets)
                .AsNoTracking()
                .Where(s => s.SourceScanId == sourceScanId)
                .OrderByDescending(s => s.ScanDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (scan != null)
                return scan;
        }

        var candidates = GetCandidateContainerNumbers(resolution);
        if (candidates.Count == 0)
            return null;

        var accessions = candidates
            .Select(candidate => long.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return await _db.EagleA25Scans
            .Include(s => s.Assets)
            .AsNoTracking()
            .Where(s =>
                accessions.Contains(s.Accession)
                || (s.CargoIdentifier != null && candidates.Contains(s.CargoIdentifier)))
            .OrderByDescending(s => s.ScanDateUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static FullScannerDataRecordDto BuildFullScannerDataResponse(
        ScanAssetResolution resolution,
        IReadOnlyList<ScannerDataRecord> scannerRecords)
    {
        var allFields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in scannerRecords)
        {
            allFields[record.Field] = record.Value;
        }

        if (allFields.Count == 0)
        {
            allFields["Status"] = "NoData";
            allFields["Source Scan Id"] = resolution.SourceScanId ?? "N/A";
        }

        return new FullScannerDataRecordDto
        {
            ContainerNumber = GetDisplayContainerNumber(resolution),
            ScannerType = resolution.SourceScannerType ?? "Unknown",
            ScanTime = scannerRecords.FirstOrDefault()?.Timestamp ?? resolution.ScanTime ?? DateTime.UtcNow,
            AllFields = allFields,
            AvailableFields = allFields.Keys.ToList(),
            MissingFields = new List<string>()
        };
    }

    private static List<ScannerDataRecord> BuildAseScannerRecords(
        AseScan scan,
        ScanAssetResolution resolution)
    {
        var timestamp = scan.ScanTime;
        return new List<ScannerDataRecord>
        {
            ScannerRecord("Container Number", resolution.ContainerNumber ?? scan.ContainerNumber, "Container Info", timestamp),
            ScannerRecord("Source Container Number(s)", scan.ContainerNumber, "Source Scan", timestamp),
            ScannerRecord("Source Scan Id", resolution.SourceScanId ?? scan.OriginalScanRecordId?.ToString(CultureInfo.InvariantCulture) ?? scan.Id.ToString(), "Source Scan", timestamp),
            ScannerRecord("Scanner Record Id", scan.Id, "Source Scan", timestamp),
            ScannerRecord("Original Scan Record Id", scan.OriginalScanRecordId, "Source Scan", timestamp),
            ScannerRecord("Resolution", resolution.ResolvedBy ?? resolution.ResolutionReason ?? "AseContainerLookup", "Source Scan", timestamp),
            ScannerRecord("Scanner Type", "ASE", "Scanner Info", timestamp),
            ScannerRecord("Scan Time", scan.ScanTime, "Scanner Info", timestamp),
            ScannerRecord("Inspection ID", scan.InspectionId, "Scanner Info", timestamp),
            ScannerRecord("Inspection UUID", scan.InspectionUuid, "Scanner Info", timestamp),
            ScannerRecord("Vehicle Number", scan.TruckPlate, "Vehicle Info", timestamp),
            ScannerRecord("Image Display Name", scan.ImageDisplayName, "Image Info", timestamp),
            ScannerRecord("Has Scan Image", scan.ScanImage != null, "Image Info", timestamp),
            ScannerRecord("Image Size", scan.ScanImage?.Length is int length ? $"{length} bytes" : null, "Image Info", timestamp),
            ScannerRecord("Synced At", scan.SyncedAt, "Status", timestamp)
        };
    }

    private static List<ScannerDataRecord> BuildFs6000ScannerRecords(
        FS6000Scan scan,
        ScanAssetResolution resolution)
    {
        var timestamp = scan.ScanTime;
        return new List<ScannerDataRecord>
        {
            ScannerRecord("Container Number", scan.ContainerNumber, "Container Info", timestamp),
            ScannerRecord("Source Container Number(s)", resolution.SourceContainerNumbers ?? scan.ContainerNumber, "Source Scan", timestamp),
            ScannerRecord("Source Scan Id", resolution.SourceScanId ?? scan.OriginalScanRecordId?.ToString(CultureInfo.InvariantCulture) ?? scan.Id.ToString(), "Source Scan", timestamp),
            ScannerRecord("Scanner Record Id", scan.Id, "Source Scan", timestamp),
            ScannerRecord("Original Scan Record Id", scan.OriginalScanRecordId, "Source Scan", timestamp),
            ScannerRecord("Resolution", resolution.ResolvedBy ?? resolution.ResolutionReason ?? "Fs6000ContainerLookup", "Source Scan", timestamp),
            ScannerRecord("Scanner Type", "FS6000", "Scanner Info", timestamp),
            ScannerRecord("Scan Time", scan.ScanTime, "Scanner Info", timestamp),
            ScannerRecord("Picture Number", scan.PicNumber, "Scanner Info", timestamp),
            ScannerRecord("Vessel Name", scan.VesselName, "Vessel Info", timestamp),
            ScannerRecord("Operator ID", scan.OperatorId, "Operator Info", timestamp),
            ScannerRecord("Scan Result", scan.ScanResult, "Scan Result", timestamp),
            ScannerRecord("Goods Description", scan.GoodsDescription, "Cargo Info", timestamp),
            ScannerRecord("Shipping Company", scan.ShippingCompany, "Shipping Info", timestamp),
            ScannerRecord("Consignee", scan.Consignee, "Party Info", timestamp),
            ScannerRecord("FYCO Present", scan.FycoPresent, "Security Info", timestamp),
            ScannerRecord("File Path", scan.FilePath, "File Info", timestamp),
            ScannerRecord("Has Image", scan.HasImage, "Image Info", timestamp),
            ScannerRecord("Image Count", scan.ImageCount, "Image Info", timestamp),
            ScannerRecord("Sync Status", scan.SyncStatus, "Status", timestamp)
        };
    }

    private static List<ScannerDataRecord> BuildEagleA25ScannerRecords(
        EagleA25Scan scan,
        ScanAssetResolution resolution)
    {
        var timestamp = scan.ScanDateUtc;
        var assets = scan.Assets?.ToList() ?? new List<EagleA25ScanAsset>();
        return new List<ScannerDataRecord>
        {
            ScannerRecord("Container Number", resolution.ContainerNumber ?? scan.CargoIdentifier ?? scan.Accession.ToString(CultureInfo.InvariantCulture), "Container Info", timestamp),
            ScannerRecord("Source Container Number(s)", resolution.SourceContainerNumbers ?? scan.Accession.ToString(CultureInfo.InvariantCulture), "Source Scan", timestamp),
            ScannerRecord("Source Scan Id", resolution.SourceScanId ?? scan.SourceScanId.ToString(CultureInfo.InvariantCulture), "Source Scan", timestamp),
            ScannerRecord("Scanner Record Id", scan.Id, "Source Scan", timestamp),
            ScannerRecord("Resolution", resolution.ResolvedBy ?? resolution.ResolutionReason ?? "EagleA25Lookup", "Source Scan", timestamp),
            ScannerRecord("Scanner Type", "EAGLE_A25", "Scanner Info", timestamp),
            ScannerRecord("Scan Time", scan.ScanDateUtc, "Scanner Info", timestamp),
            ScannerRecord("Accession", scan.Accession, "Scanner Info", timestamp),
            ScannerRecord("Scan Accession", scan.ScanAccession, "Scanner Info", timestamp),
            ScannerRecord("Cargo Identifier", scan.CargoIdentifier, "Cargo Info", timestamp),
            ScannerRecord("Air Waybill", scan.AirWaybill, "Cargo Info", timestamp),
            ScannerRecord("Flight Number", scan.FlightNumber, "Cargo Info", timestamp),
            ScannerRecord("Transit Type", scan.TransitType, "Cargo Info", timestamp),
            ScannerRecord("Weight", scan.Weight, "Cargo Info", timestamp),
            ScannerRecord("Company", scan.Company, "Cargo Info", timestamp),
            ScannerRecord("Quantity", scan.Quantity, "Cargo Info", timestamp),
            ScannerRecord("Origin From", scan.OriginFrom, "Route Info", timestamp),
            ScannerRecord("Origin To", scan.OriginTo, "Route Info", timestamp),
            ScannerRecord("XRay Done", scan.XRayDone, "Status", timestamp),
            ScannerRecord("Ready Inspect", scan.ReadyInspect, "Status", timestamp),
            ScannerRecord("Inspect Done", scan.InspectDone, "Status", timestamp),
            ScannerRecord("Inspect Suspicious", scan.InspectSuspicious, "Status", timestamp),
            ScannerRecord("Search Found", scan.SearchFound, "Status", timestamp),
            ScannerRecord("Asset Count", assets.Count, "Image Info", timestamp),
            ScannerRecord("XRay Asset Count", assets.Count(asset => asset.IsXray || string.Equals(asset.FileType, "XRAY", StringComparison.OrdinalIgnoreCase)), "Image Info", timestamp),
            ScannerRecord("Sync Status", scan.SyncStatus, "Status", timestamp)
        };
    }

    private static ScannerDataRecord ScannerRecord(
        string field,
        object? value,
        string category,
        DateTime? timestamp)
    {
        return new ScannerDataRecord
        {
            Field = field,
            Value = FormatScannerValue(value),
            Category = category,
            Timestamp = timestamp
        };
    }

    private static string FormatScannerValue(object? value)
    {
        return value switch
        {
            null => "N/A",
            string text => string.IsNullOrWhiteSpace(text) ? "N/A" : text,
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "N/A",
            _ => value.ToString() ?? "N/A"
        };
    }

    private static List<string> GetCandidateContainerNumbers(ScanAssetResolution resolution)
    {
        return new[]
            {
                resolution.SourceContainerNumbers,
                resolution.ContainerNumber,
                resolution.RequestedContainerNumber,
                resolution.NormalizedContainerNumber
            }
            .SelectMany(SplitContainerCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitContainerCandidates(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var token in value.Split(
                     new[] { ',', ';', '|', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(token))
                yield return token;
        }
    }

    private static string GetDisplayContainerNumber(ScanAssetResolution resolution)
        => GetCandidateContainerNumbers(resolution).FirstOrDefault()
            ?? resolution.ContainerNumber
            ?? resolution.SourceContainerNumbers
            ?? resolution.RequestedContainerNumber
            ?? string.Empty;

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
            var asset = await _resolver.ResolveAsync(
                new ScanAssetResolutionRequest
                {
                    ScanImageAssetId = scannerScanId
                },
                cancellationToken);

            if (asset.Found)
                return asset;

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
            || string.Equals(resolution.ScanImageAssetId?.ToString(), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.ScanImageAssetId?.ToString("N"), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.ScannerScanId?.ToString(), lookupId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolution.ScannerScanId?.ToString("N"), lookupId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEagleA25(string? scannerType)
        => string.Equals(scannerType, "EAGLE_A25", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scannerType, "EagleA25", StringComparison.OrdinalIgnoreCase);

    private static bool IsFs6000(string? scannerType)
        => string.Equals(scannerType, "FS6000", StringComparison.OrdinalIgnoreCase);

    private static bool IsAse(string? scannerType)
        => string.Equals(scannerType, "ASE", StringComparison.OrdinalIgnoreCase);

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
