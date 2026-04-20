using System;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — the canonical, scanner-agnostic set of operator image modes.
    /// Same 9-mode catalog the viewer has always exposed (matches
    /// Smiths / Rapiscan / Nuctech vendor vocabulary), just renamed from
    /// <c>Fs6000RenderMode</c> to reflect that it's universal.
    /// </summary>
    public enum RenderMode
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

        /// <summary>Python-ported composite, kept for A/B comparison only.
        /// Not exposed in the default mode toolbar.</summary>
        CompositeLegacy = 9,
    }

    /// <summary>
    /// v2.11.0 — declarative requirements for each mode. <see cref="ScanCapabilities"/>
    /// derives the supported-modes list for a given scan by checking which
    /// modes' requirements the scan's structure satisfies. No hardcoded
    /// "if FS6000 → 9 modes" tables anywhere.
    /// </summary>
    public static class RenderModeRequirements
    {
        /// <summary>
        /// True if this mode can be rendered from the supplied scan structure.
        /// Implementation: declare the structural requirements, check them.
        /// </summary>
        public static bool IsAvailable(RenderMode mode, DecodedScan scan)
        {
            return mode switch
            {
                RenderMode.Composite        => scan.IsDualEnergy && scan.Material != null,
                RenderMode.CompositeLegacy  => scan.IsDualEnergy && scan.Material != null,
                RenderMode.BlackWhite       => scan.Channels.Count >= 1,
                RenderMode.Inverse          => scan.Channels.Count >= 1,
                // HighPen / LowPen are vendor-defined "penetration" modes that
                // emphasise a specific energy channel in a dual-energy dataset.
                // They have no meaning on a single-channel scanner (the one
                // channel is neither "high" nor "low") — gate on dual-energy.
                RenderMode.HighPen          => scan.IsDualEnergy,
                RenderMode.LowPen           => scan.IsDualEnergy,
                RenderMode.OrganicStrip     => scan.IsDualEnergy && scan.Material != null,
                RenderMode.MetalStrip       => scan.IsDualEnergy && scan.Material != null,
                RenderMode.Edge             => scan.Channels.Count >= 1,
                RenderMode.Diff             => scan.IsDualEnergy,
                _ => false,
            };
        }

        /// <summary>Short wire name for a mode. Kept identical to the v2.10.x
        /// names so the frontend contract is unchanged.</summary>
        public static string Name(RenderMode mode) => mode switch
        {
            RenderMode.Composite       => "composite",
            RenderMode.CompositeLegacy => "composite-legacy",
            RenderMode.BlackWhite      => "bw",
            RenderMode.Inverse         => "inverse",
            RenderMode.HighPen         => "high-pen",
            RenderMode.LowPen          => "low-pen",
            RenderMode.OrganicStrip    => "organic-strip",
            RenderMode.MetalStrip      => "metal-strip",
            RenderMode.Edge            => "edge",
            RenderMode.Diff            => "diff",
            _ => "unknown",
        };

        /// <summary>
        /// Parse a wire-format mode name, tolerant of casing / separators /
        /// vendor synonyms. Returns null for unrecognised names so callers can
        /// choose a default.
        /// </summary>
        public static RenderMode? TryParse(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var k = input.Trim().ToLowerInvariant().Replace('_', '-');
            return k switch
            {
                "composite" or "color" or "default"                         => RenderMode.Composite,
                "composite-legacy" or "composite-python" or "composite-v1"  => RenderMode.CompositeLegacy,
                "bw" or "blackwhite" or "black-white" or "grayscale" or "greyscale" => RenderMode.BlackWhite,
                "inverse" or "inverse-video" or "negative"                  => RenderMode.Inverse,
                "high-pen" or "highpen" or "high-penetration"               => RenderMode.HighPen,
                "low-pen" or "lowpen" or "low-penetration"                  => RenderMode.LowPen,
                "organic-strip" or "organic-stripping" or "strip-organic"   => RenderMode.OrganicStrip,
                "metal-strip" or "inorganic-strip" or "strip-metal" or "inorganic-stripping" => RenderMode.MetalStrip,
                "edge" or "edge-enhance" or "unsharp"                       => RenderMode.Edge,
                "diff" or "energy-diff" or "dual-energy-diff"               => RenderMode.Diff,
                _ => null,
            };
        }
    }
}
