using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;
using NickScanCentralImagingPortal.Services.CameraEvidence;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/camera-evidence")]
    public sealed class CameraEvidenceController : ControllerBase
    {
        private readonly ICameraEvidenceService _cameraEvidenceService;

        public CameraEvidenceController(ICameraEvidenceService cameraEvidenceService)
        {
            _cameraEvidenceService = cameraEvidenceService;
        }

        [HttpGet("health")]
        public async Task<ActionResult<CameraEvidenceHealthDto>> GetHealth(CancellationToken cancellationToken)
        {
            return Ok(await _cameraEvidenceService.GetHealthAsync(cancellationToken));
        }

        [HttpGet("sites")]
        public async Task<ActionResult<IReadOnlyList<CameraEvidenceSiteDto>>> GetSites(CancellationToken cancellationToken)
        {
            return Ok(await _cameraEvidenceService.GetSitesAsync(cancellationToken));
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("sites")]
        public async Task<ActionResult<CameraEvidenceSiteDto>> CreateSite(
            [FromBody] CameraEvidenceSiteUpsertRequest request,
            CancellationToken cancellationToken)
        {
            request.Id = null;
            var result = await _cameraEvidenceService.UpsertSiteAsync(request, GetActor(), cancellationToken);
            return CreatedAtAction(nameof(GetSites), new { id = result.Id }, result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPatch("sites/{siteId:guid}")]
        public async Task<ActionResult<CameraEvidenceSiteDto>> UpdateSite(
            Guid siteId,
            [FromBody] CameraEvidenceSiteUpsertRequest request,
            CancellationToken cancellationToken)
        {
            request.Id = siteId;
            return Ok(await _cameraEvidenceService.UpsertSiteAsync(request, GetActor(), cancellationToken));
        }

        [HttpGet("sources")]
        public async Task<ActionResult<IReadOnlyList<CameraEvidenceSourceDto>>> GetSources(
            [FromQuery] Guid? siteId,
            CancellationToken cancellationToken)
        {
            return Ok(await _cameraEvidenceService.GetSourcesAsync(siteId, cancellationToken));
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("sources")]
        public async Task<ActionResult<CameraEvidenceSourceDto>> CreateSource(
            [FromBody] CameraEvidenceSourceUpsertRequest request,
            CancellationToken cancellationToken)
        {
            request.Id = null;
            var result = await _cameraEvidenceService.UpsertSourceAsync(request, GetActor(), cancellationToken);
            return CreatedAtAction(nameof(GetSources), new { id = result.Id }, result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPatch("sources/{sourceId:guid}")]
        public async Task<ActionResult<CameraEvidenceSourceDto>> UpdateSource(
            Guid sourceId,
            [FromBody] CameraEvidenceSourceUpsertRequest request,
            CancellationToken cancellationToken)
        {
            request.Id = sourceId;
            return Ok(await _cameraEvidenceService.UpsertSourceAsync(request, GetActor(), cancellationToken));
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("sites/{siteId:guid}/protect/cameras")]
        public async Task<ActionResult<IReadOnlyList<ProtectCameraDto>>> GetProtectCameras(
            Guid siteId,
            CancellationToken cancellationToken)
        {
            return Ok(await _cameraEvidenceService.GetProtectCamerasAsync(siteId, cancellationToken));
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("sources/{sourceId:guid}/test-snapshot")]
        public async Task<ActionResult<CameraEvidenceSnapshotTestResultDto>> TestSnapshot(
            Guid sourceId,
            CancellationToken cancellationToken)
        {
            var result = await _cameraEvidenceService.TestSnapshotAsync(sourceId, GetActor(), cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("events")]
        public async Task<ActionResult<CameraEvidenceEventPageDto>> GetEvents(
            [FromQuery] string? siteKey,
            [FromQuery] Guid? sourceId,
            [FromQuery] string? reviewStatus,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _cameraEvidenceService.GetEventsAsync(siteKey, sourceId, reviewStatus, page, pageSize, cancellationToken));
        }

        [HttpGet("events/{eventId:guid}")]
        public async Task<ActionResult<CameraEvidenceEventDetailDto>> GetEvent(Guid eventId, CancellationToken cancellationToken)
        {
            var result = await _cameraEvidenceService.GetEventAsync(eventId, cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("frames/{frameId:guid}/image")]
        public async Task<IActionResult> GetFrameImage(Guid frameId, CancellationToken cancellationToken)
        {
            var frame = await _cameraEvidenceService.GetFrameFileAsync(frameId, cancellationToken);
            if (frame == null)
            {
                return NotFound();
            }

            return PhysicalFile(frame.FullPath, frame.ContentType, frame.FileName, enableRangeProcessing: true);
        }

        [HttpPost("ocr-results/{ocrResultId:guid}/review")]
        public async Task<ActionResult<CameraEvidenceReviewDecisionDto>> ReviewOcrResult(
            Guid ocrResultId,
            [FromBody] CameraEvidenceReviewRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _cameraEvidenceService.ReviewOcrResultAsync(ocrResultId, request, GetActor(), cancellationToken));
        }

        private string GetActor()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.Identity?.Name
                ?? "unknown";
        }
    }
}
