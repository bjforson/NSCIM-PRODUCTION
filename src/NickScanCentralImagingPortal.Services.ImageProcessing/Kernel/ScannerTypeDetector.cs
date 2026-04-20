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

        public ScannerTypeDetector(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ScannerType> DetectAsync(string containerNumber, CancellationToken ct = default)
        {
            if (await _db.FS6000Scans.AnyAsync(s => s.ContainerNumber == containerNumber, ct))
                return ScannerType.FS6000;

            if (await _db.AseScans.AnyAsync(s => s.ContainerNumber == containerNumber, ct))
                return ScannerType.ASE;

            return ScannerType.Unknown;
        }
    }
}
