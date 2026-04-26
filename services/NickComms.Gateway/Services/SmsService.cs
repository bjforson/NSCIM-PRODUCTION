using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Entities;
using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public class SmsService : ISmsService
{
    private readonly CommsDbContext _db;
    private readonly SmsQueueService _queue;
    private readonly IHubtelClient _hubtel;
    private readonly HubtelOptions _hubtelOptions;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        CommsDbContext db,
        SmsQueueService queue,
        IHubtelClient hubtel,
        IOptions<HubtelOptions> hubtelOptions,
        ILogger<SmsService> logger)
    {
        _db = db;
        _queue = queue;
        _hubtel = hubtel;
        _hubtelOptions = hubtelOptions.Value;
        _logger = logger;
    }

    public async Task<SmsResponse> SendSingleAsync(SendSmsRequest request, string clientApp, CancellationToken ct = default)
    {
        var senderId = request.From ?? _hubtelOptions.DefaultSenderId;

        var msg = new SmsMessage
        {
            SenderId = senderId,
            Recipient = request.To,
            Content = request.Content,
            ClientApp = clientApp,
            ClientReference = request.ClientReference,
            Status = "queued"
        };

        _db.SmsMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        // Outbox: the DB row IS the queue signal; the polling worker picks it up
        // on its next tick. Enqueue() remains as a no-op for source compat.
        _queue.Enqueue(new SmsQueueItem { MessageId = msg.Id });

        return new SmsResponse { Id = msg.Id, Status = "queued" };
    }

    public async Task<BulkSmsResponse> SendBulkAsync(BulkSmsRequest request, string clientApp, CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var senderId = request.From ?? _hubtelOptions.DefaultSenderId;

        var messages = request.Recipients.Select(to => new SmsMessage
        {
            SenderId = senderId,
            Recipient = to,
            Content = request.Content,
            BatchId = batchId,
            ClientApp = clientApp,
            ClientReference = request.ClientReference,
            Status = "queued"
        }).ToList();

        _db.SmsMessages.AddRange(messages);
        await _db.SaveChangesAsync(ct);

        // Outbox no-op — DB row commit IS the queue signal.
        foreach (var msg in messages)
        {
            _queue.Enqueue(new SmsQueueItem { MessageId = msg.Id });
        }

        _logger.LogInformation("Bulk SMS queued: batch={BatchId} count={Count} by {App}", batchId, messages.Count, clientApp);

        return new BulkSmsResponse
        {
            BatchId = batchId,
            AcceptedCount = messages.Count,
            Message = $"{messages.Count} messages queued for delivery"
        };
    }

    public async Task<SmsStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct = default)
    {
        var msg = await _db.SmsMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg == null) return null;

        // If sent, try refreshing from Hubtel
        if (!string.IsNullOrEmpty(msg.HubtelMessageId) && msg.Status == "sent")
        {
            var hubtelStatus = await _hubtel.GetSmsStatusAsync(msg.HubtelMessageId, ct);
            if (hubtelStatus != null)
            {
                return new SmsStatusResponse
                {
                    Id = msg.Id,
                    Recipient = msg.Recipient,
                    Status = hubtelStatus.Status?.ToLowerInvariant() ?? msg.Status,
                    HubtelMessageId = msg.HubtelMessageId,
                    HubtelStatus = hubtelStatus.Status,
                    Rate = msg.HubtelRate,
                    CreatedAt = msg.CreatedAt,
                    SentAt = msg.SentAt,
                    ErrorMessage = msg.ErrorMessage
                };
            }
        }

        return new SmsStatusResponse
        {
            Id = msg.Id,
            Recipient = msg.Recipient,
            Status = msg.Status,
            HubtelMessageId = msg.HubtelMessageId,
            Rate = msg.HubtelRate,
            CreatedAt = msg.CreatedAt,
            SentAt = msg.SentAt,
            ErrorMessage = msg.ErrorMessage
        };
    }
}
