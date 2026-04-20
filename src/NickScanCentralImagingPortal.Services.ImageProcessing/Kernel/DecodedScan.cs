using System;
using System.Collections.Generic;
using System.Linq;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — the universal intermediate representation (IR) for every scan
    /// the system handles, regardless of which scanner produced it. This is
    /// the <b>only</b> type the kernel operates on; adding a new scanner is
    /// "write an <see cref="Abstractions.IScanFormatAdapter"/> that produces
    /// one of these" and nothing else.
    ///
    /// Design notes:
    /// <list type="bullet">
    ///   <item><b>Variadic channels</b>: future 3- or 4-energy scanners (Nuctech
    ///   MX-series, multi-energy Heimann) don't need a new shape; they populate
    ///   more <see cref="EnergyChannel"/> entries.</item>
    ///   <item><b>Declared material taxonomy</b>: we don't hardcode FS6000's
    ///   <c>{0=bg, 1-40=noise, 41-120=organic, 121-255=metal}</c> scheme. Each
    ///   adapter declares its scanner's taxonomy as data. The kernel reads it.</item>
    ///   <item><b>Per-channel bit-depth</b>: ASE is 16-bit, some scanners are
    ///   12-bit. Percentile math is the same either way but the max display
    ///   range differs.</item>
    ///   <item><b>Geometry metadata</b>: <see cref="PixelPitchMm"/> lets the
    ///   ruler tool show real millimetres. <see cref="Orientation"/> handles
    ///   right-to-left scanners without per-scanner flipping code.</item>
    ///   <item><b>Optional vendor reference JPEG</b>: lets the UI A/B compare
    ///   "our render" vs "vendor render" without separate endpoints.</item>
    ///   <item><b>Opaque source metadata</b>: serial, firmware, calibration
    ///   date — debugging + audit trail, doesn't drive behaviour.</item>
    /// </list>
    /// </summary>
    public sealed class DecodedScan
    {
        /// <summary>Stable identifier the adapter emitted. Maps to the scan's
        /// row in whatever DB table the source retriever reads from.</summary>
        public required string ScanId { get; init; }

        public required string ContainerNumber { get; init; }

        /// <summary>Short tag identifying the wire format the adapter parsed
        /// (e.g. <c>"fs6000-v1"</c>, <c>"ase-tri-panel"</c>, <c>"ase-single-view"</c>,
        /// <c>"heimann-mx3000"</c>). Powers the UI "variant" label and helps
        /// telemetry attribute scans to format versions.</summary>
        public required string SourceFormatTag { get; init; }

        public DateTime? ScanTime { get; init; }

        public required int Width { get; init; }
        public required int Height { get; init; }

        /// <summary>Pixel pitch in millimetres. 0 if unknown. Lets calibrated
        /// measurement tools (ruler, Phase 4+) report real-world distances.</summary>
        public double PixelPitchMm { get; init; }

        public ScanOrientation Orientation { get; init; } = ScanOrientation.LeftToRight;

        /// <summary>One or more energy channels. Must contain at least one.
        /// Kernel operations look up channels by <see cref="EnergyChannel.Kind"/>
        /// rather than positional index, so a 4-channel scanner's ordering is
        /// whatever the adapter emits.</summary>
        public required IReadOnlyList<EnergyChannel> Channels { get; init; }

        /// <summary>Optional material classification (dual-energy + scanners
        /// that compute Z-effective). Null when the scanner doesn't supply it.
        /// Kernel operations that require material must null-check.</summary>
        public MaterialClassification? Material { get; init; }

        /// <summary>Optional scanner-rendered reference JPEG. When present, the
        /// UI can offer an A/B comparison against the kernel's composite. Also
        /// used as a fallback when no channels support a requested mode.</summary>
        public byte[]? VendorReferenceJpeg { get; init; }

        /// <summary>Opaque per-scanner metadata: serial number, firmware version,
        /// calibration date, energy level, scan speed, etc. Not interpreted by
        /// the kernel — stored for telemetry, audit, and diagnostics.</summary>
        public IReadOnlyDictionary<string, string> SourceMetadata { get; init; }
            = new Dictionary<string, string>();

        // ── Convenience accessors ──────────────────────────────────────────

        /// <summary>Find the first channel of the given kind, or null.</summary>
        public EnergyChannel? ChannelByKind(EnergyKind kind)
            => Channels.FirstOrDefault(c => c.Kind == kind);

        public bool HasChannel(EnergyKind kind) => ChannelByKind(kind) != null;

        /// <summary>True when the scan carries both high and low energies,
        /// i.e. dual-energy modes (composite, high-pen, low-pen, organic-strip,
        /// metal-strip, diff) are structurally possible.</summary>
        public bool IsDualEnergy => HasChannel(EnergyKind.High) && HasChannel(EnergyKind.Low);

        /// <summary>True when <see cref="IsDualEnergy"/> AND a material
        /// classification is present — all 9 named modes structurally possible.</summary>
        public bool SupportsFullCatalog => IsDualEnergy && Material != null;
    }

    /// <summary>
    /// One energy channel of a scan. A single-view scanner has one
    /// (<see cref="EnergyKind.Single"/>); a dual-energy scanner has two
    /// (<see cref="EnergyKind.High"/> + <see cref="EnergyKind.Low"/>);
    /// multi-energy scanners have more.
    /// </summary>
    public sealed class EnergyChannel
    {
        public required EnergyKind Kind { get; init; }

        /// <summary>Storage bit-depth. Pixels are always stored as
        /// <c>ushort[]</c> to normalise layout, but only the low
        /// <see cref="BitDepth"/> bits carry signal. Renderers use this for
        /// correct percentile mapping on non-16-bit inputs.</summary>
        public required int BitDepth { get; init; }

        /// <summary>Row-major pixels, length = Width*Height.</summary>
        public required ushort[] Pixels { get; init; }

        /// <summary>Maximum value the channel can take given <see cref="BitDepth"/>.
        /// For 16-bit = 65535, 14-bit = 16383, etc.</summary>
        public int MaxValue => (1 << BitDepth) - 1;
    }

    public enum EnergyKind
    {
        /// <summary>Single-channel scanner (only one energy recorded).</summary>
        Single = 0,
        /// <summary>High energy (dual-energy X-ray).</summary>
        High = 1,
        /// <summary>Low energy (dual-energy X-ray).</summary>
        Low = 2,
        /// <summary>Mid energy (3+ energy scanners).</summary>
        Mid = 3,
    }

    public enum ScanOrientation
    {
        LeftToRight = 0,  // most FS6000 / ASE scanners
        RightToLeft = 1,  // some Heimann variants scan reverse
        TopDown = 2,      // vertical gantry scanners
    }

    /// <summary>
    /// Per-pixel material-class index plus the taxonomy that maps class
    /// numbers to human-meaningful bands. Declared by the format adapter, not
    /// the kernel — different vendors use different class schemes and the
    /// kernel must not assume FS6000's specific bands apply universally.
    /// </summary>
    public sealed class MaterialClassification
    {
        /// <summary>Row-major class indices, length = Width*Height.</summary>
        public required byte[] Classes { get; init; }

        /// <summary>Vendor-declared mapping from class-byte ranges to
        /// categories (organic / metal / …) and Z-effective estimates.</summary>
        public required MaterialTaxonomy Taxonomy { get; init; }
    }

    /// <summary>
    /// Ordered list of class-byte ranges → category bands. Each scanner
    /// declares its own; the kernel reads it and never assumes a specific
    /// layout. FS6000 declares
    /// <c>[{bg,0-0}, {noise,1-40}, {organic,41-120}, {metal,121-255}]</c>;
    /// a different scanner might declare <c>[{bg,0-0}, {organic,1-31},
    /// {metal,32-63}]</c>. Both work.
    /// </summary>
    public sealed class MaterialTaxonomy
    {
        public required IReadOnlyList<MaterialBand> Bands { get; init; }

        /// <summary>Look up the band for a given class byte. O(bands.Count),
        /// negligible for typical N &lt; 10 band counts.</summary>
        public MaterialBand BandFor(byte classByte)
        {
            foreach (var b in Bands)
            {
                if (classByte >= b.ClassStart && classByte <= b.ClassEnd) return b;
            }
            // Every taxonomy should declare a band covering 0..255. If not,
            // fall back to a synthetic "unknown" so lookups never null-ref.
            return MaterialBand.Unknown;
        }
    }

    public sealed class MaterialBand
    {
        public static readonly MaterialBand Unknown = new()
        {
            Category = "unknown",
            ClassStart = 0,
            ClassEnd = 255,
        };

        public required string Category { get; init; }  // "background" / "organic" / "metal" / "noise" / ...
        public required byte ClassStart { get; init; }
        public required byte ClassEnd { get; init; }

        /// <summary>Optional Z-effective range for this band. Null = unknown
        /// / scanner doesn't report Z. Powers future "what's this element" UI.</summary>
        public double? ZMin { get; init; }
        public double? ZMax { get; init; }

        /// <summary>Optional display hint colour (hex). Renderers can use this
        /// for per-band UI cues; not the actual composite LUT (that's
        /// <see cref="FS6000.FS6000VendorLutCompositor"/>).</summary>
        public string? HexColor { get; init; }
    }
}
