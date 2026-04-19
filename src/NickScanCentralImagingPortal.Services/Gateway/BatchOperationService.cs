using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Services.Gateway
{
    public class BatchOperationService : IBatchOperationService
    {
        private readonly ILogger<BatchOperationService> _logger;
        private static readonly ConcurrentDictionary<string, BatchOperationResponse> _batchCache = new();

        public BatchOperationService(ILogger<BatchOperationService> logger)
        {
            _logger = logger;
        }

        public async Task<BatchOperationResponse> QueueBatchOperationAsync(BatchOperationRequest request)
        {
            try
            {
                _logger.LogInformation("Queueing batch operation: {OperationType} for {Count} containers",
                    request.OperationType, request.ContainerNumbers.Count);

                var response = new BatchOperationResponse
                {
                    BatchId = Guid.NewGuid().ToString(),
                    OperationType = request.OperationType,
                    Status = request.ProcessAsync ? "Queued" : "Processing",
                    TotalItems = request.ContainerNumbers.Count,
                    ProcessedItems = 0,
                    SuccessfulItems = 0,
                    FailedItems = 0
                };

                // Cache the batch operation
                _batchCache[response.BatchId] = response;

                if (request.ProcessAsync)
                {
                    // Process in background
                    _ = ProcessBatchAsync(response.BatchId, request);
                }
                else
                {
                    // Process immediately
                    await ProcessBatchAsync(response.BatchId, request);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing batch operation");
                throw;
            }
        }

        public Task<BatchOperationResponse> GetBatchStatusAsync(string batchId)
        {
            if (_batchCache.TryGetValue(batchId, out var batch))
            {
                return Task.FromResult(batch);
            }

            return Task.FromResult(new BatchOperationResponse
            {
                BatchId = batchId,
                Status = "NotFound"
            });
        }

        private async Task ProcessBatchAsync(string batchId, BatchOperationRequest request)
        {
            if (!_batchCache.TryGetValue(batchId, out var batch))
                return;

            try
            {
                batch.Status = "Processing";
                batch.StartedAt = DateTime.UtcNow;

                foreach (var containerNumber in request.ContainerNumbers)
                {
                    try
                    {
                        // Simulate processing
                        await Task.Delay(10);

                        batch.Results.Add(new BatchItemResult
                        {
                            ItemId = containerNumber,
                            Status = "Success",
                            Message = "Processed successfully"
                        });

                        batch.ProcessedItems++;
                        batch.SuccessfulItems++;
                    }
                    catch (Exception ex)
                    {
                        batch.Results.Add(new BatchItemResult
                        {
                            ItemId = containerNumber,
                            Status = "Failed",
                            Message = ex.Message
                        });

                        batch.ProcessedItems++;
                        batch.FailedItems++;
                    }
                }

                batch.Status = "Completed";
                batch.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("Batch {BatchId} completed: {Success}/{Total} successful",
                    batchId, batch.SuccessfulItems, batch.TotalItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch {BatchId}", batchId);
                batch.Status = "Failed";
                batch.Errors.Add(ex.Message);
            }
        }
    }
}

