using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.RecordCompleteness;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class RecordReconciliationWorkerRegressionTests
    {
        [Fact]
        public async Task PromoteAwaitingContainersAsync_DoesNotStarveEvidenceBehindOldAwaitingRows()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"RecordRecon_{Guid.NewGuid():N}")
                .EnableServiceProviderCaching(false)
                .Options;

            await using var db = new ApplicationDbContext(options);

            for (var i = 0; i < 5000; i++)
            {
                db.RecordExpectedContainers.Add(new RecordExpectedContainer
                {
                    RecordId = 999,
                    ContainerNumber = $"NO-EVIDENCE-{i:D4}",
                    Status = "AwaitingScan",
                    FirstSeenUtc = DateTime.UtcNow.AddDays(-10)
                });
            }

            var targetRecord = new RecordCompletenessStatus
            {
                DeclarationNumber = "DECL-READY",
                Status = "Pending",
                WorkflowStage = "Pending",
                TotalExpectedContainers = 1,
                ContainersAwaitingScan = 1,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            db.RecordCompletenessStatuses.Add(targetRecord);
            await db.SaveChangesAsync();

            db.RecordExpectedContainers.Add(new RecordExpectedContainer
            {
                RecordId = targetRecord.Id,
                ContainerNumber = "TARGET-CONT",
                Status = "AwaitingScan",
                FirstSeenUtc = DateTime.UtcNow
            });
            db.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
            {
                ContainerNumber = "TARGET-CONT",
                ScannerType = "ASE",
                InspectionId = "INS-1",
                HasScannerData = true,
                HasImageData = true,
                HasICUMSData = true,
                BOEDocumentId = 123,
                Status = "Complete",
                WorkflowStage = "ImageAnalysis"
            });
            await db.SaveChangesAsync();

            var promoted = await InvokePromoteAwaitingContainersAsync(db);

            var child = await db.RecordExpectedContainers.SingleAsync(c => c.ContainerNumber == "TARGET-CONT");
            var parent = await db.RecordCompletenessStatuses.SingleAsync(r => r.Id == targetRecord.Id);

            Assert.Equal(1, promoted);
            Assert.Equal("Ready", child.Status);
            Assert.Equal("ASE", child.ScannerType);
            Assert.Equal("INS-1", child.InspectionId);
            Assert.Equal("Ready", parent.Status);
            Assert.Equal("ImageAnalysis", parent.WorkflowStage);
            Assert.Equal(1, parent.ContainersReady);
            Assert.Equal(0, parent.ContainersAwaitingScan);
        }

        [Fact]
        public async Task PromoteAwaitingContainersAsync_StillPromotesPendingRowsWhenNoAwaitingRowsExist()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"RecordRecon_{Guid.NewGuid():N}")
                .EnableServiceProviderCaching(false)
                .Options;

            await using var db = new ApplicationDbContext(options);

            var targetRecord = new RecordCompletenessStatus
            {
                DeclarationNumber = "DECL-PENDING",
                Status = "PartiallyReady",
                WorkflowStage = "Pending",
                TotalExpectedContainers = 1,
                ContainersScanned = 1,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            db.RecordCompletenessStatuses.Add(targetRecord);
            await db.SaveChangesAsync();

            db.RecordExpectedContainers.Add(new RecordExpectedContainer
            {
                RecordId = targetRecord.Id,
                ContainerNumber = "PENDING-CONT",
                Status = "Pending",
                FirstSeenUtc = DateTime.UtcNow.AddDays(-1),
                ScannedAtUtc = DateTime.UtcNow.AddHours(-2)
            });
            db.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
            {
                ContainerNumber = "PENDING-CONT",
                ScannerType = "FS6000",
                InspectionId = "INS-2",
                HasScannerData = true,
                HasImageData = true,
                Status = "Complete",
                WorkflowStage = "ImageAnalysis"
            });
            await db.SaveChangesAsync();

            var promoted = await InvokePromoteAwaitingContainersAsync(db);

            var child = await db.RecordExpectedContainers.SingleAsync(c => c.ContainerNumber == "PENDING-CONT");
            var parent = await db.RecordCompletenessStatuses.SingleAsync(r => r.Id == targetRecord.Id);

            Assert.Equal(1, promoted);
            Assert.Equal("Ready", child.Status);
            Assert.Equal("Ready", parent.Status);
            Assert.Equal("ImageAnalysis", parent.WorkflowStage);
            Assert.Equal(1, parent.ContainersReady);
            Assert.Equal(0, parent.ContainersScanned);
        }

        private static async Task<int> InvokePromoteAwaitingContainersAsync(ApplicationDbContext db)
        {
            await using var provider = new ServiceCollection().BuildServiceProvider();
            var worker = new RecordReconciliationWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RecordReconciliationWorker>.Instance);

            var method = typeof(RecordReconciliationWorker)
                .GetMethod("PromoteAwaitingContainersAsync", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            var task = (Task<int>)method!.Invoke(worker, new object[] { db, CancellationToken.None })!;
            return await task;
        }
    }
}
