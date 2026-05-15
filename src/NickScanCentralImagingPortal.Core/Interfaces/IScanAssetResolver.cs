using System;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.DTOs.ScanAssets;

namespace NickScanCentralImagingPortal.Core.Interfaces;

public interface IScanAssetResolver
{
    Task<ScanAssetResolution> ResolveAsync(
        ScanAssetResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(
            request.ContainerNumber ?? string.Empty,
            request.GroupIdentifier,
            request.AnalysisRecordId,
            request.SplitJobId,
            cancellationToken);
    }

    Task<ScanAssetResolution> ResolveAsync(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(
            new ScanAssetResolutionRequest
            {
                ContainerNumber = containerNumber,
                GroupIdentifier = groupIdentifier,
                AnalysisRecordId = analysisRecordId,
                SplitJobId = splitJobId
            },
            cancellationToken);
    }
}
