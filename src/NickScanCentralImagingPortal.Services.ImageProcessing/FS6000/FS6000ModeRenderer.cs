using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Render a decoded FS6000 scan in any of the vendor-standard operator
    /// <b>image modes</b>. Each mode is a fixed recipe built on top of the
    /// existing <see cref="FS6000FormatDecoder"/> / <see cref="FS6000Compositor"/>
    /// pipeline — no new raw data, just different presentations of the same
    /// three co-registered layers (HighEnergy, LowEnergy, Material).
    ///
    /// The set of modes here mirrors what Smiths HI-SCAN, Rapiscan, Nuctech,
    /// and Heimann consoles expose to inspectors:
    ///
    /// <list type="bullet">
    ///   <item><description><see cref="Fs6000RenderMode.Composite"/> — canonical
    ///   colorised view (dense=dark backdrop, organic=orange / metal=blue overlay).</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.BlackWhite"/> — pure
    ///   greyscale of HighEnergy, inverted (vendor convention: dense=dark).</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.Inverse"/> — skip the
    ///   invert step; dense=bright. Negative / "raw sensor" look.</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.HighPen"/> — HighEnergy
    ///   greyscale with aggressive gamma to brighten dark (dense) regions;
    ///   equivalent to Rapiscan "High Penetration".</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.LowPen"/> — opposite
    ///   gamma curve, accents shallow attenuation.</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.OrganicStrip"/> —
    ///   composite with the organic (low-Z) LUT band neutralised: metal pops
    ///   through cargo.</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.MetalStrip"/> —
    ///   composite with the metal (high-Z) LUT band neutralised: organic
    ///   contents visible inside metal containers.</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.Edge"/> — composite
    ///   followed by an unsharp-mask pass to accent material boundaries.</description></item>
    ///   <item><description><see cref="Fs6000RenderMode.Diff"/> — dual-energy
    ///   difference (HE − LE) remapped to 0–255; highlights material boundaries
    ///   in a single greyscale channel. Port of Python's
    ///   <c>dual_energy_difference</c>.</description></item>
    /// </list>
    ///
    /// All modes accept optional <c>loPct</c> / <c>hiPct</c> / <c>gamma</c>
    /// parameters for the Variable Density slider — they feed through to the
    /// underlying <c>FS6000Compositor.NormalizeEnergyChannel</c> + per-mode
    /// gamma curve.
    /// </summary>
    public static class FS6000ModeRenderer
    {
        private const int JpegQuality = 88;
        private const float DefaultLoPct = 1.0f;
        private const float DefaultHiPct = 99.5f;

        // v2.10.0: LUT class-band boundaries mirror the ones baked into
        // FS6000Compositor.BuildDefaultMaterialLut. If that ever changes, these
        // need to follow. Central constants so we don't have magic numbers in
        // two places.
        private const int OrganicBandStart = 41;
        private const int OrganicBandEnd = 121;   // exclusive
        private const int MetalBandStart = 121;
        private const int MetalBandEnd = 256;     // exclusive

        /// <summary>
        /// Entry point: render the decoded scan in the requested mode, return
        /// JPEG bytes ready to stream to the browser.
        /// </summary>
        /// <param name="decoded">Output of <see cref="FS6000FormatDecoder.Decode"/>.</param>
        /// <param name="mode">Operator image mode.</param>
        /// <param name="loPct">Lower percentile for contrast-clip. Default 1.0.</param>
        /// <param name="hiPct">Upper percentile for contrast-clip. Default 99.5.</param>
        /// <param name="gamma">Extra gamma applied to the luminance channel; 1.0 = no change. Modes that have their own gamma (HighPen/LowPen) override this when the caller leaves it at default.</param>
        public static byte[] RenderJpeg(
            FS6000FormatDecoder.DecodedFs6000 decoded,
            Fs6000RenderMode mode,
            float loPct = DefaultLoPct,
            float hiPct = DefaultHiPct,
            float gamma = 1.0f)
        {
            return mode switch
            {
                Fs6000RenderMode.Composite    => RenderCompositeJpeg(decoded, FS6000Compositor.DefaultMaterialLut, loPct, hiPct, gamma, edge: false),
                Fs6000RenderMode.Edge         => RenderCompositeJpeg(decoded, FS6000Compositor.DefaultMaterialLut, loPct, hiPct, gamma, edge: true),
                Fs6000RenderMode.OrganicStrip => RenderCompositeJpeg(decoded, BuildStrippedLut(OrganicBandStart, OrganicBandEnd), loPct, hiPct, gamma, edge: false),
                Fs6000RenderMode.MetalStrip   => RenderCompositeJpeg(decoded, BuildStrippedLut(MetalBandStart,   MetalBandEnd),   loPct, hiPct, gamma, edge: false),
                Fs6000RenderMode.BlackWhite   => RenderGrayscaleJpeg(decoded, loPct, hiPct, gamma, invert: true),
                Fs6000RenderMode.Inverse      => RenderGrayscaleJpeg(decoded, loPct, hiPct, gamma, invert: false),
                Fs6000RenderMode.HighPen      => RenderGrayscaleJpeg(decoded, loPct, hiPct, gamma: gamma == 1.0f ? 1.8f : gamma, invert: true),
                Fs6000RenderMode.LowPen       => RenderGrayscaleJpeg(decoded, loPct, hiPct, gamma: gamma == 1.0f ? 0.5f : gamma, invert: true),
                Fs6000RenderMode.Diff         => RenderDualEnergyDiffJpeg(decoded),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), $"unknown render mode: {mode}"),
            };
        }

        /// <summary>
        /// Parse a string mode (e.g. <c>"composite"</c>, <c>"organic-strip"</c>) into
        /// the enum. Tolerant of casing, dashes vs underscores, vendor synonyms.
        /// Returns <c>null</c> when the input is unrecognised so the caller can
        /// choose a default (typically <see cref="Fs6000RenderMode.Composite"/>).
        /// </summary>
        public static Fs6000RenderMode? TryParseMode(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var k = input.Trim().ToLowerInvariant().Replace('_', '-');
            return k switch
            {
                "composite" or "color" or "default"                 => Fs6000RenderMode.Composite,
                "bw" or "blackwhite" or "black-white" or "grayscale" or "greyscale" => Fs6000RenderMode.BlackWhite,
                "inverse" or "inverse-video" or "negative"         => Fs6000RenderMode.Inverse,
                "high-pen" or "highpen" or "high-penetration"      => Fs6000RenderMode.HighPen,
                "low-pen" or "lowpen" or "low-penetration"         => Fs6000RenderMode.LowPen,
                "organic-strip" or "organic-stripping" or "strip-organic" => Fs6000RenderMode.OrganicStrip,
                "metal-strip" or "inorganic-strip" or "strip-metal" or "inorganic-stripping" => Fs6000RenderMode.MetalStrip,
                "edge" or "edge-enhance" or "unsharp"              => Fs6000RenderMode.Edge,
                "diff" or "energy-diff" or "dual-energy-diff"      => Fs6000RenderMode.Diff,
                _ => null,
            };
        }

        // ── Recipes ──────────────────────────────────────────────────────

        private static byte[] RenderCompositeJpeg(
            FS6000FormatDecoder.DecodedFs6000 d,
            byte[,] lut,
            float loPct, float hiPct, float gamma,
            bool edge)
        {
            byte[] rgb = FS6000Compositor.CompositeRgb(
                low: d.Low, high: d.High, material: d.Material,
                width: d.Width, height: d.Height,
                luminanceSource: FS6000Compositor.LuminanceSource.High,
                materialStrength: 0.65f,
                customLut: lut,
                loPct: loPct, hiPct: hiPct, gamma: gamma);

            using var img = Image.LoadPixelData<Rgb24>(rgb, d.Width, d.Height);
            if (edge)
            {
                // ImageSharp's built-in unsharp mask. Values tuned for X-ray —
                // strong enough to pop material boundaries without ringing on
                // the white background.
                img.Mutate(x => x.GaussianSharpen(1.5f));
            }
            return EncodeJpeg(img);
        }

        private static byte[] RenderGrayscaleJpeg(
            FS6000FormatDecoder.DecodedFs6000 d,
            float loPct, float hiPct, float gamma,
            bool invert)
        {
            byte[] lum = FS6000Compositor.NormalizeEnergyChannel(d.High, loPct, hiPct);
            if (invert)
            {
                for (int i = 0; i < lum.Length; i++) lum[i] = (byte)(255 - lum[i]);
            }
            if (Math.Abs(gamma - 1.0f) > 0.001f)
            {
                float invGamma = 1.0f / gamma;
                for (int i = 0; i < lum.Length; i++)
                {
                    float v = lum[i] * (1.0f / 255.0f);
                    int g = (int)(MathF.Pow(v, invGamma) * 255.0f);
                    lum[i] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
                }
            }

            using var img = Image.LoadPixelData<L8>(lum, d.Width, d.Height);
            return EncodeJpeg(img);
        }

        /// <summary>
        /// Port of Python's <c>dual_energy_difference</c>: (HE − LE) mapped
        /// to 0–255 via min/max rescaling, rendered as 8-bit greyscale.
        /// Dense organic materials attenuate both energies similarly (mid-grey),
        /// metals attenuate low more (bright), plastics the opposite (dark).
        /// </summary>
        private static byte[] RenderDualEnergyDiffJpeg(FS6000FormatDecoder.DecodedFs6000 d)
        {
            int n = d.Width * d.Height;
            var diff = new int[n];
            int dMin = int.MaxValue, dMax = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                int v = d.High[i] - d.Low[i];
                diff[i] = v;
                if (v < dMin) dMin = v;
                if (v > dMax) dMax = v;
            }

            var lum = new byte[n];
            if (dMax <= dMin)
            {
                // Flat image — fill with mid-grey (matches Python fallback).
                for (int i = 0; i < n; i++) lum[i] = 128;
            }
            else
            {
                float range = dMax - dMin;
                float scale = 255.0f / range;
                for (int i = 0; i < n; i++)
                {
                    int g = (int)((diff[i] - dMin) * scale);
                    lum[i] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
                }
            }

            using var img = Image.LoadPixelData<L8>(lum, d.Width, d.Height);
            return EncodeJpeg(img);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Produce a copy of the default material LUT with the specified class
        /// range neutralised (forced to near-white). Pixels whose Material
        /// class falls in the stripped range will render with no color tint,
        /// effectively letting them fade into the grayscale backdrop.
        /// </summary>
        private static byte[,] BuildStrippedLut(int stripStart, int stripEndExclusive)
        {
            var src = FS6000Compositor.DefaultMaterialLut;
            var lut = new byte[256, 3];
            for (int i = 0; i < 256; i++)
            {
                if (i >= stripStart && i < stripEndExclusive)
                {
                    // Neutral / near-white — when the compositor blends color
                    // with luminance, the pixel reads as pure greyscale.
                    lut[i, 0] = 255; lut[i, 1] = 255; lut[i, 2] = 255;
                }
                else
                {
                    lut[i, 0] = src[i, 0];
                    lut[i, 1] = src[i, 1];
                    lut[i, 2] = src[i, 2];
                }
            }
            return lut;
        }

        private static byte[] EncodeJpeg<TPixel>(Image<TPixel> img) where TPixel : unmanaged, IPixel<TPixel>
        {
            using var ms = new MemoryStream(capacity: img.Width * img.Height / 4);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Operator-facing image modes exposed by the single-canvas viewer in
    /// v2.10.0. See <see cref="FS6000ModeRenderer"/> for recipes.
    /// </summary>
    public enum Fs6000RenderMode
    {
        Composite = 0,
        BlackWhite = 1,
        Inverse = 2,
        HighPen = 3,
        LowPen = 4,
        OrganicStrip = 5,
        MetalStrip = 6,
        Edge = 7,
        Diff = 8,
    }
}
