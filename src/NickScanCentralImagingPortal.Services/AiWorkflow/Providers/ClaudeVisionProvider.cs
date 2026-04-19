using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.AiWorkflow.Providers
{
    /// <summary>
    /// Calls Claude's vision API to analyze scanner images.
    /// Requires AiWorkflow:ClaudeApiKey in configuration.
    /// </summary>
    public class ClaudeVisionProvider : IAiModelProvider
    {
        private readonly HttpClient _http;
        private readonly AiWorkflowOptions _options;
        private readonly ILogger<ClaudeVisionProvider> _logger;

        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string DefaultModel = "claude-sonnet-4-20250514";

        public string ProviderId => "claude-vision";
        public string ModelId => _options.ClaudeModelId ?? DefaultModel;

        public ClaudeVisionProvider(
            HttpClient http,
            IOptions<AiWorkflowOptions> options,
            ILogger<ClaudeVisionProvider> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrWhiteSpace(_options.ClaudeApiKey)
                   && _options.Enabled
                   && _options.ImageAssistEnabled;
        }

        public async Task<AiImageAnalysisResult> AnalyzeImageAsync(
            AiImageAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(_options.ClaudeApiKey))
            {
                return new AiImageAnalysisResult
                {
                    Success = false,
                    Error = "Claude API key not configured.",
                    ModelId = ModelId,
                    ModelVersion = "1"
                };
            }

            try
            {
                var systemPrompt = BuildSystemPrompt();
                var userContent = BuildUserContent(request);

                var body = new
                {
                    model = ModelId,
                    max_tokens = 1024,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userContent }
                    }
                };

                var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                using var httpReq = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                httpReq.Headers.Add("x-api-key", _options.ClaudeApiKey);
                httpReq.Headers.Add("anthropic-version", "2023-06-01");
                httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(httpReq, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Claude API returned {Status}: {Body}",
                        response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

                    return new AiImageAnalysisResult
                    {
                        Success = false,
                        Error = $"Claude API error: {response.StatusCode}",
                        ModelId = ModelId,
                        ModelVersion = "1",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return ParseResponse(responseBody, request, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Claude Vision inference failed for {Container}", request.ContainerNumber);
                return new AiImageAnalysisResult
                {
                    Success = false,
                    Error = ex.Message,
                    ModelId = ModelId,
                    ModelVersion = "1",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
        }

        private static string BuildSystemPrompt()
        {
            return @"You are an expert customs X-ray image analyst assistant. You analyze cargo scanner images (FS6000, ASE, Nuctech) for anomalies.

Your role is ADVISORY ONLY. A human analyst makes the final decision. You provide:
1. A suggested assessment: ""Normal"" or ""Abnormal""
2. A confidence score from 0.0 to 1.0
3. Observations about what you see
4. Regions of interest (normalized coordinates 0.0-1.0) for any areas the analyst should examine closely

Respond ONLY with valid JSON in this exact format:
{
  ""decision"": ""Normal"" or ""Abnormal"",
  ""confidence"": 0.0 to 1.0,
  ""observations"": [""observation 1"", ""observation 2""],
  ""regionsOfInterest"": [{""x"": 0.0, ""y"": 0.0, ""w"": 0.0, ""h"": 0.0, ""label"": ""description"", ""confidence"": 0.0}],
  ""reasoning"": ""brief explanation""
}

Focus on: density anomalies, concealed compartments, inconsistent cargo patterns, undeclared items, suspicious voids or modifications. If the image is unclear or you cannot make a determination, set decision to null and explain in reasoning.";
        }

        private static object[] BuildUserContent(AiImageAnalysisRequest request)
        {
            var content = new List<object>();

            if (!string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = request.MediaType,
                        data = request.ImageBase64
                    }
                });
            }

            var textPrompt = $"Analyze this cargo scanner image.\nContainer: {request.ContainerNumber}\nScanner: {request.ScannerType}";
            if (!string.IsNullOrWhiteSpace(request.ContextNotes))
                textPrompt += $"\nContext: {request.ContextNotes}";

            content.Add(new { type = "text", text = textPrompt });

            return content.ToArray();
        }

        private AiImageAnalysisResult ParseResponse(string responseBody, AiImageAnalysisRequest request, long latencyMs)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // Extract text from Claude's response
                var textContent = "";
                if (root.TryGetProperty("content", out var contentArray))
                {
                    foreach (var block in contentArray.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                            && block.TryGetProperty("text", out var txt))
                        {
                            textContent = txt.GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(textContent))
                {
                    return new AiImageAnalysisResult
                    {
                        Success = false,
                        Error = "Empty response from Claude",
                        ModelId = ModelId,
                        ModelVersion = "1",
                        LatencyMs = latencyMs
                    };
                }

                // Extract JSON from the response (may be wrapped in markdown code block)
                var jsonStr = textContent.Trim();
                if (jsonStr.StartsWith("```"))
                {
                    var start = jsonStr.IndexOf('{');
                    var end = jsonStr.LastIndexOf('}');
                    if (start >= 0 && end > start)
                        jsonStr = jsonStr[start..(end + 1)];
                }

                using var parsed = JsonDocument.Parse(jsonStr);
                var r = parsed.RootElement;

                string? decision = null;
                if (r.TryGetProperty("decision", out var d) && d.ValueKind == JsonValueKind.String)
                    decision = d.GetString();

                double? confidence = null;
                if (r.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number)
                    confidence = c.GetDouble();

                var observations = new List<string>();
                if (r.TryGetProperty("observations", out var obs) && obs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in obs.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String)
                            observations.Add(o.GetString()!);
                }

                var rois = new List<RegionOfInterest>();
                if (r.TryGetProperty("regionsOfInterest", out var roiArr) && roiArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var roi in roiArr.EnumerateArray())
                    {
                        rois.Add(new RegionOfInterest
                        {
                            X = roi.TryGetProperty("x", out var rx) ? rx.GetDouble() : 0,
                            Y = roi.TryGetProperty("y", out var ry) ? ry.GetDouble() : 0,
                            W = roi.TryGetProperty("w", out var rw) ? rw.GetDouble() : 0,
                            H = roi.TryGetProperty("h", out var rh) ? rh.GetDouble() : 0,
                            Label = roi.TryGetProperty("label", out var rl) ? rl.GetString() ?? "" : "",
                            Confidence = roi.TryGetProperty("confidence", out var rc) && rc.ValueKind == JsonValueKind.Number ? rc.GetDouble() : null
                        });
                    }
                }

                string? reasoning = null;
                if (r.TryGetProperty("reasoning", out var reas) && reas.ValueKind == JsonValueKind.String)
                    reasoning = reas.GetString();

                var payload = new AiSuggestionPayload
                {
                    Kind = "vision-assist",
                    RegionsOfInterest = rois,
                    RankScore = confidence.HasValue ? (int)(confidence.Value * 100) : 50,
                    Summary = decision != null ? $"AI suggests: {decision}" : "AI could not determine",
                    Reasoning = reasoning,
                    Observations = observations
                };

                _logger.LogInformation(
                    "Claude Vision analyzed {Container}: decision={Decision}, confidence={Confidence:F2}, ROIs={RoiCount}, latency={Ms}ms",
                    request.ContainerNumber, decision, confidence, rois.Count, latencyMs);

                return new AiImageAnalysisResult
                {
                    Success = true,
                    SuggestedDecision = decision,
                    Confidence = confidence,
                    Payload = payload,
                    Reasoning = reasoning,
                    ModelId = ModelId,
                    ModelVersion = "1",
                    LatencyMs = latencyMs
                };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse Claude Vision JSON for {Container}", request.ContainerNumber);
                return new AiImageAnalysisResult
                {
                    Success = false,
                    Error = $"Failed to parse model response: {ex.Message}",
                    Reasoning = responseBody.Length > 500 ? responseBody[..500] : responseBody,
                    ModelId = ModelId,
                    ModelVersion = "1",
                    LatencyMs = latencyMs
                };
            }
        }
    }
}
