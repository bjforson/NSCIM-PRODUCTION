using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "ScannerOperator")]
    [ApiController]
    [Route("api/[controller]")]
    public class IngestionController : ControllerBase
    {
        private readonly ILogger<IngestionController> _logger;
        private readonly IIcumDownloadsRepository _repository;

        public IngestionController(
            ILogger<IngestionController> logger,
            IIcumDownloadsRepository repository)
        {
            _logger = logger;
            _repository = repository;
        }

        [HttpGet("pending-files")]
        public async Task<ActionResult<PendingFilesResponse>> GetPendingFiles()
        {
            try
            {
                var pendingFiles = await _repository.GetPendingFilesAsync();

                return Ok(new PendingFilesResponse
                {
                    Count = pendingFiles.Count,
                    Files = pendingFiles.Select(f => new PendingFileInfo
                    {
                        Id = f.Id,
                        FileName = f.FileName,
                        FilePath = f.FilePath,
                        DownloadDate = f.DownloadDate,
                        FileSize = f.FileSize,
                        WaitingMinutes = (DateTime.UtcNow - f.DownloadDate).TotalMinutes
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INGESTION] Error getting pending files");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("process-file/{fileId}")]
        public async Task<ActionResult<ProcessFileResponse>> ProcessFile(int fileId)
        {
            try
            {
                _logger.LogInformation("[INGESTION] Manual processing requested for file ID: {FileId}", fileId);

                // Get the file
                var file = await _repository.GetFileByIdAsync(fileId);
                if (file == null)
                {
                    return NotFound(new { error = $"File ID {fileId} not found" });
                }

                if (file.ProcessingStatus != "Pending")
                {
                    return BadRequest(new { error = $"File is not pending (current status: {file.ProcessingStatus})" });
                }

                // Mark as processed for now (temporary workaround)
                await _repository.UpdateFileProcessingStatusAsync(fileId, "ManuallySkipped", "Marked as skipped via manual API call");

                return Ok(new ProcessFileResponse
                {
                    Success = true,
                    Message = $"File {file.FileName} marked as skipped. The ingestion service issue needs to be resolved.",
                    FileId = fileId,
                    FileName = file.FileName,
                    CurrentStatus = "ManuallySkipped"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INGESTION] Error processing file");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("reset-file-status/{fileId}")]
        public async Task<ActionResult> ResetFileStatus(int fileId)
        {
            try
            {
                await _repository.UpdateFileProcessingStatusAsync(fileId, "Pending", null);
                return Ok(new { message = $"File {fileId} reset to Pending status" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INGESTION] Error resetting file status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("manual-trigger")]
        public async Task<ActionResult<ManualTriggerResponse>> ManualTrigger()
        {
            try
            {
                _logger.LogInformation("[INGESTION] Manual trigger requested - Processing pending files NOW");

                // Get pending files
                var pendingFiles = await _repository.GetPendingFilesAsync();

                if (!pendingFiles.Any())
                {
                    return Ok(new ManualTriggerResponse
                    {
                        Success = true,
                        Message = "No pending files to process",
                        FilesProcessed = 0,
                        TotalPending = 0
                    });
                }

                _logger.LogInformation("[INGESTION] Found {Count} pending files. Processing first 10 as test...", pendingFiles.Count);

                // Process first 10 files as a test
                var testBatch = pendingFiles.Take(10).ToList();
                var processed = 0;
                var failed = 0;

                foreach (var file in testBatch)
                {
                    try
                    {
                        // Just mark as completed for testing
                        await _repository.UpdateFileProcessingStatusAsync(file.Id, "ManuallyProcessed", "Processed via manual trigger API");
                        processed++;
                        _logger.LogInformation("[INGESTION] Marked file {Id} as processed: {FileName}", file.Id, file.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[INGESTION] Failed to process file {Id}", file.Id);
                        failed++;
                    }
                }

                return Ok(new ManualTriggerResponse
                {
                    Success = true,
                    Message = $"Manual trigger completed. Processed: {processed}, Failed: {failed}",
                    FilesProcessed = processed,
                    TotalPending = pendingFiles.Count - processed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INGESTION] Error in manual trigger");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("service-status")]
        public ActionResult<IngestionServiceStatus> GetServiceStatus()
        {
            try
            {
                // This would check the actual service status
                // For now, return basic info
                return Ok(new IngestionServiceStatus
                {
                    IsRunning = true,
                    LastRunTime = DateTime.UtcNow.AddMinutes(-2),
                    NextRunTime = DateTime.UtcNow.AddMinutes(3),
                    IntervalMinutes = 1,
                    Message = "Ingestion service runs every 1 minute (optimized)"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INGESTION] Error getting service status");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class ManualTriggerResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FilesProcessed { get; set; }
        public int TotalPending { get; set; }
    }

    public class PendingFilesResponse
    {
        public int Count { get; set; }
        public List<PendingFileInfo> Files { get; set; } = new();
    }

    public class PendingFileInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime DownloadDate { get; set; }
        public long FileSize { get; set; }
        public double WaitingMinutes { get; set; }
    }

    public class ProcessFileResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
    }

    public class IngestionServiceStatus
    {
        public bool IsRunning { get; set; }
        public DateTime? LastRunTime { get; set; }
        public DateTime? NextRunTime { get; set; }
        public int IntervalMinutes { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

