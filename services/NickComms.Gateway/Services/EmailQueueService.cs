using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Entities;

namespace NickComms.Gateway.Services;

/// <summary>
/// Email outbox worker. Same lifecycle contract as <see cref="SmsQueueService"/>,
/// but with one extra wrinkle: attachments are persisted on the row as a JSON
/// array of base64 blobs (column <c>attachments_json</c>) so a crashed worker
/// can resume sending after restart instead of losing the payload that
/// previously lived in the in-memory <c>Channel</c>.
///
/// The 10 MB API-side cap (in <c>EmailService.DecodeAttachments</c>) keeps the
/// row size well within Postgres's 1 GB-per-row jsonb limit, and base64 inflate
/// (~33%) is fine at that scale.
/// </summary>
public class EmailQueueService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueService> _logger;
    private readonly EmailOptions _emailOptions;
    private readonly OutboxOptions _outboxOptions;

    public EmailQueueService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueService> logger,
        IOptions<EmailOptions> emailOptions,
        IOptions<OutboxOptions> outboxOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _emailOptions = emailOptions.Value;
        _outboxOptions = outboxOptions.Value;
    }

    /// <summary>Compatibility shim — DB-row commit IS the queue signal now.</summary>
    public void Enqueue(EmailQueueItem _) { /* no-op, kept for caller compat */ }

    public int QueuedCount
    {
        get
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();
            return db.EmailMessages.Count(m => m.Status == "queued" && m.NextAttemptAt <= DateTime.UtcNow);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EmailQueueService (outbox) started — poll: {Poll}s batch: {Batch} maxAttempts: {Max}",
            _outboxOptions.PollIntervalSeconds, _outboxOptions.BatchSize, _outboxOptions.MaxAttempts);

        await ReleaseStuckRowsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_outboxOptions.PollIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email outbox loop error — backing off 5s");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<int> DrainBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        var now = DateTime.UtcNow;
        var batch = await db.EmailMessages
            .Where(m => m.Status == "queued" && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(_outboxOptions.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        foreach (var m in batch)
        {
            m.Status = "processing";
            m.ProcessingStartedAt = now;
            m.AttemptCount += 1;
        }
        await db.SaveChangesAsync(ct);

        foreach (var m in batch)
        {
            await ProcessOneAsync(m, ct);
        }
        return batch.Count;
    }

    private async Task ProcessOneAsync(EmailMessage row, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        try
        {
            var attachments = HydrateAttachments(row.AttachmentsJson);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(row.FromName ?? _emailOptions.FromName, row.FromEmail));
            message.To.Add(new MailboxAddress(row.ToName ?? row.ToEmail, row.ToEmail));
            message.Subject = row.Subject;

            if (attachments.Count > 0)
            {
                var bodyBuilder = new BodyBuilder();
                if (row.IsHtml) bodyBuilder.HtmlBody = row.Body;
                else bodyBuilder.TextBody = row.Body;

                foreach (var att in attachments)
                {
                    try
                    {
                        var contentType = ContentType.Parse(string.IsNullOrWhiteSpace(att.ContentType)
                            ? "application/octet-stream" : att.ContentType);
                        bodyBuilder.Attachments.Add(att.Filename, att.Content, contentType);
                    }
                    catch (Exception attEx)
                    {
                        _logger.LogWarning(attEx, "Skipping invalid attachment {Filename} on email {Id}",
                            att.Filename, row.Id);
                    }
                }
                message.Body = bodyBuilder.ToMessageBody();
            }
            else
            {
                message.Body = row.IsHtml
                    ? new BodyBuilder { HtmlBody = row.Body }.ToMessageBody()
                    : new TextPart("plain") { Text = row.Body };
            }

            using var client = new SmtpClient();
            var secureSocketOptions = _emailOptions.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : _emailOptions.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_emailOptions.SmtpServer, _emailOptions.SmtpPort, secureSocketOptions, ct);

            if (!string.IsNullOrEmpty(_emailOptions.SmtpUsername))
                await client.AuthenticateAsync(_emailOptions.SmtpUsername, _emailOptions.SmtpPassword, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            await db.EmailMessages.Where(m => m.Id == row.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "sent")
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

            _logger.LogInformation("Email sent to {To}: subject='{Subject}' attempt={Attempt}",
                row.ToEmail, row.Subject, row.AttemptCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email attempt {Attempt} failed for {Id} → {To}",
                row.AttemptCount, row.Id, row.ToEmail);
            await HandleFailureAsync(db, row, ex.Message, ct);
        }
    }

    private async Task HandleFailureAsync(CommsDbContext db, EmailMessage row, string error, CancellationToken ct)
    {
        if (row.AttemptCount >= _outboxOptions.MaxAttempts)
        {
            await db.EmailMessages.Where(m => m.Id == row.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "failed")
                    .SetProperty(m => m.ErrorMessage, error)
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

            _logger.LogError("Email {Id} permanently failed after {Attempts} attempts: {Error}",
                row.Id, row.AttemptCount, error);
            return;
        }

        var delay = Math.Min(
            _outboxOptions.BackoffBaseSeconds * (int)Math.Pow(2, Math.Max(0, row.AttemptCount - 1)),
            _outboxOptions.MaxBackoffSeconds);

        await db.EmailMessages.Where(m => m.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, "queued")
                .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddSeconds(delay))
                .SetProperty(m => m.ErrorMessage, error), ct);
    }

    private async Task ReleaseStuckRowsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();
            var cutoff = DateTime.UtcNow.AddMinutes(-_outboxOptions.StuckRowCutoffMinutes);

            var released = await db.EmailMessages
                .Where(m => m.Status == "processing" && m.ProcessingStartedAt < cutoff)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "queued")
                    .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow)
                    .SetProperty(m => m.ProcessingStartedAt, (DateTime?)null), ct);

            if (released > 0)
            {
                _logger.LogWarning("Released {Count} stuck email rows (>{Cutoff}min in processing)",
                    released, _outboxOptions.StuckRowCutoffMinutes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release stuck email rows on startup");
        }
    }

    /// <summary>
    /// Persistence-friendly attachment record — base64 content keeps the JSON
    /// pure ASCII so it round-trips through jsonb without surprises. Public so
    /// <see cref="EmailService"/> can use it for the inbound persistence path.
    /// </summary>
    public sealed class PersistedAttachment
    {
        public string Filename { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
    }

    /// <summary>JSON-serialise the API attachments for outbox storage.</summary>
    public static string? SerializeAttachments(IEnumerable<PersistedAttachment>? attachments)
    {
        var list = attachments?.ToList();
        if (list == null || list.Count == 0) return null;
        return JsonSerializer.Serialize(list);
    }

    /// <summary>Re-hydrate persisted attachments into the in-memory shape the SMTP path expects.</summary>
    private static List<QueuedAttachment> HydrateAttachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var list = JsonSerializer.Deserialize<List<PersistedAttachment>>(json) ?? new();
            return list.Select(a => new QueuedAttachment
            {
                Filename = a.Filename,
                Content = string.IsNullOrEmpty(a.ContentBase64) ? Array.Empty<byte>() : Convert.FromBase64String(a.ContentBase64),
                ContentType = a.ContentType
            }).ToList();
        }
        catch (Exception)
        {
            // Corrupt JSON shouldn't break delivery — send body without attachments.
            return new();
        }
    }
}

/// <summary>
/// Legacy DTOs kept for source compatibility with <c>EmailService</c>. The
/// channel they used to flow through is gone; the worker now hydrates from
/// the persisted row.
/// </summary>
public class EmailQueueItem
{
    public Guid MessageId { get; set; }
    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public List<QueuedAttachment>? Attachments { get; set; }
}

public class QueuedAttachment
{
    public string Filename { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
