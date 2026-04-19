using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/icums/transfer")]
    public class IcumDataTransferController : ControllerBase
    {
        private readonly IcumDownloadsDbContext _downloadsContext;
        private readonly IcumDbContext _icumContext;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<IcumDataTransferController> _logger;

        public IcumDataTransferController(
            IcumDownloadsDbContext downloadsContext,
            IcumDbContext icumContext,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            ILogger<IcumDataTransferController> logger)
        {
            _downloadsContext = downloadsContext;
            _icumContext = icumContext;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Get transfer service status
        /// </summary>
        [HttpGet("status")]
        public ActionResult<TransferServiceStatusDto> GetStatus()
        {
            try
            {
                var isEnabled = _configuration.GetValue<bool>("BackgroundServices:IcumDataTransferService:Enabled", true);
                var transferInterval = _configuration.GetValue<int>("BackgroundServices:IcumDataTransferService:TransferIntervalMinutes", 5);

                // Get last transfer time from most recently transferred document
                var lastTransferDoc = _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred")
                    .OrderByDescending(b => b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt)
                    .FirstOrDefault();

                DateTime? lastTransferTime = null;
                if (lastTransferDoc != null)
                {
                    lastTransferTime = lastTransferDoc.UpdatedAt > lastTransferDoc.CreatedAt
                        ? lastTransferDoc.UpdatedAt
                        : lastTransferDoc.CreatedAt;
                }

                var nextScheduled = lastTransferTime.HasValue
                    ? lastTransferTime.Value.AddMinutes(transferInterval)
                    : (DateTime?)null;

                return Ok(new TransferServiceStatusDto
                {
                    IsEnabled = isEnabled,
                    TransferIntervalMinutes = transferInterval,
                    LastTransferTime = lastTransferTime != default ? lastTransferTime : null,
                    NextScheduledTime = nextScheduled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transfer service status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get transfer statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<TransferStatisticsDto>> GetStatistics()
        {
            try
            {
                var now = DateTime.UtcNow;
                var last24h = now.AddHours(-24);
                var last7d = now.AddDays(-7);
                var last30d = now.AddDays(-30);

                // Pending transfers (Completed but not yet Transferred)
                var pendingCount = await _downloadsContext.BOEDocuments
                    .CountAsync(b => b.ProcessingStatus == "Completed");

                // Transferred counts
                var totalTransferred = await _downloadsContext.BOEDocuments
                    .CountAsync(b => b.ProcessingStatus == "Transferred");

                var transferredLast24h = await _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred" &&
                               (b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt) >= last24h)
                    .CountAsync();

                var transferredLast7d = await _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred" &&
                               (b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt) >= last7d)
                    .CountAsync();

                var transferredLast30d = await _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred" &&
                               (b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt) >= last30d)
                    .CountAsync();

                // Failed transfers
                var failedCount = await _downloadsContext.BOEDocuments
                    .CountAsync(b => b.ProcessingStatus == "TransferFailed");

                // Manifest items transferred
                var totalManifestItemsTransferred = await _downloadsContext.ManifestItems
                    .CountAsync(m => m.ProcessingStatus == "Transferred");

                // Get last transfer time
                var lastTransferDoc = await _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred")
                    .OrderByDescending(b => b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt)
                    .FirstOrDefaultAsync();

                DateTime? lastTransferTime = null;
                if (lastTransferDoc != null)
                {
                    lastTransferTime = lastTransferDoc.UpdatedAt > lastTransferDoc.CreatedAt
                        ? lastTransferDoc.UpdatedAt
                        : lastTransferDoc.CreatedAt;
                }

                // Calculate transfer rate (transfers per hour in last 24h)
                var transferRate = transferredLast24h > 0 && lastTransferTime.HasValue
                    ? Math.Round(transferredLast24h / 24.0, 2)
                    : 0.0;

                return Ok(new TransferStatisticsDto
                {
                    PendingTransfers = pendingCount,
                    TotalTransferred = totalTransferred,
                    TransferredLast24h = transferredLast24h,
                    TransferredLast7d = transferredLast7d,
                    TransferredLast30d = transferredLast30d,
                    FailedTransfers = failedCount,
                    TotalManifestItemsTransferred = totalManifestItemsTransferred,
                    LastTransferTime = lastTransferTime != default ? lastTransferTime : null,
                    TransferRatePerHour = transferRate
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transfer statistics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get transfer history
        /// </summary>
        [HttpGet("history")]
        public async Task<ActionResult<List<TransferHistoryDto>>> GetHistory(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var query = _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Transferred" || b.ProcessingStatus == "TransferFailed")
                    .AsQueryable();

                if (fromDate.HasValue)
                {
                    query = query.Where(b => (b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt) >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(b => (b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt) <= toDate.Value);
                }

                var totalCount = await query.CountAsync();

                var history = await query
                    .OrderByDescending(b => b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(b => new TransferHistoryDto
                    {
                        ContainerNumber = b.ContainerNumber,
                        DeclarationNumber = b.DeclarationNumber ?? string.Empty,
                        Status = b.ProcessingStatus,
                        TransferredAt = b.UpdatedAt > b.CreatedAt ? b.UpdatedAt : b.CreatedAt,
                        ErrorMessage = b.ProcessingStatus == "TransferFailed" ? b.ErrorMessage : null,
                        ClearanceType = b.ClearanceType ?? string.Empty,
                        IsConsolidated = b.IsConsolidated
                    })
                    .ToListAsync();

                Response.Headers["X-Total-Count"] = totalCount.ToString();
                Response.Headers["X-Page"] = page.ToString();
                Response.Headers["X-Page-Size"] = pageSize.ToString();

                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transfer history");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get pending transfers (containers ready to be transferred)
        /// </summary>
        [HttpGet("pending")]
        public async Task<ActionResult<List<PendingTransferDto>>> GetPending(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var totalCount = await _downloadsContext.BOEDocuments
                    .CountAsync(b => b.ProcessingStatus == "Completed");

                var pending = await _downloadsContext.BOEDocuments
                    .Where(b => b.ProcessingStatus == "Completed")
                    .OrderByDescending(b => b.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(b => new PendingTransferDto
                    {
                        ContainerNumber = b.ContainerNumber,
                        DeclarationNumber = b.DeclarationNumber ?? string.Empty,
                        CompletedAt = b.CreatedAt,
                        ClearanceType = b.ClearanceType ?? string.Empty,
                        IsConsolidated = b.IsConsolidated,
                        ManifestItemCount = _downloadsContext.ManifestItems
                            .Count(m => m.BOEDocumentId == b.Id)
                    })
                    .ToListAsync();

                Response.Headers["X-Total-Count"] = totalCount.ToString();
                Response.Headers["X-Page"] = page.ToString();
                Response.Headers["X-Page-Size"] = pageSize.ToString();

                return Ok(pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending transfers");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Manually trigger a transfer (for testing/admin purposes)
        /// </summary>
        [HttpPost("trigger")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<TransferTriggerResultDto>> TriggerTransfer()
        {
            try
            {
                // Get the transfer service and trigger it manually
                var transferService = _serviceProvider.GetServices<IHostedService>()
                    .OfType<NickScanCentralImagingPortal.Services.IcumApi.IcumDataTransferService>()
                    .FirstOrDefault();

                if (transferService == null)
                {
                    return BadRequest(new { error = "Transfer service not found" });
                }

                // Note: This is a simplified trigger. In a production system, you'd want
                // a more sophisticated mechanism to trigger the service on demand.
                // For now, we'll just return the current pending count and let the service
                // pick it up on its next cycle.

                var pendingCount = await _downloadsContext.BOEDocuments
                    .CountAsync(b => b.ProcessingStatus == "Completed");

                _logger.LogInformation("Manual transfer trigger requested. {PendingCount} documents pending transfer.", pendingCount);

                return Ok(new TransferTriggerResultDto
                {
                    Success = true,
                    Message = $"Transfer service will process {pendingCount} pending documents on its next cycle.",
                    PendingCount = pendingCount,
                    TriggeredAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering manual transfer");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    #region DTOs

    public class TransferServiceStatusDto
    {
        public bool IsEnabled { get; set; }
        public int TransferIntervalMinutes { get; set; }
        public DateTime? LastTransferTime { get; set; }
        public DateTime? NextScheduledTime { get; set; }
    }

    public class TransferStatisticsDto
    {
        public int PendingTransfers { get; set; }
        public int TotalTransferred { get; set; }
        public int TransferredLast24h { get; set; }
        public int TransferredLast7d { get; set; }
        public int TransferredLast30d { get; set; }
        public int FailedTransfers { get; set; }
        public int TotalManifestItemsTransferred { get; set; }
        public DateTime? LastTransferTime { get; set; }
        public double TransferRatePerHour { get; set; }
    }

    public class TransferHistoryDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime TransferredAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string ClearanceType { get; set; } = string.Empty;
        public bool IsConsolidated { get; set; }
    }

    public class PendingTransferDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public DateTime CompletedAt { get; set; }
        public string ClearanceType { get; set; } = string.Empty;
        public bool IsConsolidated { get; set; }
        public int ManifestItemCount { get; set; }
    }

    public class TransferTriggerResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int PendingCount { get; set; }
        public DateTime TriggeredAt { get; set; }
    }

    #endregion
}

