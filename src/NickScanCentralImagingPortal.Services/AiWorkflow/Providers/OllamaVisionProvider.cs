using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.AiWorkflow.Providers
{
    /// <summary>
    /// Ollama local vision model provider — runs models like Moondream and LLaVA locally.
    /// Zero cost, no data leaves the network.
    /// </summary>
    public class OllamaVisionProvider : IAiModelProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaVisionProvider> _logger;
        private readonly AiWorkflowOptions _options;

        public string ProviderId => "ollama-vision";
        public string ModelId => _options.OllamaModelId;

        public OllamaVisionProvider(
            HttpClient httpClient,
            ILogger<OllamaVisionProvider> logger,
            IOptions<AiWorkflowOptions> options)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options.Value;
            _httpClient.BaseAddress = new Uri(_options.OllamaBaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(_options.InferenceTimeoutSeconds > 0 ? _options.InferenceTimeoutSeconds : 300);
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<AiImageAnalysisResult> AnalyzeImageAsync(
            AiImageAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var prompt = $"Analyze this X-ray cargo scan image of container {request.ContainerNumber} ({request.ScannerType}). " +
                    "Is the cargo Normal (expected contents matching declaration) or Abnormal (suspicious density, hidden items, mismatch)? " +
                    "Respond with: decision (Normal or Abnormal), confidence (0-100%), reasoning, and any regions of interest.";

                var payload = new
                {
                    model = _options.OllamaModelId,
                    prompt,
                    images = string.IsNullOrEmpty(request.ImageBase64) ? Array.Empty<string>() : new[] { request.ImageBase64 },
                    stream = false
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/generate", content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new AiImageAnalysisResult
                    {
                        Success = false,
                        Error = $"Ollama returned {response.StatusCode}: {errorBody}",
                        ModelId = ModelId
                    };
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);
                var responseText = doc.RootElement.TryGetProperty("response", out var resp)
                    ? resp.GetString() ?? "" : "";

                return ParseResponse(responseText);
            }
            catch (TaskCanceledException)
            {
                return new AiImageAnalysisResult { Success = false, Error = "Ollama inference timed out", ModelId = ModelId };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[OLLAMA] Inference failed for {Container}", request.ContainerNumber);
                return new AiImageAnalysisResult { Success = false, Error = ex.Message, ModelId = ModelId };
            }
        }

        private AiImageAnalysisResult ParseResponse(string text)
        {
            var lower = text.ToLowerInvariant();
            string? decision = null;
            double? confidence = null;

            if (lower.Contains("abnormal") || lower.Contains("suspicious") || lower.Contains("alarm"))
                decision = "Abnormal";
            else if (lower.Contains("normal") || lower.Contains("clear") || lower.Contains("no threat"))
                decision = "Normal";

            // Try to extract confidence from patterns like "85%", "confidence: 0.85"
            var confMatch = System.Text.RegularExpressions.Regex.Match(text, @"(\d{1,3})%");
            if (confMatch.Success && int.TryParse(confMatch.Groups[1].Value, out var pct))
                confidence = pct / 100.0;

            return new AiImageAnalysisResult
            {
                Success = true,
                SuggestedDecision = decision,
                Confidence = confidence,
                Reasoning = text,
                ModelId = ModelId
            };
        }
    }
}
