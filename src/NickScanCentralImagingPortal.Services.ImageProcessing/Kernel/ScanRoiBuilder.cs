using System;
using System.Collections.Generic;
using System.Linq;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — pure-function ROI inspector. Takes a <see cref="DecodedScan"/>
    /// + rectangle, returns the <see cref="RoiInspectorResult"/> that powers
    /// the viewer's side panel.
    ///
    /// Dispatch is structural: scans with two energies + material get a full
    /// dual-energy ROI (both histograms + material-class distribution); scans
    /// with one channel + no material degrade to a single-channel histogram
    /// with an "n/a" material block so the UI doesn't null-check.
    ///
    /// No scanner-specific branching — a 3-energy scanner whose adapter adds
    /// a Mid channel to <see cref="DecodedScan.Channels"/> would pick up a
    /// straightforward extension here (Mid histogram added to the output).
    /// </summary>
    public static class ScanRoiBuilder
    {
        public static RoiInspectorResult Build(DecodedScan scan, int x, int y, int w, int h)
        {
            var started = DateTime.UtcNow;

            // Clamp to image bounds — out-of-range rectangles come in routinely
            // (zoomed-in pan spill-past-edge) and a 422 would be bad UX.
            int x0 = Math.Max(0, Math.Min(x, scan.Width  - 1));
            int y0 = Math.Max(0, Math.Min(y, scan.Height - 1));
            int x1 = Math.Max(x0 + 1, Math.Min(x + w, scan.Width));
            int y1 = Math.Max(y0 + 1, Math.Min(y + h, scan.Height));
            int cw = x1 - x0;
            int ch = y1 - y0;
            int n = cw * ch;

            var he  = scan.ChannelByKind(EnergyKind.High)
                   ?? scan.ChannelByKind(EnergyKind.Single);
            var le  = scan.ChannelByKind(EnergyKind.Low);
            var mat = scan.Material;

            // Crop HE (always present; for single-view we treat the Single
            // channel as HE). Same row-copy pattern as the old pipelines.
            var heCrop = he != null ? CropChannel(he.Pixels, scan.Width, x0, y0, cw, ch) : new ushort[n];
            var leCrop = le != null ? CropChannel(le.Pixels, scan.Width, x0, y0, cw, ch) : null;
            var matCrop = mat != null ? CropMaterial(mat.Classes, scan.Width, x0, y0, cw, ch) : null;

            var heStats = RoiStatsUtil.ChannelStats16(heCrop);
            var leStats = leCrop != null ? RoiStatsUtil.ChannelStats16(leCrop) : heStats; // degrade: mirror HE for single-channel
            var materialStats = matCrop != null
                ? MaterialStatsFromTaxonomy(matCrop, mat!.Taxonomy)
                : new MaterialStats
                {
                    DominantCategory = "n/a (no material)",
                    DominantPercent = 0,
                    CategoryDistribution = new Dictionary<string, double>
                    {
                        ["background"] = 0, ["noise"] = 0, ["organic"] = 0, ["metal"] = 0,
                    },
                };

            return new RoiInspectorResult
            {
                ContainerNumber = scan.ContainerNumber,
                X = x0, Y = y0, Width = cw, Height = ch,
                ImageWidth = scan.Width, ImageHeight = scan.Height,
                HighEnergy = heStats,
                LowEnergy  = leStats,
                Material   = materialStats,
                HighEnergyPreviewB64 = Convert.ToBase64String(RoiPreviewUtil.EnergyPreview(heCrop, cw, ch)),
                LowEnergyPreviewB64  = leCrop != null ? Convert.ToBase64String(RoiPreviewUtil.EnergyPreview(leCrop, cw, ch)) : string.Empty,
                MaterialPreviewB64   = matCrop != null ? Convert.ToBase64String(RoiPreviewUtil.MaterialPreview(matCrop, cw, ch)) : string.Empty,
                ElapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds,
            };
        }

        private static ushort[] CropChannel(ushort[] src, int srcWidth, int x0, int y0, int cw, int ch)
        {
            var dst = new ushort[cw * ch];
            for (int r = 0; r < ch; r++)
            {
                int srcRow = (y0 + r) * srcWidth + x0;
                int dstRow = r * cw;
                src.AsSpan(srcRow, cw).CopyTo(dst.AsSpan(dstRow, cw));
            }
            return dst;
        }

        private static byte[] CropMaterial(byte[] src, int srcWidth, int x0, int y0, int cw, int ch)
        {
            var dst = new byte[cw * ch];
            for (int r = 0; r < ch; r++)
            {
                int srcRow = (y0 + r) * srcWidth + x0;
                int dstRow = r * cw;
                src.AsSpan(srcRow, cw).CopyTo(dst.AsSpan(dstRow, cw));
            }
            return dst;
        }

        /// <summary>
        /// Material-class distribution using the scan's DECLARED taxonomy,
        /// not hardcoded FS6000 thresholds. This is the piece that lets a
        /// future scanner with its own class scheme (e.g. 0..63) get accurate
        /// ROI stats for free.
        /// </summary>
        private static MaterialStats MaterialStatsFromTaxonomy(byte[] classes, MaterialTaxonomy taxonomy)
        {
            if (classes.Length == 0) return new MaterialStats();

            var counts = new Dictionary<string, long>();
            foreach (var band in taxonomy.Bands) counts[band.Category] = 0;

            for (int i = 0; i < classes.Length; i++)
            {
                var band = taxonomy.BandFor(classes[i]);
                counts[band.Category] = counts.GetValueOrDefault(band.Category) + 1;
            }

            int n = classes.Length;
            var dist = counts.ToDictionary(kv => kv.Key, kv => (double)kv.Value / n);

            // Pick the most interesting non-background/non-noise category,
            // preferring metal over organic if both meaningful. Fall back to
            // whichever background-ish category dominates so the UI always
            // has something to display.
            var meaningful = dist.Where(kv => kv.Key != "background" && kv.Key != "noise").ToList();
            string dominant;
            double dominantPct;

            var interesting = meaningful.Where(kv => kv.Value >= 0.01).ToList();
            if (interesting.Count == 0)
            {
                var bgNoise = dist.Where(kv => kv.Key == "background" || kv.Key == "noise")
                                  .OrderByDescending(kv => kv.Value)
                                  .FirstOrDefault();
                dominant = bgNoise.Key ?? "background";
                dominantPct = bgNoise.Value;
            }
            else
            {
                // Prefer categories that look "higher Z" — fall back to
                // whichever has highest share when that can't be inferred.
                var metalLike = interesting.Where(kv => kv.Key == "metal").FirstOrDefault();
                if (metalLike.Key != null && metalLike.Value >= interesting.Where(kv => kv.Key != "metal").Select(kv => kv.Value).DefaultIfEmpty(0).Max())
                {
                    dominant = metalLike.Key;
                    dominantPct = metalLike.Value;
                }
                else
                {
                    var top = interesting.OrderByDescending(kv => kv.Value).First();
                    dominant = top.Key;
                    dominantPct = top.Value;
                }
            }

            return new MaterialStats
            {
                DominantCategory = dominant,
                DominantPercent = dominantPct,
                CategoryDistribution = dist,
            };
        }
    }
}
