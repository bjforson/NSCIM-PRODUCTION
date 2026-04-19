using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.ASE
{
    /// <summary>
    /// Turns a decoded ASE pixel array into an 8-bit grayscale <see cref="Bitmap"/>
    /// using a 1st / 99.5th percentile linear stretch — the same math as the
    /// reference Python implementation at
    /// <c>services/image-splitter/inspector/rendering.py::_normalize_percentile</c>.
    ///
    /// The percentile computation is histogram-based (O(n + 65536)), which is
    /// the only sane approach for 5-10 million-pixel scans: sorting would be
    /// 100-200× slower and allocate huge scratch buffers.
    ///
    /// Visual parity with the vendor DLL's output is NOT a goal. The vendor
    /// uses a proprietary LUT with gamma and dual-energy material-discrimination
    /// shading that we can't reproduce without the DLL. What we CAN do is
    /// produce a clean, readable X-ray at the same dimensions — which is enough
    /// for analysts to identify containers and see cargo.
    /// </summary>
    public static class AsePercentileRenderer
    {
        /// <summary>
        /// Build an 8-bit grayscale Bitmap from decoded ASE pixels. Caller
        /// owns the returned Bitmap and is responsible for disposing it
        /// (or handing it to <see cref="Image.Save"/>, which doesn't).
        ///
        /// Percentile defaults (1.0 / 99.5) match the Python reference and
        /// produce a visually comparable image without any per-scan tuning.
        /// </summary>
        public static Bitmap BuildBitmap(
            AseFormatDecoder.DecodedAse decoded,
            double loPercentile = 0.01,
            double hiPercentile = 0.995)
        {
            int width = decoded.Width;
            int height = decoded.Height;
            ushort[] pixels = decoded.Pixels;

            (int lo, int hi) = ComputePercentileBounds(pixels, loPercentile, hiPercentile);
            double range = Math.Max(1, hi - lo);

            // Allocate an 8-bit indexed bitmap with a 256-entry grayscale palette.
            // This is ~4× cheaper than RGB and matches the "grayscale PNG -> JPEG"
            // shape the existing DLL path produces.
            var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            var palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            bmp.Palette = palette;

            var rect = new Rectangle(0, 0, width, height);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = 0; y < height; y++)
                {
                    int srcBase = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int v = pixels[srcBase + x];
                        // Linear map [lo, hi] -> [0, 255]. Values outside the
                        // window clamp to the endpoints.
                        if (v <= lo)
                        {
                            row[x] = 0;
                        }
                        else if (v >= hi)
                        {
                            row[x] = 255;
                        }
                        else
                        {
                            double t = (v - lo) / range;
                            row[x] = (byte)(t * 255.0);
                        }
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * stride, stride);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Rotate 90° counter-clockwise to match the vendor DLL's output.
            //
            // The raw ASE format stores pixels in portrait orientation (544 columns
            // × N scanlines) because the scanner captures a column at a time as the
            // truck moves through. The vendor DLL always rotated 90° CCW internally
            // so clients received landscape images (N × 544). The entire frontend
            // — canvas tools, pan/zoom, ROI drawing, ruler, fullscreen viewer — was
            // built around those landscape dimensions.
            //
            // When we switched to the C# decoder without this rotation, images
            // appeared portrait and the canvas tools' coordinate math broke.
            //
            // RotateFlipType.Rotate270FlipNone = 270° CW = 90° CCW. This matches:
            //   - The vendor DLL's behavior (FINDINGS.md: "rotates 90° CCW for display")
            //   - The Python reference decoder's rotate_to_dll=True default
            //     (services/image-splitter/reverse_engineering/05_prototype_decoder.py)
            bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);

            return bmp;
        }

        /// <summary>
        /// Histogram-based percentile computation. 16-bit inputs have only
        /// 65,536 possible values so a single-pass histogram + two cumulative
        /// walks is the cheapest correct answer. Returns integer bounds because
        /// percentiles of integer inputs are integers — no float rounding needed.
        /// </summary>
        private static (int lo, int hi) ComputePercentileBounds(ushort[] pixels, double loPercentile, double hiPercentile)
        {
            // Build histogram in one pass.
            var hist = new int[65536];
            for (int i = 0; i < pixels.Length; i++)
            {
                hist[pixels[i]]++;
            }

            long total = pixels.Length;
            if (total == 0)
            {
                return (0, 1);
            }

            long loTarget = (long)(total * loPercentile);
            long hiTarget = (long)(total * hiPercentile);

            int lo = 0;
            long cum = 0;
            for (int v = 0; v < 65536; v++)
            {
                cum += hist[v];
                if (cum >= loTarget)
                {
                    lo = v;
                    break;
                }
            }

            int hi = 65535;
            cum = 0;
            for (int v = 0; v < 65536; v++)
            {
                cum += hist[v];
                if (cum >= hiTarget)
                {
                    hi = v;
                    break;
                }
            }

            // Degenerate case: all pixels identical, or high percentile is at or
            // below low percentile. Widen by one so the linear map doesn't divide
            // by zero downstream.
            if (hi <= lo)
            {
                hi = Math.Min(65535, lo + 1);
            }

            return (lo, hi);
        }

        /// <summary>
        /// One-pass pixel statistics (min/max/mean/stddev) on a <see cref="ushort"/>
        /// buffer. Used by shadow-mode comparison to quantify how close the
        /// fallback output is to the DLL output without saving the image.
        /// </summary>
        public static PixelStats ComputePixelStats(ushort[] pixels)
        {
            if (pixels == null || pixels.Length == 0)
            {
                return new PixelStats { Count = 0, Min = 0, Max = 0, Mean = 0, StdDev = 0 };
            }

            int min = ushort.MaxValue;
            int max = 0;
            double sum = 0;
            double sumSq = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int v = pixels[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
                sumSq += (double)v * v;
            }

            double mean = sum / pixels.Length;
            double variance = (sumSq / pixels.Length) - (mean * mean);
            if (variance < 0) variance = 0;
            return new PixelStats
            {
                Count = pixels.Length,
                Min = min,
                Max = max,
                Mean = mean,
                StdDev = Math.Sqrt(variance)
            };
        }

        public sealed class PixelStats
        {
            public long Count { get; set; }
            public int Min { get; set; }
            public int Max { get; set; }
            public double Mean { get; set; }
            public double StdDev { get; set; }

            public override string ToString()
                => $"count={Count} min={Min} max={Max} mean={Mean:F1} std={StdDev:F1}";
        }
    }
}
