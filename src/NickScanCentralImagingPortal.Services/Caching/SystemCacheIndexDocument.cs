namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheIndexDocument
{
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<string> Keys { get; set; } = new();
}
