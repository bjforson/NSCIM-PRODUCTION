using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Email
{
    /// <summary>
    /// Enhanced Email Service that uses System Settings database instead of appsettings.json
    /// </summary>
    public class EmailServiceWithSettings : IEmailService
    {
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILogger<EmailServiceWithSettings> _logger;
        private readonly IConfiguration _configuration;

        public EmailServiceWithSettings(
            ISettingsProvider settingsProvider,
            ILogger<EmailServiceWithSettings> logger,
            IConfiguration configuration)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<bool> SendCMRValidationAlertAsync(CMRValidationAlertModel alert)
        {
            try
            {
                var subject = $"⚠️ CMR Validation Alert - {alert.ContainerNumber}";
                var body = GenerateCMRAlertHtml(alert);

                // Admin emails would be stored in a separate settings table or as JSON array
                var adminEmail = await _settingsProvider.GetStringAsync("Email", "FromEmail", "admin@nickscan.com");
                var adminEmails = new List<string> { adminEmail }; // TODO: Parse from JSON array setting

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

                var adminEmail = await _settingsProvider.GetStringAsync("Email", "FromEmail", "admin@nickscan.com");
                var reportRecipients = new List<string> { adminEmail }; // TODO: Parse from JSON array setting

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
            return await SendEmailAsync(new List<string> { to }, subject, body, isHtml);
        }

        public async Task<bool> SendEmailAsync(List<string> to, string subject, string body, bool isHtml = true)
        {
            try
            {
                // Get settings from database
                var smtpServer = await _settingsProvider.GetStringAsync("Email", "SmtpServer", "smtp.gmail.com");
                var smtpPort = await _settingsProvider.GetIntAsync("Email", "SmtpPort", 587);
                var smtpUsername = await _settingsProvider.GetStringAsync("Email", "SmtpUsername", "");
                var smtpPassword = await _settingsProvider.GetStringAsync("Email", "SmtpPassword", "");
                var fromEmail = await _settingsProvider.GetStringAsync("Email", "FromEmail", "noreply@nickscan.com");
                var enableSsl = await _settingsProvider.GetBoolAsync("Email", "EnableSsl", true);

                // Check if email is enabled (we could add this as a setting)
                if (string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(smtpPassword))
                {
                    _logger.LogWarning("Email credentials not configured in System Settings. Email not sent: {Subject}", subject);
                    return false;
                }

                using var message = new MailMessage();
                message.From = new MailAddress(fromEmail, "NickScan Central Imaging Portal");

                foreach (var recipient in to)
                {
                    if (!string.IsNullOrWhiteSpace(recipient))
                    {
                        message.To.Add(new MailAddress(recipient));
                    }
                }

                if (message.To.Count == 0)
                {
                    _logger.LogWarning("No valid recipients for email: {Subject}", subject);
                    return false;
                }

                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = isHtml;

                using var client = new SmtpClient(smtpServer, smtpPort);
                client.EnableSsl = enableSsl;
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
                client.Timeout = _configuration.GetValue<int>("Email:SmtpTimeoutMs", 30000);

                await client.SendMailAsync(message);

                _logger.LogInformation("Email sent successfully to {Count} recipients: {Subject}", to.Count, subject);
                return true;
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "SMTP error sending email: {Subject}. StatusCode: {StatusCode}",
                    subject, smtpEx.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email: {Subject}", subject);
                return false;
            }
        }

        #region HTML Generation Methods (same as original)

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
        .container {{ max-width: 800px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #0066cc; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 20px; border: 1px solid #ddd; }}
        .footer {{ background-color: #333; color: white; padding: 10px; text-align: center; font-size: 12px; border-radius: 0 0 5px 5px; }}
        .metrics {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin: 20px 0; }}
        .metric-card {{ background-color: white; padding: 20px; border-left: 5px solid #0066cc; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .metric-value {{ font-size: 36px; font-weight: bold; color: #0066cc; }}
        .metric-label {{ font-size: 14px; color: #666; margin-top: 5px; }}
        .success-rate {{ color: {successColor}; }}
        .problematic {{ background-color: #fff3cd; padding: 15px; margin: 15px 0; border-left: 4px solid #ff9800; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📊 Daily CMR Data Quality Report</h1>
            <p>{report.ReportDate:MMMM dd, yyyy}</p>
        </div>
        <div class='content'>
            <div class='metrics'>
                <div class='metric-card'>
                    <div class='metric-value'>{report.TotalCMRRecords}</div>
                    <div class='metric-label'>Total CMR Records</div>
                </div>
                <div class='metric-card'>
                    <div class='metric-value'>{report.ValidRecords}</div>
                    <div class='metric-label'>Valid Records</div>
                </div>
                <div class='metric-card'>
                    <div class='metric-value'>{report.InvalidRecords}</div>
                    <div class='metric-label'>Invalid Records</div>
                </div>
                <div class='metric-card'>
                    <div class='metric-value success-rate'>{report.SuccessRate:F1}%</div>
                    <div class='metric-label'>Success Rate</div>
                </div>
            </div>

            <div class='metrics'>
                <div class='metric-card'>
                    <div class='metric-value'>{report.NewRecordsToday}</div>
                    <div class='metric-label'>New Records Today</div>
                </div>
                <div class='metric-card'>
                    <div class='metric-value'>{report.FixedRecordsToday}</div>
                    <div class='metric-label'>Fixed Records Today</div>
                </div>
                <div class='metric-card'>
                    <div class='metric-value'>{report.QueuedForRedownload}</div>
                    <div class='metric-label'>Queued for Re-download</div>
                </div>
            </div>

            {(report.ProblematicContainers?.Any() == true ? $@"
            <div class='problematic'>
                <h3>⚠️ Problematic Containers</h3>
                <ul>
                    {string.Join("", report.ProblematicContainers.Select(container => $"<li>{container}</li>"))}
                </ul>
            </div>
            " : "")}
            
        </div>
        <div class='footer'>
            <p>NickScan Central Imaging Portal - Daily Data Quality Report</p>
            <p>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
        </div>
    </div>
</body>
</html>";
        }

        #endregion
    }
}

