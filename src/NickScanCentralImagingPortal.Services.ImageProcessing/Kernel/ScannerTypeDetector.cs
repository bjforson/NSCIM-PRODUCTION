using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — default implementation of <see cref="IScannerTypeDetector"/>.
    /// Checks the scanner-specific tables in order. Mirrors the logic that
    /// used to live inline in <see cref="ImageProcessingService.DetectScannerTypeAsync"/>
    /// so behaviour is preserved exactly.
    /// </summary>
    public sealed class ScannerTypeDetector : IScannerTypeDetector
    {
        private readonly ApplicationDbContext _db;
        private readonly IScanAssetResolver _scanAssetResolver;

        public ScannerTypeDetector(ApplicationDbContext db, IScanAssetResolver scanAssetResolver)
        {
            _db = db;
            _scanAssetResolver = scanAssetResolver;
        }

        public async Task<ScannerType> DetectAsync(string containerNumber, CancellationToken ct = default)
        {
            var resolution = await _scanAssetResolver.ResolveAsync(containerNumber, cancellationToken: ct);
            if (resolution.Found && !resolution.IsAmbiguous)
            {
                if (string.Equals(resolution.SourceScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
                    return ScannerType.FS6000;

                if (string.Equals(resolution.SourceScannerType, "ASE", StringComparison.OrdinalIgnoreCase))
                    return ScannerType.ASE;
            }

            if (await _db.FS6000Scans.AnyAsync(s => s.ContainerNumber == containerNumber, ct))
                return ScannerType.FS6000;

            if (await _db.AseScans.AnyAsync(s => s.ContainerNumber == containerNumber, ct))
                return ScannerType.ASE;

            var trimmed = containerNumber.Trim();
            if (long.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var accession))
            {
                if (await _db.EagleA25Scans.AnyAsync(s => s.Accession == accession || s.ScanAccession == accession, ct))
                    return ScannerType.EagleA25;
            }

            if (await _db.EagleA25Scans.AnyAsync(s => s.CargoIdentifier == trimmed || s.AirWaybill == trimmed, ct))
                return ScannerType.EagleA25;

            return ScannerType.Unknown;
        }
    }
}
