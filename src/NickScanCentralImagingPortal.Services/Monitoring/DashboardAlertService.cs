using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Sprint 5G3 / audit finding 8.25 — implementation of
    /// <see cref="IDashboardAlertService"/>.
    ///
    /// Behaviour summary:
    ///   - <c>RaiseAsync</c>: dedupe-or-insert, with email-on-Critical via
    ///     <see cref="INickCommsClient"/>. The first successful queue for a
    ///     stable <c>AlertKey</c> is the only email sent for that same error by
    ///     default; later repeats stay in the persisted dashboard alert log but
    ///     do not page the same recipients again. Email recipients are looked up
    ///     from <c>Email:AdminRecipients</c> (the same list the existing
    ///     <c>EmailService</c> uses for CMR validation alerts), with a hard
    ///     fallback to <c>admin@nickscan.com</c> so a misconfiguration
    ///     downgrades to "no recipient" rather than silent suppression.
    ///   - <c>AcknowledgeAsync</c>: idempotent stamp of
    ///     <c>AcknowledgedAtUtc</c> + <c>AcknowledgedBy</c>.
    ///
    /// SignalR broadcast is intentionally NOT performed here — the calling
    /// <c>ImageAnalysisDashboardBroadcastService</c> already pushes the
    /// returned entity to clients via the existing "NewAlert" channel,
    /// preserving the broadcast service's lifecycle and avoiding a hub-
    /// context dependency in the Services layer.
    /// </summary>
    public class DashboardAlertService : IDashboardAlertService
    {
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(30);
        private const int DefaultCriticalEmailCooldownMinutes = 120;
        private const bool DefaultSendSameCriticalEmailOnlyOnce = true;

        private readonly ApplicationDbContext _db;
        private readonly INickCommsClient _comms;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardAlertService> _logger;

        public DashboardAlertService(
            ApplicationDbContext db,
            INickCommsClient comms,
            IConfiguration configuration,
            ILogger<DashboardAlertService> logger)
        {
            _db = db;
            _comms = comms;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<DashboardAlertEntity> RaiseAsync(
            string type,
            string severity,
            string title,
            string? description,
            string? source,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("type required", nameof(type));
            if (string.IsNullOrWhiteSpace(severity)) throw new ArgumentException("severity required", nameof(severity));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title required", nameof(title));

            var now = DateTime.UtcNow;
            var alertKey = BuildAlertKey(type, title);
            var dedupeFloor = now - DedupeWindow;

            // First, dedupe by open incident. Acknowledgement is the explicit
            // close signal; until then, repeated detection cycles update one
            // row instead of creating a fresh alert and email.
            var existing = await _db.DashboardAlerts
                .Where(a => a.AlertKey == alertKey && a.AcknowledgedAtUtc == null)
                .OrderByDescending(a => a.RaisedAtUtc)
                .FirstOrDefaultAsync(ct);

            // Backward-compatible short-window fallback for rows created before
            // AlertKey existed or during mixed-version deploys.
            existing ??= await _db.DashboardAlerts
                .Where(a => a.Type == type && a.Title == title && a.RaisedAtUtc >= dedupeFloor)
                .OrderByDescending(a => a.RaisedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // Touch the existing row and keep its visible details current.
                existing.RaisedAtUtc = now;
                existing.AlertKey = string.IsNullOrWhiteSpace(existing.AlertKey) ? alertKey : existing.AlertKey;
                existing.Title = Truncate(title, 200);
                existing.Description = Truncate(description, 2000);
                existing.Source = Truncate(source, 200);
                existing.Severity = severity;
                await _db.SaveChangesAsync(ct);

                _logger.LogDebug(
                    "[DASHBOARD-ALERTS] open-incident dedupe hit (id={Id}, key={AlertKey}, type={Type}, title={Title}) — touched RaisedAtUtc",
                    existing.Id, existing.AlertKey, existing.Type, existing.Title);

                if (IsCritical(existing.Severity) && await CanSendCriticalEmailAsync(existing, ct))
                {
                    await TrySendOpsEmailAsync(existing, ct);
                }

                return existing;
            }

            var alert = new DashboardAlertEntity
            {
                Type = type,
                AlertKey = alertKey,
                Severity = severity,
                Title = Truncate(title, 200) ?? string.Empty,
                Description = Truncate(description, 2000),
                Source = Truncate(source, 200),
                RaisedAtUtc = now
                // TenantId left as 0 in the entity — DB default
                // (current_setting('app.tenant_id')::bigint via the migration)
                // fills it server-side. Do not set client-side: the
                // TenantConnectionInterceptor's app.tenant_id is the source of
                // truth and the column has DEFAULT NULLIF(...).
            };

            _db.DashboardAlerts.Add(alert);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[DASHBOARD-ALERTS] persisted new alert id={Id} type={Type} severity={Severity} title={Title}",
                alert.Id, alert.Type, alert.Severity, alert.Title);

            // Email-on-Critical. Non-Critical alerts persist + broadcast only.
            if (IsCritical(severity) && await CanSendCriticalEmailAsync(alert, ct))
            {
                await TrySendOpsEmailAsync(alert, ct);
            }

            return alert;
        }

        public async Task<DashboardAlertEntity?> AcknowledgeAsync(int alertId, string acknowledgedBy, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(acknowledgedBy)) acknowledgedBy = "unknown";

            var alert = await _db.DashboardAlerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
            if (alert == null) return null;

            if (alert.AcknowledgedAtUtc.HasValue)
            {
                // Idempotent — return as-is.
                return alert;
            }

            alert.AcknowledgedAtUtc = DateTime.UtcNow;
            alert.AcknowledgedBy = acknowledgedBy.Length > 100 ? acknowledgedBy.Substring(0, 100) : acknowledgedBy;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[DASHBOARD-ALERTS] acknowledged id={Id} by={By}",
                alert.Id, alert.AcknowledgedBy);

            return alert;
        }

        /// <summary>
        /// Best-effort outbound email. Failure is logged but does not roll back
        /// the persisted alert — the operator UI (and SignalR broadcast)
        /// remains the primary surface; email is the on-call escalation hook.
        /// </summary>
        private async Task TrySendOpsEmailAsync(DashboardAlertEntity alert, CancellationToken ct)
        {
            try
            {
                var recipients = _configuration.GetSection("Email:AdminRecipients").Get<List<string>>()
                    ?? new List<string> { "admin@nickscan.com" };

                if (recipients.Count == 0)
                {
                    _logger.LogWarning("[DASHBOARD-ALERTS] no Email:AdminRecipients configured — skipping email for alert id={Id}", alert.Id);
                    return;
                }

                var subject = $"[NSCIM] {alert.Severity}: {alert.Title}";
                var body = BuildEmailBody(alert);
                var clientReference = BuildEmailDedupeReference(alert);

                NickCommsEmailResult result;
                if (recipients.Count == 1)
                {
                    result = await _comms.SendEmailAsync(
                        to: recipients[0],
                        subject: subject,
                        htmlBody: body,
                        isHtml: true,
                        clientReference: clientReference,
                        ct: ct);
                }
                else
                {
                    result = await _comms.SendBulkEmailAsync(
                        recipients: recipients,
                        subject: subject,
                        htmlBody: body,
                        isHtml: true,
                        clientReference: clientReference,
                        ct: ct);
                }

                if (result.Success)
                {
                    alert.EmailSentAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "[DASHBOARD-ALERTS] email sent for alert id={Id} (recipients={Count}, batch={Batch})",
                        alert.Id, recipients.Count, result.BatchId);
                }
                else
                {
                    _logger.LogWarning(
                        "[DASHBOARD-ALERTS] NickComms email send failed for alert id={Id}: {Error}",
                        alert.Id, result.ErrorMessage ?? "(no message)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DASHBOARD-ALERTS] error sending email for alert id={Id}", alert.Id);
            }
        }

        private async Task<bool> CanSendCriticalEmailAsync(DashboardAlertEntity alert, CancellationToken ct)
        {
            if (!IsCritical(alert.Severity))
            {
                return false;
            }

            if (alert.EmailSentAtUtc.HasValue)
            {
                return false;
            }

            var sendSameAlertKeyOnlyOnce = _configuration.GetValue<bool?>("Alerting:SendSameCriticalEmailOnlyOnce")
                ?? DefaultSendSameCriticalEmailOnlyOnce;
            if (sendSameAlertKeyOnlyOnce && !string.IsNullOrWhiteSpace(alert.AlertKey))
            {
                var alreadyEmailed = await _db.DashboardAlerts
                    .AsNoTracking()
                    .AnyAsync(a => a.AlertKey == alert.AlertKey
                        && a.Id != alert.Id
                        && a.EmailSentAtUtc != null, ct);

                if (alreadyEmailed)
                {
                    _logger.LogInformation(
                        "[DASHBOARD-ALERTS] suppressing Critical email for id={Id}, key={AlertKey}; same alert key already emailed once",
                        alert.Id, alert.AlertKey);
                }

                return !alreadyEmailed;
            }

            var cooldownMinutes = _configuration.GetValue<int?>("Alerting:CriticalEmailCooldownMinutes")
                ?? DefaultCriticalEmailCooldownMinutes;
            if (cooldownMinutes < 0) cooldownMinutes = 0;

            if (cooldownMinutes == 0)
            {
                return true;
            }

            var cutoff = DateTime.UtcNow.AddMinutes(-cooldownMinutes);
            var recentlySent = await _db.DashboardAlerts
                .AsNoTracking()
                .AnyAsync(a => a.AlertKey == alert.AlertKey
                    && a.Id != alert.Id
                    && a.EmailSentAtUtc != null
                    && a.EmailSentAtUtc >= cutoff, ct);

            if (recentlySent)
            {
                _logger.LogInformation(
                    "[DASHBOARD-ALERTS] suppressing Critical email for id={Id}, key={AlertKey}; cooldown={CooldownMinutes}m",
                    alert.Id, alert.AlertKey, cooldownMinutes);
            }

            return !recentlySent;
        }

        private static bool IsCritical(string? severity)
            => string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);

        private static string BuildAlertKey(string type, string title)
        {
            var cleanType = type.Trim();
            var cleanTitle = Regex.Replace(title.Trim(), @"\s+", " ");

            if (cleanType.Equals("DataIntegrity", StringComparison.OrdinalIgnoreCase))
            {
                return "DataIntegrity:Dashboard";
            }

            if (cleanType.Equals("AuditPoolEmpty", StringComparison.OrdinalIgnoreCase))
            {
                return "AuditPoolEmpty";
            }

            if (cleanType.Equals("Bottleneck", StringComparison.OrdinalIgnoreCase))
            {
                if (cleanTitle.Contains("Ready stage", StringComparison.OrdinalIgnoreCase))
                    return "Bottleneck:Ready";
                if (cleanTitle.Contains("Audit stage", StringComparison.OrdinalIgnoreCase))
                    return "Bottleneck:Audit";
            }

            if (cleanType.Equals("Performance", StringComparison.OrdinalIgnoreCase)
                && cleanTitle.Contains("Stale assignments", StringComparison.OrdinalIgnoreCase))
            {
                return "Performance:StaleAssignments";
            }

            if (cleanType.Equals("DriftSweepHighCounts", StringComparison.OrdinalIgnoreCase))
            {
                return "DriftSweepHighCounts";
            }

            cleanTitle = Regex.Replace(cleanTitle, @"\s*\(hash=[^)]+\)", "", RegexOptions.IgnoreCase);
            return Truncate($"{cleanType}:{cleanTitle}", 256) ?? cleanType;
        }

        private static string BuildEmailDedupeReference(DashboardAlertEntity alert)
        {
            var key = string.IsNullOrWhiteSpace(alert.AlertKey)
                ? $"{alert.Type}:{alert.Title}"
                : alert.AlertKey;

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToUpperInvariant()));
            return $"dedupe:dashboardalert:{Convert.ToHexString(bytes, 0, 16).ToLowerInvariant()}";
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value[..maxLength];
        }

        private static string BuildEmailBody(DashboardAlertEntity alert)
        {
            var color = alert.Severity?.ToLowerInvariant() switch
            {
                "critical" => "#d32f2f",
                "warning" => "#f57c00",
                _ => "#1976d2"
            };

            var safeTitle = WebUtility.HtmlEncode(alert.Title ?? "");
            var safeType = WebUtility.HtmlEncode(alert.Type ?? "");
            var safeSeverity = WebUtility.HtmlEncode(alert.Severity ?? "");
            var safeSource = WebUtility.HtmlEncode(alert.Source ?? "(unknown source)");
            var safeDescription = WebUtility.HtmlEncode(alert.Description ?? "");

            return $@"<!DOCTYPE html>
<html><body style='font-family:Arial,sans-serif;color:#333;'>
<div style='max-width:640px;margin:0 auto;'>
  <div style='background:{color};color:#fff;padding:16px;border-radius:6px 6px 0 0;'>
    <h2 style='margin:0;'>NSCIM Dashboard Alert — {safeSeverity}</h2>
  </div>
  <div style='background:#f9f9f9;padding:16px;border:1px solid #ddd;'>
    <p><strong>Title:</strong> {safeTitle}</p>
    <p><strong>Type:</strong> {safeType}</p>
    <p><strong>Source:</strong> {safeSource}</p>
    <p><strong>Raised:</strong> {alert.RaisedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
    <hr style='border:none;border-top:1px solid #ddd;' />
    <p>{safeDescription}</p>
    <p style='font-size:12px;color:#666;'>Alert id <strong>{alert.Id}</strong>.
    Acknowledge from the dashboard or POST <code>/api/admin/alerts/{alert.Id}/acknowledge</code>.</p>
  </div>
</div>
</body></html>";
        }
    }
}
