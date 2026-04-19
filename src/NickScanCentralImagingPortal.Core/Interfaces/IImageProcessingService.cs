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
        /// Ingest the three FS6000 raw .img channels (HighEnergy / LowEnergy /
        /// Material) for a scan into the fs6000images table. Caller MUST pass
        /// a stable folder path (Archive/, never Staging/) — reads are direct
        /// and will race the scanner's own file handles if Staging is used.
        /// Idempotent: upserts via a unique index, re-runs are no-ops for
        /// channels that already have rows.
        /// Returns a per-call report (channels ingested, bytes, failures).
        /// </summary>
        Task<FS6000RawChannelIngestionReport> IngestFS6000RawChannelsAsync(Guid scanId, string folderPath, CancellationToken ct = default);
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
