using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services;

public sealed class SourceScanResolverPhase2ARegressionTests
{
    [Fact]
    public async Task ResolveAsync_AnalysisRecordIdWithSplitJob_ResolvesSplitJobSourceContext()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var splitJobId = Guid.NewGuid();
        var splitResultId = Guid.NewGuid();
        var scannerRecordId = Guid.NewGuid();

        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            NormalizedGroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            GroupType = "Container"
        });
        var record = new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = splitJobId,
            SplitResultId = splitResultId,
            SplitPosition = "left",
            SplitStatus = "Chosen"
        };
        db.AnalysisRecords.Add(record);
        db.CrossRecordScans.Add(new CrossRecordScan
        {
            OriginalScanRecord = "MSMU1683356, MRKU8254509",
            ScannerRecordId = scannerRecordId,
            ScannerType = "ASE",
            ScanDateTime = DateTime.UtcNow.AddMinutes(-5),
            Container1 = "MSMU1683356",
            Container2 = "MRKU8254509",
            CrossRecordType = "DifferentBOEs",
            SplitJobId = splitJobId
        });
        await db.SaveChangesAsync();

        var resolution = await NewResolver(db).ResolveAsync(
            string.Empty,
            analysisRecordId: record.Id);

        Assert.True(resolution.Found);
        Assert.False(resolution.IsAmbiguous);
        Assert.Equal("SplitJobContext", resolution.ResolvedBy);
        Assert.Equal("SplitJobContextMatch", resolution.ResolutionReason);
        Assert.Equal("MSMU1683356", resolution.ContainerNumber);
        Assert.Equal("ASE", resolution.SourceScannerType);
        Assert.Equal(scannerRecordId, resolution.ScannerScanId);
        Assert.Equal("MSMU1683356, MRKU8254509", resolution.SourceContainerNumbers);
        Assert.Equal(splitJobId, resolution.SplitJobId);
        Assert.Equal(splitResultId, resolution.SplitResultId);
        Assert.Equal("left", resolution.SplitPosition);
    }

    [Fact]
    public async Task ResolveAsync_SplitJobOnlyWithMultipleSourceRows_ReturnsAmbiguousResult()
    {
        await using var db = NewInMemoryDb();
        var splitJobId = Guid.NewGuid();

        db.CrossRecordScans.AddRange(
            new CrossRecordScan
            {
                OriginalScanRecord = "MSMU1683356, MRKU8254509",
                ScannerRecordId = Guid.NewGuid(),
                ScannerType = "ASE",
                ScanDateTime = DateTime.UtcNow.AddMinutes(-10),
                Container1 = "MSMU1683356",
                Container2 = "MRKU8254509",
                CrossRecordType = "DifferentBOEs",
                SplitJobId = splitJobId
            },
            new CrossRecordScan
            {
                OriginalScanRecord = "MSMU1683356, TGBU5483870",
                ScannerRecordId = Guid.NewGuid(),
                ScannerType = "ASE",
                ScanDateTime = DateTime.UtcNow.AddMinutes(-5),
                Container1 = "MSMU1683356",
                Container2 = "TGBU5483870",
                CrossRecordType = "DifferentBOEs",
                SplitJobId = splitJobId
            });
        await db.SaveChangesAsync();

        var resolution = await NewResolver(db).ResolveAsync(
            string.Empty,
            splitJobId: splitJobId);

        Assert.False(resolution.Found);
        Assert.True(resolution.IsAmbiguous);
        Assert.Equal("AmbiguousSplitJobSourceScan", resolution.ResolutionReason);
        Assert.Equal(splitJobId, resolution.SplitJobId);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public async Task ResolveAsync_NonCrossRecordSplitJobOnly_CarriesAnalysisRecordIdentity()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var splitJobId = Guid.NewGuid();
        var splitResultId = Guid.NewGuid();
        var source = new OriginalScanRecord
        {
            ScannerType = "ASE",
            OriginalContainerNumbers = "MSMU1683356, MRKU8254509",
            DerivedRecordCount = 2,
            ScanTime = DateTime.UtcNow.AddMinutes(-20),
            IngestedAt = DateTime.UtcNow.AddMinutes(-19)
        };
        db.OriginalScanRecords.Add(source);
        await db.SaveChangesAsync();

        db.AseScans.Add(new AseScan
        {
            Id = Guid.NewGuid(),
            OriginalScanRecordId = source.Id,
            InspectionId = 84722,
            InspectionUuid = "ASE-84722",
            ContainerNumber = "MSMU1683356, MRKU8254509",
            ScanTime = DateTime.UtcNow.AddMinutes(-20),
            ScanImage = new byte[1_753_540],
            ImageDisplayName = "40426305424_W1.ase"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = splitJobId,
            SplitResultId = splitResultId,
            SplitPosition = "left",
            SplitStatus = "Chosen",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();
        var record = db.AnalysisRecords.Single();

        var resolution = await NewResolver(db).ResolveAsync(
            string.Empty,
            splitJobId: splitJobId);

        Assert.True(resolution.Found);
        Assert.False(resolution.IsAmbiguous);
        Assert.Equal("TokenizedSourceContainer", resolution.ResolvedBy);
        Assert.Equal(record.Id, resolution.AnalysisRecordId);
        Assert.Equal(source.Id.ToString(), resolution.SourceScanId);
        Assert.Equal("MSMU1683356, MRKU8254509", resolution.SourceContainerNumbers);
        Assert.Equal(splitJobId, resolution.SplitJobId);
        Assert.Equal(splitResultId, resolution.SplitResultId);
        Assert.Equal("left", resolution.SplitPosition);
        Assert.NotNull(resolution.SplitContext);
        Assert.Equal(record.Id, resolution.SplitContext.AnalysisRecordId);
        Assert.Equal(splitJobId, resolution.SplitContext.SplitJobId);
        Assert.Equal(splitResultId, resolution.SplitContext.SplitResultId);
    }

    [Fact]
    public async Task ResolveAsync_TokenizedLogicalChildAse_ReturnsNonZeroImageFileSize()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var splitJobId = Guid.NewGuid();
        var source = new OriginalScanRecord
        {
            ScannerType = "ASE",
            OriginalContainerNumbers = "MSMU1683356, MRKU8254509",
            DerivedRecordCount = 2,
            ScanTime = DateTime.UtcNow.AddMinutes(-20),
            IngestedAt = DateTime.UtcNow.AddMinutes(-19)
        };
        db.OriginalScanRecords.Add(source);
        await db.SaveChangesAsync();

        db.AseScans.Add(new AseScan
        {
            Id = Guid.NewGuid(),
            OriginalScanRecordId = source.Id,
            InspectionId = 84722,
            InspectionUuid = "ASE-84722",
            ContainerNumber = "MSMU1683356, MRKU8254509",
            ScanTime = DateTime.UtcNow.AddMinutes(-20),
            ScanImage = new byte[1_753_540],
            ImageDisplayName = "40426305424_W1.ase"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = splitJobId,
            SplitPosition = "left",
            SplitStatus = "Ready",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var resolution = await NewResolver(db).ResolveAsync(
            "MSMU1683356",
            analysisRecordId: db.AnalysisRecords.Single().Id);

        Assert.True(resolution.Found);
        Assert.False(resolution.IsAmbiguous);
        Assert.Equal("TokenizedSourceContainer", resolution.ResolvedBy);
        Assert.Equal("ASE", resolution.SourceScannerType);
        Assert.Equal(source.Id.ToString(), resolution.SourceScanId);
        Assert.Equal("MSMU1683356, MRKU8254509", resolution.SourceContainerNumbers);
        Assert.Equal(splitJobId, resolution.SplitJobId);
        Assert.Equal("left", resolution.SplitPosition);
        Assert.Equal(1_753_540, resolution.FileSizeBytes);
        Assert.Equal("40426305424_W1.ase", resolution.ImageDisplayName);
    }

    private static ScanAssetResolver NewResolver(ApplicationDbContext db) =>
        new(db, NullLogger<ScanAssetResolver>.Instance);

    private static ApplicationDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SourceScanResolverPhase2A_{Guid.NewGuid():N}")
            .EnableServiceProviderCaching(false)
            .Options;

        return new ApplicationDbContext(options);
    }
}
