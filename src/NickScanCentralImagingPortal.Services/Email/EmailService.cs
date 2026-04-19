using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Email
{
    /// <summary>
    /// Service for sending email notifications. All transport is delegated to
    /// <see cref="INickCommsClient"/> (the NickComms.Gateway service); this class only
    /// owns the alert/report HTML body templates.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly INickCommsClient _comms;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(INickCommsClient comms, IConfiguration configuration, ILogger<EmailService> logger)
        {
            _comms = comms;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendCMRValidationAlertAsync(CMRValidationAlertModel alert)
        {
            try
            {
                var subject = $"⚠️ CMR Validation Alert - {alert.ContainerNumber}";
                var body = GenerateCMRAlertHtml(alert);

                var adminEmails = _configuration.GetSection("Email:AdminRecipients").Get<List<string>>()
                    ?? new List<string> { "admin@nickscan.com" };

                return await SendEmailAsync(adminEmails, subject, body, isHtml: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending CMR validation alert for container {Container}", alert.ContainerNumber);
                return false;
            }
        }

        public async Task<bool> SendDailyDataQualityReportAsync(DataQualityReportModel report)
        {
            try
            {
                var subject = $"📊 Daily Data Quality Report - {report.ReportDate:MMM dd, yyyy}";
                var body = GenerateDataQualityReportHtml(report);

                var reportRecipients = _configuration.GetSection("Email:ReportRecipients").Get<List<string>>()
                    ?? new List<string> { "admin@nickscan.com" };

                return await SendEmailAsync(reportRecipients, subject, body, isHtml: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending daily data quality report for {Date}", report.ReportDate);
                return false;
            }
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            var result = await _comms.SendEmailAsync(to, subject, body, isHtml);
            if (!result.Success)
                _logger.LogWarning("NickComms email send failed for {To}: {Error}", to, result.ErrorMessage);
            else
                _logger.LogInformation("Email queued via NickComms ({Id}) to {To}: {Subject}", result.MessageId, to, subject);
            return result.Success;
        }

        public async Task<bool> SendEmailAsync(List<string> to, string subject, string body, bool isHtml = true)
        {
            if (to == null || to.Count == 0) return false;

            if (to.Count == 1)
                return await SendEmailAsync(to[0], subject, body, isHtml);

            var result = await _comms.SendBulkEmailAsync(to, subject, body, isHtml);
            if (!result.Success)
                _logger.LogWarning("NickComms bulk email send failed ({Count} recipients): {Error}", to.Count, result.ErrorMessage);
            else
                _logger.LogInformation("Bulk email queued via NickComms (batch={Batch}, accepted={Count}): {Subject}",
                    result.BatchId, result.AcceptedCount, subject);
            return result.Success;
        }

        private string GenerateCMRAlertHtml(CMRValidationAlertModel alert)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #f44336; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 20px; border: 1px solid #ddd; }}
        .footer {{ background-color: #333; color: white; padding: 10px; text-align: center; font-size: 12px; border-radius: 0 0 5px 5px; }}
        .info-box {{ background-color: white; padding: 15px; margin: 10px 0; border-left: 4px solid #f44336; }}
        .missing-fields {{ color: #f44336; font-weight: bold; }}
        .badge {{ display: inline-block; padding: 5px 10px; background-color: #ff9800; color: white; border-radius: 3px; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>⚠️ CMR Validation Alert</h1>
        </div>
        <div class='content'>
            <p>A CMR record has failed validation and is missing critical fields required for the composite key.</p>
            
            <div class='info-box'>
                <h3>Container Details</h3>
                <p><strong>Container Number:</strong> {alert.ContainerNumber}</p>
                <p><strong>Declaration Number:</strong> {alert.DeclarationNumber ?? "N/A"}</p>
                <p><strong>Source File:</strong> {alert.SourceFile}</p>
                <p><strong>Detected At:</strong> {alert.DetectedAt:yyyy-MM-dd HH:mm:ss}</p>
            </div>

            <div class='info-box'>
                <h3>Missing Fields</h3>
                <p class='missing-fields'>{string.Join(", ", alert.MissingFields)}</p>
            </div>

            <div class='info-box'>
                <h3>Action Taken</h3>
                <p>
                    {(alert.AutoQueued
                        ? "✅ <span class='badge'>AUTO-QUEUED</span> Container has been automatically queued for re-download from the Batch API."
                        : "⚠️ Manual intervention required. Please queue this container for re-download.")}
                </p>
            </div>

            <p><strong>What to do:</strong></p>
            <ul>
                <li>Check the re-download queue status in the admin dashboard</li>
                <li>Verify if the re-download completed successfully</li>
                <li>If the issue persists, contact the ICUMS API team</li>
            </ul>
        </div>
        <div class='footer'>
            <p>NickScan Central Imaging Portal - Automated Alert System</p>
            <p>Do not reply to this email</p>
        </div>
    </div>
</body>
</html>";
        }

        private string GenerateDataQualityReportHtml(DataQualityReportModel report)
        {
            var successColor = report.SuccessRate >= 99 ? "#4caf50" : report.SuccessRate >= 95 ? "#ff9800" : "#f44336";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 700px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196f3; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 20px; border: 1px solid #ddd; }}
        .footer {{ background-color: #333; color: white; padding: 10px; text-align: center; font-size: 12px; border-radius: 0 0 5px 5px; }}
        .stats-grid {{ display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; margin: 20px 0; }}
        .stat-card {{ background-color: white; padding: 15px; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .stat-value {{ font-size: 32px; font-weight: bold; color: {successColor}; }}
        .stat-label {{ font-size: 14px; color: #666; }}
        .success-rate {{ font-size: 48px; font-weight: bold; color: {successColor}; text-align: center; margin: 20px 0; }}
        .problematic-list {{ background-color: #fff3cd; padding: 15px; border-left: 4px solid #ff9800; margin: 15px 0; }}
        .table {{ width: 100%; border-collapse: collapse; margin: 15px 0; background-color: white; }}
        .table th, .table td {{ padding: 10px; text-align: left; border-bottom: 1px solid #ddd; }}
        .table th {{ background-color: #2196f3; color: white; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📊 Daily Data Quality Report</h1>
            <p>{report.ReportDate:MMMM dd, yyyy}</p>
        </div>
        <div class='content'>
            <h2>CMR Data Quality Summary</h2>
            
            <div class='success-rate'>
                {report.SuccessRate:F2}%
                <div style='font-size: 14px; color: #666;'>Validation Success Rate</div>
            </div>

            <div class='stats-grid'>
                <div class='stat-card'>
                    <div class='stat-value'>{report.TotalCMRRecords:N0}</div>
                    <div class='stat-label'>Total CMR Records</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-value' style='color: #4caf50;'>{report.ValidRecords:N0}</div>
                    <div class='stat-label'>Valid Records</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-value' style='color: #f44336;'>{report.InvalidRecords:N0}</div>
                    <div class='stat-label'>Invalid Records</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-value' style='color: #2196f3;'>{report.NewRecordsToday:N0}</div>
                    <div class='stat-label'>New Records Today</div>
                </div>
            </div>

            <h3>Today's Activity</h3>
            <table class='table'>
                <tr>
                    <th>Metric</th>
                    <th>Count</th>
                </tr>
                <tr>
                    <td>Records Fixed Today</td>
                    <td>{report.FixedRecordsToday}</td>
                </tr>
                <tr>
                    <td>Queued for Re-download</td>
                    <td>{report.QueuedForRedownload}</td>
                </tr>
                <tr>
                    <td>Successful Re-downloads</td>
                    <td>{report.SuccessfulRedownloads}</td>
                </tr>
                <tr>
                    <td>Failed Re-downloads</td>
                    <td>{report.FailedRedownloads}</td>
                </tr>
            </table>

            {(report.ProblematicContainers.Any()
                ? $@"
            <div class='problematic-list'>
                <h4>⚠️ Problematic Containers ({report.ProblematicContainers.Count})</h4>
                <ul>
                    {string.Join("", report.ProblematicContainers.Take(10).Select(c => $"<li>{c}</li>"))}
                    {(report.ProblematicContainers.Count > 10 ? $"<li><em>... and {report.ProblematicContainers.Count - 10} more</em></li>" : "")}
                </ul>
            </div>"
                : "<p style='color: #4caf50; font-weight: bold;'>✅ No problematic containers detected!</p>")}

            <p style='margin-top: 20px;'><strong>Recommendations:</strong></p>
            <ul>
                {(report.InvalidRecords > 0
                    ? "<li>Review and address invalid CMR records in the admin dashboard</li>"
                    : "")}
                {(report.FailedRedownloads > 0
                    ? "<li>Investigate failed re-download attempts and retry if necessary</li>"
                    : "")}
                <li>Monitor the re-download queue for pending items</li>
                <li>Verify ICUMS API connectivity and performance</li>
            </ul>
        </div>
        <div class='footer'>
            <p>NickScan Central Imaging Portal - Automated Reporting System</p>
            <p>Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
        </div>
    </div>
</body>
</html>";
        }
    }
}

