using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Sprint 5G3 / audit finding 8.25 — admin endpoints for the persisted
    /// dashboard alert log. The detection rules and SignalR broadcast live in
    /// <c>ImageAnalysisDashboardHub</c>; this controller is the human-facing
    /// disposition surface.
    ///
    /// Endpoints
    /// ---------
    ///   POST /api/admin/alerts/{id}/acknowledge — stamp Acknowledged{AtUtc,By}
    ///
    /// A separate sprint will add the listing/filter UI; for now this is the
    /// minimum hook the dashboard panel needs to clear an alert.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/admin/alerts")]
    public class AdminAlertsController : ControllerBase
    {
        private readonly IDashboardAlertService _alertService;
        private readonly ILogger<AdminAlertsController> _logger;

        public AdminAlertsController(
            IDashboardAlertService alertService,
            ILogger<AdminAlertsController> logger)
        {
            _alertService = alertService;
            _logger = logger;
        }

        /// <summary>
        /// Acknowledge an alert. Idempotent — repeat calls return the same
        /// state once the row is already acknowledged. Returns 404 when the
        /// alert id is unknown to the caller's tenant scope.
        /// </summary>
        [HttpPost("{id:int}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
        {
            var who = User?.Identity?.Name ?? "unknown";
            var alert = await _alertService.AcknowledgeAsync(id, who, ct);
            if (alert == null) return NotFound(new { error = "alert_not_found", id });

            _logger.LogInformation(
                "[ADMIN-ALERTS] alert id={Id} acknowledged by {User}",
                id, who);

            return Ok(new
            {
                id = alert.Id,
                type = alert.Type,
                severity = alert.Severity,
                title = alert.Title,
                description = alert.Description,
                source = alert.Source,
                raisedAtUtc = alert.RaisedAtUtc,
                acknowledgedAtUtc = alert.AcknowledgedAtUtc,
                acknowledgedBy = alert.AcknowledgedBy,
                emailSentAtUtc = alert.EmailSentAtUtc
            });
        }
    }
}
