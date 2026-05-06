using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.AiCargoSummary
{
    /// <summary>
    /// Polls for AnalysisGroups that have transitioned to Status='Completed'
    /// but have no AiCargoSummary, generates one via Ollama, and persists it.
    /// Runs in the background so analysts no longer have to click
    /// "Generate Summary" by hand on every wave.
    ///
    /// Auto-back-off when Ollama is unreachable (no install, service stopped,
    /// model not loaded): the loop pauses generations for 5 minutes after a
    /// failure, then retries. Per-AG failures are logged but never crash the
    /// loop. The component is opt-out via AiWorkflow:AutoGenerateSummaries=false.
    /// </summary>
    public class AiCargoSummaryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiCargoSummaryBackgroundService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);
        private readonly TimeSpan _ollamaBackoff = TimeSpan.FromMinutes(5);
        private DateTime _ollamaUnreachableUntil = DateTime.MinValue;
        private const int BatchSize = 5;
        private const int MaxSummaryChars = 2000;

        public AiCargoSummaryBackgroundService(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AiCargoSummaryBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("AiWorkflow:AutoGenerateSummaries", true);
            if (!enabled)
            {
                _logger.LogInformation("[AI-SUMMARY-AUTO] disabled by AiWorkflow:AutoGenerateSummaries=false");
                return;
            }

            _logger.LogInformation("[AI-SUMMARY-AUTO] background service starting (interval={Interval}, batch={Batch})",
                _interval, BatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (DateTime.UtcNow >= _ollamaUnreachableUntil)
                    {
                        await RunOnceAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI-SUMMARY-AUTO] unexpected error in loop (continuing)");
                }

                try { await Task.Delay(_interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icum = scope.ServiceProvider.GetRequiredService<IcumDbContext>();
            var cargoService = scope.ServiceProvider.GetRequiredService<ICargoGroupService>();

            var ags = await db.AnalysisGroups
                .AsTracking()
                .Where(g => g.Status == "Completed"
                            && (g.AiCargoSummary == null || g.AiCargoSummary == ""))
                .OrderByDescending(g => g.UpdatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (ags.Count == 0) return;

            _logger.LogDebug("[AI-SUMMARY-AUTO] {Count} AG(s) need summaries this cycle", ags.Count);

            foreach (var ag in ags)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await GenerateForGroupAsync(db, icum, cargoService, ag, ct);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(
                        "[AI-SUMMARY-AUTO] Ollama unreachable for AG {AgId} ({Reason}). Backing off {Backoff}.",
                        ag.Id, ex.Message, _ollamaBackoff);
                    _ollamaUnreachableUntil = DateTime.UtcNow + _ollamaBackoff;
                    return;
                }
                catch (TaskCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning("[AI-SUMMARY-AUTO] Ollama timed out for AG {AgId}: {Msg}. Backing off {Backoff}.",
                        ag.Id, ex.Message, _ollamaBackoff);
                    _ollamaUnreachableUntil = DateTime.UtcNow + _ollamaBackoff;
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AI-SUMMARY-AUTO] generation failed for AG {AgId} (will retry next cycle)", ag.Id);
                }
            }
        }

        private async Task GenerateForGroupAsync(
            ApplicationDbContext db,
            IcumDbContext icum,
            ICargoGroupService cargoService,
            Core.Entities.Analysis.AnalysisGroup ag,
            CancellationToken ct)
        {
            // Pull the containers in this AG
            var containerNumbers = await db.AnalysisRecords
                .AsNoTracking()
                .Where(r => r.GroupId == ag.Id)
                .Select(r => r.ContainerNumber)
                .ToListAsync(ct);
            if (containerNumbers.Count == 0)
            {
                _logger.LogDebug("[AI-SUMMARY-AUTO] AG {AgId} has no AnalysisRecords; skipping", ag.Id);
                return;
            }

            // Pull ICUMS metadata for those containers
            var icumRows = await icum.IcumContainerData
                .AsNoTracking()
                .Where(i => containerNumbers.Contains(i.ContainerNumber))
                .Select(i => new
                {
                    i.ContainerNumber,
                    i.ConsigneeName,
                    i.ShipperName,
                    i.CountryOfOrigin,
                    i.MasterBlNumber,
                    i.HouseBl,
                    i.DeclarationNumber,
                    i.ContainerWeight,
                    i.ContainerQuantity,
                    i.ClearanceType,
                    i.BoeData
                })
                .ToListAsync(ct);

            // Decision counts so the summary reflects the actual analyst verdict
            var decisions = await db.ImageAnalysisDecisions
                .AsNoTracking()
                .Where(d => containerNumbers.Contains(d.ContainerNumber))
                .Select(d => d.Decision)
                .ToListAsync(ct);
            int normal = decisions.Count(d => string.Equals(d, "Normal", StringComparison.OrdinalIgnoreCase));
            int abnormal = decisions.Count(d => string.Equals(d, "Abnormal", StringComparison.OrdinalIgnoreCase));

            var prompt = BuildPrompt(ag, containerNumbers, icumRows.Cast<object>().ToList(), normal, abnormal);

            var ollamaUrl = _configuration["AiWorkflow:OllamaBaseUrl"] ?? "http://localhost:11434";
            var modelId = _configuration["AiWorkflow:OllamaTextModelId"]
                          ?? _configuration["AiWorkflow:OllamaModelId"]
                          ?? "llava:7b";

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            var body = JsonSerializer.Serialize(new { model = modelId, prompt, stream = false });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await client.PostAsync($"{ollamaUrl}/api/generate", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[AI-SUMMARY-AUTO] Ollama returned {Status} for AG {AgId}: {Body}",
                    resp.StatusCode, ag.Id, errBody.Length > 200 ? errBody[..200] : errBody);

                if ((int)resp.StatusCode == 503 || (int)resp.StatusCode == 504)
                {
                    _ollamaUnreachableUntil = DateTime.UtcNow + _ollamaBackoff;
                }
                return;
            }

            var respBody = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respBody);
            var summaryText = doc.RootElement.TryGetProperty("response", out var respProp)
                ? respProp.GetString()?.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(summaryText))
            {
                _logger.LogDebug("[AI-SUMMARY-AUTO] Ollama returned empty summary for AG {AgId}; skipping", ag.Id);
                return;
            }

            if (summaryText.Length > MaxSummaryChars) summaryText = summaryText[..MaxSummaryChars];

            // Save via the existing service so the same length truncation /
            // lookup semantics are used as the user-triggered path.
            await cargoService.SaveAiCargoSummaryAsync(ag.GroupIdentifier, summaryText);
            _logger.LogInformation(
                "[AI-SUMMARY-AUTO] persisted summary for AG {AgId} (gid={GroupIdentifier}, {Chars} chars, {Containers} containers, {Normal}N/{Abnormal}A)",
                ag.Id, ag.GroupIdentifier, summaryText.Length, containerNumbers.Count, normal, abnormal);
        }

        private string BuildPrompt(
            Core.Entities.Analysis.AnalysisGroup ag,
            IReadOnlyList<string> containerNumbers,
            IReadOnlyList<object> icumRows,
            int normal,
            int abnormal)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a customs cargo analyst. Generate a concise 2-3 sentence summary of this scanned cargo wave for an analyst dashboard.");
            sb.AppendLine("Focus on what's distinctive: who's importing, what's being shipped if known, country of origin, and the analyst verdict pattern.");
            sb.AppendLine();
            sb.AppendLine($"Group: {ag.GroupIdentifier} (wave {ag.WaveNumber}, {containerNumbers.Count} containers)");
            sb.AppendLine($"Analyst verdicts: {normal} Normal / {abnormal} Abnormal");

            // Surface the most-common consignee/shipper/origin
            var consignees = icumRows
                .Select(r => GetProp(r, "ConsigneeName"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault();
            var shippers = icumRows
                .Select(r => GetProp(r, "ShipperName"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault();
            var origins = icumRows
                .Select(r => GetProp(r, "CountryOfOrigin"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault();

            if (consignees != null) sb.AppendLine($"Consignee: {consignees.Key}");
            if (shippers != null) sb.AppendLine($"Shipper: {shippers.Key}");
            if (origins != null) sb.AppendLine($"Country of origin: {origins.Key}");

            // Pull a few cargo descriptions out of boedata if present
            var descriptions = new List<string>();
            foreach (var row in icumRows)
            {
                var boeText = GetProp(row, "BoeData");
                if (string.IsNullOrWhiteSpace(boeText)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(boeText);
                    if (doc.RootElement.TryGetProperty("ContainerDescription", out var desc) && desc.ValueKind == JsonValueKind.String)
                    {
                        var s = desc.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) descriptions.Add(s);
                    }
                }
                catch { /* boedata not parseable JSON for this row */ }
            }
            if (descriptions.Count > 0)
            {
                sb.AppendLine("Cargo descriptions:");
                foreach (var d in descriptions.Distinct().Take(5)) sb.AppendLine($"  - {d}");
            }

            sb.AppendLine();
            sb.AppendLine("Containers in wave: " + string.Join(", ", containerNumbers.Take(20)));
            sb.AppendLine();
            sb.AppendLine("Summary:");
            return sb.ToString();
        }

        private static string? GetProp(object obj, string name)
        {
            var prop = obj.GetType().GetProperty(name);
            return prop?.GetValue(obj)?.ToString();
        }
    }
}
