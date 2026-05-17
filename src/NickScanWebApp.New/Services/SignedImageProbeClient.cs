namespace NickScanWebApp.New.Services;

public sealed class SignedImageProbeClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SignedImageProbeClient> _logger;

    public SignedImageProbeClient(
        IHttpClientFactory httpClientFactory,
        ILogger<SignedImageProbeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SignedImageProbeResult> ProbeAsync(string signedUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signedUrl))
        {
            return new SignedImageProbeResult(false, 0, "Image URL is empty");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("NickScanAPI");
            using var response = await client.GetAsync(
                signedUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.Content?.Dispose();

            return new SignedImageProbeResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode
                    ? $"HTTP {response.StatusCode} - Image found in database"
                    : $"HTTP {response.StatusCode} - {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signed image probe failed for {SignedUrl}", RedactSignedUrl(signedUrl));
            return new SignedImageProbeResult(false, 0, $"Error checking: {ex.Message}");
        }
    }

    private static string RedactSignedUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        var queryIndex = value.IndexOf('?');
        return queryIndex >= 0 ? value[..queryIndex] : value;
    }
}

public sealed record SignedImageProbeResult(bool Exists, int StatusCode, string Details);
