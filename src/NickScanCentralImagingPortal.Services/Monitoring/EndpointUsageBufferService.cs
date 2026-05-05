using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Buffers endpoint usage records and flushes to DB in bulk every few seconds.
    /// Reduces per-request INSERT round-trips and lock contention under high traffic.
    /// </summary>
    public class EndpointUsageBufferService : BackgroundService, IEndpointUsageBufferService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EndpointUsageBufferService> _logger;
        private readonly IConfiguration _configuration;
        private readonly Channel<EndpointUsageRecord> _channel = Channel.CreateUnbounded<EndpointUsageRecord>(new UnboundedChannelOptions { SingleReader = true });
        private readonly ResiliencePipeline _flushPipeline;

        public EndpointUsageBufferService(
            IServiceScopeFactory scopeFactory,
            ILogger<EndpointUsageBufferService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            // Circuit breaker: isolate DB failures - after 5 consecutive failures, open for 30s
            _flushPipeline = new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(30),
                    OnOpened = args => { logger.LogWarning("EndpointUsageBuffer: Circuit breaker OPEN after {Failures} failures", args.Outcome.Exception?.Message ?? "unknown"); return ValueTask.CompletedTask; },
                    OnClosed = args => { logger.LogInformation("EndpointUsageBuffer: Circuit breaker CLOSED - resuming normal operation"); return ValueTask.CompletedTask; }
                })
                .Build();
        }

        public void Enqueue(EndpointUsageRecord record)
        {
            _channel.Writer.TryWrite(record);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EndpointUsageBufferService started - batching endpoint usage logs");

            var flushIntervalSeconds = _configuration.GetValue<int>("Monitoring:EndpointUsageFlushIntervalSeconds", 10);
            var batchSize = _configuration.GetValue<int>("Monitoring:EndpointUsageBatchSize", 100);
            // Audit 8.13 (Sprint 5G2 follow-up): monotonic iteration counter for
            // the per-iteration heartbeat log line.
            var cycleCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(EndpointUsageBufferService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed time for the
                // heartbeat line emitted at the bottom of the iteration.
                var _cycleStartedAt = DateTime.UtcNow;
                cycleCount++;
                int _flushedThisCycle = 0;
                int _failedThisCycle = 0;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(flushIntervalSeconds), stoppingToken);

                    var batch = new List<EndpointUsageRecord>();
                    while (batch.Count < batchSize && _channel.Reader.TryRead(out var record))
                    {
                        batch.Add(record);
                    }

                    if (batch.Count > 0)
                    {
                        await FlushBatchAsync(batch, stoppingToken);
                        _flushedThisCycle = batch.Count;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "Error in EndpointUsageBufferService flush loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = records flushed in this batch; failed = exceptions
                // caught at the loop level (not per-record DB failures, which
                // are absorbed by the circuit-breaker pipeline).
                _logger.LogIterationSummary(
                    "ENDPOINT-USAGE-BUFFER",
                    cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _flushedThisCycle,
                    itemsSkipped: 0,
                    itemsFailed: _failedThisCycle);
            }

            _logger.LogInformation("EndpointUsageBufferService stopping - flushing remaining records");
            await FlushRemainingAsync(stoppingToken);
        }

        private async Task FlushBatchAsync(List<EndpointUsageRecord> batch, CancellationToken cancellationToken)
        {
            try
            {
                await _flushPipeline.ExecuteAsync(async ct =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var entities = batch.Select(r => new EndpointUsageLog
                    {
                        Endpoint = r.Endpoint,
                        Method = r.Method,
                        StatusCode = r.StatusCode,
                        ResponseTimeMs = r.ResponseTimeMs,
                        IpAddress = r.IpAddress,
                        UserAgent = r.UserAgent,
                        Timestamp = r.Timestamp,
                        IsDeprecated = r.IsDeprecated,
                        IsPhase3Route = r.IsPhase3Route,
                        CorrelationId = r.CorrelationId
                    }).ToList();

                    context.EndpointUsageLogs.AddRange(entities);
                    await context.SaveChangesAsync(ct);
                    _logger.LogDebug("Flushed {Count} endpoint usage records to database", batch.Count);
                }, cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker open - dropping {Count} endpoint usage records (DB unavailable)", batch.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} endpoint usage records", batch.Count);
            }
        }

        private async Task FlushRemainingAsync(CancellationToken cancellationToken)
        {
            var batch = new List<EndpointUsageRecord>();
            while (_channel.Reader.TryRead(out var record))
            {
                batch.Add(record);
            }
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch, cancellationToken);
            }
        }
    }
}
