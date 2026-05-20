using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "ScannerOperator")]
    [ApiController]
    [Route("api/[controller]")]
    public class HeimannSmithController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HeimannSmithController> _logger;

        public HeimannSmithController(
            ApplicationDbContext context,
            ILogger<HeimannSmithController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("scans")]
        public async Task<ActionResult<HeimannSmithScanResponse>> GetScans(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? containerNumber = null,
            [FromQuery] string? processingStatus = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 200);

                var queryStart = startDate.HasValue
                    ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
                    : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);
                var queryEnd = endDate.HasValue
                    ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc)
                    : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);

                var query = _context.HeimannSmithScannerData
                    .Where(s => s.ScanDateTime >= queryStart && s.ScanDateTime < queryEnd)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(containerNumber))
                {
                    query = query.Where(s => s.ContainerId.Contains(containerNumber));
                }

                if (!string.IsNullOrWhiteSpace(processingStatus))
                {
                    query = query.Where(s => s.ProcessingStatus == processingStatus);
                }

                var totalCount = await query.CountAsync();
                var scans = await query
                    .OrderByDescending(s => s.ScanDateTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new HeimannSmithScanDto
                    {
                        Id = s.Id,
                        ContainerId = s.ContainerId,
                        ScannerId = s.ScannerId,
                        ScanDateTime = s.ScanDateTime,
                        ImagePath = s.ImagePath,
                        ProcessingStatus = s.ProcessingStatus,
                        CreatedAt = s.CreatedAt,
                        ProcessedAt = s.ProcessedAt
                    })
                    .ToListAsync();

                return Ok(new HeimannSmithScanResponse
                {
                    Data = scans,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Heimann Smith scans");
                return StatusCode(500, new { error = "Failed to load Heimann Smith scans" });
            }
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<HeimannSmithStatisticsDto>> GetStatistics()
        {
            try
            {
                var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
                var todayEnd = todayStart.AddDays(1);

                var lastScan = await _context.HeimannSmithScannerData
                    .OrderByDescending(s => s.ScanDateTime)
                    .Select(s => new HeimannSmithLastScanDto
                    {
                        Id = s.Id,
                        ContainerId = s.ContainerId,
                        ScannerId = s.ScannerId,
                        ScanDateTime = s.ScanDateTime,
                        ProcessingStatus = s.ProcessingStatus
                    })
                    .FirstOrDefaultAsync();

                return Ok(new HeimannSmithStatisticsDto
                {
                    TotalScans = await _context.HeimannSmithScannerData.CountAsync(),
                    TodayScans = await _context.HeimannSmithScannerData
                        .CountAsync(s => s.ScanDateTime >= todayStart && s.ScanDateTime < todayEnd),
                    CompletedScans = await _context.HeimannSmithScannerData
                        .CountAsync(s => s.ProcessingStatus == "Completed"),
                    PendingScans = await _context.HeimannSmithScannerData
                        .CountAsync(s => s.ProcessingStatus == "Pending"),
                    FailedScans = await _context.HeimannSmithScannerData
                        .CountAsync(s => s.ProcessingStatus == "Failed"),
                    LastScan = lastScan
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Heimann Smith statistics");
                return StatusCode(500, new { error = "Failed to load Heimann Smith statistics" });
            }
        }
    }

    public class HeimannSmithScanDto
    {
        public int Id { get; set; }
        public string ContainerId { get; set; } = string.Empty;
        public string ScannerId { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public string? ImagePath { get; set; }
        public string ProcessingStatus { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    public class HeimannSmithScanResponse
    {
        public List<HeimannSmithScanDto> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class HeimannSmithStatisticsDto
    {
        public int TotalScans { get; set; }
        public int TodayScans { get; set; }
        public int CompletedScans { get; set; }
        public int PendingScans { get; set; }
        public int FailedScans { get; set; }
        public HeimannSmithLastScanDto? LastScan { get; set; }
    }

    public class HeimannSmithLastScanDto
    {
        public int Id { get; set; }
        public string ContainerId { get; set; } = string.Empty;
        public string ScannerId { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public string ProcessingStatus { get; set; } = "Pending";
    }
}
