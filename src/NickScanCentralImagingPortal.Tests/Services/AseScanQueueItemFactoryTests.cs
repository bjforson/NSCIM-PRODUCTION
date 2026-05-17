using System.Text.Json;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Services.ASE;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services;

public sealed class AseScanQueueItemFactoryTests
{
    [Fact]
    public void Create_SingleContainer_PreservesInspectionId()
    {
        var scanTime = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

        var items = AseScanQueueItemFactory.Create(
            inspectionId: 84830,
            containerNumber: "TEMU2527526",
            scanTime,
            truckPlate: "GT-1234",
            inspectionUuid: "ase-uuid");

        var item = Assert.Single(items);
        Assert.Equal("TEMU2527526", item.ContainerNumber);
        Assert.Equal(CommonScannerTypes.ASE, item.ScannerType);
        Assert.Equal("84830", item.InspectionId);
        Assert.Equal(scanTime, item.ScanDate);
        Assert.Contains("GT-1234", item.Metadata);
        Assert.DoesNotContain("MultiContainerScan", item.Metadata);
    }

    [Fact]
    public void Create_DualContainer_EmitsOneQueueItemPerPhysicalContainer()
    {
        var scanTime = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

        var items = AseScanQueueItemFactory.Create(
            inspectionId: 84830,
            containerNumber: "TEMU2527526, TIIU2732427",
            scanTime,
            priority: 1,
            recovered: true,
            recoveryDateUtc: scanTime);

        Assert.Collection(
            items,
            first =>
            {
                Assert.Equal("TEMU2527526", first.ContainerNumber);
                Assert.Equal("84830-a", first.InspectionId);
                Assert.Equal(1, first.Priority);
                AssertMetadata(first.Metadata, splitTokenIndex: 0, splitTokenCount: 2);
            },
            second =>
            {
                Assert.Equal("TIIU2732427", second.ContainerNumber);
                Assert.Equal("84830-b", second.InspectionId);
                Assert.Equal(1, second.Priority);
                AssertMetadata(second.Metadata, splitTokenIndex: 1, splitTokenCount: 2);
            });
    }

    [Fact]
    public void Create_DropsUnknownAndDuplicateTokens()
    {
        var items = AseScanQueueItemFactory.Create(
            inspectionId: 100,
            containerNumber: "Unknown, TEMU2527526, TEMU2527526",
            scanTime: DateTime.UtcNow);

        var item = Assert.Single(items);
        Assert.Equal("TEMU2527526", item.ContainerNumber);
        Assert.Equal("100", item.InspectionId);
    }

    private static void AssertMetadata(string? metadata, int splitTokenIndex, int splitTokenCount)
    {
        Assert.False(string.IsNullOrWhiteSpace(metadata));
        using var doc = JsonDocument.Parse(metadata);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("Recovered").GetBoolean());
        Assert.True(root.GetProperty("MultiContainerScan").GetBoolean());
        Assert.Equal(splitTokenIndex, root.GetProperty("SplitTokenIndex").GetInt32());
        Assert.Equal(splitTokenCount, root.GetProperty("SplitTokenCount").GetInt32());
        Assert.Equal("TEMU2527526, TIIU2732427", root.GetProperty("OriginalContainerNumber").GetString());
    }
}
