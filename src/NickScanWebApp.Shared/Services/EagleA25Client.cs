using System.Globalization;
using System.Text;

namespace NickScanWebApp.Shared.Services;

public sealed class EagleA25Client
{
    private const string BasePath = "/api/EagleA25";
    private readonly ApiService _apiService;

    public EagleA25Client(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<EagleA25SyncStatus?> GetSyncStatusAsync()
    {
        return _apiService.GetAsync<EagleA25SyncStatus>($"{BasePath}/sync-status");
    }

    public Task<EagleA25ScanResponse?> GetScansAsync(EagleA25ScanQuery? query = null)
    {
        return _apiService.GetAsync<EagleA25ScanResponse>($"{BasePath}/scans{BuildScansQuery(query)}");
    }

    public Task<EagleA25ScanDetail?> GetScanAsync(Guid scanId)
    {
        return _apiService.GetAsync<EagleA25ScanDetail>($"{BasePath}/scans/{scanId}");
    }

    public Task<EagleA25SyncResult?> TriggerSyncAsync()
    {
        return _apiService.PostAsync<object, EagleA25SyncResult>($"{BasePath}/sync", new { });
    }

    public static string BuildAssetContentPath(Guid assetId)
    {
        return $"{BasePath}/assets/{assetId}/content";
    }

    public static string BuildNativeCompleteImagePath(long accession, string size = "full")
    {
        var safeSize = string.IsNullOrWhiteSpace(size) ? "full" : size;
        return $"/api/ImageProcessing/container/{Uri.EscapeDataString(accession.ToString(CultureInfo.InvariantCulture))}/complete/image?size={Uri.EscapeDataString(safeSize)}";
    }

    public static string BuildImageProxyUrl(string targetUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(targetUrl);
        return $"/api/imageproxy?url={Uri.EscapeDataString(Convert.ToBase64String(bytes))}";
    }

    private static string BuildScansQuery(EagleA25ScanQuery? query)
    {
        query ??= new EagleA25ScanQuery();
        var parts = new List<string>
        {
            $"page={Math.Max(1, query.Page)}",
            $"pageSize={Math.Clamp(query.PageSize, 1, 200)}"
        };

        Add(parts, "accession", query.Accession);
        Add(parts, "airWaybill", query.AirWaybill);
        Add(parts, "cargoIdentifier", query.CargoIdentifier);
        Add(parts, "startDate", query.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", query.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return $"?{string.Join("&", parts)}";
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}

public sealed class EagleA25ScanQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Accession { get; set; }
    public string? AirWaybill { get; set; }
    public string? CargoIdentifier { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public sealed class EagleA25SyncStatus
{
    public bool SchemaReady { get; set; } = true;
    public string? RequiredMigration { get; set; }
    public int TotalScans { get; set; }
    public int TotalAssets { get; set; }
    public EagleA25LastScan? LastScan { get; set; }
    public EagleA25SyncLog? LastSync { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
}

public sealed class EagleA25LastScan
{
    public long Accession { get; set; }
    public DateTime ScanDateUtc { get; set; }
    public string? CargoIdentifier { get; set; }
    public string? AirWaybill { get; set; }
}

public sealed class EagleA25SyncLog
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool Succeeded { get; set; }
}

public sealed class EagleA25ScanResponse
{
    public List<EagleA25ScanDto>? Data { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public sealed class EagleA25ScanDto
{
    public Guid Id { get; set; }
    public long Accession { get; set; }
    public long? ScanAccession { get; set; }
    public DateTime ScanDateUtc { get; set; }
    public string? CargoIdentifier { get; set; }
    public string? AirWaybill { get; set; }
    public string? FlightNumber { get; set; }
    public string? TransitType { get; set; }
    public string? Weight { get; set; }
    public string? Company { get; set; }
    public string? Quantity { get; set; }
    public string? QuantityType { get; set; }
    public string? OriginFrom { get; set; }
    public string? OriginTo { get; set; }
    public bool InspectDone { get; set; }
    public bool InspectSuspicious { get; set; }
    public int AssetCount { get; set; }
}

public sealed class EagleA25ScanDetail
{
    public Guid Id { get; set; }
    public int SourceScanId { get; set; }
    public Guid SourceScanGuid { get; set; }
    public int SourceScanEntryId { get; set; }
    public int SourceManifestId { get; set; }
    public Guid SourceManifestGuid { get; set; }
    public long Accession { get; set; }
    public long? ScanAccession { get; set; }
    public int? CargoSystemId { get; set; }
    public int? LocationId { get; set; }
    public DateTime ScanDateUtc { get; set; }
    public DateTime? ScanDateLocal { get; set; }
    public DateTime? ManifestCreateDateUtc { get; set; }
    public DateTime? ManifestCreateDateLocal { get; set; }
    public string? CargoIdentifier { get; set; }
    public string? AirWaybill { get; set; }
    public string? FlightNumber { get; set; }
    public string? TransitType { get; set; }
    public string? Weight { get; set; }
    public string? Company { get; set; }
    public string? Quantity { get; set; }
    public string? QuantityType { get; set; }
    public string? OriginFrom { get; set; }
    public string? OriginTo { get; set; }
    public string? Comments { get; set; }
    public string? DataPath { get; set; }
    public string? DataUrl { get; set; }
    public bool XRayDone { get; set; }
    public bool ReadyInspect { get; set; }
    public bool InspectDone { get; set; }
    public bool InspectSuspicious { get; set; }
    public bool SearchFound { get; set; }
    public bool SearchDone { get; set; }
    public bool Archived { get; set; }
    public string? SyncStatus { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<EagleA25AssetDetail> Assets { get; set; } = new();
}

public sealed class EagleA25AssetDetail
{
    public Guid Id { get; set; }
    public int SourceExtFileId { get; set; }
    public Guid SourceExtFileGuid { get; set; }
    public int SourceExtFileTypeId { get; set; }
    public string FileType { get; set; } = "";
    public bool IsXray { get; set; }
    public string? MimeType { get; set; }
    public string? Description { get; set; }
    public string SourcePath { get; set; } = "";
    public string? ResolvedSourcePath { get; set; }
    public string? SourceUrl { get; set; }
    public string? LocalPath { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? SourceCreateDateUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class EagleA25SyncResult
{
    public int ScansRead { get; set; }
    public int ScansInserted { get; set; }
    public int ScansUpdated { get; set; }
    public int AssetsRead { get; set; }
    public int AssetsInserted { get; set; }
    public int AssetsUpdated { get; set; }
    public long? LastSyncedAccession { get; set; }
}
