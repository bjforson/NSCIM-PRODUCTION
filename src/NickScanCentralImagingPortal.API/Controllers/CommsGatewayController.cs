using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Admin endpoints for the NickComms.Gateway integration: ping, test sends,
    /// and message history. Configuration of BaseUrl/ApiKey is done via the standard
    /// Settings UI under category "NickComms".
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [HasPermission(Permissions.ControllersSystemSettings)]
    public class CommsGatewayController : ControllerBase
    {
        private readonly INickCommsClient _comms;
        private readonly ISettingsProvider _settings;
        private readonly ILogger<CommsGatewayController> _logger;

        public CommsGatewayController(
            INickCommsClient comms,
            ISettingsProvider settings,
            ILogger<CommsGatewayController> logger)
        {
            _comms = comms;
            _settings = settings;
            _logger = logger;
        }

        /// <summary>Ping the gateway health endpoint.</summary>
        [HttpGet("ping")]
        public async Task<ActionResult<PingResponse>> Ping(CancellationToken ct)
        {
            var ok = await _comms.PingAsync(ct);
            var baseUrl = await _settings.GetStringAsync("NickComms", "BaseUrl", "");
            return Ok(new PingResponse { Ok = ok, BaseUrl = baseUrl });
        }

        /// <summary>Send a test email through the gateway.</summary>
        [HttpPost("test-email")]
        public async Task<ActionResult<TestSendResponse>> SendTestEmail([FromBody] TestEmailRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.To))
                return BadRequest(new TestSendResponse { Success = false, Error = "Recipient is required." });

            var subject = string.IsNullOrWhiteSpace(req.Subject) ? "NSCIS Comms Gateway test" : req.Subject;
            var body = string.IsNullOrWhiteSpace(req.Body)
                ? "<p>This is a test email sent from NSCIS via the NickComms.Gateway.</p>"
                : req.Body;

            var result = await _comms.SendEmailAsync(req.To, subject, body, isHtml: true, ct: ct);
            return Ok(new TestSendResponse
            {
                Success = result.Success,
                MessageId = result.MessageId?.ToString(),
                Error = result.ErrorMessage
            });
        }

        /// <summary>Send a test SMS through the gateway.</summary>
        [HttpPost("test-sms")]
        public async Task<ActionResult<TestSendResponse>> SendTestSms([FromBody] TestSmsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.PhoneNumber))
                return BadRequest(new TestSendResponse { Success = false, Error = "Phone number is required." });

            var message = string.IsNullOrWhiteSpace(req.Message) ? "NSCIS Comms Gateway test SMS." : req.Message;
            var result = await _comms.SendSmsAsync(req.PhoneNumber, message, ct: ct);
            return Ok(new TestSendResponse
            {
                Success = result.Success,
                MessageId = result.MessageId?.ToString(),
                Error = result.ErrorMessage
            });
        }

        /// <summary>Recent message history (paginated).</summary>
        [HttpGet("history")]
        public async Task<ActionResult<NickCommsHistoryPage>> GetHistory(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? channel = null,
            [FromQuery] string? recipient = null,
            CancellationToken ct = default)
        {
            var result = await _comms.GetHistoryAsync(new NickCommsHistoryQuery
            {
                Page = page,
                PageSize = pageSize,
                Channel = channel,
                Recipient = recipient
            }, ct);
            return Ok(result);
        }

        public class TestEmailRequest
        {
            public string To { get; set; } = string.Empty;
            public string? Subject { get; set; }
            public string? Body { get; set; }
        }

        public class TestSmsRequest
        {
            public string PhoneNumber { get; set; } = string.Empty;
            public string? Message { get; set; }
        }

        public class TestSendResponse
        {
            public bool Success { get; set; }
            public string? MessageId { get; set; }
            public string? Error { get; set; }
        }

        public class PingResponse
        {
            public bool Ok { get; set; }
            public string? BaseUrl { get; set; }
        }
    }
}
