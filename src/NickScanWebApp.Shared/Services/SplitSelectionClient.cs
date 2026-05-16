using System.Text.Json.Serialization;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services;

public sealed class SplitSelectionClient
{
    private readonly ApiService _apiService;

    public SplitSelectionClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<ImageAnalysisSplitOptionsResponse?> TryGetContainerOptionsAsync(string containerNumber)
    {
        return _apiService.TryGetAsync<ImageAnalysisSplitOptionsResponse>(
            BuildContainerSplitOptionsPath(containerNumber));
    }

    public Task<ImageAnalysisSplitOptionsResponse?> TryGetAnalysisRecordOptionsAsync(int analysisRecordId)
    {
        return _apiService.TryGetAsync<ImageAnalysisSplitOptionsResponse>(
            BuildAnalysisRecordSplitOptionsPath(analysisRecordId));
    }

    public Task<ImageAnalysisSplitOptionsResponse?> TryGetSplitterRecordOptionsAsync(int analysisRecordId)
    {
        return _apiService.TryGetAsync<ImageAnalysisSplitOptionsResponse>(
            BuildSplitterRecordSplitOptionsPath(analysisRecordId));
    }

    public Task<ImageAnalysisSplitOptionsResponse?> TryGetJobOptionsAsync(Guid splitJobId, string containerNumber)
    {
        return _apiService.TryGetAsync<ImageAnalysisSplitOptionsResponse>(
            BuildJobSplitOptionsPath(splitJobId, containerNumber));
    }

    public async Task ChooseSplitAsync(SplitSelectionChoiceCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ResultId);

        var payload = new ChooseSplitPayload
        {
            ResultId = command.ResultId,
            ApprovedBy = string.IsNullOrWhiteSpace(command.ApprovedBy) ? "unknown" : command.ApprovedBy,
            SplitJobId = command.SplitJobId?.ToString(),
            SourceScanId = command.SourceScanId,
            ContainerNumber = command.ContainerNumber
        };

        var saved = false;
        if (command.AnalysisRecordId.HasValue)
        {
            saved = await _apiService.TryPostAsync(
                BuildAnalysisRecordChoosePath(command.AnalysisRecordId.Value),
                payload);
        }

        if (!saved && command.SplitJobId.HasValue)
        {
            saved = await _apiService.TryPostAsync(
                BuildJobChoosePath(command.SplitJobId.Value),
                payload);
        }

