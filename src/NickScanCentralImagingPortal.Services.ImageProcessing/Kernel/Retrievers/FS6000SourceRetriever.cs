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
    /// v2.11.0 — reads FS6000 scan blobs from <c>fs6000scans</c> / <c>fs6000images</c>.
    /// Separate from <see cref="FS6000FormatAdapter"/> because storage access
    /// is IO-bound and stateful while format parsing is pure/stateless —
    /// keeps the adapter trivially unit-testable with canned bytes.
    ///
    /// Loads all 4 known blobs (HighEnergy / LowEnergy / Material / Main)
    /// when present; <see cref="FS6000FormatAdapter"/> decides what to do
    /// with whatever it gets.
    /// </summary>
    public sealed class FS6000SourceRetriever : IScanSourceRetriever
    {
        private readonly ILogger<FS6000SourceRetriever> _logger;
        private readonly ApplicationDbContext _db;

        public ScannerType ScannerType => ScannerType.FS6000;

        public FS6000SourceRetriever(ILogger<FS6000SourceRetriever> logger, ApplicationDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<ScanSourceBytes?> LoadAsync(string containerNumber, CancellationToken ct = default)
        {
            var scan = await _db.FS6000Scans
                .Include(s => s.Images)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber, ct);
            if (scan == null)
            {
                _logger.LogDebug("[FS6000Retriever] No scan for {Container}", containerNumber);
                return null;
            }

            var blobs = new Dictionary<string, byte[]>();
            foreach (var img in scan.Images)
            {
                if (img.ImageData == null) continue;
                // Only load channels the adapter + vendor-reference paths care about.
                if (img.ImageType == "HighEnergy" ||
                    img.ImageType == "LowEnergy"  ||
                    img.ImageType == "Material"   ||
                    img.ImageType == "Main")
                {
                    blobs[img.ImageType] = img.ImageData;
                }
            }

            var metadata = new Dictionary<string, string>
            {
                ["Scanner"] = "FS6000",
            };
            // ScanTime is currently absent on FS6000Scans (no column) in the
            // seeded schema; we'll look it up from the scan entity if the
            // property exists.
            var scanTimeProp = scan.GetType().GetProperty("ScanTime");
            if (scanTimeProp?.GetValue(scan) is DateTime st)
            {
                metadata["ScanTime"] = st.ToString("O", CultureInfo.InvariantCulture);
            }

            return new ScanSourceBytes
            {
                ScanId          = scan.Id.ToString(),
                ContainerNumber = containerNumber,
                SourceFormatTag = FS6000FormatAdapter.FormatTag,
                Blobs           = blobs,
                Metadata        = metadata,
            };
        }

        public async Task<BlobInventory?> InventoryAsync(string containerNumber, CancellationToken ct = default)
        {
            var rows = await (
                from s in _db.FS6000Scans.AsNoTracking()
                where s.ContainerNumber == containerNumber
                join i in _db.FS6000Images.AsNoTracking() on s.Id equals i.ScanId into imgs
                select new
                {
                    ScanId = s.Id,
                    ImageTypes = imgs.Select(i => i.ImageType).ToList(),
                }).FirstOrDefaultAsync(ct);

            if (rows == null) return null;

            var required = new[] { "HighEnergy", "LowEnergy", "Material" };
            var presentSet = new HashSet<string>(rows.ImageTypes);
            var present = presentSet.Where(t => required.Contains(t) || t == "Main").ToList();
            var missing = required.Where(r => !presentSet.Contains(r)).ToList();

            return new BlobInventory
            {
                ScanId          = rows.ScanId.ToString(),
                SourceFormatTag = FS6000FormatAdapter.FormatTag,
                PresentBlobNames = present,
                MissingBlobNames = missing,
            };
        }
    }
}
