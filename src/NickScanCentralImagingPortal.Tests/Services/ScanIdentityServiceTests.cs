using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing;
using NickScanCentralImagingPortal.Services.ScanIdentity;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services;

public sealed class ScanIdentityServiceTests
{
    [Fact]
    public async Task EnsureSourceIdentityAsync_AsePair_CreatesOneAssetAndTwoContainerLinks()
    {
        await using var db = CreateDb();
        var original = new OriginalScanRecord
        {
            ScannerType = CommonScannerTypes.ASE,
            OriginalContainerNumbers = "TEMU2527526, TIIU2732427",
            DerivedRecordCount = 2,
            InspectionId = "84830",
            ScanTime = DateTime.UtcNow
        };
        db.OriginalScanRecords.Add(original);
        await db.SaveChangesAsync();

        var service = new ScanIdentityService(db, NullLogger<ScanIdentityService>.Instance);

        var result = await service.EnsureSourceIdentityAsync(new ScanIdentityRequest
        {
            OriginalScanRecordId = original.Id,
            ScannerType = CommonScannerTypes.ASE,
            ScannerNativeId = "84830",
            SourceContainerLabel = "TEMU2527526, TIIU2732427",
            ContainerNumbers = new[] { "TEMU2527526", "TIIU2732427" },
            ImageDisplayName = "Transmission",
            FileSizeBytes = 1_838_404,
            ScanTimeUtc = DateTime.UtcNow
        });

        Assert.Equal(original.Id, result.Asset.OriginalScanRecordId);
        Assert.Equal(CommonScannerTypes.ASE, result.Asset.ScannerType);
        Assert.Equal(2, result.Links.Count);
        Assert.Contains(result.Links, link => link.NormalizedContainerNumber == "TEMU2527526" && link.Position == SourceScanContainerLinkPositions.Left);
        Assert.Contains(result.Links, link => link.NormalizedContainerNumber == "TIIU2732427" && link.Position == SourceScanContainerLinkPositions.Right);
        Assert.Single(await db.ScanImageAssets.ToListAsync());
        Assert.Equal(2, await db.SourceScanContainerLinks.CountAsync());
    }

    [Fact]
    public async Task ScanAssetResolver_UsesCanonicalLinkBeforeContainerStringFallback()
    {
        await using var db = CreateDb();
        var original = new OriginalScanRecord
        {
            ScannerType = CommonScannerTypes.ASE,
            OriginalContainerNumbers = "TEMU2527526, TIIU2732427",
            DerivedRecordCount = 2,
            InspectionId = "84830",
            ScanTime = DateTime.UtcNow
        };
        db.OriginalScanRecords.Add(original);
        await db.SaveChangesAsync();

        db.AseScans.Add(new AseScan
        {
            InspectionId = 84830,
            InspectionUuid = "ASE-84830",
            OriginalScanRecordId = original.Id,
            ContainerNumber = "TEMU2527526, TIIU2732427",
            ScanTime = DateTime.UtcNow,
            ImageDisplayName = "Transmission",
            ScanImage = new byte[4096]
        });
        await db.SaveChangesAsync();

        var identity = await new ScanIdentityService(db, NullLogger<ScanIdentityService>.Instance)
            .EnsureSourceIdentityAsync(new ScanIdentityRequest
            {
                OriginalScanRecordId = original.Id,
                ScannerType = CommonScannerTypes.ASE,
                ScannerNativeId = "84830",
                SourceContainerLabel = "TEMU2527526, TIIU2732427",
                ContainerNumbers = new[] { "TEMU2527526", "TIIU2732427" },
                ImageDisplayName = "Transmission",
                FileSizeBytes = 4096,
                ScanTimeUtc = DateTime.UtcNow
            });

        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = Guid.NewGuid(),
            ContainerNumber = "TEMU2527526",
            ScannerType = CommonScannerTypes.ASE,
            ScanImageAssetId = identity.Asset.Id,
            OriginalScanRecordId = original.Id,
            SourceContainerLabel = "TEMU2527526, TIIU2732427",
            Status = "Ready"
        });
        await db.SaveChangesAsync();

        var resolver = new ScanAssetResolver(db, NullLogger<ScanAssetResolver>.Instance);

        var result = await resolver.ResolveAsync("TEMU2527526");

        Assert.True(result.Found);
        Assert.Equal("SourceScanContainerLink", result.ResolvedBy);
        Assert.Equal(identity.Asset.Id, result.ScanImageAssetId);
        Assert.Equal(original.Id, result.OriginalScanRecordId);
        Assert.Equal("TEMU2527526, TIIU2732427", result.SourceContainerNumbers);
        Assert.True(result.HasImage);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
