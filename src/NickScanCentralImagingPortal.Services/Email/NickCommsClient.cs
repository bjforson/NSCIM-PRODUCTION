using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Email
{
    /// <summary>
    /// HTTP client implementation for NickComms.Gateway. Reads its base URL and API key from
    /// <see cref="ISettingsProvider"/> on every call so the admin settings page can hot-update
    /// configuration without an app restart. Falls back to environment variables when the
    /// settings DB is empty (NICKCOMMS_BASE_URL, NICKCOMMS_API_KEY_NSCIS).
    /// </summary>
    public class NickCommsClient : INickCommsClient
    {
        private readonly HttpClient _http;
        private readonly ISettingsProvider _settings;
        private readonly ILogger<NickCommsClient> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public NickCommsClient(HttpClient http, ISettingsProvider settings, ILogger<NickCommsClient> logger)
        {
            _http = http;
            _settings = settings;
            _logger = logger;
        }

        // ===================== EMAIL =====================

        public async Task<NickCommsEmailResult> SendEmailAsync(
            string to, string subject, string htmlBody, bool isHtml = true,
            IEnumerable<NickCommsAttachment>? attachments = null,
            string? clientReference = null,
            CancellationToken ct = default)
        {
            var ready = await EnsureClientConfiguredAsync(ct);
            if (!ready.Success) return new NickCommsEmailResult { Success = false, ErrorMessage = ready.ErrorMessage };

            var payload = new
            {
                to,
                subject,
                body = htmlBody,
                isHtml,
                clientReference,
                attachments = attachments?.Select(a => new { filename = a.Filename, contentBase64 = a.ContentBase64, contentType = a.ContentType })
            };

            try
            {
                using var resp = await _http.PostAsJsonAsync("/api/email/send", payload, JsonOpts, ct);
                return await ParseEmailResponseAsync(resp, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NickComms email send failed for {To}", to);
                return new NickCommsEmailResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<NickCommsEmailResult> SendBulkEmailAsync(
            IEnumerable<string> recipients, string subject, string htmlBody, bool isHtml = true,
            IEnumerable<NickCommsAttachment>? attachments = null,
            string? clientReference = null,
            CancellationToken ct = default)
        {
            var ready = await EnsureClientConfiguredAsync(ct);
            if (!ready.Success) return new NickCommsEmailResult { Success = false, ErrorMessage = ready.ErrorMessage };

            var payload = new
            {
                recipients = recipients.Select(e => new { email = e }),
                subject,
                body = htmlBody,
                isHtml,
                clientReference,
                attachments = attachments?.Select(a => new { filename = a.Filename, contentBase64 = a.ContentBase64, contentType = a.ContentType })
            };

            try
            {
                using var resp = await _http.PostAsJsonAsync("/api/email/bulk", payload, JsonOpts, ct);
                return await ParseEmailResponseAsync(resp, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NickComms bulk email send failed");
                return new NickCommsEmailResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private static async Task<NickCommsEmailResult> ParseEmailResponseAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new NickCommsEmailResult { Success = false, ErrorMessage = $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(body)}" };

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var result = new NickCommsEmailResult { Success = true };
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var id)) result.MessageId = id;
                if (root.TryGetProperty("batchId", out var bEl) && bEl.TryGetGuid(out var bid)) result.BatchId = bid;
                if (root.TryGetProperty("acceptedCount", out var ac) && ac.TryGetInt32(out var n)) result.AcceptedCount = n;
                if (root.TryGetProperty("duplicateSuppressed", out var ds) && ds.ValueKind == JsonValueKind.True) result.DuplicateSuppressed = true;
                if (root.TryGetProperty("duplicateSuppressedCount", out var dsc) && dsc.TryGetInt32(out var dn)) result.DuplicateSuppressedCount = dn;
                return result;
            }
            catch
            {
                return new NickCommsEmailResult { Success = true };
            }
        }

        // ===================== SMS =====================

        public async Task<NickCommsSmsResult> SendSmsAsync(string phoneNumber, string message, string? clientReference = null, CancellationToken ct = default)
        {
            var ready = await EnsureClientConfiguredAsync(ct);
            if (!ready.Success) return new NickCommsSmsResult { Success = false, ErrorMessage = ready.ErrorMessage };

            var payload = new { to = phoneNumber, content = message, clientReference };

            try
            {
                using var resp = await _http.PostAsJsonAsync("/api/sms/send", payload, JsonOpts, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return new NickCommsSmsResult { Success = false, ErrorMessage = $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(body)}" };

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var result = new NickCommsSmsResult { Success = true };
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var id)) result.MessageId = id;
                if (root.TryGetProperty("status", out var sEl)) result.Status = sEl.GetString();
                if (root.TryGetProperty("rate", out var rEl) && rEl.ValueKind == JsonValueKind.Number) result.Rate = rEl.GetDecimal();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NickComms SMS send failed for {Phone}", phoneNumber);
                return new NickCommsSmsResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // ===================== HISTORY =====================

        public async Task<NickCommsHistoryPage> GetHistoryAsync(NickCommsHistoryQuery query, CancellationToken ct = default)
        {
            var ready = await EnsureClientConfiguredAsync(ct);
            if (!ready.Success) return new NickCommsHistoryPage();

            var qs = new List<string>
            {
                $"page={query.Page}",
                $"pageSize={query.PageSize}"
            };
            if (!string.IsNullOrEmpty(query.Channel)) qs.Add($"channel={Uri.EscapeDataString(query.Channel)}");
            if (!string.IsNullOrEmpty(query.ClientApp)) qs.Add($"clientApp={Uri.EscapeDataString(query.ClientApp)}");
            if (!string.IsNullOrEmpty(query.Recipient)) qs.Add($"recipient={Uri.EscapeDataString(query.Recipient)}");
            if (query.FromDate.HasValue) qs.Add($"fromDate={Uri.EscapeDataString(query.FromDate.Value.ToString("o"))}");
            if (query.ToDate.HasValue) qs.Add($"toDate={Uri.EscapeDataString(query.ToDate.Value.ToString("o"))}");

            try
            {
                var page = await _http.GetFromJsonAsync<NickCommsHistoryPage>("/api/messages/history?" + string.Join("&", qs), JsonOpts, ct);
                return page ?? new NickCommsHistoryPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NickComms history fetch failed");
                return new NickCommsHistoryPage();
            }
        }

        // ===================== HEALTH =====================

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            var ready = await EnsureClientConfiguredAsync(ct);
            if (!ready.Success) return false;

            try
            {
                using var resp = await _http.GetAsync("/api/health", ct);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NickComms ping failed");
                return false;
            }
        }

        // ===================== Configuration =====================

        private async Task<(bool Success, string? ErrorMessage)> EnsureClientConfiguredAsync(CancellationToken ct)
        {
            var enabled = await _settings.GetBoolAsync("NickComms", "Enabled", true);
            if (!enabled) return (false, "NickComms gateway is disabled in settings.");

            var baseUrl = await _settings.GetStringAsync("NickComms", "BaseUrl", "");
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = Environment.GetEnvironmentVariable("NICKCOMMS_BASE_URL") ?? "";
            if (string.IsNullOrWhiteSpace(baseUrl))
                return (false, "NickComms BaseUrl not configured.");

            var apiKey = await _settings.GetStringAsync("NickComms", "ApiKey", "");
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = Environment.GetEnvironmentVariable("NICKCOMMS_API_KEY_NSCIS") ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
                return (false, "NickComms ApiKey not configured.");

            // Apply on every call so settings updates take effect immediately
            if (_http.BaseAddress?.ToString().TrimEnd('/') != baseUrl.TrimEnd('/'))
                _http.BaseAddress = new Uri(baseUrl);

            _http.DefaultRequestHeaders.Remove("X-Api-Key");
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            var timeout = await _settings.GetIntAsync("NickComms", "TimeoutSeconds", 15);
            if (timeout > 0 && _http.Timeout.TotalSeconds != timeout)
                _http.Timeout = TimeSpan.FromSeconds(timeout);

            return (true, null);
        }

        private static string Truncate(string s, int max = 200)
            => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
