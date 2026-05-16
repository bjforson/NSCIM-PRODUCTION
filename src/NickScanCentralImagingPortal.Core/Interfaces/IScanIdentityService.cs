using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IScanIdentityService
    {
        Task<ScanIdentityResult> EnsureSourceIdentityAsync(
            ScanIdentityRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class ScanIdentityRequest
    {
        public int? OriginalScanRecordId { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string? ScannerNativeId { get; set; }
        public string? SourceContainerLabel { get; set; }
        public IReadOnlyCollection<string> ContainerNumbers { get; set; } = Array.Empty<string>();
        public string AssetKind { get; set; } = ScanImageAssetKinds.Source;
        public string StorageKind { get; set; } = ScanImageAssetStorageKinds.Database;
        public string? ImageDisplayName { get; set; }
        public long? FileSizeBytes { get; set; }
        public DateTime? ScanTimeUtc { get; set; }
        public string Confidence { get; set; } = SourceScanContainerLinkConfidence.SourceMetadata;
    }

    public sealed class ScanIdentityResult
    {
        public ScanImageAsset Asset { get; init; } = new();
        public IReadOnlyList<SourceScanContainerLink> Links { get; init; } = Array.Empty<SourceScanContainerLink>();

        public SourceScanContainerLink? FindLink(string? containerNumber)
        {
            var normalized = NickScanCentralImagingPortal.Core.Utilities.ContainerNumberListMatcher.Normalize(containerNumber);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            return Links.FirstOrDefault(link =>
                string.Equals(link.NormalizedContainerNumber, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }
}
