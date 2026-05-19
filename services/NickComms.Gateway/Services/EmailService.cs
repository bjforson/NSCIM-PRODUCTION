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
    private const string DedupeClientReferencePrefix = "dedupe:";

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
        if (IsDedupeClientReference(request.ClientReference))
        {
            var duplicate = await FindDuplicateEmailAsync(clientApp, request.To, request.ClientReference!, ct);
            if (duplicate != null)
            {
                _logger.LogInformation(
                    "Email duplicate suppressed: ref={ClientReference} to={To} existing={Id} status={Status}",
                    request.ClientReference, request.To, duplicate.Id, duplicate.Status);

                return new EmailResponse
                {
                    Id = duplicate.Id,
                    Status = duplicate.Status,
                    DuplicateSuppressed = true
                };
            }
        }

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
        var duplicates = IsDedupeClientReference(request.ClientReference)
            ? await FindDuplicateEmailsAsync(clientApp, request.Recipients.Select(r => r.Email), request.ClientReference!, ct)
            : new Dictionary<string, EmailMessage>(StringComparer.OrdinalIgnoreCase);

        var duplicateSuppressedCount = 0;
        var messages = new List<EmailMessage>();
        foreach (var r in request.Recipients)
        {
            if (duplicates.ContainsKey(NormalizeEmail(r.Email)))
            {
                duplicateSuppressedCount++;
                continue;
            }

            messages.Add(new EmailMessage
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
            });
        }

        if (messages.Count > 0)
        {
            _db.EmailMessages.AddRange(messages);
            await _db.SaveChangesAsync(ct);

            // No-op enqueue for backward compat; outbox worker picks up via poll.
            foreach (var msg in messages)
            {
                _queue.Enqueue(new EmailQueueItem { MessageId = msg.Id });
            }
        }

        _logger.LogInformation(
            "Bulk email queued: batch={BatchId} count={Count} suppressed={Suppressed} by {App}",
            batchId, messages.Count, duplicateSuppressedCount, clientApp);

        return new BulkEmailResponse
        {
            BatchId = batchId,
            AcceptedCount = messages.Count,
            DuplicateSuppressedCount = duplicateSuppressedCount,
            Message = duplicateSuppressedCount == 0
                ? $"{messages.Count} emails queued for delivery"
                : $"{messages.Count} emails queued for delivery; {duplicateSuppressedCount} duplicate(s) suppressed"
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

    private async Task<EmailMessage?> FindDuplicateEmailAsync(
        string clientApp,
        string toEmail,
        string clientReference,
        CancellationToken ct)
    {
        var normalizedTo = NormalizeEmail(toEmail);
        return await _db.EmailMessages
            .AsNoTracking()
            .Where(m => m.ClientApp == clientApp && m.ClientReference == clientReference)
            .Where(m => m.ToEmail.ToLower() == normalizedTo)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<string, EmailMessage>> FindDuplicateEmailsAsync(
        string clientApp,
        IEnumerable<string> toEmails,
        string clientReference,
        CancellationToken ct)
    {
        var normalizedEmails = toEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(NormalizeEmail)
            .Distinct()
            .ToList();

        if (normalizedEmails.Count == 0)
        {
            return new Dictionary<string, EmailMessage>(StringComparer.OrdinalIgnoreCase);
        }

        var existing = await _db.EmailMessages
            .AsNoTracking()
            .Where(m => m.ClientApp == clientApp && m.ClientReference == clientReference)
            .Where(m => normalizedEmails.Contains(m.ToEmail.ToLower()))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return existing
            .GroupBy(m => NormalizeEmail(m.ToEmail), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDedupeClientReference(string? clientReference)
        => clientReference?.StartsWith(DedupeClientReferencePrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}
