using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>Replaced by ImageAnalysisOrchestratorService. Not registered in DI. Retained for reference only.</summary>
    [Obsolete("Replaced by ImageAnalysisOrchestratorService. Not registered in DI. Retained for reference only.")]
    public class SubmissionWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubmissionWorker> _logger;
        private readonly IConfiguration _configuration;

        public SubmissionWorker(IServiceScopeFactory scopeFactory, ILogger<SubmissionWorker> logger, IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    // Find groups approved by audit but not yet submitted (including PartiallyCompleted for reprocessing)
                    var candidates = await db.AnalysisGroups
                        .Where(g => g.Status == AnalysisStatuses.AuditCompleted ||
                                   g.Status == "AuditCompleted" ||
                                   g.Status == AnalysisStatuses.PartiallyCompleted) // ✅ NEW: Include partially completed for reprocessing
                        .OrderBy(g => g.CreatedAtUtc)
                        .Take(10)
                        .ToListAsync(stoppingToken);

                    foreach (var g in candidates)
                    {
                        try
                        {
                            // ✅ FIX: Validate status transition BEFORE starting transaction to avoid double-disposal issue
                            var oldStatus = g.Status;
                            var newStatus = AnalysisStatuses.Completed;
                            if (!AnalysisStatusValidator.IsValidTransition(oldStatus, newStatus))
                            {
                                _logger.LogWarning("Invalid status transition attempted: {OldStatus} → {NewStatus} for group {GroupId}. Skipping submission.",
                                    oldStatus, newStatus, g.Id);
                                continue; // Skip this group - no transaction started, so no disposal issue
                            }

                            var subStrategy = db.Database.CreateExecutionStrategy();
                            string? fullPath = null;
                            await subStrategy.ExecuteAsync(async () =>
                            {
                            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                            try
                            {
                                // Use NormalizedGroupIdentifier for joins with ContainerCompletenessStatus
                                var normalizedForLookup = !string.IsNullOrEmpty(g.NormalizedGroupIdentifier)
                                    ? g.NormalizedGroupIdentifier
                                    : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) ?? g.GroupIdentifier);

                                var groupContainers = await db.ContainerCompletenessStatuses
                                    .Where(c => c.GroupIdentifier == normalizedForLookup)
                                    .Select(c => c.ContainerNumber)
                                    .Distinct()
                                    .ToListAsync(stoppingToken);

                                var containersWithImages = await db.ContainerCompletenessStatuses
                                    .Where(c => c.GroupIdentifier == normalizedForLookup && c.HasImageData == true)
                                    .Select(c => c.ContainerNumber)
                                    .Distinct()
                                    .ToListAsync(stoppingToken);

                                var containersWithoutImages = groupContainers
                                    .Where(c => !containersWithImages.Contains(c))
                                    .ToList();

                                var decisionGroupIds = new[] { normalizedForLookup, g.GroupIdentifier }.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                                var analyst = await db.ImageAnalysisDecisions
                                    .Where(d => decisionGroupIds.Contains(d.GroupIdentifier ?? "") &&
                                               containersWithImages.Contains(d.ContainerNumber))
                                    .ToListAsync(stoppingToken);

                                var audits = await db.AuditDecisions
                                    .Where(a => decisionGroupIds.Contains(a.GroupIdentifier ?? "") &&
                                               containersWithImages.Contains(a.ContainerNumber))
                                    .ToListAsync(stoppingToken);

                                var idempotencyKey = $"{g.Id}-{analyst.OrderBy(a => a.Id).FirstOrDefault()?.Id}-{audits.OrderBy(a => a.Id).FirstOrDefault()?.Id}";

                                // ✅ NEW: Only include containers WITH images in payload
                                var payload = new
                                {
                                    idempotencyKey,
                                    group = new { g.Id, g.GroupIdentifier, g.GroupType, g.ScannerType },
                                    analystDecisions = analyst.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.Tags, a.Comments, a.ReviewedBy, a.ReviewedAt }),
                                    auditDecisions = audits.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.AuditNotes, a.AuditedBy, a.AuditedAt }),
                                    // ✅ NEW: Track submission metadata
                                    submissionMetadata = new
                                    {
                                        totalContainerCount = groupContainers.Count,
                                        submittedContainerCount = containersWithImages.Count,
                                        pendingContainerCount = containersWithoutImages.Count,
                                        submittedContainers = containersWithImages,
                                        pendingContainers = containersWithoutImages
                                    }
                                };

                                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                                var outputFolder = _configuration["ICUMS:Submission:OutputFolder"]
                                    ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                                    ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
                                Directory.CreateDirectory(outputFolder);
                                var fileName = $"{g.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{idempotencyKey}.json";
                                fullPath = Path.Combine(outputFolder, fileName);
                                await System.IO.File.WriteAllTextAsync(fullPath, json, Encoding.UTF8, stoppingToken);
                                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));

                                // ✅ NEW: Determine if this is a partial submission
                                var isPartiallyCompleted = containersWithoutImages.Any();

                                // ✅ NEW: Serialize container lists for tracking
                                var submittedContainerNumbersJson = System.Text.Json.JsonSerializer.Serialize(containersWithImages);
                                var pendingContainerNumbersJson = System.Text.Json.JsonSerializer.Serialize(containersWithoutImages);

                                db.AnalysisSubmissions.Add(new AnalysisSubmission
                                {
                                    GroupId = g.Id,
                                    PayloadPath = fullPath,
                                    PayloadHash = hash,
                                    Status = "TestSaved",
                                    // ✅ NEW: Tracking fields for partial submission
                                    TotalContainerCount = groupContainers.Count,
                                    SubmittedContainerCount = containersWithImages.Count,
                                    PendingContainerCount = containersWithoutImages.Count,
                                    IsPartiallyCompleted = isPartiallyCompleted,
                                    PartiallyCompletedDate = isPartiallyCompleted ? DateTime.UtcNow : null,
                                    SubmittedContainerNumbers = submittedContainerNumbersJson,
                                    PendingContainerNumbers = pendingContainerNumbersJson,
                                    CreatedAtUtc = DateTime.UtcNow,
                                    SubmittedAtUtc = DateTime.UtcNow
                                });

                                // ✅ NEW: Set status based on whether this is a partial submission
                                if (isPartiallyCompleted)
                                {
                                    // ✅ Partial submission - set to PartiallyCompleted
                                    g.Status = AnalysisStatuses.PartiallyCompleted;
                                    g.PartiallyCompletedDate = DateTime.UtcNow;
                                    g.TotalContainerCount = groupContainers.Count;
                                    g.SubmittedContainerCount = containersWithImages.Count;
                                    g.PendingContainerCount = containersWithoutImages.Count;
                                    _logger.LogInformation("✅ Partial submission for group {Group}: {Submitted} submitted, {Pending} pending (no images)",
                                        g.GroupIdentifier, containersWithImages.Count, containersWithoutImages.Count);
                                }
                                else
                                {
                                    // ✅ Full submission - set to Completed
                                    g.Status = AnalysisStatuses.Completed;
                                    _logger.LogInformation("✅ Full submission for group {Group}: All {Count} containers submitted",
                                        g.GroupIdentifier, groupContainers.Count);
                                }

                                g.UpdatedAtUtc = DateTime.UtcNow;
                                await db.SaveChangesAsync(stoppingToken);
                                await transaction.CommitAsync(stoppingToken);

                                _logger.LogInformation("✅ Submission completed for group {Group} - Status set to Completed", g.Id);
                            }
                            catch
                            {
                                if (!string.IsNullOrEmpty(fullPath) && System.IO.File.Exists(fullPath))
                                {
                                    try { System.IO.File.Delete(fullPath); } catch { /* best effort */ }
                                }

                                await transaction.RollbackAsync(stoppingToken);
                                throw;
                            }
                            }); // end subStrategy.ExecuteAsync
                        }
                        catch (Exception exGroup)
                        {
                            _logger.LogWarning(exGroup, "Submission failed for group {Group}", g.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SubmissionWorker error");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}


