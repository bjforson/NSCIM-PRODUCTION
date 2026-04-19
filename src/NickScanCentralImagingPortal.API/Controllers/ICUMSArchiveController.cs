using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for ICUMS file archive management
    /// Archive Solution: Provides search, restore, and management capabilities for archived files
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CustomsOfficer")]
    public class ICUMSArchiveController : ControllerBase
    {
        private readonly IIcumDownloadsRepository _repository;
        private readonly ILogger<ICUMSArchiveController> _logger;
        private readonly IConfiguration _configuration;

        public ICUMSArchiveController(
            IIcumDownloadsRepository repository,
            ILogger<ICUMSArchiveController> logger,
            IConfiguration configuration)
        {
            _repository = repository;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Search archived files by container number, date range, or file type
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<List<ArchivedFile>>> SearchArchivedFiles(
            [FromQuery] string? containerNumber = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? fileType = null,
            [FromQuery] int maxResults = 100)
        {
            try
            {
                var results = await _repository.SearchArchivedFilesAsync(
                    containerNumber, startDate, endDate, fileType, maxResults);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching archived files");
                return StatusCode(500, new { error = "Failed to search archived files", message = ex.Message });
            }
        }

        /// <summary>
        /// Get archived file by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ArchivedFile>> GetArchivedFile(int id)
        {
            try
            {
                var archivedFile = await _repository.GetArchivedFileByIdAsync(id);
                if (archivedFile == null)
                {
                    return NotFound(new { error = "Archived file not found", id });
                }

                return Ok(archivedFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting archived file {Id}", id);
                return StatusCode(500, new { error = "Failed to get archived file", message = ex.Message });
            }
        }

        /// <summary>
        /// Restore an archived file by decompressing and copying to restore location
        /// </summary>
        [HttpPost("restore")]
        [Authorize(Policy = "CustomsOfficer")] // Same as the controller-level policy. Tighten to "AdminOnly" later if archive restore becomes admin-only.
        public async Task<ActionResult> RestoreArchivedFile([FromBody] RestoreArchiveRequest request)
        {
            try
            {
                // Find archived file
                ArchivedFile? archivedFile = null;

                if (request.ArchivedFileId.HasValue)
                {
                    archivedFile = await _repository.GetArchivedFileByIdAsync(request.ArchivedFileId.Value);
                }
                else if (!string.IsNullOrEmpty(request.ContainerNumber))
                {
                    var searchResults = await _repository.SearchArchivedFilesAsync(
                        request.ContainerNumber,
                        request.ArchiveDate?.Date,
                        request.ArchiveDate?.Date.AddDays(1),
                        null,
                        1);

                    archivedFile = searchResults.FirstOrDefault();
                }

                if (archivedFile == null)
                {
                    return NotFound(new { error = "Archived file not found" });
                }

                if (!System.IO.File.Exists(archivedFile.ArchiveFilePath))
                {
                    return NotFound(new { error = "Archived file not found on disk", path = archivedFile.ArchiveFilePath });
                }

                // Determine restore path
                var restorePath = request.RestoreToPath ??
                    Path.Combine(
                        _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads",
                        "Restored"
                    );

                Directory.CreateDirectory(restorePath);

                // Decompress and restore file
                var restoredFileName = Path.GetFileNameWithoutExtension(archivedFile.ArchiveFileName);
                var restoredFilePath = Path.Combine(restorePath, restoredFileName);

                using (var sourceStream = new System.IO.FileStream(archivedFile.ArchiveFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (var decompressionStream = new GZipStream(sourceStream, CompressionMode.Decompress))
                using (var targetStream = System.IO.File.Create(restoredFilePath))
                {
                    await decompressionStream.CopyToAsync(targetStream);
                }

                // Update database
                await _repository.RestoreArchivedFileAsync(archivedFile.Id, restoredFilePath);

                _logger.LogInformation("Restored archived file {FileName} (ID: {Id}) to {RestorePath}",
                    archivedFile.OriginalFileName, archivedFile.Id, restoredFilePath);

                return Ok(new
                {
                    message = "File restored successfully",
                    archivedFileId = archivedFile.Id,
                    restoredFilePath = restoredFilePath,
                    originalFileName = archivedFile.OriginalFileName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring archived file");
                return StatusCode(500, new { error = "Failed to restore archived file", message = ex.Message });
            }
        }

        /// <summary>
        /// Get archive statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult> GetArchiveStatistics()
        {
            try
            {
                // Get all archived files for statistics
                var allArchived = await _repository.SearchArchivedFilesAsync(null, null, null, null, int.MaxValue);

                var stats = new
                {
                    TotalArchivedFiles = allArchived.Count,
                    TotalOriginalSizeBytes = allArchived.Sum(f => f.OriginalSizeBytes),
                    TotalArchivedSizeBytes = allArchived.Sum(f => f.ArchivedSizeBytes),
                    AverageCompressionRatio = allArchived.Any()
                        ? allArchived.Average(f => f.CompressionRatio)
                        : 0,
                    FilesByType = allArchived.GroupBy(f => f.FileType)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    FilesByYear = allArchived.GroupBy(f => f.ArchivedDate.Year)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    RestoredFiles = allArchived.Count(f => f.IsRestored)
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting archive statistics");
                return StatusCode(500, new { error = "Failed to get archive statistics", message = ex.Message });
            }
        }
    }

    /// <summary>
    /// Request model for restoring archived files
    /// </summary>
    public class RestoreArchiveRequest
    {
        public int? ArchivedFileId { get; set; }
        public string? ContainerNumber { get; set; }
        public DateTime? ArchiveDate { get; set; }
        public string? RestoreToPath { get; set; }
    }
}

