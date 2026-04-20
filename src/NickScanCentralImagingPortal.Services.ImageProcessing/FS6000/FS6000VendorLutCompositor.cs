using System;
using System.IO;
using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// v2.10.1 — empirical vendor-faithful FS6000 composite renderer.
    ///
    /// Replaces the original <see cref="FS6000Compositor"/> (which was a
    /// port of a hand-coded Python recipe that didn't match what the
    /// vendor's scanner actually produces). This class uses a <b>3D lookup
    /// table fitted from production data</b>: 64 FS6000 scans × ~3.7 M
    /// pixels each = 240 M training pairs of <c>(Material class, HE bucket,
    /// LE bucket) → (R, G, B)</c> extracted from raw channels paired with
    /// each scan's own vendor-rendered Main JPEG.
    ///
    /// Why the LUT is 3D:
    /// <list type="bullet">
    ///   <item><description>The vendor's color is a function of all three
    ///   inputs, not just the Material class. A phase-1 diagnostic showed
    ///   47% of <c>(class, HE)</c> cells produce materially different RGB
    ///   when LE varies (max |dRGB| = 207). The pre-computed Material
    ///   class alone is not sufficient.</description></item>
    ///   <item><description>Physics interpretation: the vendor appears to
    ///   be deriving Z-effective from the LE/HE ratio internally rather
    ///   than trusting the scanner's material field directly. The 3D
    ///   LUT captures that dependency without needing to know the exact
    ///   formula.</description></item>
    /// </list>
    ///
    /// LUT dimensions:
    /// <code>
    ///   256 material classes × 32 HE buckets × 32 LE buckets × 3 RGB bytes
    ///   = 768 KB, embedded in this assembly as FS6000/vendor_lut_v1.bin
    /// </code>
    ///
    /// Bucketing: HE bucket = (HE_u16 * 32) &gt;&gt; 16, i.e. the top 5 bits
    /// of the 16-bit value. Same for LE. This matches the Python training
    /// script (tools/vendor-lut-research/) bit-for-bit.
    ///
    /// Sparse cells: the training pass left ~95% of cells with zero samples
    /// (most <c>(class, HE, LE)</c> combinations don't occur in nature).
    /// Those cells were filled by nearest-neighbour in HE×LE space within
    /// the same class before export, so every lookup returns a defined
    /// value. Classes with &lt; 100 total training samples default to grey.
    ///
    /// Validation: across 16 held-out scans the per-pixel reconstruction
    /// error against the vendor JPEG was mean 3.91 RGB units / channel
    /// (max per-scan 5.56). Visually indistinguishable from vendor output.
    /// See <c>tools/vendor-lut-research/03_validate.py</c>.
    /// </summary>
    public static class FS6000VendorLutCompositor
    {
        public const int NumClasses = 256;
        public const int HeBuckets = 32;
        public const int LeBuckets = 32;
        public const int RgbChannels = 3;

        // Flat uint8 buffer of shape [class, he_bucket, le_bucket, rgb] in C-order.
        // Using a flat byte[] + manual indexing is ~20% faster per-pixel than a
        // 4-D array thanks to avoiding the extra bounds checks.
        private static readonly byte[] _lut = LoadLutFromEmbeddedResource();
        private static readonly int StrideClass = HeBuckets * LeBuckets * RgbChannels;
        private static readonly int StrideHe = LeBuckets * RgbChannels;
        private static readonly int StrideLe = RgbChannels;

        private const int JpegQuality = 88;
        private const string ResourceName =
            "NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.vendor_lut_v1.bin";

        private static byte[] LoadLutFromEmbeddedResource()
        {
            var asm = typeof(FS6000VendorLutCompositor).Assembly;
            using var stream = asm.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' not found. " +
                    "Check the .csproj includes vendor_lut_v1.bin as <EmbeddedResource>.");
            using var br = new BinaryReader(stream);

            // Header: 'VLUT' + u32 version + u32 classes + u32 heBuckets + u32 leBuckets
            var magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'V' || magic[1] != 'L' || magic[2] != 'U' || magic[3] != 'T')
                throw new InvalidDataException(
                    $"Bad vendor-LUT magic: expected 'VLUT', got [{magic[0]:X2} {magic[1]:X2} {magic[2]:X2} {magic[3]:X2}]");
            int version = br.ReadInt32();
            int nc = br.ReadInt32();
            int nh = br.ReadInt32();
            int nl = br.ReadInt32();
            if (nc != NumClasses || nh != HeBuckets || nl != LeBuckets)
                throw new InvalidDataException(
                    $"Vendor-LUT dimension mismatch: expected ({NumClasses},{HeBuckets},{LeBuckets}), got ({nc},{nh},{nl})");

            int expectedBytes = nc * nh * nl * RgbChannels;
            var buf = br.ReadBytes(expectedBytes);
            if (buf.Length != expectedBytes)
                throw new InvalidDataException(
                    $"Vendor-LUT truncated: expected {expectedBytes} bytes of data, got {buf.Length}");

            return buf;
        }

        /// <summary>
        /// Render the vendor-faithful composite as a JPEG byte buffer.
        /// Legacy entry point — wraps <see cref="RenderJpeg(ushort[], ushort[], byte[], int, int)"/>.
        /// </summary>
        public static byte[] RenderJpeg(FS6000FormatDecoder.DecodedFs6000 d)
            => RenderJpeg(d.High, d.Low, d.Material, d.Width, d.Height);

        /// <summary>
        /// v2.11.0 — array-based entry point the kernel calls with
        /// <see cref="Kernel.EnergyChannel.Pixels"/> directly. Same hot loop
        /// as the legacy entry point — ~3 M pixels × ~10 ns per lookup ≈
        /// 30–50 ms on a modern server CPU.
        /// </summary>
        public static byte[] RenderJpeg(ushort[] high, ushort[] low, byte[] material, int width, int height)
        {
            int n = width * height;
            var rgb = CompositeRgbBuffer(high, low, material, n);
            using var img = Image.LoadPixelData<Rgb24>(rgb, width, height);
            using var ms = new MemoryStream(capacity: n / 4);
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
            return ms.ToArray();
        }

        /// <summary>
        /// v2.11.0 — vendor-LUT RGB composite to a flat byte[] (shape [n, 3]).
        /// Shared by the JPEG encoder + Edge mode (which runs an unsharp mask
        /// on the composite image before re-encoding).
        /// </summary>
        public static byte[] CompositeRgbBuffer(ushort[] high, ushort[] low, byte[] material, int n)
        {
            var rgb = new byte[n * RgbChannels];
            var lut = _lut;
            for (int i = 0; i < n; i++)
            {
                int cls = material[i];
                int heBucket = high[i] >> 11;
                if (heBucket >= HeBuckets) heBucket = HeBuckets - 1;
                int leBucket = low[i] >> 11;
                if (leBucket >= LeBuckets) leBucket = LeBuckets - 1;

                int lutIdx = cls * StrideClass + heBucket * StrideHe + leBucket * StrideLe;
                int dst = i * RgbChannels;
                rgb[dst + 0] = lut[lutIdx + 0];
                rgb[dst + 1] = lut[lutIdx + 1];
                rgb[dst + 2] = lut[lutIdx + 2];
            }
            return rgb;
        }

        /// <summary>
        /// v2.11.0 — single-pixel lookup for the kernel's pixel probe. Caller
        /// must clamp <paramref name="heBucket"/> / <paramref name="leBucket"/>
        /// to [0, HeBuckets) / [0, LeBuckets); we skip the re-clamp here
        /// since this is a per-hover hot path.
        /// </summary>
        public static (byte R, byte G, byte B) LookupRgb(int materialClass, int heBucket, int leBucket)
        {
            int lutIdx = materialClass * StrideClass + heBucket * StrideHe + leBucket * StrideLe;
            return (_lut[lutIdx + 0], _lut[lutIdx + 1], _lut[lutIdx + 2]);
        }
    }
}
