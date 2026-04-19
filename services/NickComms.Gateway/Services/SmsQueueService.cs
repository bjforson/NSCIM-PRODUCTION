using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;

namespace NickComms.Gateway.Services;

public class SmsQueueItem
{
    public Guid MessageId { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class SmsQueueService : BackgroundService
{
    private readonly Channel<SmsQueueItem> _channel = Channel.CreateUnbounded<SmsQueueItem>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsQueueService> _logger;
    private readonly SmsGatewayOptions _options;

    public SmsQueueService(
        IServiceScopeFactory scopeFactory,
        ILogger<SmsQueueService> logger,
        IOptions<SmsGatewayOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public void Enqueue(SmsQueueItem item) => _channel.Writer.TryWrite(item);
    public int QueuedCount => _channel.Reader.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmsQueueService started — drain interval: {Interval}s", _options.DrainIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.Reader.WaitToReadAsync(stoppingToken);

                if (_channel.Reader.TryRead(out var item))
                {
                    await ProcessItemAsync(item, stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(_options.DrainIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SMS queue processing");
                await Task.Delay(5000, stoppingToken);
            }
        }

        // Drain remaining on shutdown
        while (_channel.Reader.TryRead(out var remaining))
        {
            try { await ProcessItemAsync(remaining, CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "Error draining SMS {Id}", remaining.MessageId); }
        }
    }

    private async Task ProcessItemAsync(SmsQueueItem item, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubtel = scope.ServiceProvider.GetRequiredService<IHubtelClient>();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        try
        {
            var result = await hubtel.SendSmsAsync(item.From, item.To, item.Content, ct);

            if (result != null)
            {
                await db.SmsMessages.Where(m => m.Id == item.MessageId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Status, result.Status == 0 ? "sent" : "failed")
                        .SetProperty(m => m.HubtelMessageId, result.MessageId)
                        .SetProperty(m => m.HubtelRate, result.Rate)
                        .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

                _logger.LogInformation("SMS sent to {To}: hubtelId={HubtelId} rate={Rate}", item.To, result.MessageId, result.Rate);
            }
            else
            {
                await db.SmsMessages.Where(m => m.Id == item.MessageId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Status, "failed")
                        .SetProperty(m => m.ErrorMessage, "Hubtel API returned null response")
                        .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS {Id} to {To}", item.MessageId, item.To);
            await db.SmsMessages.Where(m => m.Id == item.MessageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "failed")
                    .SetProperty(m => m.ErrorMessage, ex.Message)
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);
        }
    }
}
