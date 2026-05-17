using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Services.CameraEvidence;

namespace NickScanCentralImagingPortal.API.Controllers.Integrations
{
    [AllowAnonymous]
    [ApiController]
    [Route("api/integrations/unifi-protect")]
    public sealed class UniFiProtectWebhookController : ControllerBase
    {
        private readonly ICameraEvidenceService _cameraEvidenceService;
        private readonly ILogger<UniFiProtectWebhookController> _logger;

        public UniFiProtectWebhookController(
            ICameraEvidenceService cameraEvidenceService,
            ILogger<UniFiProtectWebhookController> logger)
        {
            _cameraEvidenceService = cameraEvidenceService;
            _logger = logger;
        }

        [HttpPost("sites/{siteKey}/webhooks/alarm")]
        public async Task<IActionResult> ReceiveAlarmWebhook(string siteKey, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var headers = Request.Headers.ToDictionary(h => h.Key, h => (string?)h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var result = await _cameraEvidenceService.IngestWebhookAsync(
                siteKey,
                headers,
                body,
                HttpContext.Connection.RemoteIpAddress,
                cancellationToken);

            if (result.Accepted)
            {
                return Accepted(result);
            }

            _logger.LogWarning("Rejected UniFi Protect webhook for site {SiteKey}: {Status} {Message}", siteKey, result.ProcessingStatus, result.Message);
            return result.ProcessingStatus switch
            {
                "Rejected" => Unauthorized(result),
                "SiteDisabled" => NotFound(result),
                "Disabled" => StatusCode(StatusCodes.Status503ServiceUnavailable, result),
                _ => BadRequest(result)
            };
        }
    }
}
