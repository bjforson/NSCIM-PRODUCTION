using System.Text.Json;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ASE;

/// <summary>
/// Builds completeness queue items from ASE scanner rows.
/// ASE source rows preserve the raw scanner container label, but operational
/// queue rows must be keyed by one physical container only.
/// </summary>
public static class AseScanQueueItemFactory
{
    public static IReadOnlyList<ContainerScanInfo> CreateFromScan(
        AseScan scan,
        int priority = 0,
        bool recovered = false,
        DateTime? recoveryDateUtc = null)
    {
        ArgumentNullException.ThrowIfNull(scan);

        return Create(
            scan.InspectionId,
            scan.ContainerNumber,
            scan.ScanTime,
            priority,
            scan.TruckPlate,
            scan.InspectionUuid,
            recovered,
            recoveryDateUtc);
    }

    public static IReadOnlyList<ContainerScanInfo> Create(
        int inspectionId,
        string? containerNumber,
        DateTime scanTime,
        int priority = 0,
        string? truckPlate = null,
        string? inspectionUuid = null,
        bool recovered = false,
        DateTime? recoveryDateUtc = null)
    {
        var raw = (containerNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<ContainerScanInfo>();

        var tokens = raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Where(token => !string.Equals(token, "Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0)
            return Array.Empty<ContainerScanInfo>();

        if (tokens.Count == 1)
        {
            return new[]
            {
                CreateItem(
                    tokens[0],
                    inspectionId.ToString(),
                    scanTime,
                    priority,
                    BuildMetadata(raw, truckPlate, inspectionUuid, recovered, recoveryDateUtc))
            };
        }

        return tokens
            .Select((token, index) => CreateItem(
                token,
                $"{inspectionId}-{BuildSplitSuffix(index)}",
                scanTime,
                priority,
                BuildMetadata(raw, truckPlate, inspectionUuid, recovered, recoveryDateUtc, index, tokens.Count)))
            .ToList();
    }

    public static bool ContainsMultipleContainers(string? containerNumber)
    {
        return Create(
                inspectionId: 0,
                containerNumber,
                DateTime.UnixEpoch)
            .Count > 1;
    }

    private static ContainerScanInfo CreateItem(
        string containerNumber,
        string inspectionId,
        DateTime scanTime,
        int priority,
        string? metadata)
    {
        return new ContainerScanInfo
        {
            ContainerNumber = containerNumber,
            ScannerType = CommonScannerTypes.ASE,
            InspectionId = inspectionId,
            ScanDate = scanTime,
            Priority = priority,
            Metadata = metadata
        };
    }

    private static string BuildSplitSuffix(int index)
    {
        return index < 26
            ? ((char)('a' + index)).ToString()
            : (index + 1).ToString();
    }

    private static string? BuildMetadata(
        string originalContainerNumber,
        string? truckPlate,
        string? inspectionUuid,
        bool recovered,
        DateTime? recoveryDateUtc,
        int? splitTokenIndex = null,
        int? splitTokenCount = null)
    {
        var metadata = new Dictionary<string, object?>();

        AddIfPresent(metadata, "TruckPlate", truckPlate);
        AddIfPresent(metadata, "InspectionUuid", inspectionUuid);

        var hasOriginalContainerMetadata = originalContainerNumber.Contains(',', StringComparison.Ordinal)
            || originalContainerNumber.Contains(';', StringComparison.Ordinal)
            || splitTokenCount.GetValueOrDefault() > 1;

        if (hasOriginalContainerMetadata)
            metadata["OriginalContainerNumber"] = originalContainerNumber;

        if (splitTokenIndex.HasValue && splitTokenCount.HasValue)
        {
            metadata["MultiContainerScan"] = true;
            metadata["SplitTokenIndex"] = splitTokenIndex.Value;
            metadata["SplitTokenCount"] = splitTokenCount.Value;
        }

        if (recovered)
        {
            metadata["Recovered"] = true;
            metadata["RecoveryDate"] = (recoveryDateUtc ?? DateTime.UtcNow).ToString("O");
        }

        return metadata.Count == 0
            ? null
            : JsonSerializer.Serialize(metadata);
    }

    private static void AddIfPresent(Dictionary<string, object?> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value;
    }
}
