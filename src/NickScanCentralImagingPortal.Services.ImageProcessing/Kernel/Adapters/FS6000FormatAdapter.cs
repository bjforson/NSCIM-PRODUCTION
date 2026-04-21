using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters
{
    /// <summary>
    /// v2.11.0 — FS6000 wire-format → IR adapter. Wraps the existing
    /// <see cref="FS6000FormatDecoder.Decode"/> parser (which stays byte-for-byte
    /// the work we've already invested) and packs the output into a
    /// <see cref="DecodedScan"/> with:
    /// <list type="bullet">
    ///   <item>Two <see cref="EnergyChannel"/>s (High + Low, both 16-bit)</item>
    ///   <item>One <see cref="MaterialClassification"/> using the FS6000 taxonomy</item>
    ///   <item>Optional vendor Main JPEG as <see cref="DecodedScan.VendorReferenceJpeg"/> (when the <c>Main</c> blob is included)</item>
    /// </list>
    ///
    /// Pure / stateless: no DB, no cache, no IO. Decodes the bytes in-process
    /// and returns the IR.
    /// </summary>
    public sealed class FS6000FormatAdapter : IScanFormatAdapter
    {
        public const string FormatTag = "fs6000-v1";

        private readonly ILogger<FS6000FormatAdapter> _logger;

        public FS6000FormatAdapter(ILogger<FS6000FormatAdapter> logger)
        {
            _logger = logger;
        }

        public string SourceFormatTag => FormatTag;

        public Task<DecodedScan?> DecodeAsync(ScanSourceBytes bytes, CancellationToken ct = default)
        {
            // HE + LE are the absolute minimum. Without both energies there's
            // no meaningful pixel operation we can do (composite, grayscale,
            // probe, ROI all need at least one of them).
            if (!bytes.Blobs.TryGetValue("HighEnergy", out var highBlob) ||
                !bytes.Blobs.TryGetValue("LowEnergy",  out var lowBlob))
            {
                _logger.LogDebug("[FS6000Adapter] {Container} missing HE or LE raw channel — unrenderable",
                    bytes.ContainerNumber);
                return Task.FromResult<DecodedScan?>(null);
            }

            // Material is optional: v2.14.0 supports partial-channel scans for
            // the ~40 scans/week where the FS6000 scanner doesn't emit
            // material.img. Those scans get the greyscale mode subset (bw,
            // inverse, high-pen, low-pen, diff) driven by RenderModeRequirements
            // which already gates Composite / Organic / Metal / Edge on
            // scan.Material != null.
            bool hasMaterial = bytes.Blobs.TryGetValue("Material", out var matBlob);

            int width, height;
            ushort[] highPixels, lowPixels;
            byte[]?  matPixels = null;
            DateTime? decodedTimestamp;

            try
            {
                if (hasMaterial)
                {
                    var d = FS6000FormatDecoder.Decode(highBlob, lowBlob, matBlob!);
                    width = d.Width; height = d.Height;
                    highPixels = d.High; lowPixels = d.Low; matPixels = d.Material;
                    decodedTimestamp = d.Timestamp;
                }
                else
                {
                    var (w, h, he, le, ts) = FS6000FormatDecoder.DecodeEnergyOnly(highBlob, lowBlob);
                    width = w; height = h;
                    highPixels = he; lowPixels = le;
                    decodedTimestamp = ts;
                    _logger.LogInformation(
                        "[FS6000Adapter] {Container} decoded as energy-only (no material.img); greyscale + diff modes only",
                        bytes.ContainerNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000Adapter] Decode failed for {Container} (scan {ScanId})",
                    bytes.ContainerNumber, bytes.ScanId);
                return Task.FromResult<DecodedScan?>(null);
            }

            var channels = new List<EnergyChannel>
            {
                new EnergyChannel { Kind = EnergyKind.High, BitDepth = 16, Pixels = highPixels },
                new EnergyChannel { Kind = EnergyKind.Low,  BitDepth = 16, Pixels = lowPixels  },
            };

            MaterialClassification? material = null;
            if (matPixels != null)
            {
                material = new MaterialClassification
                {
                    Classes  = matPixels,
                    Taxonomy = Fs6000Taxonomy,
                };
            }

            bytes.Blobs.TryGetValue("Main", out var vendorJpeg);

            DateTime? scanTime = decodedTimestamp;
            if (scanTime == null && bytes.Metadata.TryGetValue("ScanTime", out var ts2) &&
                DateTime.TryParse(ts2, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTs))
            {
                scanTime = parsedTs;
            }

            // Use a distinct format tag for partial scans so the UI can hint
            // via the variant label. Behaviour is otherwise identical to the
            // full fs6000-v1 path — capabilities just come back as the 5-mode
            // subset because RenderModeRequirements drops Composite/Organic/
            // Metal/Edge when Material is null.
            string formatTag = material != null ? FormatTag : "fs6000-v1-no-material";

            var result = new DecodedScan
            {
                ScanId          = bytes.ScanId,
                ContainerNumber = bytes.ContainerNumber,
                SourceFormatTag = formatTag,
                ScanTime        = scanTime,
                Width           = width,
                Height          = height,
                PixelPitchMm    = 0.0, // FS6000 header doesn't carry pitch today; wire up when we extract it
                Orientation     = ScanOrientation.LeftToRight,
                Channels        = channels,
                Material        = material,
                VendorReferenceJpeg = vendorJpeg,
                SourceMetadata  = bytes.Metadata,
            };
            return Task.FromResult<DecodedScan?>(result);
        }

        /// <summary>
        /// FS6000 scanner's material-class taxonomy. Declared here so the
        /// kernel doesn't hardcode the <c>{0, 1-40, 41-120, 121-255}</c>
        /// band boundaries — a future scanner declares its own and everything
        /// downstream reads from <see cref="MaterialTaxonomy"/>.
        ///
        /// Band meanings come from FS6000 vendor documentation:
        /// <list type="bullet">
        ///   <item>0: no signal / outside scan area → "background"</item>
        ///   <item>1-40: low-signal / low-confidence → "noise"</item>
        ///   <item>41-120: low-Z (organic) materials — wood, plastic, textiles, liquids</item>
        ///   <item>121-255: high-Z (metal) materials — steel, aluminium, denser compounds</item>
        /// </list>
        /// </summary>
        public static readonly MaterialTaxonomy Fs6000Taxonomy = new()
        {
            Bands = new List<MaterialBand>
            {
                new() { Category = "background", ClassStart = 0,   ClassEnd = 0,   HexColor = "#ffffff" },
                new() { Category = "noise",      ClassStart = 1,   ClassEnd = 40,  HexColor = "#eeeeee" },
                new() { Category = "organic",    ClassStart = 41,  ClassEnd = 120, ZMin = 6,  ZMax = 11, HexColor = "#ff9f43" },
                new() { Category = "metal",      ClassStart = 121, ClassEnd = 255, ZMin = 13, ZMax = 80, HexColor = "#4a90e2" },
            },
        };
    }
}
