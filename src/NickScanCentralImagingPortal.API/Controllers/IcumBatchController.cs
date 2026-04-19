using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.IcumApi;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/icums/batch")]
    public class IcumBatchController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IIcumDownloadsRepository _downloadsRepository;
        private readonly IcumDownloadsDbContext _downloadsContext;
        private readonly ILogger<IcumBatchController> _logger;
        private readonly ISettingsService? _settingsService;
        private readonly ApplicationDbContext? _appDbContext;
        private readonly IcumPipelineOrchestratorService? _orchestratorService;

        public IcumBatchController(
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            IIcumDownloadsRepository downloadsRepository,
            IcumDownloadsDbContext downloadsContext,
            ILogger<IcumBatchController> logger,
            IEnumerable<IHostedService> hostedServices,
            ISettingsService? settingsService = null,
            ApplicationDbContext? appDbContext = null)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _downloadsRepository = downloadsRepository;
            _downloadsContext = downloadsContext;
            _logger = logger;
            _settingsService = settingsService;
            _appDbContext = appDbContext;

            // Find the IcumPipelineOrchestratorService from hosted services
            _orchestratorService = hostedServices.OfType<IcumPipelineOrchestratorService>().FirstOrDefault();
        }

        /// <summary>
        /// Get batch download service status
        /// </summary>
        [HttpGet("status")]
        public ActionResult<BatchServiceStatusDto> GetStatus()
        {
            try
            {
                var isEnabled = _configuration.GetValue<bool>("BackgroundServices:IcumBackgroundService:Enabled", false);
                var batchInterval = _configuration.GetValue<int>("BackgroundServices:IcumBackgroundService:BatchIntervalMinutes", 30);

                // Get last fetch time from database (would need to track this)
                // For now, get from most recent downloaded file
                var lastFetchTime = _downloadsContext.DownloadedFiles
                    .OrderByDescending(f => f.DownloadDate)
                    .Select(f => f.DownloadDate)
                    .FirstOrDefault();

                var nextScheduled = lastFetchTime != default
                    ? lastFetchTime.AddMinutes(batchInterval)
                    : (DateTime?)null;

                return Ok(new BatchServiceStatusDto
                {
                    IsEnabled = isEnabled,
                    BatchIntervalMinutes = batchInterval,
                    LastFetchTime = lastFetchTime != default ? lastFetchTime : null,
                    NextScheduledTime = nextScheduled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch service status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get batch download statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<BatchDownloadStatsDto>> GetStatistics()
        {
            try
            {
                var now = DateTime.UtcNow;
                var last24h = now.AddHours(-24);

                var totalFiles = await _downloadsContext.DownloadedFiles.CountAsync();
                var filesLast24h = await _downloadsContext.DownloadedFiles
                    .CountAsync(f => f.DownloadDate >= last24h);

                var totalContainers = await _downloadsContext.BOEDocuments
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                var containersLast24h = await _downloadsContext.BOEDocuments
                    .Where(b => b.CreatedAt >= last24h)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                // Consolidation breakdown
                var consolidatedContainers = await _downloadsContext.BOEDocuments
                    .Where(b => b.IsConsolidated)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                var nonConsolidatedContainers = await _downloadsContext.BOEDocuments
                    .Where(b => !b.IsConsolidated)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                var pendingFiles = await _downloadsContext.DownloadedFiles
                    .CountAsync(f => f.ProcessingStatus == "Pending");

                var failedFiles = await _downloadsContext.DownloadedFiles
                    .CountAsync(f => f.ProcessingStatus == "Failed");

                var archivedFiles = await _downloadsContext.DownloadedFiles
                    .CountAsync(f => f.ProcessingStatus == "Archived");

                return Ok(new BatchDownloadStatsDto
                {
                    TotalFiles = totalFiles,
                    FilesLast24h = filesLast24h,
                    TotalContainers = totalContainers,
                    ContainersLast24h = containersLast24h,
                    ConsolidatedContainers = consolidatedContainers,
                    NonConsolidatedContainers = nonConsolidatedContainers,
                    PendingFiles = pendingFiles,
                    FailedFiles = failedFiles,
                    ArchivedFiles = archivedFiles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch statistics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get downloaded files with filtering
        /// </summary>
        [HttpGet("files")]
        public async Task<ActionResult<List<DownloadedFileDto>>> GetFiles(
            [FromQuery] string? status = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var query = _downloadsContext.DownloadedFiles.AsQueryable();

                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(f => f.ProcessingStatus == status);
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(f => f.DownloadDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(f => f.DownloadDate <= toDate.Value);
                }

                // Materialize IDs first so we can compute accurate record counts from BOEDocuments
                var fileIds = await query
                    .OrderByDescending(f => f.DownloadDate)
                    .Take(1000) // Limit to prevent performance issues
                    .Select(f => f.Id)
                    .ToListAsync();

                // Lookup actual ingested record counts per file (BOE documents)
                // ✅ SQL Server 2014 FIX: Use FromSqlRaw to avoid CTE generation from Contains()
                // Load data first, then group in memory to prevent EF Core from generating CTE
                var recordCounts = new Dictionary<int, int>();

                if (fileIds.Any())
                {
                    var fileIdPlaceholders = string.Join(",", fileIds);
#pragma warning disable EF1002
                    var boeDocuments = await _downloadsContext.BOEDocuments
                        .FromSqlRaw($"SELECT * FROM boedocuments WHERE downloadedfileid IN ({fileIdPlaceholders})")
                        .AsNoTracking()
                        .ToListAsync();
#pragma warning restore EF1002

                    // Project in memory after loading to avoid CTE generation
                    recordCounts = boeDocuments
                        .GroupBy(b => b.DownloadedFileId)
                        .ToDictionary(g => g.Key, g => g.Count());
                }

                // Now project full DTOs and reconcile RecordCount with BOE data
                // ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
                List<DownloadedFileDto> files;

                if (fileIds.Any())
                {
                    var fileIdPlaceholders = string.Join(",", fileIds);
#pragma warning disable EF1002
                    var downloadedFiles = await _downloadsContext.DownloadedFiles
                        .FromSqlRaw($"SELECT * FROM downloadedfiles WHERE id IN ({fileIdPlaceholders})")
                        .AsNoTracking()
                        .ToListAsync();
#pragma warning restore EF1002

                    // Order and project in memory to avoid CTE generation
                    files = downloadedFiles
                        .OrderByDescending(f => f.DownloadDate)
                        .Select(f => new DownloadedFileDto
                        {
                            Id = f.Id,
                            FileName = f.FileName,
                            FilePath = f.FilePath,
                            DownloadDate = f.DownloadDate,
                            ProcessingStatus = f.ProcessingStatus,
                            RecordCount = f.RecordCount ?? 0,
                            FileSizeBytes = f.FileSize,
                            ErrorMessage = f.ErrorMessage,
                            AverageAccuracyPercent = f.AverageAccuracyPercent,
                            VerifiedDocumentCount = f.VerifiedDocumentCount,
                            PerfectDocumentCount = f.PerfectDocumentCount
                        })
                        .ToList();
                }
                else
                {
                    files = new List<DownloadedFileDto>();
                }

                // Reconcile: for completed files with zero/unknown RecordCount, fall back to BOE document count
                foreach (var file in files)
                {
                    if ((file.ProcessingStatus == "Completed" || file.ProcessingStatus == "ManuallyProcessed")
                        && file.RecordCount == 0
                        && recordCounts.TryGetValue(file.Id, out var countFromBoe))
                    {
                        file.RecordCount = countFromBoe;
                    }
                }

                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting downloaded files");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get downloaded containers with filtering
        /// </summary>
        [HttpGet("containers")]
        public async Task<ActionResult<List<DownloadedContainerDto>>> GetContainers(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var query = _downloadsContext.BOEDocuments.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(b =>
                        b.ContainerNumber.Contains(search) ||
                        (b.DeclarationNumber != null && b.DeclarationNumber.Contains(search)) ||
                        (b.BlNumber != null && b.BlNumber.Contains(search)));
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt <= toDate.Value);
                }

                var containers = await query
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(1000) // Limit to prevent performance issues
                    .Select(b => new DownloadedContainerDto
                    {
                        ContainerNumber = b.ContainerNumber,
                        DeclarationNumber = b.DeclarationNumber,
                        BlNumber = b.BlNumber,
                        DownloadDate = b.CreatedAt,
                        ClearanceType = b.ClearanceType
                    })
                    .Distinct()
                    .ToListAsync();

                return Ok(containers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting downloaded containers");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get activity logs
        /// </summary>
        [HttpGet("logs")]
        public async Task<ActionResult<List<ActivityLogDto>>> GetLogs(
            [FromQuery] string? level = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var logs = new List<ActivityLogDto>();

                // Query ApplicationLogs table if available
                if (_appDbContext != null)
                {
                    var connectionString = _configuration.GetConnectionString("NS_CIS_Connection");
                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        using var connection = new NpgsqlConnection(connectionString);
                        await connection.OpenAsync();

                        // Build WHERE clause
                        var whereClause = "WHERE serviceid LIKE '%ICUMS%' OR serviceid LIKE '%BATCH%' OR serviceid LIKE '%ICUM%'";
                        var parameters = new List<NpgsqlParameter>();

                        if (!string.IsNullOrEmpty(level))
                        {
                            whereClause += " AND level = @level";
                            parameters.Add(new NpgsqlParameter("@level", level));
                        }

                        if (fromDate.HasValue)
                        {
                            whereClause += " AND timestamp >= @fromDate";
                            parameters.Add(new NpgsqlParameter("@fromDate", fromDate.Value));
                        }
                        else
                        {
                            // Default to last 24 hours if no date specified
                            whereClause += " AND timestamp >= @fromDate";
                            parameters.Add(new NpgsqlParameter("@fromDate", DateTime.UtcNow.AddHours(-24)));
                        }

                        if (toDate.HasValue)
                        {
                            whereClause += " AND timestamp <= @toDate";
                            parameters.Add(new NpgsqlParameter("@toDate", toDate.Value));
                        }

                        var query = $@"
                            SELECT id, timestamp, level, serviceid, message, exception, properties
                            FROM applicationlogs
                            {whereClause}
                            ORDER BY timestamp DESC
                            LIMIT 1000";

                        using var command = new NpgsqlCommand(query, connection);
                        command.Parameters.AddRange(parameters.ToArray());

                        using var reader = await command.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            logs.Add(new ActivityLogDto
                            {
                                Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                                Level = reader.IsDBNull(reader.GetOrdinal("level")) ? "Information" : reader.GetString(reader.GetOrdinal("level")),
                                Message = reader.IsDBNull(reader.GetOrdinal("message")) ? "" : reader.GetString(reader.GetOrdinal("message")),
                                Details = reader.IsDBNull(reader.GetOrdinal("exception"))
                                    ? (reader.IsDBNull(reader.GetOrdinal("properties")) ? null : reader.GetString(reader.GetOrdinal("properties")))
                                    : reader.GetString(reader.GetOrdinal("exception"))
                            });
                        }
                    }
                }

                // If no logs found from database, return empty list
                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting activity logs");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Toggle batch service enabled/disabled
        /// </summary>
        [HttpPost("toggle")]
        public async Task<ActionResult> ToggleService([FromBody] ToggleServiceRequest request)
        {
            try
            {
                _logger.LogInformation("Batch service toggle requested: {Enabled}", request.Enabled);

                if (_settingsService == null)
                {
                    _logger.LogWarning("SettingsService not available, cannot update service toggle");
                    return StatusCode(503, new { error = "Settings service not available" });
                }

                // Update the setting in database
                var updateDto = new Core.DTOs.Settings.UpdateSettingDto
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumBackgroundService.Enabled",
                    SettingValue = request.Enabled.ToString().ToLower(),
                    ChangedBy = User.Identity?.Name ?? "System",
                    Reason = $"Service toggle via API - {(request.Enabled ? "Enabled" : "Disabled")}"
                };

                try
                {
                    await _settingsService.UpdateSettingAsync(updateDto, HttpContext.Connection.RemoteIpAddress?.ToString());
                    _logger.LogInformation("✅ Batch service toggle updated successfully: {Enabled}", request.Enabled);

                    return Ok(new
                    {
                        success = true,
                        enabled = request.Enabled,
                        message = "Service toggle updated. Note: Service restart may be required for changes to take effect."
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating service toggle setting");
                    return StatusCode(500, new { error = $"Failed to update setting: {ex.Message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling service");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Update batch service configuration
        /// </summary>
        [HttpPost("config")]
        public async Task<ActionResult> UpdateConfig([FromBody] UpdateConfigRequest request)
        {
            try
            {
                _logger.LogInformation("Batch service config update requested: {Interval} minutes", request.BatchIntervalMinutes);

                if (_settingsService == null)
                {
                    _logger.LogWarning("SettingsService not available, cannot update configuration");
                    return StatusCode(503, new { error = "Settings service not available" });
                }

                // Validate interval
                if (request.BatchIntervalMinutes < 1 || request.BatchIntervalMinutes > 1440)
                {
                    return BadRequest(new { error = "Batch interval must be between 1 and 1440 minutes (24 hours)" });
                }

                // Update the setting in database
                var updateDto = new Core.DTOs.Settings.UpdateSettingDto
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumBackgroundService.BatchIntervalMinutes",
                    SettingValue = request.BatchIntervalMinutes.ToString(),
                    ChangedBy = User.Identity?.Name ?? "System",
                    Reason = $"Batch interval updated via API to {request.BatchIntervalMinutes} minutes"
                };

                try
                {
                    await _settingsService.UpdateSettingAsync(updateDto, HttpContext.Connection.RemoteIpAddress?.ToString());
                    _logger.LogInformation("✅ Batch service configuration updated successfully: {Interval} minutes", request.BatchIntervalMinutes);

                    return Ok(new
                    {
                        success = true,
                        batchIntervalMinutes = request.BatchIntervalMinutes,
                        message = "Configuration updated. Note: Service restart may be required for changes to take effect."
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating batch interval setting");
                    return StatusCode(500, new { error = $"Failed to update setting: {ex.Message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating configuration");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Trigger manual batch download
        /// </summary>
        [HttpPost("trigger")]
        public async Task<ActionResult> TriggerManualBatch()
        {
            try
            {
                _logger.LogInformation("[BATCH-TRIGGER] Manual batch download triggered via API");

                if (_orchestratorService == null)
                {
                    _logger.LogError("[BATCH-TRIGGER] IcumPipelineOrchestratorService not found");
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Batch download service not available. Ensure IcumPipelineOrchestratorService is running."
                    });
                }

                if (_orchestratorService is IcumPipelineOrchestratorService orchestrator)
                {
                    var success = await orchestrator.TriggerBatchDownloadAsync();

                    if (success)
                    {
                        _logger.LogInformation("[BATCH-TRIGGER] ✅ Batch download workflow started successfully");
                        return Ok(new
                        {
                            success = true,
                            message = "Batch download triggered successfully. Check logs for progress."
                        });
                    }
                    else
                    {
                        _logger.LogWarning("[BATCH-TRIGGER] ⚠️ Batch download workflow completed with errors");
                        return StatusCode(500, new
                        {
                            success = false,
                            message = "Batch download triggered but encountered errors. Check logs for details."
                        });
                    }
                }
                else
                {
                    _logger.LogError("[BATCH-TRIGGER] Service is not IcumPipelineOrchestratorService");
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Invalid service type"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BATCH-TRIGGER] Error triggering manual batch download");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Retry processing a failed file
        /// </summary>
        [HttpPost("files/{id}/retry")]
        public async Task<ActionResult> RetryFile(int id)
        {
            try
            {
                var file = await _downloadsContext.DownloadedFiles.AsTracking().FirstOrDefaultAsync(f => f.Id == id);
                if (file == null)
                {
                    return NotFound(new { error = "File not found" });
                }

                file.ProcessingStatus = "Pending";
                file.ErrorMessage = null;
                await _downloadsContext.SaveChangesAsync();

                _logger.LogInformation("File {FileName} marked for retry", file.FileName);

                return Ok(new { success = true, message = "File marked for retry" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying file {Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Delete a downloaded file
        /// </summary>
        [HttpDelete("files/{id}")]
        public async Task<ActionResult> DeleteFile(int id)
        {
            try
            {
                var file = await _downloadsContext.DownloadedFiles.FindAsync(id);
                if (file == null)
                {
                    return NotFound(new { error = "File not found" });
                }

                _downloadsContext.DownloadedFiles.Remove(file);
                await _downloadsContext.SaveChangesAsync();

                _logger.LogInformation("File {FileName} deleted", file.FileName);

                return Ok(new { success = true, message = "File deleted" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file {Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get all BOE records contained in a downloaded file
        /// </summary>
        [HttpGet("files/{id}/records")]
        public async Task<ActionResult<List<BOERecordDto>>> GetFileRecords(int id)
        {
            try
            {
                var file = await _downloadsContext.DownloadedFiles.FindAsync(id);
                if (file == null)
                {
                    return NotFound(new { error = "File not found" });
                }

                var records = await _downloadsContext.BOEDocuments
                    .Where(b => b.DownloadedFileId == id)
                    .OrderBy(b => b.DocumentIndex)
                    .ToListAsync();

                var recordDtos = records.Select(b => new BOERecordDto
                {
                    Id = b.Id,
                    DocumentIndex = b.DocumentIndex,
                    ContainerNumber = b.ContainerNumber ?? "N/A",
                    DeclarationNumber = b.DeclarationNumber ?? string.Empty,
                    BlNumber = b.BlNumber ?? string.Empty,
                    HouseBl = b.HouseBl ?? string.Empty,
                    ClearanceType = b.ClearanceType ?? string.Empty,
                    CrmsLevel = b.CrmsLevel ?? string.Empty,
                    ImpName = b.ImpName ?? string.Empty,
                    ExpName = b.ExpName ?? string.Empty,
                    ImpAddress = b.ImpAddress ?? string.Empty,
                    ExpAddress = b.ExpAddress ?? string.Empty,
                    ImpExpName = b.ImpExpName ?? string.Empty,
                    DeclarantName = b.DeclarantName ?? string.Empty,
                    DeclarantAddress = b.DeclarantAddress ?? string.Empty,
                    DeclarationDate = b.DeclarationDate ?? string.Empty,
                    DeclarationVersion = b.DeclarationVersion,
                    RegimeCode = b.RegimeCode ?? string.Empty,
                    TotalDutyPaid = b.TotalDutyPaid,
                    ContainerISO = b.ContainerISO ?? string.Empty,
                    ContainerSize = b.ContainerSize ?? string.Empty,
                    ContainerDescription = b.ContainerDescription ?? string.Empty,
                    ContainerQuantity = b.ContainerQuantity ?? 0,
                    ContainerWeight = b.ContainerWeight,
                    SealNumber = b.SealNumber ?? string.Empty,
                    TruckPlateNumber = b.TruckPlateNumber ?? string.Empty,
                    DriverName = b.DriverName ?? string.Empty,
                    DriverLicense = b.DriverLicense ?? string.Empty,
                    ContainerStatus = b.ContainerStatus ?? string.Empty,
                    ContainerRemarks = b.ContainerRemarks ?? string.Empty,
                    NoOfContainers = b.NoOfContainers,
                    ConsigneeName = b.ConsigneeName ?? string.Empty,
                    ConsigneeAddress = b.ConsigneeAddress ?? string.Empty,
                    ShipperName = b.ShipperName ?? string.Empty,
                    ShipperAddress = b.ShipperAddress ?? string.Empty,
                    CountryOfOrigin = b.CountryOfOrigin ?? string.Empty,
                    GoodsDescription = b.GoodsDescription ?? string.Empty,
                    RotationNumber = b.RotationNumber ?? string.Empty,
                    DeliveryPlace = b.DeliveryPlace ?? string.Empty,
                    MarksNumbers = b.MarksNumbers ?? string.Empty,
                    CompOffRemarks = b.CompOffRemarks ?? string.Empty,
                    CcvrIntelRemarks = b.CcvrIntelRemarks ?? string.Empty,
                    IsConsolidated = b.IsConsolidated,
                    CreatedAt = b.CreatedAt,
                    UnmappedFields = ExtractUnmappedFields(b),
                    UnmappedFieldsCount = b.UnmappedFieldsCount,
                    UnmappedFieldsOverflow = b.UnmappedFieldsOverflow,
                    RawJsonData = b.RawJsonData
                }).ToList();

                _logger.LogInformation("Returning {Count} records for file {FileId}", recordDtos.Count, id);

                return Ok(recordDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting records for file {Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("files/{id}/verification")]
        public async Task<ActionResult<FileVerificationDto>> GetFileVerification(int id)
        {
            try
            {
                var file = await _downloadsContext.DownloadedFiles.FindAsync(id);
                if (file == null)
                    return NotFound(new { error = "File not found" });

                var dto = new FileVerificationDto
                {
                    VerifiedDocumentCount = file.VerifiedDocumentCount ?? 0,
                    PerfectDocumentCount = file.PerfectDocumentCount ?? 0,
                    PartialDocumentCount = file.PartialDocumentCount ?? 0,
                    AverageAccuracyPercent = file.AverageAccuracyPercent,
                    LowestAccuracyPercent = file.LowestAccuracyPercent,
                    LowestAccuracyContainer = file.LowestAccuracyContainer ?? string.Empty,
                };

                if (!string.IsNullOrEmpty(file.VerificationDetails))
                {
                    try
                    {
                        dto.Documents = System.Text.Json.JsonSerializer.Deserialize<List<DocVerificationDetailDto>>(file.VerificationDetails) ?? new();
                    }
                    catch { }
                }

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting verification for file {Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Extracts unmapped fields from BOEDocument into DTOs
        /// </summary>
        private List<UnmappedFieldDto> ExtractUnmappedFields(Core.Models.BOEDocument document)
        {
            var unmappedFields = new List<UnmappedFieldDto>();

            // Extract from structured columns (fields 1-20)
            for (int i = 1; i <= 20; i++)
            {
                var labelProp = typeof(Core.Models.BOEDocument).GetProperty($"UnmappedField{i}Label");
                var valueProp = typeof(Core.Models.BOEDocument).GetProperty($"UnmappedField{i}Value");

                if (labelProp == null || valueProp == null) continue;

                var label = labelProp.GetValue(document) as string;
                var value = valueProp.GetValue(document) as string;

                if (string.IsNullOrEmpty(label)) continue;

                // Parse label format: "Header:NewField"
                var parts = label.Split(':', 2);
                var section = parts.Length > 0 ? parts[0] : "Unknown";
                var fieldName = parts.Length > 1 ? parts[1] : label;

                // Check if value was truncated (ends with "...")
                var isTruncated = value != null && value.EndsWith("...") && value.Length >= 3997;

                unmappedFields.Add(new UnmappedFieldDto
                {
                    Label = label,
                    Value = value,
                    Section = section,
                    FieldName = fieldName,
                    IsTruncated = isTruncated
                });
            }

            return unmappedFields;
        }
    }

    // DTOs
    public class BatchServiceStatusDto
    {
        public bool IsEnabled { get; set; }
        public int BatchIntervalMinutes { get; set; }
        public DateTime? LastFetchTime { get; set; }
        public DateTime? NextScheduledTime { get; set; }
    }

    public class BatchDownloadStatsDto
    {
        public int TotalFiles { get; set; }
        public int FilesLast24h { get; set; }
        public int TotalContainers { get; set; }
        public int ContainersLast24h { get; set; }
        public int ConsolidatedContainers { get; set; }
        public int NonConsolidatedContainers { get; set; }
        public int PendingFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ArchivedFiles { get; set; }
    }

    public class DownloadedFileDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime DownloadDate { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public long FileSizeBytes { get; set; }
        public string? ErrorMessage { get; set; }
        public double? AverageAccuracyPercent { get; set; }
        public int? VerifiedDocumentCount { get; set; }
        public int? PerfectDocumentCount { get; set; }
    }

    public class DownloadedContainerDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string? DeclarationNumber { get; set; }
        public string? BlNumber { get; set; }
        public DateTime DownloadDate { get; set; }
        public string? ClearanceType { get; set; }
    }

    public class ActivityLogDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public class ToggleServiceRequest
    {
        public bool Enabled { get; set; }
    }

    public class UpdateConfigRequest
    {
        public int BatchIntervalMinutes { get; set; }
    }

    public class BOERecordDto
    {
        public int Id { get; set; }
        public int DocumentIndex { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public string BlNumber { get; set; } = string.Empty;
        public string HouseBl { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public string CrmsLevel { get; set; } = string.Empty;
        public string ImpName { get; set; } = string.Empty;
        public string ExpName { get; set; } = string.Empty;
        public string ImpAddress { get; set; } = string.Empty;
        public string ExpAddress { get; set; } = string.Empty;
        public string ImpExpName { get; set; } = string.Empty;
        public string DeclarantName { get; set; } = string.Empty;
        public string DeclarantAddress { get; set; } = string.Empty;
        public string DeclarationDate { get; set; } = string.Empty;
        public int? DeclarationVersion { get; set; }
        public string RegimeCode { get; set; } = string.Empty;
        public decimal? TotalDutyPaid { get; set; }
        public string ContainerISO { get; set; } = string.Empty;
        public string ContainerSize { get; set; } = string.Empty;
        public string ContainerDescription { get; set; } = string.Empty;
        public int ContainerQuantity { get; set; }
        public decimal? ContainerWeight { get; set; }
        public string SealNumber { get; set; } = string.Empty;
        public string TruckPlateNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string DriverLicense { get; set; } = string.Empty;
        public string ContainerStatus { get; set; } = string.Empty;
        public string ContainerRemarks { get; set; } = string.Empty;
        public int? NoOfContainers { get; set; }
        public string ConsigneeName { get; set; } = string.Empty;
        public string ConsigneeAddress { get; set; } = string.Empty;
        public string ShipperName { get; set; } = string.Empty;
        public string ShipperAddress { get; set; } = string.Empty;
        public string CountryOfOrigin { get; set; } = string.Empty;
        public string GoodsDescription { get; set; } = string.Empty;
        public string RotationNumber { get; set; } = string.Empty;
        public string DeliveryPlace { get; set; } = string.Empty;
        public string MarksNumbers { get; set; } = string.Empty;
        public string CompOffRemarks { get; set; } = string.Empty;
        public string CcvrIntelRemarks { get; set; } = string.Empty;
        public bool IsConsolidated { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<UnmappedFieldDto> UnmappedFields { get; set; } = new();
        public int? UnmappedFieldsCount { get; set; }
        public bool UnmappedFieldsOverflow { get; set; }
        public string? RawJsonData { get; set; }
    }

    public class FileVerificationDto
    {
        public int VerifiedDocumentCount { get; set; }
        public int PerfectDocumentCount { get; set; }
        public int PartialDocumentCount { get; set; }
        public double? AverageAccuracyPercent { get; set; }
        public double? LowestAccuracyPercent { get; set; }
        public string LowestAccuracyContainer { get; set; } = string.Empty;
        public List<DocVerificationDetailDto> Documents { get; set; } = new();
    }

    public class DocVerificationDetailDto
    {
        public string Container { get; set; } = string.Empty;
        public double Accuracy { get; set; }
        public int SourceFields { get; set; }
        public int MatchedFields { get; set; }
        public List<string> Missing { get; set; } = new();
    }

    public class UnmappedFieldDto
    {
        public string Label { get; set; } = string.Empty;  // Format: "Header:NewField"
        public string? Value { get; set; }
        public string Section { get; set; } = string.Empty;  // Extracted from Label
        public string FieldName { get; set; } = string.Empty;  // Extracted from Label
        public bool IsTruncated { get; set; }  // true if value was truncated (length > 4000)
    }
}

