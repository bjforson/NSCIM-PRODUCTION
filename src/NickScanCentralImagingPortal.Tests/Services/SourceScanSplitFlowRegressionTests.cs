using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.Security;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Retrievers;
using NickScanCentralImagingPortal.Services.ImageSplitter;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services;

public sealed class SourceScanSplitFlowRegressionTests
{
    [Fact]
    public async Task AseSourceRetriever_ExactSingleContainer_LoadsAseScanIdentityAndBlob()
    {
        await using var db = NewInMemoryDb();
        var scanId = Guid.NewGuid();
        var scanTime = DateTime.UtcNow.AddMinutes(-20);
        var scanBytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        db.AseScans.Add(new AseScan
        {
            Id = scanId,
            InspectionId = 84722,
            InspectionUuid = "ASE-84722",
            ContainerNumber = "MSMU1683356",
            ScanTime = scanTime,
            ScanImage = scanBytes,
            ImageDisplayName = "MSMU1683356.ase"
        });
        await db.SaveChangesAsync();

        var resolver = new ScanAssetResolver(db, NullLogger<ScanAssetResolver>.Instance);
        var retriever = new ASESourceRetriever(NullLogger<ASESourceRetriever>.Instance, db, resolver);

        var source = await retriever.LoadAsync("MSMU1683356");

        Assert.NotNull(source);
        Assert.Equal(scanId.ToString(), source.ScanId);
        Assert.Equal("MSMU1683356", source.ContainerNumber);
        Assert.Equal(ASEFormatAdapter.FormatTag, source.SourceFormatTag);
        Assert.Equal(scanBytes, source.Blobs["ScanImage"]);
        Assert.Equal("ASE", source.Metadata["Scanner"]);
        Assert.Equal(scanTime.ToString("O"), source.Metadata["ScanTime"]);
    }

