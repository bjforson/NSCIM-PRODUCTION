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
        var attachments = DecodeAttachments(request.Attachments);

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
            Status = "queued"
        };

        _db.EmailMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        _queue.Enqueue(new EmailQueueItem
        {
            MessageId = msg.Id,
            FromEmail = msg.FromEmail,
            FromName = msg.FromName,
            ToEmail = msg.ToEmail,
            ToName = msg.ToName,
            Subject = msg.Subject,
            Body = msg.Body,
            IsHtml = msg.IsHtml,
            Attachments = attachments
        });

        return new EmailResponse { Id = msg.Id, Status = "queued" };
    }

    private static List<QueuedAttachment>? DecodeAttachments(List<EmailAttachment>? source)
    {
        if (source == null || source.Count == 0) return null;

        var result = new List<QueuedAttachment>(source.Count);
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

            result.Add(new QueuedAttachment
            {
                Filename = att.Filename,
                Content = bytes,
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
        var sharedAttachments = DecodeAttachments(request.Attachments);

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
            Status = "queued"
        }).ToList();

        _db.EmailMessages.AddRange(messages);
        await _db.SaveChangesAsync(ct);

        foreach (var msg in messages)
        {
            _queue.Enqueue(new EmailQueueItem
            {
                MessageId = msg.Id,
                FromEmail = msg.FromEmail,
                FromName = msg.FromName,
                ToEmail = msg.ToEmail,
                ToName = msg.ToName,
                Subject = msg.Subject,
                Body = msg.Body,
                IsHtml = msg.IsHtml,
                Attachments = sharedAttachments
            });
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
