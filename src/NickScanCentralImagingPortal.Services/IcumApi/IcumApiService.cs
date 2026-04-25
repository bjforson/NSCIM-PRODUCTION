using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public class IcumApiService : IIcumApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IcumApiService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly IIcumBackupService _backupService;
        private readonly ICUMSMetrics? _metrics; // ✅ PHASE 3.1: Optional metrics (may not be registered)
        private const string SERVICE_ID = "[ICUMS-API]";

        // Circuit Breaker State
        private CircuitBreakerState _circuitState = CircuitBreakerState.Closed;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private int _consecutiveFailures = 0;

        // Metrics tracking
        private int _totalApiCalls = 0;
        private int _successfulCalls = 0;
        private int _failedCalls = 0;
        private int _retryAttempts = 0;
        private DateTime _lastSuccessfulCall = DateTime.MinValue;
        private DateTime _lastFailedCall = DateTime.MinValue;

        // Configuration
        private readonly int _maxRetries = 3;
        private readonly int _circuitBreakerThreshold = 5;
        private readonly TimeSpan _circuitBreakerTimeout;
        private readonly TimeSpan _cacheTimeout;

        public IcumApiService(
            HttpClient httpClient,
            ILogger<IcumApiService> logger,
            IConfiguration configuration,
            IMemoryCache cache,
            IIcumBackupService backupService,
            ICUMSMetrics? metrics = null) // ✅ PHASE 3.1: Optional metrics injection
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
            _backupService = backupService;
            _metrics = metrics;

            _circuitBreakerTimeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("ICUMS:CircuitBreakerTimeoutMinutes", 5));
            _cacheTimeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("ICUMS:CacheTimeoutMinutes", 10));

            // Configure HttpClient with better defaults for ICUMS API
            _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("ICUMS:HttpClientTimeoutMinutes", 5));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NickScan-CIM/1.0");

            // Configure connection settings for better reliability
            _httpClient.DefaultRequestHeaders.ConnectionClose = false; // Enable connection pooling

            // ✅ PERFORMANCE FIX: Removed initialization from constructor
            // Initialization now happens lazily in BackupJsonResponseAsync when first needed
            // This prevents repeated initialization calls from multiple scoped instances
        }

        public async Task<IcumApiResponse<BoeSelectivityResponse>> GetApiStatusAsync()
        {
            var cacheKey = "icum_api_status";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out IcumApiResponse<BoeSelectivityResponse>? cachedResponse))
            {
                _logger.LogDebug("Returning cached API status");
                return cachedResponse!;
            }

            return await ExecuteWithResilience(async () =>
            {
                _logger.LogInformation("Checking ICUMS API status with enhanced monitoring");

                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddMinutes(-30); // Use 30-minute increment for status check

                var formattedStartDate = startDate.ToString("dd-MM-yyyy HH:mm:ss.ff");
                var formattedEndDate = endDate.ToString("dd-MM-yyyy HH:mm:ss.ff");

                var url = string.Format(_configuration["ICUMS:FetchBatchUrl"], formattedStartDate, formattedEndDate);

                var response = await MakeApiCall(url, _configuration["ICUMS:FetchBatchKey"]);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Handle empty response (no data available)
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogInformation("ICUMS API returned empty response for date range {StartDate} to {EndDate}",
                            startDate, endDate);

                        return new IcumApiResponse<BoeSelectivityResponse>
                        {
                            Status = "Success",
                            Data = new BoeSelectivityResponse
                            {
                                BoeScanDocuments = new List<BoeScanDocument>(),
                                Status = "SUCC"
                            }
                        };
                    }

                    // ✅ FIX: Validate JSON before deserialization
                    if (!IsValidJson(content))
                    {
                        _logger.LogWarning("{ServiceId} ICUMS API returned non-JSON response for status check. Content: {Content}",
                            SERVICE_ID, content.Length > 200 ? content.Substring(0, 200) + "..." : content);
                        throw new InvalidOperationException("ICUMS API returned invalid JSON response for status check");
                    }

                    BoeSelectivityResponse? data;
                    try
                    {
                        data = JsonSerializer.Deserialize<BoeSelectivityResponse>(content);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "{ServiceId} Failed to deserialize ICUMS status response. Content length: {Length}",
                            SERVICE_ID, content.Length);
                        throw new InvalidOperationException($"Failed to deserialize ICUMS API status response: {jsonEx.Message}", jsonEx);
                    }

                    var result = new IcumApiResponse<BoeSelectivityResponse>
                    {
                        Status = "Success",
                        Data = data
                    };

                    // Backup the response
                    await _backupService.BackupJsonResponseAsync("StatusCheck", content, $"{startDate:yyyyMMdd}_{endDate:yyyyMMdd}");

                    // Cache successful response with size
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(_cacheTimeout)
                        .SetSize(1); // Each cache entry counts as 1 unit
                    _cache.Set(cacheKey, result, cacheOptions);

                    return result;
                }
                else
                {
                    return await HandleApiErrorAsync<BoeSelectivityResponse>(response);
                }
            });
        }

        public async Task<IcumApiResponse<BoeSelectivityResponse>> FetchBatchDataAsync(DateTime startDate, DateTime endDate)
        {
            return await ExecuteWithResilience(async () =>
            {
                _logger.LogInformation("{ServiceId} Fetching ICUMS batch data from {StartDate} to {EndDate}", SERVICE_ID, startDate, endDate);

                // Validate date range (max 1 day according to API spec)
                if ((endDate - startDate).TotalDays > 1)
                {
                    _logger.LogWarning("Date range exceeds 1 day limit, adjusting to 1 day");
                    endDate = startDate.AddDays(1);
                }

                var formattedStartDate = startDate.ToString("dd-MM-yyyy HH:mm:ss.ff");
                var formattedEndDate = endDate.ToString("dd-MM-yyyy HH:mm:ss.ff");

                var url = string.Format(_configuration["ICUMS:FetchBatchUrl"], formattedStartDate, formattedEndDate);

                var response = await MakeApiCall(url, _configuration["ICUMS:FetchBatchKey"]);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Handle empty response (no data available)
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogInformation("ICUMS API returned empty response for date range {StartDate} to {EndDate}",
                            startDate, endDate);

                        return new IcumApiResponse<BoeSelectivityResponse>
                        {
                            Status = "Success",
                            Data = new BoeSelectivityResponse
                            {
                                BoeScanDocuments = new List<BoeScanDocument>(),
                                Status = "SUCC"
                            }
                        };
                    }

                    try
                    {
                        var data = JsonSerializer.Deserialize<BoeSelectivityResponse>(content);

                        _logger.LogInformation("{ServiceId} Successfully fetched {Count} records from ICUMS", SERVICE_ID,
                            data?.BoeScanDocuments?.Count ?? 0);

                        // Backup the response
                        await _backupService.BackupJsonResponseAsync("BatchData", content, $"{startDate:yyyyMMdd}_{endDate:yyyyMMdd}");

                        return new IcumApiResponse<BoeSelectivityResponse>
                        {
                            Status = "Success",
                            Data = data
                        };
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to parse ICUMS JSON response. Content preview: {ContentPreview}",
                            content.Length > 200 ? content.Substring(0, 200) + "..." : content);

                        // Backup the problematic response for debugging
                        await _backupService.BackupJsonResponseAsync("BatchData_Error", content, $"{startDate:yyyyMMdd}_{endDate:yyyyMMdd}");

                        return new IcumApiResponse<BoeSelectivityResponse>
                        {
                            Status = "Error",
                            Error = new IcumError
                            {
                                ErrorCode = -1,
                                ErrorMsg = $"JSON parsing failed: {jsonEx.Message}"
                            }
                        };
                    }
                }
                else
                {
                    return await HandleApiErrorAsync<BoeSelectivityResponse>(response);
                }
            });
        }

        public async Task<IcumApiResponse<BoeScanDocument>> FetchContainerDataAsync(string containerNumber)
        {
            var cacheKey = $"icum_container_{containerNumber}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out IcumApiResponse<BoeScanDocument>? cachedResponse))
            {
                _logger.LogDebug("Returning cached container data for {ContainerNumber}", containerNumber);
                return cachedResponse!;
            }

            return await ExecuteWithResilience(async () =>
            {
                _logger.LogInformation("{ServiceId} Fetching ICUMS container data for {ContainerNumber}", SERVICE_ID, containerNumber);

                var url = string.Format(_configuration["ICUMS:FetchUrl"], containerNumber);

                var response = await MakeApiCall(url, _configuration["ICUMS:FetchKey"]);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // ✅ FIX: Validate content before deserialization
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("{ServiceId} ICUMS API returned empty response for container {ContainerNumber}", SERVICE_ID, containerNumber);
                        throw new InvalidOperationException($"ICUMS API returned empty response for container {containerNumber}");
                    }

                    // ✅ FIX: Check if content is valid JSON before deserializing
                    if (!IsValidJson(content))
                    {
                        _logger.LogWarning("{ServiceId} ICUMS API returned non-JSON response for container {ContainerNumber}. Content: {Content}",
                            SERVICE_ID, containerNumber, content.Length > 200 ? content.Substring(0, 200) + "..." : content);
                        throw new InvalidOperationException($"ICUMS API returned invalid JSON response for container {containerNumber}");
                    }

                    BoeScanDocument? data;
                    try
                    {
                        var wrapper = JsonSerializer.Deserialize<BoeSelectivityResponse>(content);
                        data = wrapper?.BoeScanDocuments?.FirstOrDefault();

                        _logger.LogInformation("{ServiceId} Parsed ICUMS response for {ContainerNumber}: {Count} document(s) in BOEScanDocument array",
                            SERVICE_ID, containerNumber, wrapper?.BoeScanDocuments?.Count ?? 0);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "{ServiceId} Failed to deserialize ICUMS response for container {ContainerNumber}. Content length: {Length}",
                            SERVICE_ID, containerNumber, content.Length);
                        throw new InvalidOperationException($"Failed to deserialize ICUMS API response for container {containerNumber}: {jsonEx.Message}", jsonEx);
                    }

                    var result = new IcumApiResponse<BoeScanDocument>
                    {
                        Status = "Success",
                        Data = data
                    };

                    // Backup the response
                    await _backupService.BackupJsonResponseAsync("ContainerData", content, containerNumber);

                    // Cache successful response with size
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(_cacheTimeout)
                        .SetSize(1); // Each cache entry counts as 1 unit
                    _cache.Set(cacheKey, result, cacheOptions);

                    return result;
                }
                else
                {
                    return await HandleApiErrorAsync<BoeScanDocument>(response);
                }
            });
        }

        public async Task<IcumApiResponse<BoeSelectivityResponse>> FetchBOEDataAsync(string declarationNumber)
        {
            var cacheKey = $"icum_boe_{declarationNumber}";

            if (_cache.TryGetValue(cacheKey, out IcumApiResponse<BoeSelectivityResponse>? cachedResponse))
            {
                _logger.LogDebug("Returning cached BOE data for {DeclarationNumber}", declarationNumber);
                return cachedResponse!;
            }

            return await ExecuteWithResilience(async () =>
            {
                _logger.LogInformation("{ServiceId} Fetching ICUMS BOE data for declaration {DeclarationNumber}", SERVICE_ID, declarationNumber);

                var url = string.Format(_configuration["ICUMS:FetchJsonDocumentsUrl"]!, declarationNumber);
                var interfaceKey = _configuration["ICUMS:FetchJsonDocumentsKey"]!;
                var authKey = _configuration["ICUMS:JsonDocumentsAuthKey"]
                              ?? Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_JSON_AUTH_KEY");

                var response = await MakeApiCall(url, interfaceKey, authKey);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("{ServiceId} ICUMS BOE API returned empty response for {DeclarationNumber}", SERVICE_ID, declarationNumber);
                        return new IcumApiResponse<BoeSelectivityResponse>
                        {
                            Status = "Success",
                            Data = new BoeSelectivityResponse()
                        };
                    }

                    if (!IsValidJson(content))
                    {
                        _logger.LogWarning("{ServiceId} ICUMS BOE API returned non-JSON response for {DeclarationNumber}. Content: {Content}",
                            SERVICE_ID, declarationNumber, content.Length > 200 ? content[..200] + "..." : content);
                        throw new InvalidOperationException($"ICUMS BOE API returned invalid JSON for declaration {declarationNumber}");
                    }

                    BoeSelectivityResponse? data;
                    try
                    {
                        data = JsonSerializer.Deserialize<BoeSelectivityResponse>(content);
                        _logger.LogInformation("{ServiceId} Parsed ICUMS BOE response for {DeclarationNumber}: {Count} container(s)",
                            SERVICE_ID, declarationNumber, data?.BoeScanDocuments?.Count ?? 0);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "{ServiceId} Failed to deserialize ICUMS BOE response for {DeclarationNumber}. Content length: {Length}",
                            SERVICE_ID, declarationNumber, content.Length);
                        throw new InvalidOperationException($"Failed to deserialize ICUMS BOE API response for declaration {declarationNumber}: {jsonEx.Message}", jsonEx);
                    }

                    var result = new IcumApiResponse<BoeSelectivityResponse>
                    {
                        Status = "Success",
                        Data = data ?? new BoeSelectivityResponse()
                    };

                    await _backupService.BackupJsonResponseAsync("BOEData", content, declarationNumber);

                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(10))
                        .SetSize(1);
                    _cache.Set(cacheKey, result, cacheOptions);

                    return result;
                }
                else
                {
                    return await HandleApiErrorAsync<BoeSelectivityResponse>(response);
                }
            });
        }

        public async Task<IcumApiResponse<object>> SubmitScanResultAsync(BoeScanDocument scanResult)
        {
            return await ExecuteWithResilience(async () =>
            {
                _logger.LogInformation("Submitting scan result to ICUMS for container {ContainerNumber}",
                    scanResult.ContainerDetails.ContainerNumber);

                var url = _configuration["ICUMS:SubmitResultUrl"];

                // Create the scan data payload according to API spec
                var scanData = new
                {
                    scanData = new
                    {
                        DeclarationNumber = scanResult.Header.DeclarationNumber,
                        VersionNumber = scanResult.Header.DeclarationVersion,
                        RotationNumber = scanResult.ManifestDetails.RotationNumber,
                        BlNumber = scanResult.ManifestDetails.MasterBlNumber,
                        HouseBl = scanResult.ManifestDetails.HouseBl,
                        ContainerNumber = scanResult.ContainerDetails.ContainerNumber,
                        ScanReferenceNumber = Guid.NewGuid().ToString("N")[..20],
                        ScanDate = DateTime.UtcNow.ToString("dd-MM-yy"),
                        ScanStartDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                        ScanEndDate = DateTime.UtcNow.AddMinutes(10).ToString("yyyyMMddHHmmss"),
                        ScanAnalysisStartDate = DateTime.UtcNow.AddMinutes(5).ToString("yyyyMMddHHmmss"),
                        ScanAnalysisEndDate = DateTime.UtcNow.AddMinutes(15).ToString("yyyyMMddHHmmss"),
                        TruckPlateNumber = "TEMP001",
                        Verdict = "clear",
                        FindingsDescription = "No suspicious items found",
                        ImageAnalystName = "System",
                        CustomOfficerName = "System",
                        ImageDocument = ""
                    }
                };

                var json = JsonSerializer.Serialize(scanData, new JsonSerializerOptions { WriteIndented = true });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Backup the request payload
                await _backupService.BackupJsonResponseAsync("ScanResults", json, scanResult.ContainerDetails.ContainerNumber, "request");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully submitted scan result for container {ContainerNumber}",
                        scanResult.ContainerDetails.ContainerNumber);

                    // Backup the response
                    await _backupService.BackupJsonResponseAsync("ScanResults", responseContent, scanResult.ContainerDetails.ContainerNumber, "response");

                    // ✅ FIX: Validate JSON before deserialization (response may be empty or non-JSON)
                    object? data = null;
                    if (!string.IsNullOrWhiteSpace(responseContent) && IsValidJson(responseContent))
                    {
                        try
                        {
                            data = JsonSerializer.Deserialize<object>(responseContent);
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogWarning(jsonEx, "Failed to deserialize scan result submission response for container {ContainerNumber}",
                                scanResult.ContainerDetails.ContainerNumber);
                            // Continue with null data - submission may still be successful
                        }
                    }

                    return new IcumApiResponse<object>
                    {
                        Status = "Success",
                        Data = data
                    };
                }
                else
                {
                    return await HandleApiErrorAsync<object>(response);
                }
            });
        }

        // Enhanced Resilience Methods
        private async Task<T> ExecuteWithResilience<T>(Func<Task<T>> operation) where T : class
        {
            // Check circuit breaker
            if (_circuitState == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow - _lastFailureTime > _circuitBreakerTimeout)
                {
                    _logger.LogInformation("Circuit breaker timeout expired, attempting to close circuit");
                    _circuitState = CircuitBreakerState.HalfOpen;
                }
                else
                {
                    _logger.LogWarning("Circuit breaker is open, rejecting request");
                    throw new InvalidOperationException("ICUMS API circuit breaker is open");
                }
            }

            Exception? lastException = null;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    _totalApiCalls++;
                    var result = await operation();
                    stopwatch.Stop();

                    // ✅ PHASE 3.1: Record metrics
                    _metrics?.RecordApiCall(stopwatch.ElapsedMilliseconds, success: true, timeout: false);

                    // Reset circuit breaker on success
                    if (_circuitState == CircuitBreakerState.HalfOpen)
                    {
                        _circuitState = CircuitBreakerState.Closed;
                        _consecutiveFailures = 0;
                        _logger.LogInformation("Circuit breaker closed after successful operation");
                    }

                    _successfulCalls++;
                    _lastSuccessfulCall = DateTime.UtcNow;

                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _consecutiveFailures++;
                    _retryAttempts++;

                    // ✅ PHASE 3.1: Record metrics for failures
                    var isTimeout = ex is TaskCanceledException;
                    if (attempt == _maxRetries)
                    {
                        stopwatch.Stop();
                        _metrics?.RecordApiCall(stopwatch.ElapsedMilliseconds, success: false, timeout: isTimeout);
                    }

                    // Special handling for timeout exceptions
                    if (ex is TaskCanceledException tce && tce.InnerException is TimeoutException)
                    {
                        _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: TIMEOUT - Request exceeded {TimeoutMinutes} minutes",
                            SERVICE_ID, attempt, _httpClient.Timeout.TotalMinutes);
                    }
                    else if (ex is System.Net.Http.HttpRequestException httpEx &&
                             httpEx.InnerException is System.Net.Sockets.SocketException socketEx)
                    {
                        // ✅ ENHANCEMENT: Detect proxy/network connection failures
                        var isConnectionError = socketEx.Message.Contains("connected party did not properly respond") ||
                                              socketEx.Message.Contains("No connection could be made") ||
                                              socketEx.Message.Contains("A connection attempt failed");

                        // Check if proxy is configured
                        var proxyAddress = _configuration["ICUMS:Proxy:Address"];
                        var proxyEnabled = bool.Parse(_configuration["ICUMS:Proxy:Enabled"] ?? "false");
                        var isProxyError = proxyEnabled && !string.IsNullOrEmpty(proxyAddress) && isConnectionError;

                        if (isProxyError)
                        {
                            _logger.LogError(
                                "{ServiceId} ICUMS API attempt {Attempt} failed: PROXY CONNECTION ERROR - " +
                                "Cannot reach proxy server at {ProxyAddress}. " +
                                "Check network connectivity, firewall rules, and proxy server status. " +
                                "To temporarily disable proxy, set ICUMS:Proxy:Enabled=false in appsettings.json. " +
                                "Error: {ErrorMessage}",
                                SERVICE_ID, attempt, proxyAddress, ex.Message);
                        }
                        else if (isConnectionError)
                        {
                            _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: NETWORK CONNECTION ERROR - {ErrorMessage}",
                                SERVICE_ID, attempt, ex.Message);
                        }
                        else
                        {
                            _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: NETWORK ERROR - {ErrorMessage}",
                                SERVICE_ID, attempt, ex.Message);
                        }
                    }
                    else if (ex is JsonException jsonEx)
                    {
                        // ✅ FIX: Special handling for JSON deserialization errors
                        _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: JSON_DESERIALIZATION_ERROR - {ErrorMessage}. " +
                            "This usually means the API returned empty or invalid JSON. Container may not exist in ICUMS.",
                            SERVICE_ID, attempt, jsonEx.Message);
                    }
                    else if (ex is InvalidOperationException invalidOp && invalidOp.Message.Contains("empty response"))
                    {
                        // ✅ FIX: Handle empty response errors
                        _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: EMPTY_RESPONSE - {ErrorMessage}. " +
                            "Container may not exist in ICUMS or API returned no data.",
                            SERVICE_ID, attempt, invalidOp.Message);
                    }
                    else
                    {
                        _logger.LogWarning("{ServiceId} ICUMS API attempt {Attempt} failed: {ErrorType} - {ErrorMessage}",
                            SERVICE_ID, attempt, ex.GetType().Name, ex.Message);
                    }

                    if (attempt < _maxRetries)
                    {
                        // ✅ PHASE 2.1: Use exponential backoff with jitter for better retry behavior
                        var retryOptions = RetryPolicy.CreateIcumApiRetryPolicy();
                        // Adjust initial delay for timeout errors
                        if (ex is TaskCanceledException)
                        {
                            retryOptions.InitialDelay = TimeSpan.FromSeconds(5); // Start with 5s for timeouts
                            retryOptions.MaxDelay = TimeSpan.FromSeconds(60); // Allow up to 60s for timeouts
                        }
                        var delay = RetryPolicy.CalculateDelay(attempt, retryOptions);
                        _logger.LogInformation("{ServiceId} Waiting {Delay:F1} seconds before retry (exponential backoff)", SERVICE_ID, delay.TotalSeconds);
                        await Task.Delay(delay);
                    }
                }
            }

            // All retries failed
            _lastFailureTime = DateTime.UtcNow;
            _lastFailedCall = DateTime.UtcNow;
            _failedCalls++;

            if (_consecutiveFailures >= _circuitBreakerThreshold)
            {
                _circuitState = CircuitBreakerState.Open;
                _logger.LogError(" CIRCUIT BREAKER ACTIVATED: ICUMS API marked as unhealthy after {Failures} consecutive failures. System will skip ICUMS calls for {TimeoutMinutes} minutes.", _consecutiveFailures, _circuitBreakerTimeout.TotalMinutes);
            }

            throw lastException ?? new InvalidOperationException("ICUMS API call failed after all retries");
        }

        private Task<HttpResponseMessage> MakeApiCall(string url, string interfaceKey)
            => MakeApiCall(url, interfaceKey, authKeyOverride: null);

        private async Task<HttpResponseMessage> MakeApiCall(string url, string interfaceKey, string? authKeyOverride)
        {
            // Clear and set headers
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("ESB_IF_ID", interfaceKey);

            // ✅ FIX: Get auth key from configuration, with fallback to environment variable
            // This ensures the auth key is always loaded correctly
            var authKey = authKeyOverride
                         ?? _configuration["ICUMS:AuthKey"]
                         ?? Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY")
                         ?? _configuration["NICKSCAN_ICUMS_AUTH_KEY"];

            if (string.IsNullOrEmpty(authKey) || authKey.Contains("***USE_ENV_VAR***"))
            {
                _logger.LogError("ICUMS AuthKey is not configured. Cannot make API call.");
                throw new InvalidOperationException("ICUMS AuthKey is not configured. Please set NICKSCAN_ICUMS_AUTH_KEY environment variable.");
            }

            _httpClient.DefaultRequestHeaders.Add("ESB_AUTH_KEY", authKey);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            // ✅ DETAILED LOGGING: Log exact headers being sent for debugging
            _logger.LogInformation(
                "[ICUMS-API-HEADERS] Making API call to {Url} | Headers: ESB_IF_ID={InterfaceKey}, ESB_AUTH_KEY={AuthKeyPrefix}... (length: {AuthKeyLength}), Accept=application/json",
                url, interfaceKey, authKey.Substring(0, Math.Min(20, authKey.Length)), authKey.Length);

            // Log all headers for detailed debugging
            var allHeaders = new System.Text.StringBuilder();
            allHeaders.AppendLine($"  URL: {url}");
            allHeaders.AppendLine($"  ESB_IF_ID: {interfaceKey}");
            allHeaders.AppendLine($"  ESB_AUTH_KEY: {authKey} (full key for debugging)");
            allHeaders.AppendLine($"  Accept: application/json");
            if (_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                allHeaders.AppendLine($"  User-Agent: {string.Join(", ", _httpClient.DefaultRequestHeaders.GetValues("User-Agent"))}");
            }

            _logger.LogDebug("[ICUMS-API-HEADERS] Full request details:\n{Headers}", allHeaders.ToString());

            var response = await _httpClient.GetAsync(url);

            // Log response details
            _logger.LogInformation(
                "[ICUMS-API-RESPONSE] Response Status: {StatusCode} {StatusReason} | URL: {Url}",
                response.StatusCode, response.ReasonPhrase, url);

            // If unauthorized, log detailed error information
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "[ICUMS-API-ERROR] Unauthorized response received. Headers sent: ESB_IF_ID={InterfaceKey}, ESB_AUTH_KEY={AuthKeyPrefix}... (length: {AuthKeyLength}) | Error: {ErrorContent}",
                    interfaceKey, authKey.Substring(0, Math.Min(20, authKey.Length)), authKey.Length, errorContent);
            }

            return response;
        }

        private async Task<IcumApiResponse<T>> HandleApiErrorAsync<T>(HttpResponseMessage response) where T : class
        {
            // Was: .Result blocking call — risks deadlock under sync contexts and thread-pool starvation.
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            _logger.LogError("ICUMS API error: {StatusCode} - {Content}", response.StatusCode, content);

            return new IcumApiResponse<T>
            {
                Status = "Error",
                Error = new IcumError
                {
                    ErrorCode = (int)response.StatusCode,
                    ErrorMsg = $"API returned {response.StatusCode}: {content}"
                }
            };
        }

        // Health Check Methods
        public async Task<IcumHealthStatus> GetHealthStatusAsync()
        {
            try
            {
                var statusResponse = await GetApiStatusAsync();

                return new IcumHealthStatus
                {
                    IsHealthy = statusResponse.Status == "Success",
                    CircuitBreakerState = _circuitState.ToString(),
                    ConsecutiveFailures = _consecutiveFailures,
                    LastFailureTime = _lastFailureTime,
                    ResponseTime = DateTime.UtcNow - _lastFailureTime,
                    LastSuccessfulCall = _lastSuccessfulCall
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return new IcumHealthStatus
                {
                    IsHealthy = false,
                    CircuitBreakerState = _circuitState.ToString(),
                    ConsecutiveFailures = _consecutiveFailures,
                    LastFailureTime = _lastFailureTime,
                    Error = ex.Message,
                    LastSuccessfulCall = _lastSuccessfulCall
                };
            }
        }

        public async Task<bool> IsApiHealthyAsync()
        {
            var healthStatus = await GetHealthStatusAsync();
            return healthStatus.IsHealthy;
        }

        public async Task<ServiceMetrics> GetServiceMetricsAsync()
        {
            var backupMetrics = await _backupService.GetBackupMetricsAsync();

            return await Task.FromResult(new ServiceMetrics
            {
                TotalApiCalls = _totalApiCalls,
                SuccessfulCalls = _successfulCalls,
                FailedCalls = _failedCalls,
                SuccessRate = _totalApiCalls > 0 ? (double)_successfulCalls / _totalApiCalls : 0,
                LastSuccessfulCall = _lastSuccessfulCall,
                LastFailedCall = _lastFailedCall,
                CircuitBreakerFailures = _consecutiveFailures,
                RetryAttempts = _retryAttempts,
                BackupFilesCreated = backupMetrics.BackupFilesCreated,
                TotalDataDownloadedBytes = backupMetrics.TotalDataDownloadedBytes
            });
        }

        // Circuit Breaker State Management
        private enum CircuitBreakerState
        {
            Closed,
            Open,
            HalfOpen
        }

        /// <summary>
        /// Validates if a string is valid JSON by attempting to parse it
        /// </summary>
        private bool IsValidJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // Trim whitespace
            content = content.Trim();

            // Quick check: JSON should start with { or [
            if (!content.StartsWith("{") && !content.StartsWith("["))
                return false;

            // Try to parse as JSON
            try
            {
                using var doc = JsonDocument.Parse(content);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
