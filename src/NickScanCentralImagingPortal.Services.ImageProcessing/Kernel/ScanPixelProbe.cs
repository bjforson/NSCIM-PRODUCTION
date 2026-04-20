using System;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — pure-function pixel probe. Takes a <see cref="DecodedScan"/>
    /// + coords, returns the <see cref="PixelValueResult"/> that powers the
    /// viewer's hover chip. Dispatches on scan structure (dual-energy vs
    /// single-channel), NOT on scanner identity — a 3-energy scanner plugs in
    /// through the adapter and this probe automatically handles it.
    /// </summary>
    public static class ScanPixelProbe
    {
        public static PixelValueResult Probe(DecodedScan scan, int x, int y)
        {
            // Clamp to image bounds. Clients can freely send rounded or
            // transformed coordinates; we don't 422 on edge overflow.
            int xc = Math.Max(0, Math.Min(x, scan.Width  - 1));
            int yc = Math.Max(0, Math.Min(y, scan.Height - 1));
            int idx = yc * scan.Width + xc;

            var he = scan.ChannelByKind(EnergyKind.High)
                  ?? scan.ChannelByKind(EnergyKind.Single);
            var le = scan.ChannelByKind(EnergyKind.Low);
            var mat = scan.Material;

            int? heVal = he?.Pixels[idx];
            int? leVal = le?.Pixels[idx];
            int? matVal = mat?.Classes[idx];

            // Vendor-LUT RGB lookup only works when both energies + material
            // are all present. Skip for single-view / material-less scans.
            int? r = null, g = null, b = null;
            if (heVal.HasValue && leVal.HasValue && matVal.HasValue)
            {
                int heBucket = heVal.Value >> 11;
                if (heBucket >= FS6000VendorLutCompositor.HeBuckets) heBucket = FS6000VendorLutCompositor.HeBuckets - 1;
                int leBucket = leVal.Value >> 11;
                if (leBucket >= FS6000VendorLutCompositor.LeBuckets) leBucket = FS6000VendorLutCompositor.LeBuckets - 1;
                var (cr, cg, cb) = FS6000VendorLutCompositor.LookupRgb(matVal.Value, heBucket, leBucket);
                r = cr; g = cg; b = cb;
            }

            string materialCategory = "";
            if (mat != null && matVal.HasValue)
            {
                materialCategory = mat.Taxonomy.BandFor((byte)matVal.Value).Category;
            }

            return new PixelValueResult
            {
                ContainerNumber = scan.ContainerNumber,
                X = xc, Y = yc,
                ImageWidth = scan.Width, ImageHeight = scan.Height,
                HighEnergy = heVal,
                LowEnergy = leVal,
                Material = matVal,
                Red = r, Green = g, Blue = b,
                MaterialCategory = materialCategory,
                Variant = scan.SourceFormatTag,
            };
        }
    }
}
