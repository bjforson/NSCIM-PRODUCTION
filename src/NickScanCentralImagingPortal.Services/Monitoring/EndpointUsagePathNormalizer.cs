namespace NickScanCentralImagingPortal.Services.Monitoring;

public static class EndpointUsagePathNormalizer
{
    public static string Normalize(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        var normalized = endpoint.Trim();
        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        return normalized.ToLowerInvariant();
    }
}
