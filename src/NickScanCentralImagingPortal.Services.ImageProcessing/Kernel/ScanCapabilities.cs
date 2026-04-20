using System;
using System.Collections.Generic;
using System.Linq;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — mode-catalog capability derivation. Given a
    /// <see cref="DecodedScan"/>, returns the list of operator modes whose
    /// structural requirements it satisfies. Pure function, no tables.
    ///
    /// Adding a new mode: add an enum value to <see cref="RenderMode"/>, add
    /// its structural requirement to <see cref="RenderModeRequirements.IsAvailable"/>,
    /// and implement its rendering in <see cref="ScanRenderer"/>. The
    /// capabilities endpoint picks it up automatically for every scan
    /// (existing and future) that satisfies the requirement.
    ///
    /// Adding a new scanner: implement <see cref="Abstractions.IScanFormatAdapter"/>
    /// that produces a valid <see cref="DecodedScan"/>. The new scanner picks
    /// up every mode its structure supports for free.
    /// </summary>
    public static class ScanCapabilities
    {
        /// <summary>Derive the supported-modes list from scan structure.</summary>
        public static ScanModeCapabilities Derive(DecodedScan scan, string scannerLabel)
        {
            var modes = new List<string>();
            foreach (RenderMode m in Enum.GetValues(typeof(RenderMode)))
            {
                // CompositeLegacy exists for A/B debugging but isn't a normal
                // toolbar option — hide it from the wire list so the UI
                // doesn't have to filter.
                if (m == RenderMode.CompositeLegacy) continue;
                if (RenderModeRequirements.IsAvailable(m, scan))
                {
                    modes.Add(RenderModeRequirements.Name(m));
                }
            }

            return new ScanModeCapabilities
            {
                Scanner        = scannerLabel,
                Variant        = scan.SourceFormatTag,
                SupportedModes = modes.ToArray(),
            };
        }

        /// <summary>
        /// Build a degraded / "scan exists but unrenderable" response for the
        /// partial-channel case, so the UI can show an accurate hint instead
        /// of the blank "no scan" 404. Mirrors the v2.10.5 hotfix behaviour.
        /// </summary>
        public static ScanModeCapabilities Unrenderable(string scannerLabel, Abstractions.BlobInventory inventory)
        {
            string variantLabel = inventory.MissingBlobNames.Count == 0
                ? $"{inventory.SourceFormatTag} (unrenderable)"
                : $"vendor-jpeg-only (missing: {string.Join(",", inventory.MissingBlobNames)})";
            return new ScanModeCapabilities
            {
                Scanner        = scannerLabel,
                Variant        = variantLabel,
                SupportedModes = Array.Empty<string>(),
            };
        }
    }
}
