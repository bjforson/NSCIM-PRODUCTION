using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.DTOs.AiWorkflow;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiWorkflow
{
    /// <summary>
    /// Phase 1: ops triage over recent error investigations.
    /// Uses Claude text API if configured, falls back to heuristic rules.
    /// </summary>
    public class OpsLogTriageService : IOpsLogTriageService
    {
        private readonly ApplicationDbContext _db;
        private readonly AiWorkflowOptions _options;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<OpsLogTriageService> _logger;

        public OpsLogTriageService(
            ApplicationDbContext db,
            IOptions<AiWorkflowOptions> options,
            IHttpClientFactory httpFactory,
            ILogger<OpsLogTriageService> logger)
        {
            _db = db;
            _options = options.Value;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task<OpsTriageResultDto> TriageRecentAsync(int maxItems, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || !_options.OpsTriageEnabled)
                return new OpsTriageResultDto { Enabled = false, Message = "AiWorkflow.OpsTriageEnabled is false" };

            maxItems = maxItems <= 0 ? 15 : Math.Min(maxItems, 100);

            var rows = await _db.ErrorInvestigations
                .AsNoTracking()
                .OrderByDescending(e => e.LastSeen)
                .Take(maxItems)
                .Select(e => new ErrorRow
                {
                    Id = e.Id,
                    GroupId = e.InvestigationGroupId,
                    ErrorCode = e.ErrorCode,
                    ServiceId = e.ServiceId,
                    Operation = e.Operation,
                    ExceptionType = e.ExceptionType,
                    ErrorPattern = e.ErrorPattern,
                    OccurrenceCount = e.OccurrenceCount,
                    LastSeen = e.LastSeen,
                    Status = e.Status,
                    Priority = e.Priority
                })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                return new OpsTriageResultDto
                {
                    Enabled = true,
                    Message = "No recent error investigations found.",
                    SummaryText = "System appears healthy — no recent errors."
                };
            }

            // Try LLM triage if Claude API key is configured
            if (!string.IsNullOrWhiteSpace(_options.ClaudeApiKey))
            {
                try
                {
                    return await LlmTriageAsync(rows, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM triage failed, falling back to heuristic");
                }
            }

            // Fallback: heuristic triage
            return HeuristicTriage(rows);
        }

        private async Task<OpsTriageResultDto> LlmTriageAsync(List<ErrorRow> rows, CancellationToken cancellationToken)
        {
            var errorSummary = new StringBuilder();
            errorSummary.AppendLine("Recent error investigations from NickScan/NSCIM production system:");
            errorSummary.AppendLine();
            foreach (var e in rows)
            {
                errorSummary.AppendLine($"- [{e.Priority}] Error: {e.ErrorCode} | Service: {e.ServiceId} | Op: {e.Operation}");
                errorSummary.AppendLine($"  Exception: {e.ExceptionType} | Count: {e.OccurrenceCount} | Last: {e.LastSeen:u} | Status: {e.Status}");
                if (!string.IsNullOrWhiteSpace(e.ErrorPattern))
                    errorSummary.AppendLine($"  Pattern: {(e.ErrorPattern.Length > 300 ? e.ErrorPattern[..300] + "..." : e.ErrorPattern)}");
                errorSummary.AppendLine();
            }

            var body = new
            {
                model = _options.ClaudeModelId ?? "claude-sonnet-4-20250514",
                max_tokens = 1500,
                system = @"You are an operations engineer assistant for NickScan/NSCIM, a customs cargo scanning and image analysis system running .NET 8, PostgreSQL, with FS6000/ASE scanners and ICUMS integration.

Analyze the error investigations and provide:
1. A brief summary of the overall system health
2. For each significant error cluster, provide:
   - Root cause hypothesis
   - Specific diagnostic steps (commands, queries, checks)
   - Recommended remediation
3. Priority ranking of which issues to address first

Be specific to this system's components: API service, scanner services (FS6000, ASE, Nuctech), ICUMS integration, image processing, container completeness, PostgreSQL database.

Format as structured text. Be concise and actionable.",
                messages = new[]
                {
                    new { role = "user", content = errorSummary.ToString() }
                }
            };

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var client = _httpFactory.CreateClient("AiVision");
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", _options.ClaudeApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(req, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Claude text API returned {Status} for ops triage", response.StatusCode);
                throw new InvalidOperationException($"Claude API error: {response.StatusCode}");
            }

            // Extract text from response
            var llmText = ExtractTextFromResponse(responseBody);

            // Build hints from both LLM and heuristic (belt and suspenders)
            var hints = rows.Select(e => new OpsTriageHintDto
            {
                InvestigationId = e.Id,
                GroupId = e.GroupId,
                ErrorCode = e.ErrorCode,
                SuggestedChecks = BuildSuggestedChecks(e.ExceptionType, e.Operation)
            }).ToList();

            _logger.LogInformation("LLM ops triage completed for {Count} errors", rows.Count);

            return new OpsTriageResultDto
            {
                Enabled = true,
                SummaryText = llmText,
                Hints = hints,
                Message = "LLM-assisted triage — verify recommendations in staging before acting in production.",
                Source = "claude"
            };
        }

        private OpsTriageResultDto HeuristicTriage(List<ErrorRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Recent error investigations (heuristic advisory):");
            foreach (var e in rows)
                sb.AppendLine($"- [{e.Priority}] {e.ErrorCode} x{e.OccurrenceCount} last {e.LastSeen:u} | {e.ServiceId} {e.Operation} | {e.Status}");

            var hints = rows.Select(e => new OpsTriageHintDto
            {
                InvestigationId = e.Id,
                GroupId = e.GroupId,
                ErrorCode = e.ErrorCode,
                SuggestedChecks = BuildSuggestedChecks(e.ExceptionType, e.Operation)
            }).ToList();

            return new OpsTriageResultDto
            {
                Enabled = true,
                SummaryText = sb.ToString(),
                Hints = hints,
                Message = "Heuristic triage only — validate in staging before acting in production.",
                Source = "heuristic"
            };
        }

        private string ExtractTextFromResponse(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("content", out var content))
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                            && block.TryGetProperty("text", out var txt))
                            return txt.GetString() ?? "";
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                // Round-1 audit C-3: previously silent. If Claude returns
                // malformed JSON, log it and fall through to the truncated
                // raw-body path — the heuristic fallback in the caller still
                // produces a useful triage even without parsed text.
                // (Both feature branches addressed C-3; this version uses the
                // class's existing _logger field rather than Debug.WriteLine.)
                _logger?.LogWarning(jsonEx,
                    "OpsLogTriage: Claude response body was not parseable JSON; falling back to raw text");
            }
            return responseBody.Length > 2000 ? responseBody[..2000] : responseBody;
        }

        private static IReadOnlyList<string> BuildSuggestedChecks(string? exceptionType, string? operation)
        {
            var list = new List<string>
            {
                "Confirm service health and recent deploys",
                "Check DB connectivity and pool exhaustion"
            };

            if (!string.IsNullOrEmpty(exceptionType))
            {
                if (exceptionType.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                    list.Add("Inspect command timeouts and slow queries for the reported operation");
                if (exceptionType.Contains("NpgsqlException", StringComparison.OrdinalIgnoreCase) ||
                    exceptionType.Contains("PostgresException", StringComparison.OrdinalIgnoreCase))
                    list.Add("Check PostgreSQL logs, connection pool stats, and active locks");
                if (exceptionType.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase))
                    list.Add("Verify outbound network connectivity and DNS resolution");
                if (exceptionType.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase))
                    list.Add("Check application memory usage, GC pressure, and image processing buffer sizes");
            }

            if (!string.IsNullOrEmpty(operation))
            {
                if (operation.Contains("ICUMS", StringComparison.OrdinalIgnoreCase))
                    list.Add("Verify ICUMS credentials, rate limits, and outbound network to ICUMS endpoint");
                if (operation.Contains("FS6000", StringComparison.OrdinalIgnoreCase))
                    list.Add("Check FS6000 scanner network share, staging folder, and file watcher service");
                if (operation.Contains("ASE", StringComparison.OrdinalIgnoreCase))
                    list.Add("Verify ASE scanner connectivity and data format compliance");
                if (operation.Contains("Image", StringComparison.OrdinalIgnoreCase))
                    list.Add("Check image storage paths, disk space, and ImageSharp processing pipeline");
            }

            return list;
        }

        private class ErrorRow
        {
            public long Id { get; set; }
            public string GroupId { get; set; } = "";
            public string? ErrorCode { get; set; }
            public string? ServiceId { get; set; }
            public string? Operation { get; set; }
            public string? ExceptionType { get; set; }
            public string? ErrorPattern { get; set; }
            public int OccurrenceCount { get; set; }
            public DateTime LastSeen { get; set; }
            public string? Status { get; set; }
            public string Priority { get; set; } = "Medium";
        }
    }

}
