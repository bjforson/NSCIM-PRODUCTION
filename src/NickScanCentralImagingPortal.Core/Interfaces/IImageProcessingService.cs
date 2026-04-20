using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IImageProcessingService
    {
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber);
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber, ScannerType preferredScanner);
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(ImageDetails image, ImageProcessingRequest request);
        Task<ImageMetadata> GetImageMetadataAsync(string containerNumber);
        Task<ImageMetadata> GetImageMetadataAsync(string containerNumber, ScannerType? preferredScanner = null);
        Task<ScannerType> DetectScannerTypeAsync(string containerNumber);
        Task<BatchProcessingResult> BatchProcessImagesAsync(BatchProcessingRequest request);
        Task RetryImageProcessingAsync(int imageId);
        Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null);
        Task<Core.Models.ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber);
        Task<Core.Models.ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber, string? imageType);

        /// <summary>
        /// v2.10.0 mode-catalog rendering: return JPEG bytes for the requested
        /// operator image mode (composite / bw / organic-strip / metal-strip /
        /// high-pen / inverse / edge / diff). Returns <c>null</c> when the
        /// container isn't FS6000 or the scan doesn't have the three raw
        /// channels required for mode-based rendering — the controller falls
        /// back to the existing <see cref="GetCompleteContainerDataAsync"/>
        /// path in that case.
        /// Parameters <paramref name="loPct"/> / <paramref name="hiPct"/> /
        /// <paramref name="gamma"/> back the Variable Density + gamma sliders.
        /// </summary>
        Task<byte[]?> GetRenderedImageBytesAsync(
            string containerNumber,
            string mode,
            float loPct = 1.0f,
            float hiPct = 99.5f,
            float gamma = 1.0f,
            CancellationToken ct = default);

        /// <summary>
        /// v2.10.0 ROI Inspector: crop a rectangle from the three FS6000 raw
        /// channels, compute per-channel stats, compute material-class
        /// distribution, and return small preview JPEGs. Client calls this
        /// once the operator draws a rectangle on the canvas; response
        /// powers the side-panel histogram + dominant-Z-class chip.
        /// Returns <c>null</c> when the container isn't FS6000 or the scan
        /// lacks raw channels.
        /// Coordinates are in the image's native pixel space.
        /// </summary>
        Task<RoiInspectorResult?> GetRoiInspectorAsync(
            string containerNumber,
            int x, int y, int width, int height,
            CancellationToken ct = default);

        /// <summary>
        /// v2.10.0 scan-mode capability manifest. The single-canvas viewer
        /// calls this once on open to gate its mode-toolbar buttons to only
        /// the modes the underlying scan supports. v2.11.0 derives the list
        /// from scan structure (channel count + material presence), not from
        /// hardcoded scanner tables, so new scanners + new modes get correct
        /// capabilities automatically. Returns null when the container has
        /// no scan on any scanner.
        /// </summary>
        Task<ScanModeCapabilities?> GetScanModeCapabilitiesAsync(
            string containerNumber,
            CancellationToken ct = default);

        /// <summary>
        /// v2.11.0 — per-pixel probe for the hover chip in the viewer.
        /// Returns HE / LE / Material + vendor-LUT RGB at (x, y). Single-
        /// channel scans return only HighEnergy (the sole channel) and leave
        /// the dual-energy-dependent fields null. Coordinates are image-
        /// native; clamped to bounds server-side. Returns null when no
        /// decoded scan is available for the container.
        /// </summary>
        Task<PixelValueResult?> GetPixelValueAsync(
            string containerNumber,
            int x, int y,
            CancellationToken ct = default);

        /// <summary>
        /// Ingest the three FS6000 raw .img channels (HighEnergy / LowEnergy /
        /// Material) for a scan into the fs6000images table. Caller MUST pass
        /// a stable folder path (Archive/, never Staging/) — reads are direct
        /// and will race the scanner's own file handles if Staging is used.
        /// Idempotent: upserts via a unique index, re-runs are no-ops for
        /// channels that already have rows.
        /// Returns a per-call report (channels ingested, bytes, failures).
        /// </summary>
        Task<FS6000RawChannelIngestionReport> IngestFS6000RawChannelsAsync(Guid scanId, string folderPath, CancellationToken ct = default);

        /// <summary>
        /// Report the pixel dimensions of the image that <see cref="GetCompleteContainerDataAsync"/>
        /// will currently serve for <paramref name="containerNumber"/>, plus a short
        /// string identifying the serving mode. Consumed by annotation endpoints
        /// so they can scale stored coordinates into the current image's space.
        /// Returns <c>(0, 0, "unknown")</c> if the container or scan can't be resolved.
        /// </summary>
        Task<ServedImageDimensions> GetServedImageDimensionsAsync(string containerNumber, CancellationToken ct = default);
    }

    /// <summary>
    /// Describes the image that <see cref="IImageProcessingService.GetCompleteContainerDataAsync"/>
    /// currently serves for a container.
    /// </summary>
    /// <remarks>
    /// <see cref="Mode"/> values (stable; used for annotation coordspace tagging):
    /// <list type="bullet">
    /// <item><c>"fs6000-vendorjpeg"</c> — vendor-rendered 8-bit JPEG, ~2295x1378.</item>
    /// <item><c>"fs6000-composite16bit"</c> — server-rendered composite from 16-bit raw channels, ~3256x1378 (varies per scan).</item>
    /// <item><c>"ase"</c> — ASE percentile-stretched JPEG, ASE-native dims.</item>
    /// <item><c>"unknown"</c> — container/scan not resolvable.</item>
    /// </list>
    /// </remarks>
    public class ServedImageDimensions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Mode { get; set; } = "unknown";
    }

    /// <summary>
    /// Thin projection of <c>RawChannelIngestionResult</c> for the public
    /// interface — the full type lives in Services.ImageProcessing and would
    /// create a circular reference if exposed here.
    /// </summary>
    public class FS6000RawChannelIngestionReport
    {
        public Guid ScanId { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public int IngestedChannels { get; set; }
        public long IngestedBytes { get; set; }
        public int AlreadyPresent { get; set; }
        public int MissingFiles { get; set; }
        public int FailedChannels { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LastError { get; set; }
    }


    /// <summary>
    /// Response from <see cref="IImageProcessingService.GetRoiInspectorAsync"/>.
    /// Flat structure — the consumer is a Blazor component that binds directly
    /// to named properties, not a complex dashboard.
    /// </summary>
    public class RoiInspectorResult
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>Per-channel stats (HE / LE 16-bit energies).</summary>
        public ChannelStats HighEnergy { get; set; } = new();
        public ChannelStats LowEnergy { get; set; } = new();

        /// <summary>Material-class histogram + dominant class within the ROI.</summary>
        public MaterialStats Material { get; set; } = new();

        /// <summary>Small (max 240px on the long side) preview crops, base64 JPEG.</summary>
        public string HighEnergyPreviewB64 { get; set; } = string.Empty;
        public string LowEnergyPreviewB64 { get; set; } = string.Empty;
        public string MaterialPreviewB64 { get; set; } = string.Empty;

        public long ElapsedMs { get; set; }
    }

    public class ChannelStats
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public double Mean { get; set; }
        public double Median { get; set; }
        public int P01 { get; set; }
        public int P99 { get; set; }
        /// <summary>32-bucket normalized histogram (0..1 per bucket).</summary>
        public double[] Histogram { get; set; } = Array.Empty<double>();
    }

    public class MaterialStats
    {
        /// <summary>Category with the highest pixel coverage inside the ROI.</summary>
        public string DominantCategory { get; set; } = "background";
        /// <summary>0..1, how much of the ROI is the dominant category.</summary>
        public double DominantPercent { get; set; }
        /// <summary>Per-category breakdown — keys: background, noise, organic, metal.</summary>
        public Dictionary<string, double> CategoryDistribution { get; set; } = new();
    }

    /// <summary>
    /// v2.11.0 response from <see cref="IImageProcessingService.GetPixelValueAsync"/>.
    /// Powers the viewer's hover chip. Null fields mean "the scan variant
    /// doesn't carry this channel" — e.g. single-view ASE has no LE or
    /// material, so those stay null and the UI renders the chip with
    /// whatever's present.
    /// </summary>
    public class PixelValueResult
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>Raw HE value (or sole channel for single-view scans).</summary>
        public int? HighEnergy { get; set; }

        /// <summary>Raw LE value. Null for single-view.</summary>
        public int? LowEnergy { get; set; }

        /// <summary>Material class 0..255. Null when scan doesn't carry material.</summary>
        public int? Material { get; set; }

        /// <summary>Vendor-LUT composite colour at this pixel. Null for
        /// scans without both energies + material (the LUT needs all three).</summary>
        public int? Red { get; set; }
        public int? Green { get; set; }
        public int? Blue { get; set; }

        /// <summary>Category from the scan's declared material taxonomy
        /// ("organic" / "metal" / "background" / "noise" / etc.). Empty
        /// string when no material channel is present.</summary>
        public string MaterialCategory { get; set; } = string.Empty;

        /// <summary>Scan variant tag — "fs6000-v1", "ase-tri-panel", "ase-single-view",
        /// etc. Lets the UI format the chip appropriately.</summary>
        public string Variant { get; set; } = string.Empty;
    }

    /// <summary>
    /// Scan-mode capability manifest returned by
    /// <see cref="IImageProcessingService.GetScanModeCapabilitiesAsync"/>.
    /// The frontend mode-toolbar reads <see cref="SupportedModes"/> to decide
    /// which buttons to render (or grey out) for the currently-open scan.
    /// <see cref="Variant"/> is an informational tag (e.g. "composite16bit",
    /// "tri-panel", "single-view") shown as a sub-label or tooltip.
    /// </summary>
    public class ScanModeCapabilities
    {
        public string Scanner { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public string[] SupportedModes { get; set; } = Array.Empty<string>();
    }

    public class ImageMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime ScanTime { get; set; }
        public string ScannerId { get; set; } = string.Empty;
        public string ImageFormat { get; set; } = string.Empty;
        public string ProcessingPipeline { get; set; } = string.Empty;
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();
    }

    public enum ScannerType
    {
        Unknown = 0,
        FS6000 = 1,
        ASE = 2,
        HeimannSmith = 3
    }
}
