using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// ROI inspector stat helpers. Both <c>FS6000ImagePipeline</c> and
    /// <c>ASEImagePipeline</c> use these — the shapes (16-bit energy channels,
    /// 8-bit material-class indices) are identical after decode, so the stats
    /// pipeline is the same regardless of scanner.
    ///
    /// Split out of FS6000ImagePipeline in v2.10.0 when ASE tri-panel was
    /// added; same math, two callers, one implementation.
    /// </summary>
    internal static class RoiStatsUtil
    {
        /// <summary>
        /// Histogram-based stats on a 16-bit energy channel crop. O(N + 65536)
        /// regardless of ROI size.
        /// </summary>
        public static ChannelStats ChannelStats16(ushort[] values)
        {
            if (values.Length == 0) return new ChannelStats();

            var hist = new int[65536];
            int min = ushort.MaxValue, max = 0;
            long sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int v = values[i];
                hist[v]++;
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            int n = values.Length;

            int PercentileBin(double pct)
            {
                long target = (long)(n * pct);
                long acc = 0;
                for (int b = 0; b < 65536; b++)
                {
                    acc += hist[b];
                    if (acc >= target) return b;
                }
                return 65535;
            }

            // 32-bucket normalised histogram across the actual min..max range.
            var outHist = new double[32];
            if (max > min)
            {
                int range = max - min;
                for (int b = 0; b < 65536; b++)
                {
                    if (hist[b] == 0) continue;
                    int bucket = (int)((b - min) * 31L / range);
                    if (bucket < 0) bucket = 0;
                    if (bucket > 31) bucket = 31;
                    outHist[bucket] += hist[b];
                }
                for (int b = 0; b < 32; b++) outHist[b] /= n;
            }

            return new ChannelStats
            {
                Min = min,
                Max = max,
                Mean = (double)sum / n,
                Median = PercentileBin(0.5),
                P01 = PercentileBin(0.01),
                P99 = PercentileBin(0.99),
                Histogram = outHist,
            };
        }

        /// <summary>
        /// Count each material-class band within the crop and pick the
        /// dominant non-background category. "Meaningful" is organic or metal
        /// — if both fall below 1% of the ROI we fall back to reporting
        /// background / noise so the UI chip always shows something.
        /// </summary>
        public static MaterialStats MaterialStats(byte[] classes)
        {
            if (classes.Length == 0) return new MaterialStats();

            int background = 0, noise = 0, organic = 0, metal = 0;
            for (int i = 0; i < classes.Length; i++)
            {
                byte c = classes[i];
                if (c == 0) background++;
                else if (c < 41) noise++;
                else if (c < 121) organic++;
                else metal++;
            }
            int n = classes.Length;
            var dist = new Dictionary<string, double>
            {
                ["background"] = (double)background / n,
                ["noise"] = (double)noise / n,
                ["organic"] = (double)organic / n,
                ["metal"] = (double)metal / n,
            };

            string dominant;
            double dominantPct;
            if (dist["organic"] < 0.01 && dist["metal"] < 0.01)
            {
                dominant = dist["background"] >= dist["noise"] ? "background" : "noise";
                dominantPct = Math.Max(dist["background"], dist["noise"]);
            }
            else
            {
                if (dist["metal"] >= dist["organic"])
                {
                    dominant = "metal";
                    dominantPct = dist["metal"];
                }
                else
                {
                    dominant = "organic";
                    dominantPct = dist["organic"];
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

    /// <summary>
    /// ROI preview JPEG builders. Both energy + material previews are capped
    /// at 240 px on the long edge to keep the base64 wire payload small.
    /// </summary>
    internal static class RoiPreviewUtil
    {
        /// <summary>
        /// 16-bit energy channel → percentile-normalised, inverted (vendor
        /// convention), downscaled grayscale JPEG.
        /// </summary>
        public static byte[] EnergyPreview(ushort[] channel, int w, int h)
        {
            byte[] lum = FS6000Compositor.NormalizeEnergyChannel(channel);
            for (int i = 0; i < lum.Length; i++) lum[i] = (byte)(255 - lum[i]);
            using var img = Image.LoadPixelData<L8>(lum, w, h);
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(240, 240),
            }));
            using var ms = new MemoryStream(capacity: 8192);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }

        /// <summary>
        /// 8-bit material-class indices → LUT-colorised, downscaled JPEG.
        /// </summary>
        public static byte[] MaterialPreview(byte[] classes, int w, int h)
        {
            var lut = FS6000Compositor.DefaultMaterialLut;
            var rgb = new byte[classes.Length * 3];
            for (int i = 0; i < classes.Length; i++)
            {
                byte c = classes[i];
                int o = i * 3;
                rgb[o + 0] = lut[c, 0];
                rgb[o + 1] = lut[c, 1];
                rgb[o + 2] = lut[c, 2];
            }
            using var img = Image.LoadPixelData<Rgb24>(rgb, w, h);
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(240, 240),
            }));
            using var ms = new MemoryStream(capacity: 8192);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }
    }
}
