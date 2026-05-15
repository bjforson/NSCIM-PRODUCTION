using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NickScanCentralImagingPortal.Core.Entities.EagleA25;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.EagleA25;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.EagleA25
{
    public class EagleA25SyncStatusTests
    {
        [Fact]
        public void UndefinedTableClassifier_DetectsProductionPostgres42P01()
        {
            var exception = new InvalidOperationException(
                "Failed executing DbCommand",
                new Exception("42P01: relation \"eaglea25synclogs\" does not exist"));

            Assert.True(EagleA25DatabaseExceptionClassifier.IsPostgresUndefinedTable(exception));
        }

        [Fact]
        public void ApplicationDbContext_MapsEagleA25TablesToExpectedLowercaseNames()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var db = new ApplicationDbContext(options);

            Assert.Equal("eaglea25scans", db.Model.FindEntityType(typeof(EagleA25Scan))?.GetTableName());
            Assert.Equal("eaglea25scanassets", db.Model.FindEntityType(typeof(EagleA25ScanAsset))?.GetTableName());
            Assert.Equal("eaglea25synclogs", db.Model.FindEntityType(typeof(EagleA25SyncLog))?.GetTableName());
        }

        [Fact]
        public void EagleA25Migration_IsDiscoverableByEfMigrationsAssembly()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=nscim_test;Username=test;Password=test")
                .Options;

            using var db = new ApplicationDbContext(options);

            var migrations = db.GetService<IMigrationsAssembly>().Migrations;

            Assert.Contains(EagleA25DatabaseExceptionClassifier.ScannerTablesMigrationId, migrations.Keys);
        }
    }
}
