using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — scanner-agnostic mode renderer. Every one of the 9 operator
    /// modes is a pure function of <see cref="DecodedScan"/> + tonal
    /// parameters. Dispatches on structure (channels + material presence),
    /// not on scanner identity.
    ///
    /// Preserves every algorithmic decision from the legacy
    /// <c>FS6000ModeRenderer</c>: the 1.8 gamma in HighPen, the 0.5 gamma in
    /// LowPen, the GaussianSharpen 1.5 in Edge, the organic/metal band strip
    /// math, the invert-for-vendor-convention in BW. The 240 M-pixel vendor
    /// LUT composite runs unchanged through
    /// <see cref="FS6000VendorLutCompositor.RenderJpeg(ushort[], ushort[], byte[], int, int)"/>.
    ///
    /// When a mode's structural requirement isn't met (e.g. Composite on a
    /// single-view scan), <see cref="Render"/> returns null — the pipeline
    /// maps that to HTTP 422 upstream.
    /// </summary>
    public static class ScanRenderer
    {
        private const int JpegQuality = 88;

        public static byte[]? Render(DecodedScan scan, RenderMode mode,
                                     float loPct = 1.0f, float hiPct = 99.5f, float gamma = 1.0f)
        {
            if (!RenderModeRequirements.IsAvailable(mode, scan)) return null;

            return mode switch
            {
                RenderMode.Composite       => RenderCompositeVendorLut(scan),
                RenderMode.CompositeLegacy => RenderCompositeLegacy(scan, FS6000Compositor.DefaultMaterialLut, loPct, hiPct, gamma, edge: false),
                RenderMode.OrganicStrip    => RenderCompositeLegacy(scan, BuildStrippedLut(OrganicBandStart, OrganicBandEnd), loPct, hiPct, gamma, edge: false),
                RenderMode.MetalStrip      => RenderCompositeLegacy(scan, BuildStrippedLut(MetalBandStart,   MetalBandEnd),   loPct, hiPct, gamma, edge: false),
                RenderMode.Edge            => RenderEdgePreservingLegacyBehaviour(scan, loPct, hiPct, gamma),
                RenderMode.BlackWhite      => RenderGrayscale(scan, loPct, hiPct, gamma, invert: true),
                RenderMode.Inverse         => RenderGrayscale(scan, loPct, hiPct, gamma, invert: false),
                RenderMode.HighPen         => RenderGrayscale(scan, loPct, hiPct, gamma == 1.0f ? 1.8f : gamma, invert: true),
                RenderMode.LowPen          => RenderGrayscale(scan, loPct, hiPct, gamma == 1.0f ? 0.5f : gamma, invert: true),
                RenderMode.Diff            => RenderDualEnergyDiff(scan),
                _ => null,
            };
        }

        // ── Recipes ──────────────────────────────────────────────────────

        private static byte[] RenderCompositeVendorLut(DecodedScan scan)
        {
            var he = scan.ChannelByKind(EnergyKind.High)!;
            var le = scan.ChannelByKind(EnergyKind.Low)!;
            var mat = scan.Material!;
            return FS6000VendorLutCompositor.RenderJpeg(he.Pixels, le.Pixels, mat.Classes, scan.Width, scan.Height);
        }

        /// <summary>
        /// Edge mode — preserves v2.10.5 byte-for-byte behaviour. For
        /// dual-energy scans this is legacy composite + unsharp; for
        /// single-view scans the old code synthesised a zero-Low / zero-
        /// Material dual-energy bundle so it could run the same legacy
        /// path. We do the same here so output is identical. Not the most
        /// elegant, but it preserves exact parity without surprising the
        /// operators who've seen this particular Edge look for years.
        /// </summary>
        private static byte[] RenderEdgePreservingLegacyBehaviour(DecodedScan scan, float loPct, float hiPct, float gamma)
        {
            if (scan.IsDualEnergy && scan.Material != null)
            {
                return RenderCompositeLegacy(scan, FS6000Compositor.DefaultMaterialLut, loPct, hiPct, gamma, edge: true);
            }

            // Single-channel (single-view ASE): synthesise zero Low + zero
            // Material so we can call the same legacy composite-edge path.
            var primary = scan.ChannelByKind(EnergyKind.Single)
                       ?? scan.ChannelByKind(EnergyKind.High)
                       ?? scan.Channels[0];
            int n = primary.Pixels.Length;
            byte[] rgb = FS6000Compositor.CompositeRgb(
                low: new ushort[n], high: primary.Pixels, material: new byte[n],
                width: scan.Width, height: scan.Height,
                luminanceSource: FS6000Compositor.LuminanceSource.High,
                materialStrength: 0.65f,
                customLut: FS6000Compositor.DefaultMaterialLut,
                loPct: loPct, hiPct: hiPct, gamma: gamma);

            using var img = Image.LoadPixelData<Rgb24>(rgb, scan.Width, scan.Height);
            img.Mutate(x => x.GaussianSharpen(1.5f));
            return EncodeJpeg(img);
        }

        private static byte[] RenderCompositeLegacy(DecodedScan scan, byte[,] lut,
                                                    float loPct, float hiPct, float gamma, bool edge)
        {
            var he = scan.ChannelByKind(EnergyKind.High)!;
            var le = scan.ChannelByKind(EnergyKind.Low)!;
            var mat = scan.Material!;

            byte[] rgb = FS6000Compositor.CompositeRgb(
                low: le.Pixels, high: he.Pixels, material: mat.Classes,
                width: scan.Width, height: scan.Height,
                luminanceSource: FS6000Compositor.LuminanceSource.High,
                materialStrength: 0.65f,
                customLut: lut,
                loPct: loPct, hiPct: hiPct, gamma: gamma);

            using var img = Image.LoadPixelData<Rgb24>(rgb, scan.Width, scan.Height);
            if (edge) img.Mutate(x => x.GaussianSharpen(1.5f));
            return EncodeJpeg(img);
        }

        private static byte[] RenderGrayscale(DecodedScan scan, float loPct, float hiPct, float gamma,
                                              bool invert, bool sharpen = false)
        {
            // Prefer High. Fall back to Single for single-view scanners, or
            // the first available channel for anything else.
            var primary = scan.ChannelByKind(EnergyKind.High)
                       ?? scan.ChannelByKind(EnergyKind.Single)
                       ?? scan.Channels[0];

            byte[] lum = FS6000Compositor.NormalizeEnergyChannel(primary.Pixels, loPct, hiPct);
            if (invert)
            {
                for (int i = 0; i < lum.Length; i++) lum[i] = (byte)(255 - lum[i]);
            }
            ApplyGamma(lum, gamma);
            using var img = Image.LoadPixelData<L8>(lum, scan.Width, scan.Height);
            if (sharpen) img.Mutate(x => x.GaussianSharpen(1.5f));
            return EncodeJpeg(img);
        }

        /// <summary>
        /// Dual-energy difference: (HE − LE) remapped to 0..255. Exact port
        /// of the Python <c>dual_energy_difference</c> logic.
        /// </summary>
        private static byte[] RenderDualEnergyDiff(DecodedScan scan)
        {
            var he = scan.ChannelByKind(EnergyKind.High)!;
            var le = scan.ChannelByKind(EnergyKind.Low)!;

            int n = scan.Width * scan.Height;
            var diff = new int[n];
            int dMin = int.MaxValue, dMax = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                int v = he.Pixels[i] - le.Pixels[i];
                diff[i] = v;
                if (v < dMin) dMin = v;
                if (v > dMax) dMax = v;
            }

            var lum = new byte[n];
            if (dMax <= dMin)
            {
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

            using var img = Image.LoadPixelData<L8>(lum, scan.Width, scan.Height);
            return EncodeJpeg(img);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static void ApplyGamma(byte[] lum, float gamma)
        {
            if (Math.Abs(gamma - 1.0f) < 0.001f) return;
            float invGamma = 1.0f / gamma;
            for (int i = 0; i < lum.Length; i++)
            {
                float v = lum[i] * (1.0f / 255.0f);
                int g = (int)(MathF.Pow(v, invGamma) * 255.0f);
                lum[i] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
            }
        }

        private const int OrganicBandStart = 41;
        private const int OrganicBandEnd   = 121;
        private const int MetalBandStart   = 121;
        private const int MetalBandEnd     = 256;

        private static byte[,] BuildStrippedLut(int stripStart, int stripEndExclusive)
        {
            var src = FS6000Compositor.DefaultMaterialLut;
            var lut = new byte[256, 3];
            for (int i = 0; i < 256; i++)
            {
                if (i >= stripStart && i < stripEndExclusive)
                {
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
}
