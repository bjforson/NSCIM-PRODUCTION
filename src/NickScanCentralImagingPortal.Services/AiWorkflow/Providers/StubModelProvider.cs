using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.AiWorkflow.Providers
{
    /// <summary>
    /// Fallback provider that returns placeholder suggestions when no real model is configured.
    /// </summary>
    public class StubModelProvider : IAiModelProvider
    {
        public string ProviderId => "stub";
        public string ModelId => "stub-v1";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<AiImageAnalysisResult> AnalyzeImageAsync(
            AiImageAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var rankScore = 50 + Math.Abs(request.ContainerNumber.GetHashCode()) % 50;

            var payload = new AiSuggestionPayload
            {
                Kind = "stub-assist",
                RegionsOfInterest = new List<RegionOfInterest>
                {
                    new() { X = 0.1, Y = 0.1, W = 0.3, H = 0.4, Label = "review-area" }
                },
                RankScore = rankScore,
                Summary = "Stub model — no real analysis performed.",
                Reasoning = "This is a placeholder. Configure a real AI provider (Claude Vision, etc.) in AiWorkflow settings.",
                Observations = new List<string> { "Placeholder observation — replace with real model." }
            };

            return Task.FromResult(new AiImageAnalysisResult
            {
                Success = true,
                SuggestedDecision = null,
                Confidence = null,
                Payload = payload,
                Reasoning = payload.Reasoning,
                ModelId = ModelId,
                ModelVersion = "1",
                LatencyMs = 0
            });
        }
    }
}
