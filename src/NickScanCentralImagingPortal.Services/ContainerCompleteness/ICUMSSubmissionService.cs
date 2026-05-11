using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for submitting container data back to ICUMS.
    ///
    /// 2026-05-05 (Sprint 2C, audit 5.04): the IHostedService registration for
    /// this class has been removed in <c>ServiceConfiguration.cs</c>. The class
    /// inherits from <see cref="BackgroundService"/> for legacy reasons but its
    /// <see cref="ExecuteAsync"/> loop is no longer started by the host, so
    /// <see cref="SimulateICUMSSubmissionAsync"/> is now disarmed and cannot
    /// fabricate fake success responses against the queue.
    ///
    /// The Singleton + IICUMSSubmissionService DI registrations are still wired so
    /// <c>ContainerValidationController.ApproveContainer</c> can call
    /// <see cref="QueueForSubmissionAsync"/> — the DB enqueue is real. A future
    /// sprint will either add a real consumer or repoint the controller at the
    /// file-based ICUMS Outbox used by <c>ImageAnalysisOrchestratorService.RunSubmissionWorkflowAsync</c>.
    /// </summary>
    public class ICUMSSubmissionService : BackgroundService, IICUMSSubmissionService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ICUMSSubmissionService> _logger;
        private readonly int _maxConcurrentSubmissions = 3;
        private const string SERVICE_ID = "[ICUMS-SUBMISSION]";

        public ICUMSSubmissionService(IServiceProvider serviceProvider, ILogger<ICUMSSubmissionService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} ICUMSSubmissionService started - processing submissions at configured interval from settings", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubmissionQueueAsync(stoppingToken);
                    _logger.LogDebug("ICUMS submission processing completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error during ICUMS submission processing", SERVICE_ID);
                }

                // Wait for configured interval (read from database settings)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    var submissionIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "ICUMS.SubmissionIntervalMinutes", 10);
                    _logger.LogDebug("⏰ Next ICUMS submission in {Interval} minutes (from settings)", submissionIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(submissionIntervalMinutes), stoppingToken);
                }
            }

            _logger.LogInformation("{ServiceId} ICUMSSubmissionService stopped", SERVICE_ID);
        }

        public async Task ProcessSubmissionQueueAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            _logger.LogInformation("{ServiceId} Starting ICUMS submission queue processing", SERVICE_ID);

            try
            {
                // Get pending submissions ordered by priority and creation time
                var pendingSubmissions = await dbContext.ICUMSSubmissionQueues
                    .Where(s => s.Status == ICUMSSubmissionQueueStatus.Pending ||
                               (s.Status == ICUMSSubmissionQueueStatus.Failed && s.RetryCount < 3 &&
                                (s.NextRetryAt == null || s.NextRetryAt <= DateTime.UtcNow)))
                    .OrderByDescending(s => s.Priority)
                    .ThenBy(s => s.CreatedAt)
                    .Take(_maxConcurrentSubmissions)
                    .ToListAsync();

                _logger.LogInformation("{ServiceId} Found {Count} submissions to process", SERVICE_ID, pendingSubmissions.Count);

                var processedCount = 0;
                var semaphore = new SemaphoreSlim(_maxConcurrentSubmissions);

                var tasks = pendingSubmissions.Select(async submission =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        await ProcessSubmissionAsync(submission);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing submission {SubmissionId} for container {ContainerNumber}",
                            submission.Id, submission.ContainerNumber);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                _logger.LogInformation("{ServiceId} ICUMS submission processing completed - processed {Count} submissions", SERVICE_ID, processedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ICUMS submission queue processing");
                throw;
            }
        }

        public async Task<SubmissionResult> SubmitToICUMSAsync(ContainerSubmissionData submissionData)
        {
            // 2026-05-05 (Sprint 2C, audit 5.04): deprecation warning. This method
            // routes through SimulateICUMSSubmissionAsync (line 404) which fabricates
            // success responses. The hosted-service drainer that used to call it has
            // been removed; this path remains only because IICUMSSubmissionService is
            // still resolvable from DI. Anyone landing here directly is bypassing the
            // real submission pipeline at ImageAnalysisOrchestratorService.RunSubmissionWorkflowAsync.
            _logger.LogWarning(
                "{ServiceId} DEPRECATED: SubmitToICUMSAsync called for {ContainerNumber} — this path is a simulator stub and does NOT submit to ICUMS. Use the orchestrator's RunSubmissionWorkflowAsync (file-based Outbox) instead.",
                SERVICE_ID, submissionData.ContainerNumber);

            var result = new SubmissionResult();

            try
            {
                // Prepare submission payload
                var payload = await PrepareSubmissionPayloadAsync(submissionData);

                // TODO: Implement actual ICUMS API call
                // For now, simulate a successful submission
                await SimulateICUMSSubmissionAsync(submissionData, payload);

                result.IsSuccess = true;
                result.ICUMSResponseId = $"ICUMS-{Guid.NewGuid():N}";
                result.ResponseData["SubmissionId"] = result.ICUMSResponseId;
                result.ResponseData["ContainerNumber"] = submissionData.ContainerNumber;
                result.ResponseData["ImagesCount"] = submissionData.ImagePaths.Count;

                _logger.LogInformation("Successfully submitted container {ContainerNumber} to ICUMS with response ID {ResponseId}",
                    submissionData.ContainerNumber, result.ICUMSResponseId);
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Failed to submit container {ContainerNumber} to ICUMS", submissionData.ContainerNumber);
            }

            return result;
        }

        public async Task<ICUMSSubmissionQueue> QueueForSubmissionAsync(ContainerSubmissionData submissionData, int priority = 1, string? submittedBy = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var submission = new ICUMSSubmissionQueue
            {
                ContainerNumber = submissionData.ContainerNumber,
                ScannerType = submissionData.ScannerType,
                ImagePaths = JsonSerializer.Serialize(submissionData.ImagePaths),
                ReportData = JsonSerializer.Serialize(submissionData.ReportData),
                Status = ICUMSSubmissionQueueStatus.Pending,
                Priority = priority,
                SubmittedBy = submittedBy ?? "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            dbContext.ICUMSSubmissionQueues.Add(submission);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Queued container {ContainerNumber} for ICUMS submission with priority {Priority}",
                submissionData.ContainerNumber, priority);

            return submission;
        }

        public async Task<SubmissionStatistics> GetSubmissionStatisticsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var stats = new SubmissionStatistics();

            try
            {
                // ✅ OPTIMIZED: Use database aggregation instead of loading all records into memory
                stats.TotalSubmissions = await dbContext.ICUMSSubmissionQueues.CountAsync();
                stats.PendingSubmissions = await dbContext.ICUMSSubmissionQueues.CountAsync(s => s.Status == ICUMSSubmissionQueueStatus.Pending);
                stats.ProcessingSubmissions = await dbContext.ICUMSSubmissionQueues.CountAsync(s => s.Status == ICUMSSubmissionQueueStatus.Processing);
                stats.CompletedSubmissions = await dbContext.ICUMSSubmissionQueues.CountAsync(s => s.Status == ICUMSSubmissionQueueStatus.Submitted);
                stats.FailedSubmissions = await dbContext.ICUMSSubmissionQueues.CountAsync(s => s.Status == ICUMSSubmissionQueueStatus.Failed);
                stats.RetryCount = await dbContext.ICUMSSubmissionQueues.SumAsync(s => s.RetryCount);
                stats.LastSubmissionAt = await dbContext.ICUMSSubmissionQueues.AnyAsync()
                    ? await dbContext.ICUMSSubmissionQueues.MaxAsync(s => s.CreatedAt)
                    : DateTime.MinValue;

                // Calculate average processing time using database aggregation
                var completedCount = await dbContext.ICUMSSubmissionQueues
                    .CountAsync(s => s.Status == ICUMSSubmissionQueueStatus.Submitted && s.CompletedAt.HasValue);

                if (completedCount > 0)
                {
                    var avgMilliseconds = await dbContext.ICUMSSubmissionQueues
                        .Where(s => s.Status == ICUMSSubmissionQueueStatus.Submitted && s.CompletedAt.HasValue)
                        .AverageAsync(s => (double)((s.CompletedAt!.Value - s.CreatedAt).TotalMilliseconds));
                    stats.AverageProcessingTime = TimeSpan.FromMilliseconds(avgMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting submission statistics");
            }

            return stats;
        }

        public async Task RetryFailedSubmissionsAsync(int maxRetries = 3)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var failedSubmissions = await dbContext.ICUMSSubmissionQueues
                .Where(s => s.Status == ICUMSSubmissionQueueStatus.Failed && s.RetryCount < maxRetries)
                .ToListAsync();

            _logger.LogInformation("Retrying {Count} failed submissions", failedSubmissions.Count);

            foreach (var submission in failedSubmissions)
            {
                submission.Status = ICUMSSubmissionQueueStatus.Pending;
                submission.RetryCount++;
                submission.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, submission.RetryCount)); // Exponential backoff
                submission.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Reset {Count} failed submissions for retry", failedSubmissions.Count);
        }

        private async Task ProcessSubmissionAsync(ICUMSSubmissionQueue submission)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                _logger.LogDebug("Processing submission {SubmissionId} for container {ContainerNumber}",
                    submission.Id, submission.ContainerNumber);

                // Update status to Processing
                submission.Status = ICUMSSubmissionQueueStatus.Processing;
                submission.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();

                // Parse submission data
                var imagePaths = JsonSerializer.Deserialize<List<string>>(submission.ImagePaths) ?? new List<string>();
                var reportData = JsonSerializer.Deserialize<Dictionary<string, object>>(submission.ReportData) ?? new Dictionary<string, object>();

                var submissionData = new ContainerSubmissionData
                {
                    ContainerNumber = submission.ContainerNumber,
                    ScannerType = submission.ScannerType,
                    ImagePaths = imagePaths,
                    ReportData = reportData
                };

                // Submit to ICUMS
                var result = await SubmitToICUMSAsync(submissionData);

                if (result.IsSuccess)
                {
                    // Update submission as successful
                    submission.Status = ICUMSSubmissionQueueStatus.Submitted;
                    submission.SubmittedAt = DateTime.UtcNow;
                    submission.ICUMSResponseId = result.ICUMSResponseId;
                    submission.CompletedAt = submission.SubmittedAt;
                    submission.ErrorMessage = null;
                }
                else
                {
                    // Update submission as failed
                    submission.Status = ICUMSSubmissionQueueStatus.Failed;
                    submission.ErrorMessage = result.ErrorMessage;
                    submission.RetryCount++;
                    submission.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, submission.RetryCount));
                }

                submission.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("Processed submission {SubmissionId} for container {ContainerNumber} with status {Status}",
                    submission.Id, submission.ContainerNumber, submission.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing submission {SubmissionId}", submission.Id);

                // Update submission as failed
                submission.Status = ICUMSSubmissionQueueStatus.Failed;
                submission.ErrorMessage = ex.Message;
                submission.RetryCount++;
                submission.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, submission.RetryCount));
                submission.UpdatedAt = DateTime.UtcNow;

                await dbContext.SaveChangesAsync();
            }
        }

        private async Task<Dictionary<string, object>> PrepareSubmissionPayloadAsync(ContainerSubmissionData submissionData)
        {
            // Look up annotations for this container and burn them onto images
            var annotatedImages = new List<object>();
            string? suspiciousAreas = null;
            string? tags = null;

            try
            {
                using var annotationScope = _serviceProvider.CreateScope();
                var dbContext = annotationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var decision = await dbContext.ImageAnalysisDecisions
                    .FirstOrDefaultAsync(d => d.ContainerNumber == submissionData.ContainerNumber
                        && d.ScannerType == submissionData.ScannerType);

                if (decision != null)
                {
                    suspiciousAreas = decision.SuspiciousAreas;
                    tags = decision.Tags;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Failed to load annotations for {Container} (non-fatal, submitting without annotations)",
                    SERVICE_ID, submissionData.ContainerNumber);
            }

            bool hasAnnotations = !string.IsNullOrWhiteSpace(suspiciousAreas) || !string.IsNullOrWhiteSpace(tags);

            foreach (var path in submissionData.ImagePaths)
            {
                long size = 0;
                if (File.Exists(path))
                {
                    if (hasAnnotations)
                    {
                        try
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var renderer = scope.ServiceProvider.GetService<IImageAnnotationRenderer>();
                            if (renderer != null)
                            {
                                var rawBytes = await File.ReadAllBytesAsync(path);
                                var annotatedBytes = await renderer.RenderAnnotationsAsync(rawBytes, suspiciousAreas, tags);
                                // Write annotated image to a temp file for submission
                                var annotatedPath = Path.Combine(Path.GetTempPath(), $"nscim_annotated_{Path.GetFileName(path)}");
                                await File.WriteAllBytesAsync(annotatedPath, annotatedBytes);
                                annotatedImages.Add(new
                                {
                                    Path = annotatedPath,
                                    OriginalPath = path,
                                    Type = Path.GetExtension(path).ToLowerInvariant(),
                                    Size = annotatedBytes.Length,
                                    HasAnnotations = true
                                });
                                _logger.LogInformation("{ServiceId} Burned annotations onto image for {Container}: {Path} ({OrigSize} -> {NewSize} bytes)",
                                    SERVICE_ID, submissionData.ContainerNumber, Path.GetFileName(path), rawBytes.Length, annotatedBytes.Length);
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{ServiceId} Failed to burn annotations onto {Path} (submitting raw image)",
                                SERVICE_ID, path);
                        }
                    }

                    size = new FileInfo(path).Length;
                }

                annotatedImages.Add(new
                {
                    Path = path,
                    OriginalPath = path,
                    Type = Path.GetExtension(path).ToLowerInvariant(),
                    Size = size,
                    HasAnnotations = false
                });
            }

            var payload = new Dictionary<string, object>
            {
                ["ContainerNumber"] = submissionData.ContainerNumber,
                ["ScannerType"] = submissionData.ScannerType,
                ["ScanDate"] = submissionData.ScanDate,
                ["ICUMSDataDate"] = submissionData.ICUMSDataDate,
                ["Images"] = annotatedImages,
                ["ReportData"] = submissionData.ReportData,
                ["SubmissionId"] = Guid.NewGuid().ToString(),
                ["SubmittedAt"] = DateTime.UtcNow
            };

            _logger.LogDebug("Prepared submission payload for container {ContainerNumber} with {ImageCount} images ({AnnotatedCount} annotated)",
                submissionData.ContainerNumber, submissionData.ImagePaths.Count,
                annotatedImages.Count(i => ((dynamic)i).HasAnnotations));

            return payload;
        }

        private async Task SimulateICUMSSubmissionAsync(ContainerSubmissionData submissionData, Dictionary<string, object> payload)
        {
            // Simulate API call delay
            await Task.Delay(Random.Shared.Next(1000, 3000));

            // Simulate occasional failures (10% chance)
            if (Random.Shared.NextDouble() < 0.1)
            {
                throw new Exception("Simulated ICUMS API failure");
            }

            _logger.LogDebug("Simulated successful ICUMS submission for container {ContainerNumber}", submissionData.ContainerNumber);
        }
    }
}
