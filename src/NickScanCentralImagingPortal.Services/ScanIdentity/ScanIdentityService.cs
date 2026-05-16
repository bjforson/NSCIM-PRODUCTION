using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Utilities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ScanIdentity
{
    public sealed class ScanIdentityService : IScanIdentityService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ScanIdentityService> _logger;

        public ScanIdentityService(ApplicationDbContext db, ILogger<ScanIdentityService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ScanIdentityResult> EnsureSourceIdentityAsync(
            ScanIdentityRequest request,
            CancellationToken cancellationToken = default)
        {
            var scannerType = NormalizeScannerType(request.ScannerType);
            if (string.IsNullOrWhiteSpace(scannerType))
                throw new ArgumentException("ScannerType is required.", nameof(request));

            var tokens = BuildContainerTokens(request).ToList();
            var asset = await FindExistingAssetAsync(request, scannerType, cancellationToken)
                ?? new ScanImageAsset
                {
                    Id = Guid.NewGuid(),
                    OriginalScanRecordId = request.OriginalScanRecordId,
                    ScannerType = scannerType,
                    ScannerNativeId = NormalizeNullable(request.ScannerNativeId),
                    AssetKind = NormalizeNullable(request.AssetKind) ?? ScanImageAssetKinds.Source,
                    StorageKind = NormalizeNullable(request.StorageKind) ?? ScanImageAssetStorageKinds.Database,
                    CreatedAtUtc = DateTime.UtcNow
                };

            asset.SourceContainerLabel = NormalizeNullable(request.SourceContainerLabel) ?? asset.SourceContainerLabel;
            asset.ImageDisplayName = NormalizeNullable(request.ImageDisplayName) ?? asset.ImageDisplayName;
            asset.FileSizeBytes = request.FileSizeBytes ?? asset.FileSizeBytes;
            asset.ScanTimeUtc = request.ScanTimeUtc ?? asset.ScanTimeUtc;
            asset.UpdatedAtUtc = DateTime.UtcNow;

            if (_db.Entry(asset).State == EntityState.Detached)
                _db.ScanImageAssets.Add(asset);

            await _db.SaveChangesAsync(cancellationToken);

            var links = new List<SourceScanContainerLink>();
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                var normalized = ContainerNumberListMatcher.Normalize(token);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                var link = await _db.SourceScanContainerLinks
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        row => row.ScanImageAssetId == asset.Id
                            && row.NormalizedContainerNumber == normalized,
                        cancellationToken);

                if (link == null)
                {
                    link = new SourceScanContainerLink
                    {
                        ScanImageAssetId = asset.Id,
                        OriginalScanRecordId = request.OriginalScanRecordId,
                        ScannerType = scannerType,
                        ScannerNativeId = NormalizeNullable(request.ScannerNativeId),
                        ContainerNumber = token.Trim(),
                        NormalizedContainerNumber = normalized,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _db.SourceScanContainerLinks.Add(link);
                }

                link.SourceContainerLabel = NormalizeNullable(request.SourceContainerLabel) ?? link.SourceContainerLabel;
                link.Position = DeterminePosition(tokens.Count, index);
                link.Confidence = NormalizeNullable(request.Confidence) ?? SourceScanContainerLinkConfidence.SourceMetadata;
                link.UpdatedAtUtc = DateTime.UtcNow;
                links.Add(link);
            }

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Ensured scan identity {AssetId} for {ScannerType}/{NativeId} with {LinkCount} container link(s)",
                asset.Id,
                scannerType,
                request.ScannerNativeId,
                links.Count);

            return new ScanIdentityResult
            {
                Asset = asset,
                Links = links
            };
        }

        private async Task<ScanImageAsset?> FindExistingAssetAsync(
            ScanIdentityRequest request,
            string scannerType,
            CancellationToken cancellationToken)
        {
            var assetKind = NormalizeNullable(request.AssetKind) ?? ScanImageAssetKinds.Source;
            if (request.OriginalScanRecordId.HasValue)
            {
                var byOriginal = await _db.ScanImageAssets
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        asset => asset.OriginalScanRecordId == request.OriginalScanRecordId.Value
                            && asset.ScannerType == scannerType
                            && asset.AssetKind == assetKind,
                        cancellationToken);

                if (byOriginal != null)
                    return byOriginal;
            }

            var scannerNativeId = NormalizeNullable(request.ScannerNativeId);
            if (!string.IsNullOrWhiteSpace(scannerNativeId))
            {
                return await _db.ScanImageAssets
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        asset => asset.ScannerType == scannerType
                            && asset.ScannerNativeId == scannerNativeId
                            && asset.AssetKind == assetKind,
                        cancellationToken);
            }

            return null;
        }

        private static IEnumerable<string> BuildContainerTokens(ScanIdentityRequest request)
        {
            var explicitTokens = request.ContainerNumbers
                .Select(ContainerNumberListMatcher.Normalize)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (explicitTokens.Count > 0)
                return explicitTokens;

            return ContainerNumberListMatcher.ExtractContainerTokens(request.SourceContainerLabel);
        }

        private static string DeterminePosition(int tokenCount, int index)
        {
            if (tokenCount <= 1)
                return SourceScanContainerLinkPositions.Single;

            return index switch
            {
                0 => SourceScanContainerLinkPositions.Left,
                1 => SourceScanContainerLinkPositions.Right,
                _ => SourceScanContainerLinkPositions.Unknown
            };
        }

        private static string NormalizeScannerType(string? scannerType) =>
            string.IsNullOrWhiteSpace(scannerType)
                ? string.Empty
                : scannerType.Trim();

        private static string? NormalizeNullable(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
