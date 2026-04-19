using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;

namespace NickComms.Gateway.Services;

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

public class EmailQueueService : BackgroundService
{
    private readonly Channel<EmailQueueItem> _channel = Channel.CreateUnbounded<EmailQueueItem>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueService> _logger;
    private readonly EmailOptions _options;

    public EmailQueueService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueService> logger,
        IOptions<EmailOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public void Enqueue(EmailQueueItem item) => _channel.Writer.TryWrite(item);
    public int QueuedCount => _channel.Reader.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailQueueService started — drain interval: {Interval}s", _options.DrainIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.Reader.WaitToReadAsync(stoppingToken);

                if (_channel.Reader.TryRead(out var item))
                {
                    await ProcessItemAsync(item, stoppingToken);

                    if (_options.DrainIntervalSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(_options.DrainIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in email queue processing");
                await Task.Delay(5000, stoppingToken);
            }
        }

        while (_channel.Reader.TryRead(out var remaining))
        {
            try { await ProcessItemAsync(remaining, CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "Error draining email {Id}", remaining.MessageId); }
        }
    }

    private async Task ProcessItemAsync(EmailQueueItem item, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(item.FromName ?? _options.FromName, item.FromEmail));
            message.To.Add(new MailboxAddress(item.ToName ?? item.ToEmail, item.ToEmail));
            message.Subject = item.Subject;

            if (item.Attachments != null && item.Attachments.Count > 0)
            {
                var bodyBuilder = new BodyBuilder();
                if (item.IsHtml) bodyBuilder.HtmlBody = item.Body;
                else bodyBuilder.TextBody = item.Body;

                foreach (var att in item.Attachments)
                {
                    try
                    {
                        var contentType = ContentType.Parse(string.IsNullOrWhiteSpace(att.ContentType)
                            ? "application/octet-stream" : att.ContentType);
                        bodyBuilder.Attachments.Add(att.Filename, att.Content, contentType);
                    }
                    catch (Exception attEx)
                    {
                        _logger.LogWarning(attEx, "Skipping invalid attachment {Filename} on email {Id}", att.Filename, item.MessageId);
                    }
                }

                message.Body = bodyBuilder.ToMessageBody();
            }
            else
            {
                message.Body = item.IsHtml
                    ? new BodyBuilder { HtmlBody = item.Body }.ToMessageBody()
                    : new TextPart("plain") { Text = item.Body };
            }

            using var client = new SmtpClient();
            // Port 465 = implicit SSL (SslOnConnect), Port 587 = STARTTLS
            var secureSocketOptions = _options.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : _options.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_options.SmtpServer, _options.SmtpPort, secureSocketOptions, ct);

            if (!string.IsNullOrEmpty(_options.SmtpUsername))
                await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            await db.EmailMessages.Where(m => m.Id == item.MessageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "sent")
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

            _logger.LogInformation("Email sent to {To}: subject={Subject}", item.ToEmail, item.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email {Id} to {To}", item.MessageId, item.ToEmail);
            await db.EmailMessages.Where(m => m.Id == item.MessageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "failed")
                    .SetProperty(m => m.ErrorMessage, ex.Message)
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);
        }
    }
}
