using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Pure-C# dual-energy compositor for FS6000 scans. Produces the
    /// vendor-style colorized X-ray view by combining a high-energy luminance
    /// channel (percentile-clipped + inverted) with a material-class LUT.
    ///
    /// Port of <c>services/image-splitter/inspector/composite.py</c> —
    /// specifically <c>composite_fs6000_color</c> and
    /// <c>build_default_material_lut</c>. The algorithm, LUT values, gamma
    /// curve, brightness boost, and percentile bounds are kept bit-identical
    /// so native-C# output matches the Python inspector to within ±1 uint8
    /// per channel (the only nondeterminism is the order of floating-point
    /// operations, which can round differently at the last decimal place).
    ///
    /// Color palette (indexed by material class byte):
    ///   0            pass-through white (background / no classification)
    ///   1..40        near-neutral slight-cool tint (low-attenuation noise band)
    ///   41..120      warm orange/brown (mid-density organic)
    ///   121..255     vibrant blue (dense / metallic, ramping to saturated blue)
    ///
    /// The output is RGB 24-bit (not BGR — ImageSharp's PngEncoder wants RGB
    /// pixel order natively). This differs from the Python implementation,
    /// which builds a BGR array and passes it through <c>cv2.imencode</c>
    /// (OpenCV internally converts BGR→RGB when writing PNG). Net result on
    /// disk: identical RGB pixel values.
    /// </summary>
    public static class FS6000Compositor
    {
        /// <summary>Which energy channel to use for base luminance.</summary>
        public enum LuminanceSource
        {
            High,
            Low,
            Avg,
        }

        /// <summary>
        /// 256-entry material LUT in RGB order. Exposed for testing and so
        /// downstream callers can swap in a calibrated palette if needed.
        /// </summary>
        public static readonly byte[,] DefaultMaterialLut = BuildDefaultMaterialLut();

        private static byte[,] BuildDefaultMaterialLut()
        {
            var lut = new byte[256, 3];

            // 0 = background / no classification → pass-through white
            lut[0, 0] = 255; lut[0, 1] = 255; lut[0, 2] = 255;

            // 1..40 = low-attenuation noise band → near-neutral slight cool
            // Python: lut[i] = (220+30t, 220+20t, 220+20t)  [BGR]
            //    → RGB:       (220+20t, 220+20t, 220+30t)
            for (int i = 1; i < 41; i++)
            {
                float t = i / 40f;
                lut[i, 0] = (byte)(int)(220 + 20 * t);   // R
                lut[i, 1] = (byte)(int)(220 + 20 * t);   // G
                lut[i, 2] = (byte)(int)(220 + 30 * t);   // B
            }

            // 41..120 = mid-density organic → warm orange/brown
            // Python: lut[i] = (80-60t, 140+30t, 220+30t)  [BGR]
            //    → RGB:       (220+30t, 140+30t, 80-60t)
            for (int i = 41; i < 121; i++)
            {
                float t = (i - 41) / 79f;
                lut[i, 0] = (byte)(int)(220 + 30 * t);   // R
                lut[i, 1] = (byte)(int)(140 + 30 * t);   // G
                lut[i, 2] = (byte)(int)(80 - 60 * t);    // B
            }

            // 121..255 = dense / metallic → vibrant blue
            // Python: lut[i] = (255, 80-40t, 30*(1-t))  [BGR]
            //    → RGB:       (30*(1-t), 80-40t, 255)
            for (int i = 121; i < 256; i++)
            {
                float t = (i - 121) / 134f;
                lut[i, 0] = (byte)(int)(30 * (1f - t));  // R
                lut[i, 1] = (byte)(int)(80 - 40 * t);    // G
                lut[i, 2] = 255;                         // B
            }

            return lut;
        }

        /// <summary>
        /// Produce an RGB24 composite (row-major, length <c>width*height*3</c>)
        /// from a decoded FS6000 scan. This is the canonical entry point — the
        /// PNG-bytes wrapper below simply encodes the return value.
        /// </summary>
        /// <param name="decoded">Output from <see cref="FS6000FormatDecoder.Decode"/>.</param>
        /// <param name="luminanceSource">Which energy channel drives the grayscale base. Default <see cref="LuminanceSource.High"/> matches Python.</param>
        /// <param name="materialStrength">Tint strength 0..1 (0 = pure grayscale, 1 = pure LUT color). Default 0.65 matches Python.</param>
        public static byte[] CompositeRgb(
            FS6000FormatDecoder.DecodedFs6000 decoded,
            LuminanceSource luminanceSource = LuminanceSource.High,
            float materialStrength = 0.65f)
        {
            return CompositeRgb(
                low: decoded.Low,
                high: decoded.High,
                material: decoded.Material,
                width: decoded.Width,
                height: decoded.Height,
                luminanceSource: luminanceSource,
                materialStrength: materialStrength);
        }

        /// <summary>
        /// Lower-level entry point for callers that already have loose pixel
        /// arrays (e.g. unit tests). All three channels must have length
        /// <c>width*height</c> and use the same coordinate orientation.
        /// </summary>
        public static byte[] CompositeRgb(
            ReadOnlySpan<ushort> low,
            ReadOnlySpan<ushort> high,
            ReadOnlySpan<byte> material,
            int width,
            int height,
            LuminanceSource luminanceSource = LuminanceSource.High,
            float materialStrength = 0.65f)
        {
            int n = width * height;
            if (low.Length != n)
                throw new ArgumentException($"low channel length {low.Length} != width*height {n}");
            if (high.Length != n)
                throw new ArgumentException($"high channel length {high.Length} != width*height {n}");
            if (material.Length != n)
                throw new ArgumentException($"material channel length {material.Length} != width*height {n}");
            if (materialStrength < 0f || materialStrength > 1f)
                throw new ArgumentOutOfRangeException(nameof(materialStrength),
                    $"material_strength must be in [0,1]; got {materialStrength}");

            // ── 1. Build 8-bit luminance from the chosen energy channel ──
            byte[] lum8 = luminanceSource switch
            {
                LuminanceSource.High => NormalizeEnergyToLuminance(high, loPct: 1.0f, hiPct: 99.5f),
                LuminanceSource.Low => NormalizeEnergyToLuminance(low, loPct: 1.0f, hiPct: 99.5f),
                LuminanceSource.Avg => NormalizeEnergyToLuminanceAverage(high, low, loPct: 1.0f, hiPct: 99.5f),
                _ => throw new ArgumentOutOfRangeException(nameof(luminanceSource)),
            };

            // ── 2. Invert so dense=dark, air=bright (vendor convention) ──
            for (int i = 0; i < n; i++)
            {
                lum8[i] = (byte)(255 - lum8[i]);
            }

            // ── 3. Blend with material LUT + brightness boost + gamma ──
            //
            //     blended = gray * (1-s) + color * gray * s
            //     out     = clip(blended * 1.15, 0, 1) ^ 0.9 * 255  (truncate to uint8)
            //
            // Per-pixel: one 3-tuple LUT lookup + 3×(mul, mul, add, boost, pow, mul, cast).
            // ~3M pixels × 3 channels = ~9M pow calls; this is the dominant cost
            // (~20–40ms single-threaded on a modern server CPU).
            float s = materialStrength;
            float oneMinusS = 1f - s;
            const float Boost = 1.15f;
            const float Gamma = 0.9f;

            var rgb = new byte[n * 3];
            var lut = DefaultMaterialLut;

            for (int i = 0; i < n; i++)
            {
                byte mClass = material[i];
                float cr = lut[mClass, 0] * (1f / 255f);
                float cg = lut[mClass, 1] * (1f / 255f);
                float cb = lut[mClass, 2] * (1f / 255f);
                float lum = lum8[i] * (1f / 255f);

                float br = lum * oneMinusS + cr * lum * s;
                float bg = lum * oneMinusS + cg * lum * s;
                float bb = lum * oneMinusS + cb * lum * s;

                // Clip to [0,1] then gamma (matches np.clip(blended*1.15, 0, 1) ** 0.9)
                br = MathF.Pow(MathF.Min(MathF.Max(br * Boost, 0f), 1f), Gamma);
                bg = MathF.Pow(MathF.Min(MathF.Max(bg * Boost, 0f), 1f), Gamma);
                bb = MathF.Pow(MathF.Min(MathF.Max(bb * Boost, 0f), 1f), Gamma);

                // Truncate (matches numpy .astype(uint8) after clip(..., 0, 255))
                int ri = (int)(br * 255f);
                int gi = (int)(bg * 255f);
                int bi = (int)(bb * 255f);

                int dst = i * 3;
                rgb[dst + 0] = (byte)(ri < 0 ? 0 : (ri > 255 ? 255 : ri));
                rgb[dst + 1] = (byte)(gi < 0 ? 0 : (gi > 255 ? 255 : gi));
                rgb[dst + 2] = (byte)(bi < 0 ? 0 : (bi > 255 ? 255 : bi));
            }

            return rgb;
        }

        /// <summary>
        /// Wrap <see cref="CompositeRgb(FS6000FormatDecoder.DecodedFs6000, LuminanceSource, float)"/>
        /// in a PNG encoder. Returns the complete PNG file bytes ready to
        /// stream back to the browser.
        /// </summary>
        public static byte[] CompositeRgbPng(
            FS6000FormatDecoder.DecodedFs6000 decoded,
            LuminanceSource luminanceSource = LuminanceSource.High,
            float materialStrength = 0.65f)
        {
            byte[] rgb = CompositeRgb(decoded, luminanceSource, materialStrength);
            return EncodeRgbPng(rgb, decoded.Width, decoded.Height);
        }

        /// <summary>Encode a flat RGB24 buffer to PNG bytes via ImageSharp.</summary>
        public static byte[] EncodeRgbPng(byte[] rgb, int width, int height)
        {
            if (rgb.Length != width * height * 3)
                throw new ArgumentException(
                    $"rgb buffer length {rgb.Length} != width*height*3 ({width * height * 3})");

            using var img = Image.LoadPixelData<Rgb24>(rgb, width, height);
            using var ms = new MemoryStream(capacity: width * height);
            var encoder = new PngEncoder
            {
                // Match OpenCV's default compression level (1 — fast, largish).
                // Visually lossless regardless of level; speed matters more in
                // the hot request path than on-disk size.
                CompressionLevel = PngCompressionLevel.BestSpeed,
                ColorType = PngColorType.Rgb,
                BitDepth = PngBitDepth.Bit8,
            };
            img.SaveAsPng(ms, encoder);
            return ms.ToArray();
        }

        // ── Percentile-based luminance normalization ─────────────────────

        /// <summary>
        /// Build an 8-bit luminance buffer from a 16-bit energy channel using
        /// percentile clipping (matches Python's <c>_normalize_energy_to_luminance</c>).
        /// </summary>
        private static byte[] NormalizeEnergyToLuminance(ReadOnlySpan<ushort> energy, float loPct, float hiPct)
        {
            int n = energy.Length;
            var hist = new int[65536];
            for (int i = 0; i < n; i++)
            {
                hist[energy[i]]++;
            }

            float lo = PercentileFromHistogram(hist, n, loPct);
            float hi = PercentileFromHistogram(hist, n, hiPct);

            var output = new byte[n];
            if (hi <= lo)
            {
                // Flat image — return zeros (matches Python fallback).
                return output;
            }

            float range = hi - lo;
            float scale = 255f / range;
            for (int i = 0; i < n; i++)
            {
                float v = energy[i];
                if (v < lo) v = lo;
                else if (v > hi) v = hi;
                int pix = (int)((v - lo) * scale);   // truncate (matches .astype(uint8))
                output[i] = (byte)(pix < 0 ? 0 : (pix > 255 ? 255 : pix));
            }
            return output;
        }

        /// <summary>
        /// Luminance from the per-pixel average of two 16-bit channels.
        /// Matches Python's "avg" path (uint16 average, then normalize).
        /// </summary>
        private static byte[] NormalizeEnergyToLuminanceAverage(
            ReadOnlySpan<ushort> high,
            ReadOnlySpan<ushort> low,
            float loPct,
            float hiPct)
        {
            int n = high.Length;
            // Python: ((high + low) * 0.5).astype(uint16) — truncated average,
            // using float32 temporary to avoid uint16 overflow.
            var avg = new ushort[n];
            for (int i = 0; i < n; i++)
            {
                avg[i] = (ushort)((high[i] + low[i]) * 0.5f);
            }
            return NormalizeEnergyToLuminance(avg, loPct, hiPct);
        }

        /// <summary>
        /// NumPy-compatible percentile from a 65536-bin histogram, using
        /// linear interpolation (NumPy's default). The histogram buckets
        /// are uint16 values so the interpolation gap is ≤1 gray level —
        /// after normalization to uint8 this produces pixel-identical
        /// output to <c>np.percentile</c> in ~99.9% of cases.
        /// </summary>
        private static float PercentileFromHistogram(int[] hist, int n, float percentile)
        {
            if (n <= 0) return 0f;
            if (n == 1)
            {
                for (int v = 0; v < hist.Length; v++)
                    if (hist[v] > 0) return v;
                return 0f;
            }

            double virtualIndex = (n - 1) * (percentile / 100.0);
            int lowerRank = (int)Math.Floor(virtualIndex);
            int upperRank = Math.Min(lowerRank + 1, n - 1);
            double frac = virtualIndex - lowerRank;

            // Walk the histogram once, picking off both ranks.
            int accum = 0;
            int valLower = 0, valUpper = 0;
            bool foundLower = false, foundUpper = false;
            for (int v = 0; v < hist.Length; v++)
            {
                int next = accum + hist[v];
                if (!foundLower && lowerRank < next) { valLower = v; foundLower = true; }
                if (!foundUpper && upperRank < next) { valUpper = v; foundUpper = true; break; }
                accum = next;
            }
            if (!foundLower) valLower = hist.Length - 1;
            if (!foundUpper) valUpper = hist.Length - 1;

            return (float)(valLower + frac * (valUpper - valLower));
        }
    }
}
