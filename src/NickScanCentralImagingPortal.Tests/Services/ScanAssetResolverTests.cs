using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services;

public sealed class ScanAssetResolverTests
{
    [Fact]
    public async Task ResolveAsync_ExactAse_WinsBeforeTokenizedMatches()
    {
        await using var db = CreateDb();
        db.AseScans.Add(new AseScan
        {
            InspectionId = 1,
            InspectionUuid = "exact",
            ContainerNumber = "MSMU1683356",
            ScanTime = DateTime.UtcNow,
            ScanImage = new byte[2048]
        });
        db.AseScans.Add(new AseScan
        {
            InspectionId = 2,
            InspectionUuid = "tokenized",
            ContainerNumber = "MSMU1683356, MRKU8254509",
            ScanTime = DateTime.UtcNow.AddMinutes(1),
            ScanImage = new byte[4096]
        });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("MSMU1683356");

        Assert.True(result.Found);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("ASE", result.SourceScannerType);
        Assert.Equal("ExactAse", result.ResolvedBy);
        Assert.Equal("MSMU1683356", result.SourceContainerNumbers);
    }

    [Fact]
    public async Task ResolveAsync_TokenizedAseOriginal_ReturnsSourceScanAndSplitContext()
    {
        await using var db = CreateDb();
        var splitJobId = Guid.NewGuid();
        db.AseScans.Add(new AseScan
        {
            Id = Guid.NewGuid(),
            InspectionId = 84180,
            InspectionUuid = "ase-two-container",
            ContainerNumber = "MSMU1683356, MRKU8254509",
            OriginalScanRecordId = 4243,
            ScanTime = DateTime.UtcNow,
            ImageDisplayName = "ase-combined.jpg",
            ScanImage = new byte[1_753_540]
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            Id = 3418,
            GroupId = Guid.NewGuid(),
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = splitJobId,
            SplitPosition = "left",
            Status = "Ready"
        });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("MSMU1683356", analysisRecordId: 3418);

        Assert.True(result.Found);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("ASE", result.SourceScannerType);
        Assert.Equal("TokenizedSourceContainer", result.ResolvedBy);
        Assert.Equal("4243", result.SourceScanId);
        Assert.Equal("MSMU1683356, MRKU8254509", result.SourceContainerNumbers);
        Assert.Equal(3418, result.AnalysisRecordId);
        Assert.Equal(splitJobId, result.SplitJobId);
        Assert.Equal("left", result.SplitPosition);
        Assert.NotNull(result.SplitContext);
        Assert.Equal(3418, result.SplitContext.AnalysisRecordId);
        Assert.True(result.SplitContext.IsMultiContainer);
        Assert.Equal("MSMU1683356", result.SplitContext.ContainerNumber);
        Assert.Equal(splitJobId, result.SplitContext.SplitJobId);
        Assert.Equal("left", result.SplitContext.SplitPosition);
        Assert.Equal(1_753_540, result.ImageSizeBytes);
        Assert.Contains(splitJobId.ToString("N"), result.CacheKey?.Value);
    }

