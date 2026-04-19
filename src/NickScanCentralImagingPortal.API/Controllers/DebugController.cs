using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
#if DEBUG
    [Authorize(Policy = "AdminOnly")]
    [Route("api/[controller]")]
    [ApiController]
    public class DebugController : ControllerBase
#else
    // DebugController disabled in Production
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
#endif
    {
        private readonly ILogger<DebugController> _logger;
        private readonly ApplicationDbContext _context;

        public DebugController(ILogger<DebugController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

#if DEBUG
        [HttpGet("ase/scan/{containerNumber}")]
        public async Task<IActionResult> GetAseScan(string containerNumber)
        {
            try
            {
                var scan = await _context.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan == null)
                {
                    return NotFound($"No ASE scan found for container: {containerNumber}");
                }

                return Ok(new
                {
                    Id = scan.Id,
                    InspectionId = scan.InspectionId,
                    ScanTime = scan.ScanTime,
                    InspectionUuid = scan.InspectionUuid,
                    ContainerNumber = scan.ContainerNumber,
                    TruckPlate = scan.TruckPlate,
                    HasImageData = scan.ScanImage != null && scan.ScanImage.Length > 0,
                    ImageSize = scan.ScanImage?.Length ?? 0,
                    ImageDisplayName = scan.ImageDisplayName,
                    SyncedAt = scan.SyncedAt,
                    CreatedAt = scan.CreatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE scan for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("ase/scans/count")]
        public async Task<IActionResult> GetAseScansCount()
        {
            try
            {
                var totalCount = await _context.AseScans.CountAsync();
                var withImages = await _context.AseScans.Where(s => s.ScanImage != null).CountAsync();
                var withoutImages = totalCount - withImages;

                return Ok(new
                {
                    TotalScans = totalCount,
                    WithImages = withImages,
                    WithoutImages = withoutImages,
                    PercentageWithImages = totalCount > 0 ? (double)withImages / totalCount * 100 : 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE scans count");
                return StatusCode(500, "Internal server error");
            }
        }
#else
        // All debug endpoints disabled in Production
        [HttpGet("ase/scan/{containerNumber}")]
        public IActionResult GetAseScan(string containerNumber) => NotFound();

        [HttpGet("ase/scans/count")]
        public IActionResult GetAseScansCount() => NotFound();
#endif
    }
}
