namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Abstraction for AI model inference. Implementations can target Claude Vision,
    /// OpenAI GPT-4V, on-prem models, or the built-in stub.
    /// </summary>
    public interface IAiModelProvider
    {
        string ProviderId { get; }
        string ModelId { get; }

        /// <summary>
        /// Analyze a scanner image and return a structured suggestion.
        /// </summary>
        Task<AiImageAnalysisResult> AnalyzeImageAsync(
            AiImageAnalysisRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>Check if the provider is configured and reachable.</summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    }

    public class AiImageAnalysisRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? GroupIdentifier { get; set; }

        /// <summary>Base64-encoded image bytes, or null if only URL is available.</summary>
        public string? ImageBase64 { get; set; }

        /// <summary>Image media type (e.g., "image/png", "image/jpeg").</summary>
        public string MediaType { get; set; } = "image/png";

        /// <summary>Image URL if base64 is not available.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>Additional context for the model (metadata, prior decisions on similar containers).</summary>
        public string? ContextNotes { get; set; }
    }

    public class AiImageAnalysisResult
    {
        public bool Success { get; set; }

        /// <summary>"Normal", "Abnormal", or null if model declines to suggest.</summary>
        public string? SuggestedDecision { get; set; }

        /// <summary>0.0 to 1.0 confidence score.</summary>
        public double? Confidence { get; set; }

        /// <summary>Structured payload: ROI boxes, reasoning, rank score.</summary>
        public AiSuggestionPayload? Payload { get; set; }

        /// <summary>Model's reasoning text for the analyst.</summary>
        public string? Reasoning { get; set; }

        /// <summary>Error message if inference failed.</summary>
        public string? Error { get; set; }

        /// <summary>Model identifier that produced this result.</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>Model version.</summary>
        public string ModelVersion { get; set; } = string.Empty;

        /// <summary>Inference latency in milliseconds.</summary>
        public long LatencyMs { get; set; }
    }

    public class AiSuggestionPayload
    {
        public string Kind { get; set; } = "vision-assist";
        public List<RegionOfInterest> RegionsOfInterest { get; set; } = new();
        public int RankScore { get; set; }
        public string? Summary { get; set; }
        public string? Reasoning { get; set; }
        public List<string> Observations { get; set; } = new();
    }

    public class RegionOfInterest
    {
        /// <summary>Normalized coordinates (0.0-1.0 relative to image dimensions).</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public string Label { get; set; } = string.Empty;
        public double? Confidence { get; set; }
    }
}