    [Fact]
    public async Task ResolveAsync_TokenizedAseMultipleSources_ReturnsAmbiguous()
    {
        await using var db = CreateDb();
        db.AseScans.AddRange(
            new AseScan
            {
                InspectionId = 1,
                InspectionUuid = "first",
                ContainerNumber = "MSMU1683356, MRKU8254509",
                ScanTime = DateTime.UtcNow,
                ScanImage = new byte[2048]
            },
            new AseScan
            {
                InspectionId = 2,
                InspectionUuid = "second",
                ContainerNumber = "MSMU1683356, TGBU5483870",
                ScanTime = DateTime.UtcNow.AddMinutes(1),
                ScanImage = new byte[2048]
            });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("MSMU1683356");

        Assert.False(result.Found);
        Assert.True(result.IsAmbiguous);
        Assert.Equal("AmbiguousSourceScan", result.ResolutionReason);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task ResolveAsync_AnalysisRecordScanImageAssetId_WinsBeforeTokenizedAmbiguity()
    {
        await using var db = CreateDb();
        var assetId = Guid.NewGuid();
        var splitJobId = Guid.NewGuid();

        db.ScanImageAssets.Add(new ScanImageAsset
        {
            Id = assetId,
            OriginalScanRecordId = 9201,
            ScannerType = "ASE",
            ScannerNativeId = Guid.NewGuid().ToString(),
            SourceContainerLabel = "TEMU2527526, TIIU2732427",
            ImageDisplayName = "ase-pair.jpg",
            FileSizeBytes = 517_120,
            ScanTimeUtc = DateTime.UtcNow
        });
        db.SourceScanContainerLinks.Add(new SourceScanContainerLink
        {
            ScanImageAssetId = assetId,
            OriginalScanRecordId = 9201,
            ScannerType = "ASE",
            ContainerNumber = "TIIU2732427",
            NormalizedContainerNumber = "TIIU2732427",
            SourceContainerLabel = "TEMU2527526, TIIU2732427"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            Id = 4410,
            GroupId = Guid.NewGuid(),
            ContainerNumber = "TEMU2527526",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = splitJobId,
            SplitPosition = "left",
            ScanImageAssetId = assetId,
            OriginalScanRecordId = 9201,
            Status = "Ready"
        });
        db.AseScans.AddRange(
            new AseScan
            {
                InspectionId = 101,
                InspectionUuid = "ambiguous-1",
                ContainerNumber = "TEMU2527526, TIIU2732427",
                ScanTime = DateTime.UtcNow.AddMinutes(-10),
                ScanImage = new byte[2048]
            },
            new AseScan
            {
                InspectionId = 102,
                InspectionUuid = "ambiguous-2",
                ContainerNumber = "TEMU2527526, CMAU7810482",
                ScanTime = DateTime.UtcNow.AddMinutes(-5),
                ScanImage = new byte[2048]
            });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("TEMU2527526", analysisRecordId: 4410);

        Assert.True(result.Found);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(assetId, result.ScanImageAssetId);
        Assert.Equal("ScanImageAssetId", result.ResolvedBy);
        Assert.Equal("CanonicalScanImageAssetId", result.ResolutionReason);
        Assert.Equal("TEMU2527526, TIIU2732427", result.SourceContainerNumbers);
        Assert.Equal(splitJobId, result.SplitJobId);
        Assert.Equal("left", result.SplitPosition);
        Assert.Equal(517_120, result.ImageSizeBytes);
    }

    [Fact]
    public async Task ResolveAsync_RequestScanImageAssetId_CanResolveWithoutContainerNumber()
    {
        await using var db = CreateDb();
        var assetId = Guid.NewGuid();

        db.ScanImageAssets.Add(new ScanImageAsset
        {
            Id = assetId,
            OriginalScanRecordId = 9301,
            ScannerType = "ASE",
            ScannerNativeId = Guid.NewGuid().ToString(),
            SourceContainerLabel = "TEMU2527526, TIIU2732427",
            ImageDisplayName = "ase-pair-no-container.jpg",
            FileSizeBytes = 811_008,
            ScanTimeUtc = DateTime.UtcNow
        });
        db.SourceScanContainerLinks.AddRange(
            new SourceScanContainerLink
            {
                ScanImageAssetId = assetId,
                OriginalScanRecordId = 9301,
                ScannerType = "ASE",
                ContainerNumber = "TEMU2527526",
                NormalizedContainerNumber = "TEMU2527526",
                SourceContainerLabel = "TEMU2527526, TIIU2732427"
            },
            new SourceScanContainerLink
            {
                ScanImageAssetId = assetId,
                OriginalScanRecordId = 9301,
                ScannerType = "ASE",
                ContainerNumber = "TIIU2732427",
                NormalizedContainerNumber = "TIIU2732427",
                SourceContainerLabel = "TEMU2527526, TIIU2732427"
            });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync(new ScanAssetResolutionRequest
        {
            ScanImageAssetId = assetId
        });

        Assert.True(result.Found);
        Assert.Equal(assetId, result.ScanImageAssetId);
        Assert.Equal("ScanImageAssetId", result.ResolvedBy);
        Assert.Equal("TEMU2527526, TIIU2732427", result.SourceContainerNumbers);
        Assert.Equal("9301", result.SourceScanId);
        Assert.True(result.HasImage);
    }

    [Fact]
    public async Task ResolveAsync_SplitJobOnly_UsesCrossRecordSourceContext()
    {
        await using var db = CreateDb();
        var splitJobId = Guid.NewGuid();
        var scannerRecordId = Guid.NewGuid();

        db.CrossRecordScans.Add(new CrossRecordScan
        {
            OriginalScanRecord = "MSMU1683356, MRKU8254509",
            ScannerRecordId = scannerRecordId,
            ScannerType = "ASE",
            ScanDateTime = DateTime.UtcNow,
            Container1 = "MSMU1683356",
            Container2 = "MRKU8254509",
            CrossRecordType = "DifferentBOEs",
            SplitJobId = splitJobId
        });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync(string.Empty, splitJobId: splitJobId);

        Assert.True(result.Found);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("ASE", result.SourceScannerType);
        Assert.Equal("SplitJobContext", result.ResolvedBy);
        Assert.Equal(scannerRecordId.ToString(), result.SourceScanId);
        Assert.Equal("MSMU1683356, MRKU8254509", result.SourceContainerNumbers);
        Assert.Equal(splitJobId, result.SplitJobId);
        Assert.Contains($"scan-asset:ASE:{scannerRecordId}", result.CacheKey?.Value);
    }

    [Fact]
    public async Task ResolveAsync_ExactFs6000_ReturnsFs6000Source()
    {
        await using var db = CreateDb();
        db.FS6000Scans.Add(new FS6000Scan
        {
            ContainerNumber = "TGBU5483870",
            PicNumber = "PIC-1",
            ScanTime = DateTime.UtcNow,
            HasImage = true
        });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("TGBU5483870");

        Assert.True(result.Found);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("FS6000", result.SourceScannerType);
        Assert.Equal("ExactFs6000", result.ResolvedBy);
        Assert.Equal("TGBU5483870", result.SourceContainerNumbers);
    }

    [Fact]
    public async Task ResolveAsync_ExactFs6000_UsesImageRowsWhenSummaryFlagsAreStale()
    {
        await using var db = CreateDb();
        var scanId = Guid.NewGuid();
        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            ContainerNumber = "HASU1135624",
            PicNumber = "PIC-STALE",
            ScanTime = DateTime.UtcNow,
            HasImage = false,
            ImageCount = 0
        });
        db.FS6000Images.Add(new FS6000Image
        {
            ScanId = scanId,
            ImageType = "Main",
            FileName = "23301FS01202605160017.jpg",
            FileSizeBytes = 311_958,
            ImageData = new byte[] { 0xFF, 0xD8, 0xFF }
        });
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveAsync("HASU1135624");

        Assert.True(result.Found);
        Assert.True(result.HasImage);
        Assert.Equal(311_958, result.ImageSizeBytes);
        Assert.Equal("23301FS01202605160017.jpg", result.ImageDisplayName);
    }

    private static ScanAssetResolver CreateResolver(ApplicationDbContext db) =>
        new(db, NullLogger<ScanAssetResolver>.Instance);

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
