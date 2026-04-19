using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for evaluating queue publishing metrics and triggering alerts
    /// Provides alerting based on success rate thresholds and recovery service status
    /// </summary>
    public class QueuePublishingAlertService
    {
        private readonly QueuePublishingMetricsService _metricsService;
        private readonly ILogger<QueuePublishingAlertService> _logger;
        private readonly IEmailService? _emailService;
        private readonly Dictionary<AlertType, DateTime> _lastAlertTimes;
        private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(30);
        private const double WARNING_THRESHOLD = 99.0; // Success rate < 99% triggers warning
        private const double CRITICAL_THRESHOLD = 95.0; // Success rate < 95% triggers critical

        public QueuePublishingAlertService(
            QueuePublishingMetricsService metricsService,
            ILogger<QueuePublishingAlertService> logger,
            IEmailService? emailService = null)
        {
            _metricsService = metricsService;
            _logger = logger;
            _emailService = emailService;
            _lastAlertTimes = new Dictionary<AlertType, DateTime>();
        }

        /// <summary>
        /// Evaluate metrics and return list of alerts
        /// </summary>
        public List<QueuePublishingAlert> EvaluateAlerts(QueuePublishingMetrics? metrics = null)
        {
            var alerts = new List<QueuePublishingAlert>();
            metrics ??= _metricsService.GetMetrics();

            try
            {
                // Check success rate alerts
                var successRateAlerts = CheckSuccessRateAlerts(metrics);
                alerts.AddRange(successRateAlerts);

                // Check average retry count alert
                if (metrics.AverageRetryCount > 2.0)
                {
                    var alert = new QueuePublishingAlert
                    {
                        Level = AlertLevel.Warning,
                        Type = AlertType.HighRetryCount,
                        Message = $"Average retry count is high: {metrics.AverageRetryCount:F2} (indicates frequent transient failures)",
                        Timestamp = DateTime.UtcNow
                    };
                    if (ShouldSendAlert(AlertType.HighRetryCount))
                    {
                        alerts.Add(alert);
                        SendAlert(alert);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHING-ALERTS] Error evaluating alerts");
            }

            return alerts;
        }

        /// <summary>
        /// Check success rate and generate alerts
        /// </summary>
        private List<QueuePublishingAlert> CheckSuccessRateAlerts(QueuePublishingMetrics metrics)
        {
            var alerts = new List<QueuePublishingAlert>();

            if (metrics.TotalAttempts == 0)
            {
                // No data - not an alert
                return alerts;
            }

            if (metrics.SuccessRate < CRITICAL_THRESHOLD)
            {
                var alert = new QueuePublishingAlert
                {
                    Level = AlertLevel.Critical,
                    Type = AlertType.LowSuccessRate,
                    Message = $"CRITICAL: Queue publishing success rate is {metrics.SuccessRate:F2}% (below {CRITICAL_THRESHOLD}% threshold). " +
                             $"Total: {metrics.TotalAttempts}, Successful: {metrics.SuccessfulPublishes}, Failed: {metrics.FailedPublishes}",
                    Timestamp = DateTime.UtcNow
                };
                if (ShouldSendAlert(AlertType.LowSuccessRate))
                {
                    alerts.Add(alert);
                    SendAlert(alert);
                }
            }
            else if (metrics.SuccessRate < WARNING_THRESHOLD)
            {
                var alert = new QueuePublishingAlert
                {
                    Level = AlertLevel.Warning,
                    Type = AlertType.LowSuccessRate,
                    Message = $"WARNING: Queue publishing success rate is {metrics.SuccessRate:F2}% (below {WARNING_THRESHOLD}% threshold). " +
                             $"Total: {metrics.TotalAttempts}, Successful: {metrics.SuccessfulPublishes}, Failed: {metrics.FailedPublishes}",
                    Timestamp = DateTime.UtcNow
                };
                if (ShouldSendAlert(AlertType.LowSuccessRate))
                {
                    alerts.Add(alert);
                    SendAlert(alert);
                }
            }

            return alerts;
        }

        /// <summary>
        /// Alert when recovery service finds missed scans
        /// </summary>
        public void AlertRecoveryFoundMissedScans(int missedScansCount)
        {
            try
            {
                var alert = new QueuePublishingAlert
                {
                    Level = AlertLevel.Warning,
                    Type = AlertType.RecoveryFoundMissedScans,
                    Message = $"Recovery service found {missedScansCount} missed scan(s) that were not queued. " +
                             "This indicates previous queue publishing failures. All scans have been queued.",
                    Timestamp = DateTime.UtcNow
                };

                if (ShouldSendAlert(AlertType.RecoveryFoundMissedScans))
                {
                    SendAlert(alert);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHING-ALERTS] Error alerting on recovery found missed scans");
            }
        }

        /// <summary>
        /// Alert when recovery service fails
        /// </summary>
        public void AlertRecoveryServiceFailure(string errorMessage)
        {
            try
            {
                var alert = new QueuePublishingAlert
                {
                    Level = AlertLevel.Critical,
                    Type = AlertType.RecoveryServiceFailure,
                    Message = $"CRITICAL: Recovery service failed: {errorMessage}",
                    Timestamp = DateTime.UtcNow
                };

                if (ShouldSendAlert(AlertType.RecoveryServiceFailure))
                {
                    SendAlert(alert);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHING-ALERTS] Error alerting on recovery service failure");
            }
        }

        /// <summary>
        /// Send alert (log and optionally email)
        /// </summary>
        private void SendAlert(QueuePublishingAlert alert)
        {
            // Always log alerts
            switch (alert.Level)
            {
                case AlertLevel.Critical:
                    _logger.LogError("[QUEUE-PUBLISHING-ALERTS] {Message}", alert.Message);
                    break;
                case AlertLevel.Warning:
                    _logger.LogWarning("[QUEUE-PUBLISHING-ALERTS] {Message}", alert.Message);
                    break;
            }

            // Send email for critical alerts
            if (alert.Level == AlertLevel.Critical && _emailService != null)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var subject = $"🚨 {alert.Level}: Queue Publishing Alert";
                        var body = $@"
<h2>Queue Publishing Alert</h2>
<p><strong>Level:</strong> {alert.Level}</p>
<p><strong>Type:</strong> {alert.Type}</p>
<p><strong>Message:</strong> {alert.Message}</p>
<p><strong>Time:</strong> {alert.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</p>
";
                        // Note: Email recipients should be configured in appsettings or settings database
                        // For now, just log - email sending can be configured later
                        _logger.LogInformation("[QUEUE-PUBLISHING-ALERTS] Critical alert would be emailed (email service configured)");
                        // await _emailService.SendEmailAsync("admin@nickscan.com", subject, body, isHtml: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[QUEUE-PUBLISHING-ALERTS] Error sending alert email");
                    }

                    return Task.CompletedTask;
                });
            }

            _lastAlertTimes[alert.Type] = DateTime.UtcNow;
        }

        /// <summary>
        /// Get current alerts (for API endpoint)
        /// </summary>
        public List<string> GetCurrentAlerts()
        {
            try
            {
                var alerts = EvaluateAlerts();
                return alerts.Select(a => $"[{a.Level}] {a.Message}").ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHING-ALERTS] Error getting current alerts");
                return new List<string>();
            }
        }

        /// <summary>
        /// Check if alert should be sent (cooldown period)
        /// </summary>
        private bool ShouldSendAlert(AlertType alertType)
        {
            if (!_lastAlertTimes.ContainsKey(alertType))
            {
                return true;
            }

            var lastAlertTime = _lastAlertTimes[alertType];
            return DateTime.UtcNow - lastAlertTime > AlertCooldown;
        }
    }

    /// <summary>
    /// Queue publishing alert
    /// </summary>
    public class QueuePublishingAlert
    {
        public AlertLevel Level { get; set; }
        public AlertType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Alert level
    /// </summary>
    public enum AlertLevel
    {
        Warning,
        Critical
    }

    /// <summary>
    /// Alert type
    /// </summary>
    public enum AlertType
    {
        LowSuccessRate,
        HighRetryCount,
        RecoveryFoundMissedScans,
        RecoveryServiceFailure
    }
}