    [Fact]
    public async Task Fs6000SourceRetriever_ExactSingleContainer_LoadsSourceIdentityAndRenderableBlobs()
    {
        await using var db = NewInMemoryDb();
        var scanId = Guid.NewGuid();
        var scanTime = DateTime.UtcNow.AddMinutes(-10);
        var mainBytes = new byte[] { 1, 2, 3 };
        var highEnergyBytes = new byte[] { 4, 5, 6 };

        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            ContainerNumber = "TGHU7654321",
            PicNumber = "PIC-7654321",
            ScanTime = scanTime,
            HasImage = true,
            ImageCount = 3,
            Images =
            {
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "Main",
                    FileName = "main.jpg",
                    ImageData = mainBytes,
                    FileSizeBytes = mainBytes.Length,
                    CreatedAt = scanTime.AddSeconds(1)
                },
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "HighEnergy",
                    FileName = "he.img",
                    ImageData = highEnergyBytes,
                    FileSizeBytes = highEnergyBytes.Length,
                    CreatedAt = scanTime.AddSeconds(2)
                },
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "Icon",
                    FileName = "icon.jpg",
                    ImageData = new byte[] { 9 },
                    FileSizeBytes = 1,
                    CreatedAt = scanTime.AddSeconds(3)
                }
            }
        });
        await db.SaveChangesAsync();

        var retriever = new FS6000SourceRetriever(NullLogger<FS6000SourceRetriever>.Instance, db);

        var source = await retriever.LoadAsync("TGHU7654321");

        Assert.NotNull(source);
        Assert.Equal(scanId.ToString(), source.ScanId);
        Assert.Equal("TGHU7654321", source.ContainerNumber);
        Assert.Equal(FS6000FormatAdapter.FormatTag, source.SourceFormatTag);
        Assert.Equal(mainBytes, source.Blobs["Main"]);
        Assert.Equal(highEnergyBytes, source.Blobs["HighEnergy"]);
        Assert.False(source.Blobs.ContainsKey("Icon"));
        Assert.Equal("FS6000", source.Metadata["Scanner"]);
        Assert.Equal(scanTime.ToString("O"), source.Metadata["ScanTime"]);
    }

    [Fact]
    public async Task TwoContainerSplitIntake_TokenizedAseOriginal_LinksLogicalChildrenToSplitJob()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var optionA = Guid.NewGuid();
        var optionB = Guid.NewGuid();
        var renderedAseBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var splitter = new StubImageSplitterService
        {
            SubmittedJob = new SplitJobReference(jobId, "completed"),
            StatusByJobId =
            {
                [jobId] = new SplitJobStatus(
                    jobId,
                    "completed",
                    BestStrategy: "projection",
                    BestConfidence: 0.98,
                    SplitX: 420,
                    ResultCount: 2)
            },
            ResultsByJobId =
            {
                [jobId] = new[]
                {
                    new SplitResultReference(optionA, "projection", 0.98),
                    new SplitResultReference(optionB, "edge", 0.91)
                }
            }
        };

        var original = await SeedOriginalScanAsync(db, "ASE", "MSMU1683356, MRKU8254509");
        db.AseScans.Add(new AseScan
        {
            Id = Guid.NewGuid(),
            OriginalScanRecordId = original.Id,
            InspectionId = 84722,
            InspectionUuid = "ASE-84722",
            ContainerNumber = "MSMU1683356, MRKU8254509",
            ScanTime = DateTime.UtcNow.AddMinutes(-30),
            ScanImage = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
            ImageDisplayName = "40426305424_W1.ase"
        });
        SeedAnalysisGroupWithRecords(db, groupId, "ASE", "MSMU1683356", "MRKU8254509");
        await db.SaveChangesAsync();

        var service = NewSplitIntake(db, splitter, new StubAseConverter(renderedAseBytes));

        var result = await service.EnsureSplitJobForOriginalAsync(original.Id);

        Assert.True(result.IsApplicable);
        Assert.True(result.SplitJobCreated);
        Assert.False(result.SplitJobFound);
        Assert.Equal(2, result.LinkedAnalysisRecords);
        Assert.Equal("completed", result.Status);
        Assert.Equal("MSMU1683356,MRKU8254509", splitter.SubmittedContainerNumbers);
        Assert.Equal("ASE", splitter.SubmittedScannerType);
        Assert.Equal(renderedAseBytes, splitter.SubmittedImageData);

        var records = await db.AnalysisRecords
            .AsNoTracking()
            .OrderBy(record => record.ContainerNumber)
            .ToListAsync();

        Assert.All(records, record =>
        {
            Assert.True(record.IsMultiContainerScan);
            Assert.Equal(jobId, record.SplitJobId);
            Assert.Equal(SplitAnalysisStatus.Ready, record.SplitStatus);
            Assert.Equal(optionA, record.SplitOptionA_ResultId);
            Assert.Equal(optionB, record.SplitOptionB_ResultId);
        });
        Assert.Equal("right", records.Single(record => record.ContainerNumber == "MRKU8254509").SplitPosition);
        Assert.Equal("left", records.Single(record => record.ContainerNumber == "MSMU1683356").SplitPosition);
    }

    [Fact]
    public async Task TwoContainerSplitIntake_ExactFs6000Original_UsesImageBytesAndFileSizeRankForSubmission()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var smallerMain = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var largerMain = new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0xD9 };
        var splitter = new StubImageSplitterService
        {
            SubmittedJob = new SplitJobReference(jobId, "pending"),
            StatusByJobId =
            {
                [jobId] = new SplitJobStatus(
                    jobId,
                    "pending",
                    BestStrategy: null,
                    BestConfidence: null,
                    SplitX: null,
                    ResultCount: 0)
            }
        };

        var original = await SeedOriginalScanAsync(db, "FS6000", "CAXU6863152, MSBU3047832");
        var scanId = Guid.NewGuid();
        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            OriginalScanRecordId = original.Id,
            ContainerNumber = "CAXU6863152",
            PicNumber = "PIC-FS-1",
            ScanTime = DateTime.UtcNow.AddMinutes(-15),
            HasImage = true,
            ImageCount = 2,
            Images =
            {
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "Main",
                    FileName = "small-main.jpg",
                    ImageData = smallerMain,
                    FileSizeBytes = smallerMain.Length,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-15)
                },
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "Main",
                    FileName = "large-main.jpg",
                    ImageData = largerMain,
                    FileSizeBytes = largerMain.Length,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-14)
                }
            }
        });
        SeedAnalysisGroupWithRecords(db, groupId, "FS6000", "CAXU6863152", "MSBU3047832");
        await db.SaveChangesAsync();

        var service = NewSplitIntake(db, splitter, new StubAseConverter());

        var result = await service.EnsureSplitJobForOriginalAsync(original.Id);

        Assert.True(result.IsApplicable);
        Assert.True(result.SplitJobCreated);
        Assert.Equal("pending", result.Status);
        Assert.Equal(scanId, splitter.SubmittedSourceImageId);
        Assert.Equal(largerMain, splitter.SubmittedImageData);
        Assert.Equal("FS6000", splitter.SubmittedScannerType);

        var linked = await db.AnalysisRecords.AsNoTracking().ToListAsync();
        Assert.All(linked, record =>
        {
            Assert.True(record.IsMultiContainerScan);
            Assert.Equal(jobId, record.SplitJobId);
            Assert.Equal(SplitAnalysisStatus.Pending, record.SplitStatus);
        });
    }

    [Fact]
    public async Task TwoContainerSplitIntake_Fs6000NonJpegImage_UsesViewerRenderedImageForSubmission()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var rawPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var renderedViewerJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var splitter = new StubImageSplitterService
        {
            SubmittedJob = new SplitJobReference(jobId, "pending"),
            StatusByJobId =
            {
                [jobId] = new SplitJobStatus(
                    jobId,
                    "pending",
                    BestStrategy: null,
                    BestConfidence: null,
                    SplitX: null,
                    ResultCount: 0)
            }
        };

        var original = await SeedOriginalScanAsync(db, "FS6000", "CAXU6863152, MSBU3047832");
        var scanId = Guid.NewGuid();
        db.FS6000Scans.Add(new FS6000Scan
        {
            Id = scanId,
            OriginalScanRecordId = original.Id,
            ContainerNumber = "CAXU6863152",
            PicNumber = "PIC-FS-RAW",
            ScanTime = DateTime.UtcNow.AddMinutes(-15),
            HasImage = true,
            ImageCount = 1,
            Images =
            {
                new FS6000Image
                {
                    ScanId = scanId,
                    ImageType = "Main",
                    FileName = "raw-main.png",
                    ImageData = rawPng,
                    FileSizeBytes = rawPng.Length,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-15)
                }
            }
        });
        SeedAnalysisGroupWithRecords(db, groupId, "FS6000", "CAXU6863152", "MSBU3047832");
        await db.SaveChangesAsync();

        var service = NewSplitIntake(
            db,
            splitter,
            new StubAseConverter(),
            new ThrowingImageProcessingService(renderedViewerJpeg));

        var result = await service.EnsureSplitJobForOriginalAsync(original.Id);

        Assert.True(result.IsApplicable);
        Assert.True(result.SplitJobCreated);
        Assert.Equal(scanId, splitter.SubmittedSourceImageId);
        Assert.Equal(renderedViewerJpeg, splitter.SubmittedImageData);
        Assert.NotEqual(rawPng, splitter.SubmittedImageData);
    }

    [Fact]
    public async Task TwoContainerSplitIntake_CompositeAnalysisRecord_PromotesToLogicalChildAndCreatesSibling()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var optionA = Guid.NewGuid();
        var optionB = Guid.NewGuid();
        var splitter = new StubImageSplitterService
        {
            ExistingJob = new SplitJobReference(jobId, "completed"),
            StatusByJobId =
            {
                [jobId] = new SplitJobStatus(
                    jobId,
                    "completed",
                    BestStrategy: "projection",
                    BestConfidence: 0.93,
                    SplitX: 512,
                    ResultCount: 2)
            },
            ResultsByJobId =
            {
                [jobId] = new[]
                {
                    new SplitResultReference(optionA, "projection", 0.93),
                    new SplitResultReference(optionB, "edge", 0.89)
                }
            }
        };

        var original = await SeedOriginalScanAsync(db, "ASE", "MSMU1683356, MRKU8254509");
        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            NormalizedGroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            GroupType = "Container"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            Status = AnalysisStatuses.Ready,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
        {
            ContainerNumber = "MSMU1683356",
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            WorkflowStage = "SplitPending",
            Status = "Complete",
            HasImageData = false
        });
        db.ContainerCompletenessStatuses.Add(new ContainerCompletenessStatus
        {
            ContainerNumber = "MRKU8254509",
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            WorkflowStage = "SplitPending",
            Status = "Complete",
            HasImageData = false
        });
        await db.SaveChangesAsync();

        var service = NewSplitIntake(db, splitter, new StubAseConverter());

        var result = await service.EnsureSplitJobForOriginalAsync(original.Id);

        Assert.True(result.IsApplicable);
        Assert.True(result.SplitJobFound);
        Assert.Equal(4, result.LinkedAnalysisRecords);

        var records = await db.AnalysisRecords
            .AsNoTracking()
            .OrderBy(record => record.ContainerNumber)
            .ToListAsync();
        Assert.Equal(new[] { "MRKU8254509", "MSMU1683356" }, records.Select(record => record.ContainerNumber));
        Assert.DoesNotContain(records, record => record.ContainerNumber.Contains(','));
        Assert.All(records, record =>
        {
            Assert.True(record.IsMultiContainerScan);
            Assert.Equal(jobId, record.SplitJobId);
            Assert.Equal(SplitAnalysisStatus.Ready, record.SplitStatus);
        });

        var completeness = await db.ContainerCompletenessStatuses
            .AsNoTracking()
            .OrderBy(row => row.ContainerNumber)
            .ToListAsync();
        Assert.All(completeness, row =>
        {
            Assert.True(row.HasImageData);
            Assert.Null(row.GroupIdentifier);
            Assert.Equal("ImageAnalysis", row.WorkflowStage);
        });
    }

    [Fact]
    public async Task ImageSplitterController_GetSplitOptions_UsesLogicalChildAnalysisRecordSplitJob()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var optionA = Guid.NewGuid();
        var optionB = Guid.NewGuid();
        var responseJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = optionA.ToString(),
                strategy_name = "projection",
                split_x = 420,
                confidence = 0.98,
                reasoning = "primary candidate"
            },
            new
            {
                id = optionB.ToString(),
                strategy_name = "edge",
                split_x = 430,
                confidence = 0.91,
                reasoning = "secondary candidate"
            }
        });
        var clientFactory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });

        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            NormalizedGroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            GroupType = "Container"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = jobId,
            SplitPosition = "left",
            SplitStatus = SplitAnalysisStatus.Ready,
            SplitOptionA_ResultId = optionA,
            SplitOptionB_ResultId = optionB,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new ImageSplitterController(
            clientFactory,
            NullLogger<ImageSplitterController>.Instance,
            db,
            new PassthroughSigner(),
            new StubSplitIntakeService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var action = await controller.GetSplitOptions("MSMU1683356");

        var ok = Assert.IsType<OkObjectResult>(action);
        var payload = JsonSerializer.SerializeToElement(ok.Value);
        Assert.True(payload.GetProperty("isMultiContainer").GetBoolean());
        Assert.Equal(jobId, payload.GetProperty("jobId").GetGuid());
        Assert.Equal(SplitAnalysisStatus.Ready, payload.GetProperty("splitStatus").GetString());
        Assert.Equal("left", payload.GetProperty("position").GetString());
        Assert.Equal(
            $"/api/image-splitter/jobs/{jobId}/original?signed=1",
            payload.GetProperty("originalImageUrl").GetString());

        var options = payload.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(2, options.Count);
        Assert.Equal(optionA.ToString(), options[0].GetProperty("resultId").GetString());
        Assert.Equal(
            $"/api/image-splitter/jobs/{jobId}/results/{optionA}/lossless/left?signed=1",
            options[0].GetProperty("cropImageUrl").GetString());
        Assert.Equal($"/api/split/{jobId}/results", clientFactory.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task ImageSplitterController_GetSplitOptions_FallsBackToStoredSplitResultIds_WhenResultListingFails()
    {
        await using var db = NewInMemoryDb();
        var groupId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var optionA = Guid.NewGuid();
        var optionB = Guid.NewGuid();
        var clientFactory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"results unavailable\"}")
        });

        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = "MSMU1683356, MRKU8254509",
            NormalizedGroupIdentifier = "MSMU1683356, MRKU8254509",
            ScannerType = "ASE",
            GroupType = "Container"
        });
        db.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = groupId,
            ContainerNumber = "MSMU1683356",
            ScannerType = "ASE",
            IsMultiContainerScan = true,
            SplitJobId = jobId,
            SplitPosition = "right",
            SplitStatus = SplitAnalysisStatus.Ready,
            SplitOptionA_ResultId = optionA,
            SplitOptionB_ResultId = optionB,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new ImageSplitterController(
            clientFactory,
            NullLogger<ImageSplitterController>.Instance,
            db,
            new PassthroughSigner(),
            new StubSplitIntakeService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var action = await controller.GetSplitOptions("MSMU1683356");

        var ok = Assert.IsType<OkObjectResult>(action);
        var payload = JsonSerializer.SerializeToElement(ok.Value);
        var options = payload.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(2, options.Count);
        Assert.Equal(optionA.ToString(), options[0].GetProperty("resultId").GetString());
        Assert.Equal("Stored option A", options[0].GetProperty("strategy").GetString());
        Assert.Equal("right", options[0].GetProperty("side").GetString());
        Assert.Equal(
            $"/api/image-splitter/jobs/{jobId}/results/{optionA}/lossless/right?signed=1",
            options[0].GetProperty("cropImageUrl").GetString());
        Assert.Equal(optionB.ToString(), options[1].GetProperty("resultId").GetString());
        Assert.Equal($"/api/split/{jobId}/results", clientFactory.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task ScanAssetsController_GetSourceImage_WithSplitCropIdentity_ProxiesLosslessCropBytes()
    {
        await using var db = NewInMemoryDb();
        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var cropBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var cropContent = new ByteArrayContent(cropBytes);
        cropContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var clientFactory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = cropContent
        });

        var controller = new ScanAssetsController(
            new StaticScanAssetResolver(new NickScanCentralImagingPortal.Core.DTOs.ScanAssets.ScanAssetResolution
            {
                Found = true,
                SourceScannerType = "ASE",
                SourceScanId = "4812",
                OriginalScanRecordId = 4812,
                SourceContainerNumbers = "MSMU1683356, MRKU8254509",
                SplitJobId = jobId,
                SplitResultId = resultId,
                SplitPosition = "left"
            }),
            new ThrowingImageProcessingService(),
            clientFactory,
            db,
            NullLogger<ScanAssetsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var action = await controller.GetSourceImage(
            "4812",
            "MSMU1683356",
            imageType: null,
            size: "full",
            splitJobId: jobId,
            splitResultId: resultId,
            side: "left");

        var file = Assert.IsType<FileContentResult>(action);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(cropBytes, file.FileContents);
        Assert.Equal(cropBytes.Length, controller.Response.ContentLength);
        Assert.Equal($"/api/split/{jobId}/results/{resultId}/lossless/left", clientFactory.Requests.Single().PathAndQuery);
    }

    private static ApplicationDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SourceScanSplitFlow_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .EnableServiceProviderCaching(false)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static TwoContainerSplitIntakeService NewSplitIntake(
        ApplicationDbContext db,
        IImageSplitterService splitter,
        IASEImageConverterService aseConverter,
        IImageProcessingService? imageProcessing = null)
    {
        return new TwoContainerSplitIntakeService(
            db,
            splitter,
            Array.Empty<IScanFormatAdapter>(),
            aseConverter,
            imageProcessing ?? new ThrowingImageProcessingService(),
            new ConfigurationBuilder().Build(),
            NullLogger<TwoContainerSplitIntakeService>.Instance);
    }

    private static async Task<OriginalScanRecord> SeedOriginalScanAsync(
        ApplicationDbContext db,
        string scannerType,
        string originalContainerNumbers)
    {
        var original = new OriginalScanRecord
        {
            ScannerType = scannerType,
            OriginalContainerNumbers = originalContainerNumbers,
            DerivedRecordCount = 2,
            ScanTime = DateTime.UtcNow.AddMinutes(-30),
            IngestedAt = DateTime.UtcNow.AddMinutes(-20)
        };

        db.OriginalScanRecords.Add(original);
        await db.SaveChangesAsync();
        return original;
    }

    private static void SeedAnalysisGroupWithRecords(
        ApplicationDbContext db,
        Guid groupId,
        string scannerType,
        params string[] containers)
    {
        db.AnalysisGroups.Add(new AnalysisGroup
        {
            Id = groupId,
            GroupIdentifier = string.Join(", ", containers),
            NormalizedGroupIdentifier = string.Join(", ", containers),
            ScannerType = scannerType,
            GroupType = "Container"
        });

        foreach (var container in containers)
        {
            db.AnalysisRecords.Add(new AnalysisRecord
            {
                GroupId = groupId,
                ContainerNumber = container,
                ScannerType = scannerType,
                Status = AnalysisStatuses.Ready,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class StubImageSplitterService : IImageSplitterService
    {
        public SplitJobReference? ExistingJob { get; init; }
        public SplitJobReference? SubmittedJob { get; init; }
        public string? SubmittedContainerNumbers { get; private set; }
        public byte[]? SubmittedImageData { get; private set; }
        public Guid? SubmittedSourceImageId { get; private set; }
        public string? SubmittedScannerType { get; private set; }
        public Dictionary<Guid, SplitJobStatus> StatusByJobId { get; } = new();
        public Dictionary<Guid, IReadOnlyList<SplitResultReference>> ResultsByJobId { get; } = new();

        public Task<SplitJobReference?> SubmitSplitJobAsync(
            string containerNumbers,
            byte[] imageData,
            Guid? sourceImageId = null,
            string? scannerType = null,
            CancellationToken cancellationToken = default)
        {
            SubmittedContainerNumbers = containerNumbers;
            SubmittedImageData = imageData;
            SubmittedSourceImageId = sourceImageId;
            SubmittedScannerType = scannerType;
            return Task.FromResult(SubmittedJob);
        }

        public Task<SplitJobReference?> FindLatestJobByContainersAsync(
            string containerNumbers,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingJob);
        }

        public Task<SplitJobStatus?> GetJobStatusAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            StatusByJobId.TryGetValue(jobId, out var status);
            return Task.FromResult(status);
        }

        public Task<IReadOnlyList<SplitResultReference>> GetTopSplitResultsAsync(
            Guid jobId,
            int take = 2,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ResultsByJobId.TryGetValue(jobId, out var results)
                    ? results.Take(take).ToList()
                    : (IReadOnlyList<SplitResultReference>)Array.Empty<SplitResultReference>());
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class StubAseConverter : IASEImageConverterService
    {
        private readonly byte[] _imageData;

        public StubAseConverter(byte[]? imageData = null)
        {
            _imageData = imageData ?? new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        }

        public Task<AseImageConversionResult> ConvertAseImageToJpegAsync(byte[] proprietaryImageData)
        {
            return Task.FromResult(new AseImageConversionResult
            {
                Success = true,
                ImageData = _imageData,
                Metadata = new NickScanCentralImagingPortal.Core.Entities.ImageMetadata
                {
                    FileSizeBytes = _imageData.Length,
                    ImageFormat = "JPEG"
                }
            });
        }
    }

    private sealed class PassthroughSigner : ISignedImageUrlSigner
    {
        public string SignRelative(string apiPath, TimeSpan? ttl = null, string? uid = null)
        {
            var separator = apiPath.Contains('?') ? "&" : "?";
            return $"{apiPath}{separator}signed=1";
        }

        public string SignAbsolute(string absoluteUrl, TimeSpan? ttl = null, string? uid = null)
        {
            return absoluteUrl;
        }
    }

    private sealed class StubSplitIntakeService : ITwoContainerSplitIntakeService
    {
        public Task<TwoContainerSplitEnsureResult> EnsureSplitJobForOriginalAsync(
            int originalScanRecordId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TwoContainerSplitEnsureResult(
                originalScanRecordId,
                IsApplicable: false,
                SplitJobCreated: false,
                SplitJobFound: false,
                LinkedAnalysisRecords: 0,
                Status: "NotUsed"));
        }

        public Task<TwoContainerSplitSweepResult> SweepAsync(
            int submitLimit = 25,
            int linkLimit = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TwoContainerSplitSweepResult(0, 0, 0, 0, 0));
        }
    }

    private sealed class StaticScanAssetResolver : IScanAssetResolver
    {
        private readonly NickScanCentralImagingPortal.Core.DTOs.ScanAssets.ScanAssetResolution _resolution;

        public StaticScanAssetResolver(NickScanCentralImagingPortal.Core.DTOs.ScanAssets.ScanAssetResolution resolution)
        {
            _resolution = resolution;
        }

        public Task<NickScanCentralImagingPortal.Core.DTOs.ScanAssets.ScanAssetResolution> ResolveAsync(
            string containerNumber,
            string? groupIdentifier = null,
            int? analysisRecordId = null,
            Guid? splitJobId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_resolution);
        }
    }

    private sealed class ThrowingImageProcessingService : IImageProcessingService
    {
        private readonly byte[]? _completeContainerImageBytes;

        public ThrowingImageProcessingService(byte[]? completeContainerImageBytes = null)
        {
            _completeContainerImageBytes = completeContainerImageBytes;
        }

        public Task<NickScanCentralImagingPortal.Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber) => throw new NotSupportedException();
        public Task<NickScanCentralImagingPortal.Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber, ScannerType preferredScanner) => throw new NotSupportedException();
        public Task<NickScanCentralImagingPortal.Core.Models.ImageProcessingResult> ProcessImageAsync(ImageDetails image, ImageProcessingRequest request) => throw new NotSupportedException();
        public Task<NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber) => throw new NotSupportedException();
        public Task<NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber, ScannerType? preferredScanner = null) => throw new NotSupportedException();
        public Task<ScannerType> DetectScannerTypeAsync(string containerNumber) => throw new NotSupportedException();
        public Task<BatchProcessingResult> BatchProcessImagesAsync(BatchProcessingRequest request) => throw new NotSupportedException();
        public Task RetryImageProcessingAsync(int imageId) => throw new NotSupportedException();
        public Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null) => throw new NotSupportedException();
        public Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber) => GetCompleteContainerDataAsync(containerNumber, null);
        public Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber, string? imageType)
        {
            if (_completeContainerImageBytes == null)
                throw new NotSupportedException();

            return Task.FromResult<ContainerImageDataResponse?>(new ContainerImageDataResponse
            {
                ContainerNumber = containerNumber,
                DetectedScanner = ScannerType.FS6000,
                ImageBytes = _completeContainerImageBytes,
                MimeType = "image/jpeg",
                ImageSizeBytes = _completeContainerImageBytes.Length
            });
        }
        public Task<byte[]?> GetRenderedImageBytesAsync(string containerNumber, string mode, float loPct = 1, float hiPct = 99.5F, float gamma = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RoiInspectorResult?> GetRoiInspectorAsync(string containerNumber, int x, int y, int width, int height, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ScanModeCapabilities?> GetScanModeCapabilitiesAsync(string containerNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PixelValueResult?> GetPixelValueAsync(string containerNumber, int x, int y, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RawPlaneResult?> GetRawPlaneAsync(string containerNumber, string plane, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FS6000RawChannelIngestionReport> IngestFS6000RawChannelsAsync(Guid scanId, string folderPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ServedImageDimensions> GetServedImageDimensionsAsync(string containerNumber, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpResponseMessage _response;

        public StubHttpClientFactory(HttpResponseMessage response)
        {
            _response = response;
        }

        public List<Uri> Requests { get; } = new();

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(_response, Requests))
            {
                BaseAddress = new Uri("http://splitter.test")
            };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly List<Uri> _requests;

        public StubHttpMessageHandler(HttpResponseMessage response, List<Uri> requests)
        {
            _response = response;
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request.RequestUri!);
            return Task.FromResult(_response);
        }
    }
}
