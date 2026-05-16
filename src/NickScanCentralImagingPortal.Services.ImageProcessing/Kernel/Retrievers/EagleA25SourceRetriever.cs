using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Retrievers
{
    public sealed class EagleA25SourceRetriever : IScanSourceRetriever
    {
        private readonly ILogger<EagleA25SourceRetriever> _logger;
        private readonly ApplicationDbContext _db;

        public EagleA25SourceRetriever(ILogger<EagleA25SourceRetriever> logger, ApplicationDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public ScannerType ScannerType => ScannerType.EagleA25;

        public async Task<ScanSourceBytes?> LoadAsync(string containerNumber, CancellationToken ct = default)
        {
            var row = await FindScanAsync(containerNumber, ct);
            if (row == null)
            {
                _logger.LogDebug("[EagleA25Retriever] No Eagle A25 scan for lookup {Lookup}", containerNumber);
                return null;
            }

            var asset = row.Assets
                .Where(a => string.Equals(a.FileType, "XRAY", StringComparison.OrdinalIgnoreCase))
                .Where(a => !string.IsNullOrWhiteSpace(a.LocalPath))
                .OrderByDescending(a => a.FileSizeBytes ?? 0)
                .FirstOrDefault();

            if (asset == null || string.IsNullOrWhiteSpace(asset.LocalPath) || !File.Exists(asset.LocalPath))
            {
                _logger.LogDebug("[EagleA25Retriever] No copied XRAY cargoimage for lookup {Lookup}", containerNumber);
                return null;
            }

            var blob = await File.ReadAllBytesAsync(asset.LocalPath, ct);
            if (blob.Length < 40)
            {
                _logger.LogDebug("[EagleA25Retriever] XRAY cargoimage too small for lookup {Lookup}: {Bytes} bytes", containerNumber, blob.Length);
                return null;
            }

            var displayId = !string.IsNullOrWhiteSpace(row.CargoIdentifier)
                ? row.CargoIdentifier!
                : row.Accession.ToString(CultureInfo.InvariantCulture);

            var metadata = BuildMetadata(row, asset);

            return new ScanSourceBytes
            {
                ScanId = row.Id.ToString(),
                ContainerNumber = displayId,
                SourceFormatTag = EagleA25FormatAdapter.FormatTag,
                Blobs = new Dictionary<string, byte[]> { ["XRAY"] = blob },
                Metadata = metadata,
            };
        }

        public async Task<BlobInventory?> InventoryAsync(string containerNumber, CancellationToken ct = default)
        {
            var row = await FindScanAsync(containerNumber, ct);
            if (row == null)
                return null;

            var hasXray = row.Assets.Any(a =>
                string.Equals(a.FileType, "XRAY", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(a.LocalPath) &&
                File.Exists(a.LocalPath));

            return new BlobInventory
            {
                ScanId = row.Id.ToString(),
                SourceFormatTag = EagleA25FormatAdapter.FormatTag,
                PresentBlobNames = hasXray ? new[] { "XRAY" } : Array.Empty<string>(),
                MissingBlobNames = hasXray ? Array.Empty<string>() : new[] { "XRAY" },
            };
        }

        private async Task<Core.Entities.EagleA25.EagleA25Scan?> FindScanAsync(string lookup, CancellationToken ct)
        {
            var trimmed = lookup.Trim();
            var query = _db.EagleA25Scans
                .Include(s => s.Assets)
                .AsNoTracking();

            if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var accession))
            {
                return await query
                    .Where(s => s.Accession == accession || s.ScanAccession == accession)
                    .OrderByDescending(s => s.ScanDateUtc)
                    .FirstOrDefaultAsync(ct);
            }

            return await query
                .Where(s => s.CargoIdentifier == trimmed || s.AirWaybill == trimmed)
                .OrderByDescending(s => s.ScanDateUtc)
                .FirstOrDefaultAsync(ct);
        }

        private static Dictionary<string, string> BuildMetadata(
            Core.Entities.EagleA25.EagleA25Scan scan,
            Core.Entities.EagleA25.EagleA25ScanAsset asset)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scanner"] = "EAGLE_A25",
                ["SourceScanId"] = scan.SourceScanId.ToString(CultureInfo.InvariantCulture),
                ["SourceManifestId"] = scan.SourceManifestId.ToString(CultureInfo.InvariantCulture),
                ["Accession"] = scan.Accession.ToString(CultureInfo.InvariantCulture),
                ["AssetId"] = asset.Id.ToString(),
                ["FileType"] = asset.FileType,
                ["LocalPath"] = asset.LocalPath ?? string.Empty,
                ["ScanTime"] = scan.ScanDateUtc.ToString("O", CultureInfo.InvariantCulture),
            };

            if (!string.IsNullOrWhiteSpace(scan.CargoIdentifier))
                metadata["CargoIdentifier"] = scan.CargoIdentifier!;
            if (!string.IsNullOrWhiteSpace(scan.AirWaybill))
                metadata["AirWaybill"] = scan.AirWaybill!;

            return metadata;
        }
    }
}
