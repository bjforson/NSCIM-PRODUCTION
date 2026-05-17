using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services;

public static class ContainerDetailsRoutes
{
    public const string BasePath = "/api/containerdetails";
    public const string PredictiveCacheBasePath = "/api/cache/predictive";

    public static string BuildBasicPath(string containerNumber)
        => $"{BasePath}/basic/{Escape(containerNumber)}";

    public static string BuildFullPath(string containerNumber)
        => $"{BasePath}/full/{Escape(containerNumber)}";

    public static string BuildScannerPagedPath(string containerNumber, int page = 1, int pageSize = 50)
        => $"{BasePath}/scanner/{Escape(containerNumber)}?page={page}&pageSize={pageSize}";

    public static string BuildScannerFullPath(string containerNumber)
        => $"{BasePath}/scanner/{Escape(containerNumber)}?full=true";

    public static string BuildScannerAliasWithSourceScanQueryPath(
        string containerNumber,
        string? groupIdentifier,
        int? analysisRecordId,
        Guid? splitJobId,
        ScanAssetResolution? resolution = null,
        int? page = null,
        int? pageSize = null)
        => $"{BasePath}/scanner/{Escape(containerNumber)}?{BuildSourceScanQuery(
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            resolution,
            page,
            pageSize)}";

    public static string BuildIcumsPagedPath(string containerNumber, int page = 1, int pageSize = 50)
        => $"{BasePath}/icums/{Escape(containerNumber)}?page={page}&pageSize={pageSize}";

    public static string BuildIcumsFullPath(string containerNumber)
        => $"{BasePath}/icums/{Escape(containerNumber)}?full=true";

    public static string BuildImagesPath(string containerNumber)
        => $"{BasePath}/images/{Escape(containerNumber)}";

    public static string BuildImagesWithQueryPath(
        string containerNumber,
        string? groupIdentifier,
        int? analysisRecordId,
        Guid? splitJobId,
        ScanAssetResolution? resolution = null)
        => $"{BasePath}/images/{Escape(containerNumber)}?{BuildSourceScanQuery(
            containerNumber,
            groupIdentifier,
            analysisRecordId,
            splitJobId,
            resolution)}";

    public static string BuildImageByIdPath(int imageId)
        => $"{BasePath}/image/{imageId}";

    public static string BuildSearchPath()
        => $"{BasePath}/search";

    public static string BuildPredictiveCacheContainerPath(string containerNumber)
        => $"{PredictiveCacheBasePath}/container/{Escape(containerNumber)}";

    public static string BuildImageAnalysisGroupScannerPath(
        string groupIdentifier,
        int page = 1,
        int pageSize = 1000)
        => BuildImageAnalysisGroupEndpointPath("scanner", groupIdentifier, page, pageSize);

    public static string BuildImageAnalysisGroupIcumsPath(
        string groupIdentifier,
        int page = 1,
        int pageSize = 1000)
        => BuildImageAnalysisGroupEndpointPath("icums", groupIdentifier, page, pageSize);

    public static string BuildImageAnalysisContainerScannerPath(
        string routeContainer,
        string declarationNumber,
        int page = 1,
        int pageSize = 1000)
        => BuildImageAnalysisContainerEndpointPath("scanner", routeContainer, declarationNumber, page, pageSize);

    public static string BuildImageAnalysisContainerIcumsPath(
        string routeContainer,
        string declarationNumber,
        int page = 1,
        int pageSize = 1000)
        => BuildImageAnalysisContainerEndpointPath("icums", routeContainer, declarationNumber, page, pageSize);

    public static string BuildImageAnalysisRecordIcumsPath(
        string routeContainer,
        int? recordCompletenessStatusId,
        string? recordKey,
        int page = 1,
        int pageSize = 1000)
    {
        var parts = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (recordCompletenessStatusId.HasValue)
        {
            parts.Add($"recordCompletenessStatusId={recordCompletenessStatusId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(recordKey))
        {
            parts.Add($"recordKey={Escape(recordKey.Trim())}");
        }

        return $"{BasePath}/icums/{Escape(routeContainer)}?{string.Join("&", parts)}";
    }

    public static string BuildImageAnalysisGroupEndpointPath(
        string endpoint,
        string groupIdentifier,
        int page = 1,
        int pageSize = 1000)
        => $"{BasePath}/{endpoint}/{Escape(groupIdentifier)}?page={page}&pageSize={pageSize}";

    public static string BuildImageAnalysisContainerEndpointPath(
        string endpoint,
        string routeContainer,
        string declarationNumber,
        int page = 1,
        int pageSize = 1000)
        => $"{BasePath}/{endpoint}/{Escape(routeContainer)}?page={page}&pageSize={pageSize}&declarationNumber={Escape(declarationNumber)}";

    public static string BuildSourceScanQuery(
        string? containerNumber,
        string? groupIdentifier,
        int? analysisRecordId,
        Guid? splitJobId,
        ScanAssetResolution? resolution = null,
        int? page = null,
        int? pageSize = null,
        bool includeContainerWhenEmpty = true)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(containerNumber) || includeContainerWhenEmpty)
        {
            parts.Add($"containerNumber={Escape(containerNumber ?? string.Empty)}");
        }

        if (!string.IsNullOrWhiteSpace(groupIdentifier))
        {
            parts.Add($"groupIdentifier={Escape(groupIdentifier.Trim())}");
        }

        var effectiveAnalysisRecordId = analysisRecordId ?? resolution?.AnalysisRecordId;
        if (effectiveAnalysisRecordId.HasValue)
        {
            parts.Add($"analysisRecordId={effectiveAnalysisRecordId.Value}");
        }

        var effectiveSourceScanId = resolution?.EffectiveSourceScanId;
        if (!string.IsNullOrWhiteSpace(effectiveSourceScanId))
        {
            parts.Add($"sourceScanId={Escape(effectiveSourceScanId)}");
        }

        var effectiveSplitJobId = splitJobId ?? resolution?.SplitJobId;
        if (effectiveSplitJobId.HasValue)
        {
            parts.Add($"splitJobId={effectiveSplitJobId.Value}");
        }

        if (resolution?.SplitResultId.HasValue == true)
        {
            parts.Add($"splitResultId={resolution.SplitResultId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(resolution?.EffectiveSplitSide))
        {
            parts.Add($"side={Escape(resolution.EffectiveSplitSide!)}");
        }

        if (page.HasValue)
        {
            parts.Add($"page={page.Value}");
        }

        if (pageSize.HasValue)
        {
            parts.Add($"pageSize={pageSize.Value}");
        }

        return string.Join("&", parts);
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value);
}
