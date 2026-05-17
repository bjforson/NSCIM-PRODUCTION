using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed class UniFiProtectClient : IUniFiProtectClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UniFiProtectOptions _options;
        private readonly ILogger<UniFiProtectClient> _logger;

        public UniFiProtectClient(
            IHttpClientFactory httpClientFactory,
            IOptions<UniFiProtectOptions> options,
            ILogger<UniFiProtectClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> GetApplicationInfoAsync(CameraEvidenceRuntimeSite site, CancellationToken cancellationToken)
        {
            using var request = CreateRequest(site, HttpMethod.Get, "v1/meta/info");
            using var response = await SendAsync(site, request, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ProtectCameraDto>> GetCamerasAsync(CameraEvidenceRuntimeSite site, CancellationToken cancellationToken)
        {
            using var request = CreateRequest(site, HttpMethod.Get, "v1/cameras");
            using var response = await SendAsync(site, request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                using var document = JsonDocument.Parse(json);
                var source = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                        ? data.EnumerateArray()
                        : Enumerable.Empty<JsonElement>();

                return source.Select(ParseCamera).Where(c => !string.IsNullOrWhiteSpace(c.Id)).ToList();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Unable to parse UniFi Protect camera response for site {SiteKey}", site.Site.SiteKey);
                return Array.Empty<ProtectCameraDto>();
            }
        }

        public async Task<UniFiProtectSnapshotResult> GetSnapshotAsync(
            CameraEvidenceRuntimeSite site,
            string cameraId,
            string channel,
            bool highQuality,
            CancellationToken cancellationToken)
        {
            var safeChannel = string.IsNullOrWhiteSpace(channel) ? "main" : channel.Trim();
            var path = $"v1/cameras/{Uri.EscapeDataString(cameraId)}/snapshot?channel={Uri.EscapeDataString(safeChannel)}&highQuality={highQuality.ToString().ToLowerInvariant()}";
            using var request = CreateRequest(site, HttpMethod.Get, path);
            using var response = await SendAsync(site, request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var parameters = JsonSerializer.Serialize(new { channel = safeChannel, highQuality });
            return new UniFiProtectSnapshotResult(bytes, contentType, highQuality, parameters);
        }

        private static ProtectCameraDto ParseCamera(JsonElement element)
        {
            var id = ReadString(element, "id")
                ?? ReadString(element, "cameraId")
                ?? ReadString(element, "deviceId")
                ?? ReadString(element, "key")
                ?? string.Empty;

            var isConnected = ReadBool(element, "isConnected")
                ?? ReadBool(element, "connected")
                ?? ReadBool(element, "isOnline")
                ?? false;

            return new ProtectCameraDto
            {
                Id = id,
                Name = ReadString(element, "name") ?? ReadString(element, "displayName"),
                Mac = ReadString(element, "mac") ?? ReadString(element, "macAddress"),
                Type = ReadString(element, "type") ?? ReadString(element, "modelKey"),
                IsConnected = isConnected,
                RawJson = element.GetRawText()
            };
        }

        private static string? ReadString(JsonElement element, string name)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null
                    };
                }
            }

            return null;
        }

        private static bool? ReadBool(JsonElement element, string name)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.True) return true;
                if (property.Value.ValueKind == JsonValueKind.False) return false;
                if (property.Value.ValueKind == JsonValueKind.String &&
                    bool.TryParse(property.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private HttpRequestMessage CreateRequest(CameraEvidenceRuntimeSite site, HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, BuildUri(site.Site.BaseUrl, path));
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeader, site.ApiKey);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NSCIM-CameraEvidence", "1.0"));
            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(CameraEvidenceRuntimeSite site, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("UniFiProtect");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(site.Site.RequestTimeoutSeconds, 1, 120));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }

        private static Uri BuildUri(string baseUrl, string path)
        {
            var trimmedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(trimmedBase))
            {
                throw new InvalidOperationException("UniFi Protect site BaseUrl is empty.");
            }

            var trimmedPath = path.TrimStart('/');
            return new Uri($"{trimmedBase}/{trimmedPath}", UriKind.Absolute);
        }
    }
}
