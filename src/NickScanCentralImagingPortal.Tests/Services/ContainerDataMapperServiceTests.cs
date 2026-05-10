using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ContainerCompleteness;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class ContainerDataMapperServiceTests
    {
        [Fact]
        public async Task MapContainerDataAsync_WhenActiveRelationExists_UpdatesItInsteadOfInserting()
        {
            using var provider = NewServiceProvider();
            const string containerNumber = "MSCU1234567";

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.ContainerBOERelations.Add(new ContainerBOERelation
                {
                    ContainerNumber = containerNumber,
                    ScannerType = "FS6000",
                    ScannerDataId = 11,
                    ICUMSBOEId = 101,
                    RelationType = "Primary",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var service = NewMapper(provider);
            var relation = await service.MapContainerDataAsync(containerNumber, "ASE", 22, 202);

            using var verifyScope = provider.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await verifyDb.ContainerBOERelations
                .Where(r => r.ContainerNumber == containerNumber)
                .OrderBy(r => r.Id)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(rows[0].Id, relation!.Id);
            Assert.True(rows[0].IsActive);
            Assert.Equal("ASE", rows[0].ScannerType);
            Assert.Equal(22, rows[0].ScannerDataId);
            Assert.Equal(202, rows[0].ICUMSBOEId);
        }

        [Fact]
        public async Task MapContainerDataAsync_WhenExactRelationIsInactive_ReactivatesItAndDeactivatesCurrentActive()
        {
            using var provider = NewServiceProvider();
            const string containerNumber = "TCLU7654321";
            int inactiveId;

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.ContainerBOERelations.Add(new ContainerBOERelation
                {
                    ContainerNumber = containerNumber,
                    ScannerType = "FS6000",
                    ScannerDataId = 11,
                    ICUMSBOEId = 101,
                    RelationType = "Primary",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    IsActive = true,
                });

                var inactive = new ContainerBOERelation
                {
                    ContainerNumber = containerNumber,
                    ScannerType = "ASE",
                    ScannerDataId = 22,
                    ICUMSBOEId = 202,
                    RelationType = "Primary",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    IsActive = false,
                };
                db.ContainerBOERelations.Add(inactive);
                await db.SaveChangesAsync();
                inactiveId = inactive.Id;
            }

            var service = NewMapper(provider);
            var relation = await service.MapContainerDataAsync(containerNumber, "ASE", 22, 202);

            using var verifyScope = provider.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await verifyDb.ContainerBOERelations
                .Where(r => r.ContainerNumber == containerNumber)
                .OrderBy(r => r.Id)
                .ToListAsync();

            Assert.Equal(inactiveId, relation!.Id);
            Assert.Equal(2, rows.Count);
            Assert.Single(rows, r => r.IsActive);
            Assert.True(rows.Single(r => r.Id == inactiveId).IsActive);
            Assert.False(rows.Single(r => r.Id != inactiveId).IsActive);
        }

        private static ServiceProvider NewServiceProvider()
        {
            var dbName = $"ContainerDataMapperServiceTests_{Guid.NewGuid():N}";
            var appDatabaseRoot = new InMemoryDatabaseRoot();
            var downloadsDatabaseRoot = new InMemoryDatabaseRoot();
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(dbName, appDatabaseRoot).EnableServiceProviderCaching(false));
            services.AddDbContext<IcumDownloadsDbContext>(options =>
                options.UseInMemoryDatabase($"{dbName}_downloads", downloadsDatabaseRoot).EnableServiceProviderCaching(false));

            return services.BuildServiceProvider();
        }

        private static ContainerDataMapperService NewMapper(IServiceProvider provider)
        {
            return new ContainerDataMapperService(
                provider,
                NullLogger<ContainerDataMapperService>.Instance);
        }
    }
}
