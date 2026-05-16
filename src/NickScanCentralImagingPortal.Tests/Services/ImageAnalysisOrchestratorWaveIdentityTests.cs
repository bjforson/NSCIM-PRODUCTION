using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class ImageAnalysisOrchestratorWaveIdentityTests
    {
        [Fact]
        public void CreateAnalysisRecordFromReadyRecordChild_CopiesCanonicalScanIdentity()
        {
            var groupId = Guid.NewGuid();
            var scanImageAssetId = Guid.NewGuid();
            var child = new RecordExpectedContainer
            {
                ContainerNumber = "CMAU7810482",
                ScannerType = "ASE",
                Status = "Ready",
                ScanImageAssetId = scanImageAssetId,
                OriginalScanRecordId = 5786,
                SourceContainerLabel = "CMAU7810482, TIIU2732427"
            };

            var method = typeof(ImageAnalysisOrchestratorService).GetMethod(
                "CreateAnalysisRecordFromReadyRecordChild",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var record = Assert.IsType<AnalysisRecord>(method!.Invoke(
                null,
                new object?[] { groupId, child, "FS6000" }));

            Assert.Equal(groupId, record.GroupId);
            Assert.Equal("CMAU7810482", record.ContainerNumber);
            Assert.Equal("ASE", record.ScannerType);
            Assert.Equal(scanImageAssetId, record.ScanImageAssetId);
            Assert.Equal(5786, record.OriginalScanRecordId);
            Assert.Equal("CMAU7810482, TIIU2732427", record.SourceContainerLabel);
            Assert.Equal("Ready", record.Status);
        }

        [Fact]
        public async Task BuildWaveReadyContainerIdentityMapAsync_UsesRecordCompletenessIdentityForLaterCmrWaves()
        {
            await using var db = CreateAppDb();
            var scanImageAssetId = Guid.NewGuid();
            var parent = new AnalysisParentGroup
            {
                Id = Guid.NewGuid(),
                GroupIdentifier = "26CMA000037|TEMU2527526|LGS0207682",
                ScannerType = "ASE",
                TotalExpectedContainers = 2,
                Status = "Active"
            };
            var record = new RecordCompletenessStatus
            {
                DeclarationNumber = parent.GroupIdentifier,
                ClearanceType = "CMR",
                ScannerType = "ASE",
                TotalExpectedContainers = 2,
                ContainersReady = 1,
                ContainersAwaitingScan = 1,
                Status = "PartiallyReady",
                WorkflowStage = "ImageAnalysis"
            };

            db.AnalysisParentGroups.Add(parent);
            db.RecordCompletenessStatuses.Add(record);
            await db.SaveChangesAsync();

            db.RecordExpectedContainers.Add(new RecordExpectedContainer
            {
                RecordId = record.Id,
                ContainerNumber = "TEMU2527526",
                ScannerType = "ASE",
                Status = "Ready",
                ScanImageAssetId = scanImageAssetId,
                OriginalScanRecordId = 84830,
                SourceContainerLabel = "TEMU2527526, TIIU2732427"
            });
            await db.SaveChangesAsync();

            var readyContainers = new List<WavePendingContainer>
            {
                new()
                {
                    ParentGroupId = parent.Id,
                    ContainerNumber = "TEMU2527526",
                    ScannerType = "ASE",
                    Status = "Ready",
                    BecameReadyUtc = DateTime.UtcNow
                }
            };

            var method = typeof(ImageAnalysisOrchestratorService).GetMethod(
                "BuildWaveReadyContainerIdentityMapAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = Assert.IsAssignableFrom<Task>(method!.Invoke(
                null,
                new object?[] { db, parent, record.Id, readyContainers, CancellationToken.None }));
            await task;

            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(result);
            Assert.True(dictionary.Contains("TEMU2527526"));

            var identity = dictionary["TEMU2527526"]!;
            Assert.Equal(scanImageAssetId, identity.GetType().GetProperty("ScanImageAssetId")!.GetValue(identity));
            Assert.Equal(84830, identity.GetType().GetProperty("OriginalScanRecordId")!.GetValue(identity));
            Assert.Equal("TEMU2527526, TIIU2732427", identity.GetType().GetProperty("SourceContainerLabel")!.GetValue(identity));
            Assert.Equal("ASE", identity.GetType().GetProperty("ScannerType")!.GetValue(identity));
        }

        private static ApplicationDbContext CreateAppDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"OrchestratorWaveIdentity_{Guid.NewGuid():N}")
                .EnableServiceProviderCaching(false)
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
