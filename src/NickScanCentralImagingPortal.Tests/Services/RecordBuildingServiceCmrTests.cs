using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.RecordCompleteness;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class RecordBuildingServiceCmrTests
    {
        [Fact]
        public void BuildCmr_CreatesCompositeKeyRecordWithAwaitingContainer()
        {
            var nowUtc = new DateTime(2026, 5, 13, 9, 30, 0, DateTimeKind.Utc);
            var row = new BOEDocument
            {
                Id = 42,
                ClearanceType = "CMR",
                RotationNumber = " rot-123 ",
                ContainerNumber = " pidu4444900 ",
                BlNumber = " bl 789 ",
                HouseBl = "HBL-1",
                ConsigneeName = "Example Consignee"
            };

            var built = RecordCompletenessBuilder.BuildCmr(row, nowUtc);

            Assert.True(CmrCompositeKeyHelper.IsOperationalKey(built.Record.DeclarationNumber));
            Assert.Equal("CMR", built.Record.ClearanceType);
            Assert.Equal(42, built.Record.PrimaryBoeDocumentId);
            Assert.Equal("ROT-123", built.Record.RotationNumber);
            Assert.Equal("BL 789", built.Record.BlNumber);
            Assert.Equal(1, built.Record.TotalExpectedContainers);
            Assert.Equal(1, built.Record.ContainersAwaitingScan);

            var child = Assert.Single(built.Children);
            Assert.Equal("PIDU4444900", child.ContainerNumber);
            Assert.Equal("AwaitingScan", child.Status);
            Assert.Equal(42, child.BoeDocumentId);
            Assert.Equal("HBL-1", child.HouseBl);
            Assert.Equal("Example Consignee", child.ConsigneeName);
        }

        [Fact]
        public async Task BuildOrUpdateCmrRecordAsync_CreatesRecordWhenGateEnabled()
        {
            await using var provider = CreateProvider();

            await SeedCmrRowAsync(provider, new BOEDocument
            {
                Id = 100,
                ClearanceType = "CMR",
                RotationNumber = "ROT-123",
                ContainerNumber = "PIDU4444900",
                BlNumber = "BL789",
                HouseBl = "HBL-100",
                ConsigneeName = "CMR Consignee",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow
            });

            var service = new RecordBuildingService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RecordBuildingService>.Instance);

            await service.BuildOrUpdateCmrRecordAsync(" rot-123 ", " pidu4444900 ", " bl789 ", true);

            await using var scope = provider.CreateAsyncScope();
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await appDb.RecordCompletenessStatuses
                .Include(r => r.ExpectedContainers)
                .SingleAsync();

            Assert.True(CmrCompositeKeyHelper.IsOperationalKey(record.DeclarationNumber));
            Assert.Equal("CMR", record.ClearanceType);
            Assert.Equal(100, record.PrimaryBoeDocumentId);
            Assert.Equal("ROT-123", record.RotationNumber);
            Assert.Equal("BL789", record.BlNumber);
            Assert.Equal("Pending", record.Status);
            Assert.Equal(1, record.TotalExpectedContainers);
            Assert.Equal(1, record.ContainersAwaitingScan);

            var child = Assert.Single(record.ExpectedContainers);
            Assert.Equal("PIDU4444900", child.ContainerNumber);
            Assert.Equal("AwaitingScan", child.Status);
            Assert.Equal(100, child.BoeDocumentId);
            Assert.Equal("HBL-100", child.HouseBl);
            Assert.Equal("CMR Consignee", child.ConsigneeName);
        }

        [Fact]
        public async Task BuildOrUpdateCmrRecordAsync_DoesNotCreateRecordWhenGateDisabled()
        {
            await using var provider = CreateProvider();

            await SeedCmrRowAsync(provider, new BOEDocument
            {
                Id = 101,
                ClearanceType = "CMR",
                RotationNumber = "ROT-123",
                ContainerNumber = "PIDU4444900",
                BlNumber = "BL789"
            });

            var service = new RecordBuildingService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RecordBuildingService>.Instance);

            await service.BuildOrUpdateCmrRecordAsync("ROT-123", "PIDU4444900", "BL789", false);

            await using var scope = provider.CreateAsyncScope();
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Empty(await appDb.RecordCompletenessStatuses.ToListAsync());
            Assert.Empty(await appDb.RecordExpectedContainers.ToListAsync());
        }

        [Fact]
        public async Task BuildOrUpdateCmrRecordAsync_UpdatesExistingRecordWithoutResettingChildStatus()
        {
            await using var provider = CreateProvider();
            var service = new RecordBuildingService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RecordBuildingService>.Instance);

            await SeedCmrRowAsync(provider, new BOEDocument
            {
                Id = 110,
                ClearanceType = "CMR",
                RotationNumber = "ROT-123",
                ContainerNumber = "PIDU4444900",
                BlNumber = "BL789",
                HouseBl = "HBL-OLD",
                ConsigneeName = "Old Consignee",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-20)
            });

            await service.BuildOrUpdateCmrRecordAsync("ROT-123", "PIDU4444900", "BL789", true);

            await using (var scope = provider.CreateAsyncScope())
            {
                var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var child = await appDb.RecordExpectedContainers.SingleAsync();
                child.Status = "Ready";
                child.BecameReadyUtc = DateTime.UtcNow;
                await appDb.SaveChangesAsync();
            }

            await SeedCmrRowAsync(provider, new BOEDocument
            {
                Id = 111,
                ClearanceType = "CMR",
                RotationNumber = "ROT-123",
                ContainerNumber = "PIDU4444900",
                BlNumber = "BL789",
                HouseBl = "HBL-NEW",
                ConsigneeName = "New Consignee",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt = DateTime.UtcNow
            });

            await service.BuildOrUpdateCmrRecordAsync("ROT-123", "PIDU4444900", "BL789", true);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await verifyDb.RecordCompletenessStatuses
                .Include(r => r.ExpectedContainers)
                .SingleAsync();
            var updatedChild = Assert.Single(record.ExpectedContainers);

            Assert.Equal(111, record.PrimaryBoeDocumentId);
            Assert.Equal(111, updatedChild.BoeDocumentId);
            Assert.Equal("HBL-NEW", updatedChild.HouseBl);
            Assert.Equal("New Consignee", updatedChild.ConsigneeName);
            Assert.Equal("Ready", updatedChild.Status);
            Assert.Equal("Ready", record.Status);
        }

        [Fact]
        public async Task BuildOrUpdateRecordAsync_CmrOperationalKey_CreatesAndPromotesFromExistingCompletenessEvidence()
        {
            await using var provider = CreateProvider();
            var scanImageAssetId = Guid.NewGuid();

            await SeedCmrRowAsync(provider, new BOEDocument
            {
                Id = 120,
                ClearanceType = "CMR",
                RotationNumber = "26CMA000037",
                ContainerNumber = "TEMU2527526",
                BlNumber = "LGS0207682",
                HouseBl = "HBL-CMR",
                ConsigneeName = "CMR Consignee",
                CreatedAt = DateTime.UtcNow.AddDays(-9),
                UpdatedAt = DateTime.UtcNow.AddDays(-9)
            });

            await using (var scope = provider.CreateAsyncScope())
            {
                var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                appDb.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
                {
                    ContainerNumber = "TEMU2527526",
                    ScannerType = "ASE",
                    InspectionId = "84830-a",
                    ScanImageAssetId = scanImageAssetId,
                    OriginalScanRecordId = 5786,
                    SourceContainerLabel = "TEMU2527526, TIIU2732427",
                    ScanDate = DateTime.UtcNow,
                    HasScannerData = true,
                    HasICUMSData = true,
                    HasImageData = true,
                    Status = "Complete",
                    WorkflowStage = "ImageAnalysis"
                });
                await appDb.SaveChangesAsync();
            }

            Assert.True(CmrCompositeKeyHelper.TryCreate(
                "26CMA000037",
                "TEMU2527526",
                "LGS0207682",
                out var key));

            var service = new RecordBuildingService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RecordBuildingService>.Instance);

            await service.BuildOrUpdateRecordAsync(key.OperationalKey, includeCmrCompositeRecords: true);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await verifyDb.RecordCompletenessStatuses
                .Include(r => r.ExpectedContainers)
                .SingleAsync();
            var child = Assert.Single(record.ExpectedContainers);

            Assert.Equal(key.OperationalKey, record.DeclarationNumber);
            Assert.Equal("Ready", record.Status);
            Assert.Equal("ImageAnalysis", record.WorkflowStage);
            Assert.Equal("Ready", child.Status);
            Assert.Equal(scanImageAssetId, child.ScanImageAssetId);
            Assert.Equal(5786, child.OriginalScanRecordId);
            Assert.Equal("TEMU2527526, TIIU2732427", child.SourceContainerLabel);
        }

        private static ServiceProvider CreateProvider()
        {
            var appDatabaseName = $"RecordBuilding_App_{Guid.NewGuid():N}";
            var icumDatabaseName = $"RecordBuilding_Icum_{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(appDatabaseName));
            services.AddDbContext<IcumDownloadsDbContext>(options =>
                options.UseInMemoryDatabase(icumDatabaseName));

            return services.BuildServiceProvider();
        }

        private static async Task SeedCmrRowAsync(ServiceProvider provider, BOEDocument row)
        {
            await using var scope = provider.CreateAsyncScope();
            var icumDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            icumDb.BOEDocuments.Add(row);
            await icumDb.SaveChangesAsync();
        }
    }
}
