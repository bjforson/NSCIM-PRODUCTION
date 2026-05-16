using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.EagleA25;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EagleA25Controller : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEagleA25SyncService _syncService;
        private readonly EagleA25Configuration _config;
        private readonly ILogger<EagleA25Controller> _logger;

        public EagleA25Controller(
            ApplicationDbContext context,
            IEagleA25SyncService syncService,
            IOptions<EagleA25Configuration> config,
            ILogger<EagleA25Controller> logger)
        {
            _context = context;
            _syncService = syncService;
            _config = config.Value;
            _logger = logger;
        }

        [Authorize(Policy = "ScannerOperator")]
        [HttpGet("scans")]
        public async Task<ActionResult<object>> GetScans(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? accession = null,
            [FromQuery] string? airWaybill = null,
            [FromQuery] string? cargoIdentifier = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _context.EagleA25Scans.AsNoTracking();

            if (startDate.HasValue)
            {
                query = query.Where(s => s.ScanDateUtc >= DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc));
            }

            if (endDate.HasValue)
            {
                query = query.Where(s => s.ScanDateUtc <= DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc));
            }

            if (!string.IsNullOrWhiteSpace(accession) && long.TryParse(accession, out var accessionValue))
            {
                query = query.Where(s => s.Accession == accessionValue);
            }

            if (!string.IsNullOrWhiteSpace(airWaybill))
            {
                query = query.Where(s => s.AirWaybill != null && s.AirWaybill.Contains(airWaybill));
            }

            if (!string.IsNullOrWhiteSpace(cargoIdentifier))
            {
                query = query.Where(s => s.CargoIdentifier != null && s.CargoIdentifier.Contains(cargoIdentifier));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var scans = await query
                .OrderByDescending(s => s.ScanDateUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.Accession,
                    s.ScanAccession,
                    s.ScanDateUtc,
                    s.CargoIdentifier,
                    s.AirWaybill,
                    s.FlightNumber,
                    s.TransitType,
                    s.Weight,
                    s.Company,
                    s.Quantity,
                    s.QuantityType,
                    s.OriginFrom,
                    s.OriginTo,
                    s.InspectDone,
                    s.InspectSuspicious,
                    AssetCount = s.Assets.Count
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                Data = scans,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }

        [Authorize(Policy = "ScannerOperator")]
        [HttpGet("scans/{id:guid}")]
        public async Task<ActionResult<object>> GetScan(Guid id, CancellationToken cancellationToken)
        {
            var scan = await _context.EagleA25Scans
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new
                {
                    s.Id,
                    s.SourceScanId,
                    s.SourceScanGuid,
                    s.SourceScanEntryId,
                    s.SourceManifestId,
                    s.SourceManifestGuid,
                    s.Accession,
                    s.ScanAccession,
                    s.CargoSystemId,
                    s.LocationId,
                    s.ScanDateUtc,
                    s.ScanDateLocal,
                    s.ManifestCreateDateUtc,
                    s.ManifestCreateDateLocal,
                    s.CargoIdentifier,
                    s.AirWaybill,
                    s.FlightNumber,
                    s.TransitType,
                    s.Weight,
                    s.Company,
                    s.Quantity,
                    s.QuantityType,
                    s.OriginFrom,
                    s.OriginTo,
                    s.Comments,
                    s.DataPath,
                    s.DataUrl,
                    s.XRayDone,
                    s.ReadyInspect,
                    s.InspectDone,
                    s.InspectSuspicious,
                    s.SearchFound,
                    s.SearchDone,
                    s.Archived,
                    s.SyncStatus,
                    s.SyncedAtUtc,
                    s.CreatedAtUtc,
                    s.UpdatedAtUtc,
                    Assets = s.Assets
                        .OrderBy(a => a.SourceExtFileTypeId)
                        .ThenBy(a => a.SourceExtFileId)
                        .Select(a => new
                        {
                            a.Id,
                            a.SourceExtFileId,
                            a.SourceExtFileGuid,
                            a.SourceExtFileTypeId,
                            a.FileType,
                            a.IsXray,
                            a.MimeType,
                            a.Description,
                            a.SourcePath,
                            a.ResolvedSourcePath,
                            a.SourceUrl,
                            a.LocalPath,
                            a.FileSizeBytes,
                            a.SourceCreateDateUtc,
                            a.SyncedAtUtc
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return scan == null ? NotFound() : Ok(scan);
        }

        [Authorize]
        [HttpGet("assets/{id:guid}/content")]
        public async Task<IActionResult> GetAssetContent(Guid id, CancellationToken cancellationToken)
        {
            var asset = await _context.EagleA25ScanAssets
                .AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new
                {
                    a.LocalPath,
                    a.FileType,
                    a.MimeType
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (asset == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(asset.LocalPath) || !System.IO.File.Exists(asset.LocalPath))
            {
                return NotFound(new { error = "Eagle A25 asset has not been copied to local storage" });
            }

            var root = Path.GetFullPath(_config.LocalAssetRoot);
            var path = Path.GetFullPath(asset.LocalPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Blocked Eagle A25 asset path outside local root: {Path}", path);
                return Forbid();
            }

            var contentType = string.IsNullOrWhiteSpace(asset.MimeType)
                ? "application/octet-stream"
                : asset.MimeType;
            var fileName = Path.GetFileName(path);

            return PhysicalFile(path, contentType, fileName, enableRangeProcessing: true);
        }

        [Authorize(Policy = "ScannerOperator")]
        [HttpGet("sync-status")]
        public async Task<ActionResult<object>> GetSyncStatus(CancellationToken cancellationToken)
        {
            try
            {
                var lastSync = await _context.EagleA25SyncLogs
                    .AsNoTracking()
                    .OrderByDescending(l => l.StartedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                var totalScans = await _context.EagleA25Scans.CountAsync(cancellationToken);
                var totalAssets = await _context.EagleA25ScanAssets.CountAsync(cancellationToken);
                var lastScan = await _context.EagleA25Scans
                    .AsNoTracking()
                    .OrderByDescending(s => s.ScanDateUtc)
                    .Select(s => new { s.Accession, s.ScanDateUtc, s.CargoIdentifier, s.AirWaybill })
                    .FirstOrDefaultAsync(cancellationToken);

                return Ok(new
                {
                    SchemaReady = true,
                    RequiredMigration = EagleA25DatabaseExceptionClassifier.ScannerTablesMigrationId,
                    TotalScans = totalScans,
                    TotalAssets = totalAssets,
                    LastScan = lastScan,
                    LastSync = lastSync
                });
            }
            catch (Exception ex) when (EagleA25DatabaseExceptionClassifier.IsPostgresUndefinedTable(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Eagle A25 sync status is unavailable because one or more scanner tables are missing. Apply migration {MigrationId}.",
                    EagleA25DatabaseExceptionClassifier.ScannerTablesMigrationId);

                return Ok(new
                {
                    SchemaReady = false,
                    RequiredMigration = EagleA25DatabaseExceptionClassifier.ScannerTablesMigrationId,
                    TotalScans = 0,
                    TotalAssets = 0,
                    LastScan = (object?)null,
                    LastSync = (object?)null,
                    Status = "MigrationPending",
                    Message = "Eagle A25 database tables are not available. Apply the Eagle A25 scanner tables migration."
                });
            }
        }

        [Authorize(Policy = "ScannerOperator")]
        [HttpPost("sync")]
        public async Task<ActionResult<object>> TriggerSync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _syncService.SyncAsync(cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering Eagle A25 sync");
                return StatusCode(500, new { error = "Eagle A25 sync failed", message = ex.Message });
            }
        }
    }
}
