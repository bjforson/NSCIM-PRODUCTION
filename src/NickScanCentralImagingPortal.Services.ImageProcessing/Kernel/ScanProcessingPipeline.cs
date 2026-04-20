using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — the single public entry point for every scan-processing
    /// operation: mode rendering, pixel probe, ROI inspection, capability
    /// discovery. Wraps <see cref="ScanRouter"/> (container → decoded IR)
    /// with the kernel's pure-function operations over <see cref="DecodedScan"/>.
    ///
    /// Controllers / services talk to this class — not to individual
    /// adapters, retrievers, or the kernel helpers directly. Adding a new
    /// operation: add a method here that does "route → decode → kernel".
    /// Adding a new scanner: implement
    /// <see cref="Abstractions.IScanFormatAdapter"/> +
    /// <see cref="Abstractions.IScanSourceRetriever"/>. No changes here.
    /// </summary>
    public sealed class ScanProcessingPipeline
    {
        private readonly ILogger<ScanProcessingPipeline> _logger;
        private readonly ScanRouter _router;

        public ScanProcessingPipeline(ILogger<ScanProcessingPipeline> logger, ScanRouter router)
        {
            _logger = logger;
            _router = router;
        }

        /// <summary>
        /// Render the container's scan in the named operator mode. Returns:
        /// <list type="bullet">
        ///   <item>JPEG bytes on success</item>
        ///   <item>null when no decoded scan is available (no scan, partial channels, decode failure)</item>
        ///   <item>null when the mode isn't structurally supported for this scan (upstream maps to 422)</item>
        ///   <item>null when the mode name can't be parsed</item>
        /// </list>
        /// </summary>
        public async Task<byte[]?> RenderAsync(
            string containerNumber, string mode,
            float loPct = 1.0f, float hiPct = 99.5f, float gamma = 1.0f,
            CancellationToken ct = default)
        {
            var parsed = RenderModeRequirements.TryParse(mode);
            if (parsed == null)
            {
                _logger.LogWarning("[Pipeline] Unknown mode '{Mode}' for {Container}", mode, containerNumber);
                return null;
            }
            var decoded = await _router.GetDecodedAsync(containerNumber, ct);
            if (decoded == null) return null;

            var jpeg = ScanRenderer.Render(decoded, parsed.Value, loPct, hiPct, gamma);
            if (jpeg != null)
            {
                _logger.LogInformation(
                    "[Pipeline] {Container} variant={Variant} mode={Mode} loPct={LoPct} hiPct={HiPct} gamma={Gamma}: {OutBytes} bytes",
                    containerNumber, decoded.SourceFormatTag, parsed.Value, loPct, hiPct, gamma, jpeg.Length);
            }
            return jpeg;
        }

        /// <summary>Pixel probe at image-native coordinates.</summary>
        public async Task<PixelValueResult?> ProbePixelAsync(
            string containerNumber, int x, int y,
            CancellationToken ct = default)
        {
            var decoded = await _router.GetDecodedAsync(containerNumber, ct);
            if (decoded == null) return null;
            return ScanPixelProbe.Probe(decoded, x, y);
        }

        /// <summary>ROI analysis for a rectangle.</summary>
        public async Task<RoiInspectorResult?> BuildRoiAsync(
            string containerNumber, int x, int y, int w, int h,
            CancellationToken ct = default)
        {
            var decoded = await _router.GetDecodedAsync(containerNumber, ct);
            if (decoded == null) return null;
            return ScanRoiBuilder.Build(decoded, x, y, w, h);
        }

        /// <summary>
        /// Scan-mode capability manifest for the container. Drives the
        /// viewer's mode toolbar. When a scan exists but can't be decoded
        /// (partial channels), returns a "vendor-jpeg-only (missing: X,Y)"
        /// variant label + empty mode list so the UI hides the toolbar
        /// instead of offering guaranteed-broken buttons. Returns null only
        /// when NO scan exists at all.
        /// </summary>
        public async Task<ScanModeCapabilities?> GetCapabilitiesAsync(
            string containerNumber,
            CancellationToken ct = default)
        {
            var decoded = await _router.GetDecodedAsync(containerNumber, ct);
            if (decoded != null)
            {
                return ScanCapabilities.Derive(decoded, ScannerLabel(decoded));
            }

            // Decode returned null. Either no scan, or the scan exists but
            // is partial. Use the retriever's cheap inventory path to tell
            // the two apart.
            var (scannerType, inventory) = await _router.InventoryAsync(containerNumber, ct);
            if (inventory == null) return null;
            return ScanCapabilities.Unrenderable(scannerType.ToString(), inventory);
        }

        private static string ScannerLabel(DecodedScan scan) => scan.SourceFormatTag switch
        {
            var t when t.StartsWith("fs6000") => "FS6000",
            var t when t.StartsWith("ase")    => "ASE",
            _ => "unknown",
        };
    }
}
