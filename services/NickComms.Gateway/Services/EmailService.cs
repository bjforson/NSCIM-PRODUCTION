using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Entities;
using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public class EmailService : IEmailService
{
    private const long MaxAttachmentBytes = 10L * 1024 * 1024; // 10 MB total per email

    private readonly CommsDbContext _db;
    private readonly EmailQueueService _queue;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        CommsDbContext db,
        EmailQueueService queue,
        IOptions<EmailOptions> emailOptions,
        ILogger<EmailService> logger)
    {
        _db = db;
        _queue = queue;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task<EmailResponse> SendSingleAsync(SendEmailRequest request, string clientApp, CancellationToken ct = default)
    {
        // Outbox: attachments are persisted on the row as base64 JSON so a
        // crash between ACK and SMTP send can't lose them. We still validate
        // up-front so a malformed/oversized request fails fast at 4xx.
        var persisted = ValidateAndPersistAttachments(request.Attachments);

        var msg = new EmailMessage
        {
            FromEmail = request.FromEmail ?? _emailOptions.FromEmail,
            FromName = request.FromName ?? _emailOptions.FromName,
            ToEmail = request.To,
            ToName = request.ToName,
            Subject = request.Subject,
            Body = request.Body,
            IsHtml = request.IsHtml,
            ClientApp = clientApp,
            ClientReference = request.ClientReference,
            Status = "queued",
            AttachmentsJson = EmailQueueService.SerializeAttachments(persisted)
        };

        _db.EmailMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        // Enqueue is now a no-op kept for backward compat — the DB row IS
        // the queue signal. The outbox worker polls and picks it up.
        _queue.Enqueue(new EmailQueueItem { MessageId = msg.Id });

        return new EmailResponse { Id = msg.Id, Status = "queued" };
    }

    /// <summary>
    /// Validates each attachment (decodes the base64 to verify it parses,
    /// enforces the 10 MB cap) and returns the list in
    /// <see cref="EmailQueueService.PersistedAttachment"/> shape ready for
    /// JSON-serialised outbox storage. We DON'T cache the decoded bytes here
    /// — the worker decodes them on demand from the persisted JSON.
    /// </summary>
    private static List<EmailQueueService.PersistedAttachment>? ValidateAndPersistAttachments(List<EmailAttachment>? source)
    {
        if (source == null || source.Count == 0) return null;

        var result = new List<EmailQueueService.PersistedAttachment>(source.Count);
        long total = 0;

        foreach (var att in source)
        {
            if (string.IsNullOrWhiteSpace(att.Filename) || string.IsNullOrWhiteSpace(att.ContentBase64))
                throw new ArgumentException($"Attachment '{att.Filename}' has empty filename or content.");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(att.ContentBase64); }
            catch (FormatException) { throw new ArgumentException($"Attachment '{att.Filename}' has invalid base64 content."); }

            total += bytes.LongLength;
            if (total > MaxAttachmentBytes)
                throw new ArgumentException($"Attachments exceed maximum size of {MaxAttachmentBytes / (1024 * 1024)} MB.");

            result.Add(new EmailQueueService.PersistedAttachment
            {
                Filename = att.Filename,
                ContentBase64 = att.ContentBase64,
                ContentType = string.IsNullOrWhiteSpace(att.ContentType) ? "application/octet-stream" : att.ContentType
            });
        }

        return result;
    }

    public async Task<BulkEmailResponse> SendBulkAsync(BulkEmailRequest request, string clientApp, CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var fromEmail = request.FromEmail ?? _emailOptions.FromEmail;
        var fromName = request.FromName ?? _emailOptions.FromName;
        var persistedShared = ValidateAndPersistAttachments(request.Attachments);
        var sharedJson = EmailQueueService.SerializeAttachments(persistedShared);

        var messages = request.Recipients.Select(r => new EmailMessage
        {
            FromEmail = fromEmail,
            FromName = fromName,
            ToEmail = r.Email,
            ToName = r.Name,
            Subject = request.Subject,
            Body = request.Body,
            IsHtml = request.IsHtml,
            BatchId = batchId,
            ClientApp = clientApp,
            ClientReference = request.ClientReference,
            Status = "queued",
            // Each row gets its own copy of the same shared attachment JSON
            // so the outbox path is uniform regardless of single vs bulk.
            AttachmentsJson = sharedJson
        }).ToList();

        _db.EmailMessages.AddRange(messages);
        await _db.SaveChangesAsync(ct);

        // No-op enqueue for backward compat; outbox worker picks up via poll.
        foreach (var msg in messages)
        {
            _queue.Enqueue(new EmailQueueItem { MessageId = msg.Id });
        }

        _logger.LogInformation("Bulk email queued: batch={BatchId} count={Count} by {App}", batchId, messages.Count, clientApp);

        return new BulkEmailResponse
        {
            BatchId = batchId,
            AcceptedCount = messages.Count,
            Message = $"{messages.Count} emails queued for delivery"
        };
    }

    public async Task<EmailStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct = default)
    {
        var msg = await _db.EmailMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg == null) return null;

        return new EmailStatusResponse
        {
            Id = msg.Id,
            ToEmail = msg.ToEmail,
            Subject = msg.Subject,
            Status = msg.Status,
            CreatedAt = msg.CreatedAt,
            SentAt = msg.SentAt,
            ErrorMessage = msg.ErrorMessage
        };
    }
}
