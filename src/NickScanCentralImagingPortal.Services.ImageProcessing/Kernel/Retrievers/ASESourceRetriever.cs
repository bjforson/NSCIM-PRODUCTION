using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// v2.11.0 — reads ASE scan blobs from <c>asescans</c>. Single blob per
    /// scan (ScanImage column); variant (tri-panel vs single-view) is
    /// determined by the blob's internal header, not at retrieval time.
    /// </summary>
    public sealed class ASESourceRetriever : IScanSourceRetriever
    {
        private readonly ILogger<ASESourceRetriever> _logger;
        private readonly ApplicationDbContext _db;
        private readonly IScanAssetResolver _scanAssetResolver;

        public ScannerType ScannerType => ScannerType.ASE;

        public ASESourceRetriever(
            ILogger<ASESourceRetriever> logger,
            ApplicationDbContext db,
            IScanAssetResolver scanAssetResolver)
        {
            _logger = logger;
            _db = db;
            _scanAssetResolver = scanAssetResolver;
        }

        public async Task<ScanSourceBytes?> LoadAsync(string containerNumber, CancellationToken ct = default)
        {
            var sourceContainerNumber = await ResolveAseSourceContainerAsync(containerNumber, ct);
            var row = await _db.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber == sourceContainerNumber
                        && s.ScanImage != null
                        && s.ScanImage.Length > 16)
                .OrderByDescending(s => s.ScanTime)
                .Select(s => new { s.Id, s.ScanImage, s.ScanTime })
                .FirstOrDefaultAsync(ct);
            if (row == null)
            {
                _logger.LogDebug("[ASERetriever] No ASE scan with usable blob for {Container}", containerNumber);
                return null;
            }

            var blobs = new Dictionary<string, byte[]> { ["ScanImage"] = row.ScanImage! };
            var metadata = new Dictionary<string, string>
            {
                ["Scanner"]  = "ASE",
                ["ScanTime"] = row.ScanTime.ToString("O", CultureInfo.InvariantCulture),
            };

            return new ScanSourceBytes
            {
                ScanId          = row.Id.ToString(),
                ContainerNumber = sourceContainerNumber,
                SourceFormatTag = ASEFormatAdapter.FormatTag,
                Blobs           = blobs,
                Metadata        = metadata,
            };
        }

        public async Task<BlobInventory?> InventoryAsync(string containerNumber, CancellationToken ct = default)
        {
            var sourceContainerNumber = await ResolveAseSourceContainerAsync(containerNumber, ct);
            var row = await _db.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber == sourceContainerNumber)
                .OrderByDescending(s => s.ScanTime)
                .Select(s => new { s.Id, HasBlob = s.ScanImage != null && s.ScanImage.Length > 16 })
                .FirstOrDefaultAsync(ct);
            if (row == null) return null;

            return new BlobInventory
            {
                ScanId          = row.Id.ToString(),
                SourceFormatTag = ASEFormatAdapter.FormatTag,
                PresentBlobNames = row.HasBlob ? new[] { "ScanImage" } : Array.Empty<string>(),
                MissingBlobNames = row.HasBlob ? Array.Empty<string>() : new[] { "ScanImage" },
            };
        }

        private async Task<string> ResolveAseSourceContainerAsync(string containerNumber, CancellationToken ct)
        {
            var resolution = await _scanAssetResolver.ResolveAsync(containerNumber, cancellationToken: ct);
            if (resolution.Found
                && !resolution.IsAmbiguous
                && string.Equals(resolution.SourceScannerType, "ASE", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
            {
                return resolution.SourceContainerNumbers;
            }

            return containerNumber;
        }
    }
}
