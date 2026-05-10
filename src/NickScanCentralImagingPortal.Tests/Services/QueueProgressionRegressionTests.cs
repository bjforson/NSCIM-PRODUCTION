using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    /// <summary>
    /// Regression coverage for the image-analysis queue/progression fixes.
    ///
    /// We intentionally do not add a unique filtered AnalysisAssignments constraint
    /// here. Several assignment paths disagree about whether an "Active" row with an
    /// expired lease still blocks a new assignment, so enforcing that in the model
    /// needs a migration/backfill and lease-policy cleanup first.
    /// </summary>
    public class QueueProgressionRegressionTests
    {
        private static ApplicationDbContext CreateAppDb(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName ?? $"QueueProgression_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .EnableServiceProviderCaching(false)
                .Options;

            return new ApplicationDbContext(options);
        }

        private static IcumDownloadsDbContext CreateIcumDb()
        {
            var options = new DbContextOptionsBuilder<IcumDownloadsDbContext>()
                .UseInMemoryDatabase($"QueueProgression_Icum_{Guid.NewGuid():N}")
                .EnableServiceProviderCaching(false)
                .Options;

            return new IcumDownloadsDbContext(options);
        }

        [Fact]
        public async Task GetMyAssignments_FastPath_IgnoresReleasedAndExpiredQueueRows()
        {
            var dbName = $"QueueProgression_Controller_{Guid.NewGuid():N}";
            await using var db = CreateAppDb(dbName);
            await using var icumDb = CreateIcumDb();
            await using var provider = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName).EnableServiceProviderCaching(false))
                .BuildServiceProvider();
            using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

            var now = DateTime.UtcNow;
            var liveGroup = await SeedAssignedGroupAsync(db, "LIVE-GROUP", "FS6000");
            var releasedGroup = await SeedAssignedGroupAsync(db, "RELEASED-GROUP", "FS6000");
            var expiredGroup = await SeedAssignedGroupAsync(db, "EXPIRED-GROUP", "FS6000");

            var live = new AnalysisAssignment
            {
                GroupId = liveGroup.Id,
                AssignedTo = "analyst-one",
                Role = "Analyst",
                State = "Active",
                LeaseUntilUtc = now.AddMinutes(20),
                CreatedAtUtc = now
            };
            var released = new AnalysisAssignment
            {
                GroupId = releasedGroup.Id,
                AssignedTo = "analyst-one",
                Role = "Analyst",
                State = "Released",
                LeaseUntilUtc = now.AddMinutes(20),
                CreatedAtUtc = now.AddMinutes(-10)
            };
            var expired = new AnalysisAssignment
            {
                GroupId = expiredGroup.Id,
                AssignedTo = "analyst-one",
                Role = "Analyst",
                State = "Active",
                LeaseUntilUtc = now.AddMinutes(-1),
                CreatedAtUtc = now.AddMinutes(-20)
            };

            db.AnalysisAssignments.AddRange(live, released, expired);
            await db.SaveChangesAsync();

            db.AnalysisQueueEntries.AddRange(
                QueueEntryFor(live, liveGroup, "LIVE-CONT"),
                QueueEntryFor(released, releasedGroup, "RELEASED-CONT"),
                QueueEntryFor(expired, expiredGroup, "EXPIRED-CONT"));
            await db.SaveChangesAsync();

            var controller = new ImageAnalysisController(
                db,
                icumDb,
                NullLogger<ImageAnalysisController>.Instance,
                new ConfigurationBuilder().Build(),
                cache,
                new StubImageAnalysisFacade(),
                provider.GetRequiredService<IServiceScopeFactory>());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.Name, "analyst-one"),
                            new Claim(ClaimTypes.Role, "Analyst")
                        },
                        authenticationType: "test")),
                    RequestServices = provider
                }
            };

            var result = await controller.GetMyAssignments("Analyst");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var assignments = Assert.IsAssignableFrom<List<ImageAnalysisController.MyAssignmentResponse>>(ok.Value);
            var groupIdentifiers = assignments.Select(a => a.GroupIdentifier).ToArray();

            Assert.Equal(new[] { "LIVE-GROUP" }, groupIdentifiers);
        }

        [Fact]
        public async Task DecisionSideEffects_AreScopedToMatchingGroupAndScanner()
        {
            await using var db = CreateAppDb();
            var sharedContainer = "CONT-SCOPE-001";
            var sharedGroupIdentifier = "SHARED-SCOPE-GROUP";

            var targetGroup = new AnalysisGroup
            {
                Id = Guid.NewGuid(),
                GroupIdentifier = sharedGroupIdentifier,
                NormalizedGroupIdentifier = sharedGroupIdentifier,
                ScannerType = "FS6000",
                GroupType = "Container"
            };
            var otherScannerGroup = new AnalysisGroup
            {
                Id = Guid.NewGuid(),
                GroupIdentifier = sharedGroupIdentifier,
                NormalizedGroupIdentifier = sharedGroupIdentifier,
                ScannerType = "ASE",
                GroupType = "Container"
            };

            db.AnalysisGroups.AddRange(targetGroup, otherScannerGroup);
            db.AnalysisRecords.AddRange(
                new AnalysisRecord
                {
                    GroupId = targetGroup.Id,
                    ContainerNumber = sharedContainer,
                    ScannerType = "FS6000",
                    Status = "Ready"
                },
                new AnalysisRecord
                {
                    GroupId = otherScannerGroup.Id,
                    ContainerNumber = sharedContainer,
                    ScannerType = "ASE",
                    Status = "Ready"
                });
            db.AnalysisAssignments.AddRange(
                new AnalysisAssignment
                {
                    GroupId = targetGroup.Id,
                    AssignedTo = "analyst-one",
                    Role = "Analyst",
                    State = "Active",
                    LeaseUntilUtc = DateTime.UtcNow.AddMinutes(15)
                },
                new AnalysisAssignment
                {
                    GroupId = otherScannerGroup.Id,
                    AssignedTo = "analyst-two",
                    Role = "Analyst",
                    State = "Active",
                    LeaseUntilUtc = DateTime.UtcNow.AddMinutes(15)
                });
            db.ContainerCompletenessStatuses.AddRange(
                new ContainerCompletenessStatus
                {
                    ContainerNumber = sharedContainer,
                    GroupIdentifier = sharedGroupIdentifier,
                    ScannerType = "FS6000",
                    WorkflowStage = "ImageAnalysis",
                    Status = "Complete"
                },
                new ContainerCompletenessStatus
                {
                    ContainerNumber = sharedContainer,
                    GroupIdentifier = sharedGroupIdentifier,
                    ScannerType = "ASE",
                    WorkflowStage = "ImageAnalysis",
                    Status = "Complete"
                });
            db.ImageAnalysisDecisions.Add(new ImageAnalysisDecision
            {
                ContainerNumber = sharedContainer,
                ScannerType = "FS6000",
                GroupIdentifier = sharedGroupIdentifier,
                Decision = "Normal",
                ReviewedBy = "analyst-one",
                ReviewedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var service = new DecisionSideEffectsService(NullLogger<DecisionSideEffectsService>.Instance);

            await service.ApplyAsync(db, sharedContainer, sharedGroupIdentifier, "FS6000");

            var records = await db.AnalysisRecords
                .AsNoTracking()
                .OrderBy(r => r.ScannerType)
                .ToListAsync();
            var completenessRows = await db.ContainerCompletenessStatuses
                .AsNoTracking()
                .OrderBy(c => c.ScannerType)
                .ToListAsync();
            var assignments = await db.AnalysisAssignments
                .AsNoTracking()
                .OrderBy(a => a.AssignedTo)
                .ToListAsync();

            Assert.Equal("Ready", records.Single(r => r.ScannerType == "ASE").Status);
            Assert.Equal("Decided", records.Single(r => r.ScannerType == "FS6000").Status);
            Assert.Equal("ImageAnalysis", completenessRows.Single(c => c.ScannerType == "ASE").WorkflowStage);
            Assert.Equal("Audit", completenessRows.Single(c => c.ScannerType == "FS6000").WorkflowStage);
            Assert.Equal("Active", assignments.Single(a => a.AssignedTo == "analyst-two").State);
            Assert.Equal("Released", assignments.Single(a => a.AssignedTo == "analyst-one").State);
        }

        private static async Task<AnalysisGroup> SeedAssignedGroupAsync(ApplicationDbContext db, string groupIdentifier, string scannerType)
        {
            var group = new AnalysisGroup
            {
                Id = Guid.NewGuid(),
                GroupIdentifier = groupIdentifier,
                NormalizedGroupIdentifier = groupIdentifier,
                ScannerType = scannerType,
                GroupType = "Container"
            };

            db.AnalysisGroups.Add(group);
            await db.SaveChangesAsync();

            await AnalysisGroupStateMachine.TransitionAsync(
                db,
                group,
                AnalysisStatuses.AnalystAssigned,
                triggerName: "QueueRegressionSeed",
                actor: "test",
                reason: $"Seed {groupIdentifier} as assigned for queue regression coverage.");

            return group;
        }

        private static AnalysisQueueEntry QueueEntryFor(AnalysisAssignment assignment, AnalysisGroup group, string containerNumber)
        {
            return new AnalysisQueueEntry
            {
                AssignmentId = assignment.Id,
                AssignedTo = assignment.AssignedTo,
                Role = assignment.Role,
                LeaseUntilUtc = assignment.LeaseUntilUtc,
                AssignmentCreatedAtUtc = assignment.CreatedAtUtc,
                GroupId = group.Id,
                GroupIdentifier = group.GroupIdentifier,
                ScannerType = group.ScannerType,
                GroupStatus = AnalysisStatuses.AnalystAssigned,
                GroupCreatedAtUtc = group.CreatedAtUtc,
                GroupUpdatedAtUtc = group.UpdatedAtUtc,
                ContainerCount = 1,
                ContainersJson = JsonSerializer.Serialize(new[] { containerNumber }),
                QueuedAtUtc = DateTime.UtcNow,
                LastRefreshedAtUtc = DateTime.UtcNow
            };
        }

        private sealed class StubImageAnalysisFacade : IImageAnalysisFacade
        {
            public Task<byte[]?> GetEnhancedImageAsync(string containerNumber)
            {
                return Task.FromResult<byte[]?>(null);
            }

            public Task<byte[]?> GetEnhancedImageAsync(
                string containerNumber,
                float brightness = 1.15f,
                float contrast = 1.1f,
                float blurAmount = 0.3f,
                bool applyHistogramEqualization = true)
            {
                return Task.FromResult<byte[]?>(null);
            }

            public Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null)
            {
                return Task.FromResult(string.Empty);
            }

            public Task<OcrResult> ExtractContainerNumberAsync(string containerNumber)
            {
                return Task.FromResult(new OcrResult());
            }

            public Task<ObjectDetectionResult> DetectObjectsAsync(string containerNumber)
            {
                return Task.FromResult(new ObjectDetectionResult());
            }

            public Task<QualityAssessment> AssessQualityAsync(string containerNumber)
            {
                return Task.FromResult(new QualityAssessment());
            }
        }
    }
}