        if (!saved)
        {
            var containerNumber = RequireContainerNumber(command.FallbackContainerNumber ?? command.ContainerNumber);
            await _apiService.PostAsync<ChooseSplitPayload, object>(
                BuildContainerChoosePath(containerNumber),
                payload);
        }
    }

    public async Task SkipSplitAsync(SplitSelectionSkipCommand command)
    {
        var payload = new SkipSplitPayload
        {
            SkippedBy = string.IsNullOrWhiteSpace(command.SkippedBy) ? "unknown" : command.SkippedBy,
            SplitJobId = command.SplitJobId?.ToString(),
            SourceScanId = command.SourceScanId,
            ContainerNumber = command.ContainerNumber
        };

        var saved = false;
        if (command.AnalysisRecordId.HasValue)
        {
            saved = await _apiService.TryPostAsync(
                BuildAnalysisRecordSkipPath(command.AnalysisRecordId.Value),
                payload);
        }

        if (!saved && command.SplitJobId.HasValue)
        {
            saved = await _apiService.TryPostAsync(
                BuildJobSkipPath(command.SplitJobId.Value),
                payload);
        }

        if (!saved)
        {
            var containerNumber = RequireContainerNumber(command.FallbackContainerNumber ?? command.ContainerNumber);
            await _apiService.PostAsync<SkipSplitPayload, object>(
                BuildContainerSkipPath(containerNumber),
                payload);
        }
    }

    public static string BuildContainerSplitOptionsPath(string containerNumber)
    {
        return $"/api/image-splitter/container/{Uri.EscapeDataString(containerNumber)}/split-options";
    }

    public static string BuildAnalysisRecordSplitOptionsPath(int analysisRecordId)
    {
        return $"/api/image-analysis/records/{analysisRecordId}/split-options";
    }

    public static string BuildSplitterRecordSplitOptionsPath(int analysisRecordId)
    {
        return $"/api/image-splitter/records/{analysisRecordId}/split-options";
    }

    public static string BuildJobSplitOptionsPath(Guid splitJobId, string containerNumber)
    {
        return $"/api/image-splitter/jobs/{splitJobId}/split-options?containerNumber={Uri.EscapeDataString(containerNumber)}";
    }

    public static string BuildOriginalImagePath(Guid splitJobId)
    {
        return $"/api/image-splitter/jobs/{splitJobId}/original";
    }

    public static string BuildResultImagePath(Guid splitJobId, string resultId, string side, bool lossless)
    {
        var kind = lossless ? "lossless" : "image";
        return $"/api/image-splitter/jobs/{splitJobId}/results/{Uri.EscapeDataString(resultId)}/{kind}/{Uri.EscapeDataString(side)}";
    }

    private static string BuildAnalysisRecordChoosePath(int analysisRecordId)
    {
        return $"/api/image-analysis/records/{analysisRecordId}/choose-split";
    }

    private static string BuildJobChoosePath(Guid splitJobId)
    {
        return $"/api/image-splitter/jobs/{splitJobId}/choose-split";
    }

    private static string BuildContainerChoosePath(string containerNumber)
    {
        return $"/api/image-splitter/container/{Uri.EscapeDataString(containerNumber)}/choose-split";
    }

    private static string BuildAnalysisRecordSkipPath(int analysisRecordId)
    {
        return $"/api/image-analysis/records/{analysisRecordId}/skip-split";
    }

    private static string BuildJobSkipPath(Guid splitJobId)
    {
        return $"/api/image-splitter/jobs/{splitJobId}/skip-split";
    }

    private static string BuildContainerSkipPath(string containerNumber)
    {
        return $"/api/image-splitter/container/{Uri.EscapeDataString(containerNumber)}/skip-split";
    }

    private static string RequireContainerNumber(string? containerNumber)
    {
        if (string.IsNullOrWhiteSpace(containerNumber))
        {
            throw new InvalidOperationException("A container number is required for the split selection compatibility route.");
        }

        return containerNumber;
    }

    private sealed class ChooseSplitPayload
    {
        [JsonPropertyName("resultId")]
        public string ResultId { get; set; } = string.Empty;

        [JsonPropertyName("approvedBy")]
        public string ApprovedBy { get; set; } = "unknown";

        [JsonPropertyName("splitJobId")]
        public string? SplitJobId { get; set; }

        [JsonPropertyName("sourceScanId")]
        public string? SourceScanId { get; set; }

        [JsonPropertyName("containerNumber")]
        public string? ContainerNumber { get; set; }
    }

    private sealed class SkipSplitPayload
    {
        [JsonPropertyName("skippedBy")]
        public string SkippedBy { get; set; } = "unknown";

        [JsonPropertyName("splitJobId")]
        public string? SplitJobId { get; set; }

        [JsonPropertyName("sourceScanId")]
        public string? SourceScanId { get; set; }

        [JsonPropertyName("containerNumber")]
        public string? ContainerNumber { get; set; }
    }
}

public sealed class SplitSelectionChoiceCommand
{
    public int? AnalysisRecordId { get; set; }
    public Guid? SplitJobId { get; set; }
    public string? ContainerNumber { get; set; }
    public string? FallbackContainerNumber { get; set; }
    public string? SourceScanId { get; set; }
    public string ResultId { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = "unknown";
}

public sealed class SplitSelectionSkipCommand
{
    public int? AnalysisRecordId { get; set; }
    public Guid? SplitJobId { get; set; }
    public string? ContainerNumber { get; set; }
    public string? FallbackContainerNumber { get; set; }
    public string? SourceScanId { get; set; }
    public string SkippedBy { get; set; } = "unknown";
}
