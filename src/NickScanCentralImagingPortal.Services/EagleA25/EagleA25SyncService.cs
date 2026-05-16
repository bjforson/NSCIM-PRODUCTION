using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Entities.EagleA25;
using NickScanCentralImagingPortal.Infrastructure.Data;
using System.Runtime.InteropServices;

namespace NickScanCentralImagingPortal.Services.EagleA25
{
    public class EagleA25SyncService : IEagleA25SyncService
    {
        private readonly ApplicationDbContext _db;
        private readonly EagleA25Configuration _config;
        private readonly ILogger<EagleA25SyncService> _logger;
        private bool _sourceShareConnectionAttempted;

        public EagleA25SyncService(
            ApplicationDbContext db,
            IOptions<EagleA25Configuration> config,
            ILogger<EagleA25SyncService> logger)
        {
            _db = db;
            _config = config.Value;
            _logger = logger;
        }

        public async Task<EagleA25SyncResult> SyncAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.ConnectionString))
            {
                throw new InvalidOperationException("EagleA25:ConnectionString is required");
            }

            var syncLog = new EagleA25SyncLog();
            _db.EagleA25SyncLogs.Add(syncLog);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
            var rows = await ReadSourceRowsAsync(cancellationToken);
                syncLog.ScansRead = rows.Select(r => r.SourceManifestId).Distinct().Count();
                syncLog.AssetsRead = rows.Count(r => r.SourceExtFileId.HasValue);

                var sourceManifestIds = rows.Select(r => r.SourceManifestId).Distinct().ToList();
                var existingScans = await _db.EagleA25Scans
                    .AsTracking()
                    .Include(s => s.Assets)
                    .Where(s => sourceManifestIds.Contains(s.SourceManifestId))
                    .ToDictionaryAsync(s => s.SourceManifestId, cancellationToken);

                var inserted = 0;
                var updated = 0;
                var assetsInserted = 0;
                var assetsUpdated = 0;

                foreach (var group in rows.GroupBy(r => r.SourceManifestId))
                {
                    var first = group.First();
                    if (!existingScans.TryGetValue(group.Key, out var scan))
                    {
                        scan = CreateScan(first);
                        _db.EagleA25Scans.Add(scan);
                        inserted++;
                    }
                    else
                    {
                        UpdateScan(scan, first);
                        updated++;
                    }

                    var existingAssets = scan.Assets.ToDictionary(a => a.SourceExtFileId);
                    foreach (var row in group.Where(r => r.SourceExtFileId.HasValue))
                    {
                        var extFileId = row.SourceExtFileId!.Value;
                        if (!existingAssets.TryGetValue(extFileId, out var asset))
                        {
                            asset = CreateAsset(scan.Id, row);
                            scan.Assets.Add(asset);
                            assetsInserted++;
                        }
                        else
                        {
                            UpdateAsset(asset, row);
                            assetsUpdated++;
                        }

                        await CopyAssetIfEnabledAsync(asset, row.Accession, cancellationToken);
                    }
                }

                syncLog.ScansInserted = inserted;
                syncLog.ScansUpdated = updated;
                syncLog.AssetsInserted = assetsInserted;
                syncLog.AssetsUpdated = assetsUpdated;
                syncLog.LastSyncedAccession = rows.Count == 0 ? null : rows.Max(r => r.Accession);
                syncLog.Status = "Completed";
                syncLog.CompletedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                return new EagleA25SyncResult(
                    syncLog.ScansRead,
                    inserted,
                    updated,
                    syncLog.AssetsRead,
                    assetsInserted,
                    assetsUpdated,
                    syncLog.LastSyncedAccession);
            }
            catch (Exception ex)
            {
                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                syncLog.CompletedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<List<SourceRow>> ReadSourceRowsAsync(CancellationToken cancellationToken)
        {
            var rows = new List<SourceRow>();
            var lastLocalAccession = await _db.EagleA25Scans
                .AsNoTracking()
                .MaxAsync(s => (long?)s.Accession, cancellationToken);
            var accessionFloor = Math.Max(lastLocalAccession ?? 0, 202605010000L - 1);

            await using var conn = new SqlConnection(_config.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = """
                ;WITH CandidateManifests AS
                (
                    SELECT TOP (@BatchSize)
                        s.ID AS SourceScanId,
                        s.GUID AS SourceScanGuid,
                        s.CargoSystemID,
                        s.Date AS ScanDateLocal,
                        s.DateUTC AS ScanDateUtc,
                        se.ID AS SourceScanEntryId,
                        se.ScanPos,
                        se.ScanSubPos,
                        m.ID AS SourceManifestId,
                        m.GUID AS SourceManifestGuid,
                        m.Accession,
                        m.ScanAccession,
                        m.DataPath,
                        m.DataURL,
                        m.CreateDate,
                        m.CreateDateUTC,
                        m.XRayDone,
                        m.ReadyInspect,
                        m.InspectDone,
                        m.InspectSuspicious,
                        m.SearchFound,
                        m.SearchDone,
                        m.Archived,
                        m.LocationId,
                        u.Container,
                        u.AirWaybill,
                        u.FlightNumber,
                        u.TransitType,
                        u.Weight,
                        u.Company,
                        u.Quantity,
                        u.QuantityType,
                        u.OriginFrom,
                        u.OriginTo,
                        u.Comments
                    FROM dbo.Scan s
                    INNER JOIN dbo.ScanEntry se ON se.ScanID = s.ID
                    INNER JOIN dbo.Manifest m ON m.ID = se.ManifestID
                    LEFT JOIN dbo.USERDATA u ON u.ManifestID = m.ID
                    WHERE s.DateUTC >= @MinimumScanDateUtc
                      AND m.Accession > @LastSyncedAccession
                    ORDER BY m.Accession ASC
                )
                SELECT
                    s.SourceScanId,
                    s.SourceScanGuid,
                    s.CargoSystemID,
                    s.ScanDateLocal,
                    s.ScanDateUtc,
                    s.SourceScanEntryId,
                    s.ScanPos,
                    s.ScanSubPos,
                    s.SourceManifestId,
                    s.SourceManifestGuid,
                    s.Accession,
                    s.ScanAccession,
                    s.DataPath,
                    s.DataURL,
                    s.CreateDate,
                    s.CreateDateUTC,
                    s.XRayDone,
                    s.ReadyInspect,
                    s.InspectDone,
                    s.InspectSuspicious,
                    s.SearchFound,
                    s.SearchDone,
                    s.Archived,
                    s.LocationId,
                    s.Container,
                    s.AirWaybill,
                    s.FlightNumber,
                    s.TransitType,
                    s.Weight,
                    s.Company,
                    s.Quantity,
                    s.QuantityType,
                    s.OriginFrom,
                    s.OriginTo,
                    s.Comments,
                    ef.ID AS SourceExtFileId,
                    ef.GUID AS SourceExtFileGuid,
                    ef.Path AS SourcePath,
                    ef.URL AS SourceUrl,
                    ef.CreateDateUTC AS SourceFileCreateDateUtc,
                    eft.ID AS SourceExtFileTypeId,
                    eft.Name AS FileType,
                    eft.Xray AS IsXray,
                    eft.MimeType,
                    eft.Description
                FROM CandidateManifests s
                LEFT JOIN dbo.ManifestExtFile mef ON mef.ManifestID = s.SourceManifestId
                LEFT JOIN dbo.ExtFile ef ON ef.ID = mef.ExtFileID
                LEFT JOIN dbo.ExtFileType eft ON eft.ID = ef.ExtFileTypeID
                ORDER BY s.Accession ASC, ef.ID ASC
                """;
            cmd.Parameters.AddWithValue("@BatchSize", Math.Max(1, _config.BatchSize));
            cmd.Parameters.AddWithValue("@MinimumScanDateUtc", _config.MinimumScanDateUtc);
            cmd.Parameters.AddWithValue("@LastSyncedAccession", accessionFloor);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(SourceRow.From(reader));
            }

            _logger.LogDebug("[EAGLE-A25] Read {Count} source rows", rows.Count);
            return rows;
        }

        private EagleA25Scan CreateScan(SourceRow row)
        {
            var scan = new EagleA25Scan();
            UpdateScan(scan, row);
            return scan;
        }

        private void UpdateScan(EagleA25Scan scan, SourceRow row)
        {
            scan.SourceScanId = row.SourceScanId;
            scan.SourceScanGuid = row.SourceScanGuid;
            scan.SourceScanEntryId = row.SourceScanEntryId;
            scan.SourceManifestId = row.SourceManifestId;
            scan.SourceManifestGuid = row.SourceManifestGuid;
            scan.Accession = row.Accession;
            scan.ScanAccession = row.ScanAccession;
            scan.CargoSystemId = row.CargoSystemId;
            scan.LocationId = row.LocationId;
            scan.ScanDateUtc = DateTime.SpecifyKind(row.ScanDateUtc, DateTimeKind.Utc);
            scan.ScanDateLocal = AsUtc(row.ScanDateLocal);
            scan.ManifestCreateDateUtc = row.ManifestCreateDateUtc.HasValue
                ? AsUtc(row.ManifestCreateDateUtc.Value)
                : null;
            scan.ManifestCreateDateLocal = row.ManifestCreateDateLocal.HasValue
                ? AsUtc(row.ManifestCreateDateLocal.Value)
                : null;
            scan.CargoIdentifier = Trim(row.Container);
            scan.AirWaybill = Trim(row.AirWaybill);
            scan.FlightNumber = Trim(row.FlightNumber);
            scan.TransitType = Trim(row.TransitType);
            scan.Weight = Trim(row.Weight);
            scan.Company = Trim(row.Company);
            scan.Quantity = Trim(row.Quantity);
            scan.QuantityType = Trim(row.QuantityType);
            scan.OriginFrom = Trim(row.OriginFrom);
            scan.OriginTo = Trim(row.OriginTo);
            scan.Comments = Trim(row.Comments);
            scan.DataPath = Trim(row.DataPath);
            scan.DataUrl = Trim(row.DataUrl);
            scan.XRayDone = row.XRayDone;
            scan.ReadyInspect = row.ReadyInspect;
            scan.InspectDone = row.InspectDone;
            scan.InspectSuspicious = row.InspectSuspicious;
            scan.SearchFound = row.SearchFound;
            scan.SearchDone = row.SearchDone;
            scan.Archived = row.Archived;
            scan.SyncStatus = "Synced";
            scan.SyncedAtUtc = DateTime.UtcNow;
            scan.UpdatedAtUtc = DateTime.UtcNow;
        }

        private EagleA25ScanAsset CreateAsset(Guid scanId, SourceRow row)
        {
            var asset = new EagleA25ScanAsset { EagleA25ScanId = scanId };
            UpdateAsset(asset, row);
            return asset;
        }

        private void UpdateAsset(EagleA25ScanAsset asset, SourceRow row)
        {
            if (!row.SourceExtFileId.HasValue || !row.SourceExtFileGuid.HasValue || !row.SourceExtFileTypeId.HasValue)
            {
                throw new InvalidOperationException("Cannot create Eagle A25 asset without source ExtFile identity");
            }

            asset.SourceExtFileId = row.SourceExtFileId.Value;
            asset.SourceExtFileGuid = row.SourceExtFileGuid.Value;
            asset.SourceExtFileTypeId = row.SourceExtFileTypeId.Value;
            asset.FileType = row.FileType ?? "UNKNOWN";
            asset.IsXray = row.IsXray;
            asset.MimeType = Trim(row.MimeType);
            asset.Description = Trim(row.Description);
            asset.SourcePath = row.SourcePath ?? string.Empty;
            asset.ResolvedSourcePath = ResolveSourcePath(row.SourcePath);
            asset.SourceUrl = Trim(row.SourceUrl);
            asset.SourceCreateDateUtc = row.SourceFileCreateDateUtc.HasValue
                ? AsUtc(row.SourceFileCreateDateUtc.Value)
                : null;
            asset.FileSizeBytes = TryGetFileSize(asset.ResolvedSourcePath);
            asset.SyncedAtUtc = DateTime.UtcNow;
        }

        private async Task CopyAssetIfEnabledAsync(EagleA25ScanAsset asset, long accession, CancellationToken cancellationToken)
        {
            if (!_config.CopyAssetsToLocalStorage || string.IsNullOrWhiteSpace(_config.LocalAssetRoot))
            {
                return;
            }

            var sourcePath = asset.ResolvedSourcePath;
            EnsureSourceShareConnection();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            try
            {
                var accessionText = accession.ToString();
                var year = accessionText.Length >= 4 ? accessionText[..4] : "unknown-year";
                var month = accessionText.Length >= 6 ? accessionText.Substring(4, 2) : "unknown-month";
                var day = accessionText.Length >= 8 ? accessionText.Substring(6, 2) : "unknown-day";
                var fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"{asset.SourceExtFileId}-{asset.FileType}";
                }

                var destinationDirectory = Path.Combine(_config.LocalAssetRoot, year, month, day, accessionText);
                Directory.CreateDirectory(destinationDirectory);

                var destinationPath = Path.Combine(destinationDirectory, SanitizeFileName(fileName));
                var sourceInfo = new FileInfo(sourcePath);

                if (File.Exists(destinationPath))
                {
                    var destinationInfo = new FileInfo(destinationPath);
                    if (destinationInfo.Length == sourceInfo.Length)
                    {
                        asset.LocalPath = destinationPath;
                        asset.FileSizeBytes = destinationInfo.Length;
                        return;
                    }
                }

                await using var sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1024 * 128,
                    useAsync: true);
                await using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 128,
                    useAsync: true);

                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                asset.LocalPath = destinationPath;
                asset.FileSizeBytes = new FileInfo(destinationPath).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex,
                    "[EAGLE-A25] Unable to copy asset {SourceExtFileId} from {SourcePath}",
                    asset.SourceExtFileId,
                    sourcePath);
            }
        }

        private string? ResolveSourcePath(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }

            if (!string.IsNullOrWhiteSpace(_config.SourceDbPathRoot)
                && sourcePath.StartsWith(_config.SourceDbPathRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _config.SourceShareRoot.TrimEnd('\\', '/')
                    + sourcePath[_config.SourceDbPathRoot.Length..];
            }

            return sourcePath;
        }

        private void EnsureSourceShareConnection()
        {
            if (_sourceShareConnectionAttempted
                || string.IsNullOrWhiteSpace(_config.SourceShareRoot)
                || string.IsNullOrWhiteSpace(_config.SourceShareUsername)
                || string.IsNullOrWhiteSpace(_config.SourceSharePassword)
                || !OperatingSystem.IsWindows())
            {
                return;
            }

            _sourceShareConnectionAttempted = true;

            try
            {
                var resource = new NetResource
                {
                    Scope = 2,
                    Type = 1,
                    DisplayType = 0,
                    Usage = 0,
                    RemoteName = _config.SourceShareRoot
                };

                var result = WNetAddConnection2(ref resource, _config.SourceSharePassword, _config.SourceShareUsername, 0);
                if (result != 0 && result != 85 && result != 1219)
                {
                    _logger.LogWarning("[EAGLE-A25] Source share connection returned Windows error {ErrorCode}", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EAGLE-A25] Unable to authenticate to source share");
            }
        }

        private static long? TryGetFileSize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;
            public string? LocalName;
            public string RemoteName;
            public string? Comment;
            public string? Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(
            ref NetResource netResource,
            string? password,
            string? username,
            int flags);

        private sealed class SourceRow
        {
            public int SourceScanId { get; init; }
            public Guid SourceScanGuid { get; init; }
            public int? CargoSystemId { get; init; }
            public DateTime ScanDateLocal { get; init; }
            public DateTime ScanDateUtc { get; init; }
            public int SourceScanEntryId { get; init; }
            public int SourceManifestId { get; init; }
            public Guid SourceManifestGuid { get; init; }
            public long Accession { get; init; }
            public long? ScanAccession { get; init; }
            public string? DataPath { get; init; }
            public string? DataUrl { get; init; }
            public DateTime? ManifestCreateDateLocal { get; init; }
            public DateTime? ManifestCreateDateUtc { get; init; }
            public bool XRayDone { get; init; }
            public bool ReadyInspect { get; init; }
            public bool InspectDone { get; init; }
            public bool InspectSuspicious { get; init; }
            public bool SearchFound { get; init; }
            public bool SearchDone { get; init; }
            public bool Archived { get; init; }
            public int? LocationId { get; init; }
            public string? Container { get; init; }
            public string? AirWaybill { get; init; }
            public string? FlightNumber { get; init; }
            public string? TransitType { get; init; }
            public string? Weight { get; init; }
            public string? Company { get; init; }
            public string? Quantity { get; init; }
            public string? QuantityType { get; init; }
            public string? OriginFrom { get; init; }
            public string? OriginTo { get; init; }
            public string? Comments { get; init; }
            public int? SourceExtFileId { get; init; }
            public Guid? SourceExtFileGuid { get; init; }
            public string? SourcePath { get; init; }
            public string? SourceUrl { get; init; }
            public DateTime? SourceFileCreateDateUtc { get; init; }
            public int? SourceExtFileTypeId { get; init; }
            public string? FileType { get; init; }
            public bool IsXray { get; init; }
            public string? MimeType { get; init; }
            public string? Description { get; init; }

            public static SourceRow From(SqlDataReader reader) => new()
            {
                SourceScanId = reader.GetInt32(0),
                SourceScanGuid = reader.GetGuid(1),
                CargoSystemId = GetNullableInt(reader, 2),
                ScanDateLocal = reader.GetDateTime(3),
                ScanDateUtc = reader.GetDateTime(4),
                SourceScanEntryId = reader.GetInt32(5),
                SourceManifestId = reader.GetInt32(8),
                SourceManifestGuid = reader.GetGuid(9),
                Accession = reader.GetInt64(10),
                ScanAccession = GetNullableInt64(reader, 11),
                DataPath = GetNullableString(reader, 12),
                DataUrl = GetNullableString(reader, 13),
                ManifestCreateDateLocal = GetNullableDateTime(reader, 14),
                ManifestCreateDateUtc = GetNullableDateTime(reader, 15),
                XRayDone = reader.GetBoolean(16),
                ReadyInspect = reader.GetBoolean(17),
                InspectDone = reader.GetBoolean(18),
                InspectSuspicious = reader.GetBoolean(19),
                SearchFound = reader.GetBoolean(20),
                SearchDone = reader.GetBoolean(21),
                Archived = reader.GetBoolean(22),
                LocationId = GetNullableInt(reader, 23),
                Container = GetNullableString(reader, 24),
                AirWaybill = GetNullableString(reader, 25),
                FlightNumber = GetNullableString(reader, 26),
                TransitType = GetNullableString(reader, 27),
                Weight = GetNullableString(reader, 28),
                Company = GetNullableString(reader, 29),
                Quantity = GetNullableString(reader, 30),
                QuantityType = GetNullableString(reader, 31),
                OriginFrom = GetNullableString(reader, 32),
                OriginTo = GetNullableString(reader, 33),
                Comments = GetNullableString(reader, 34),
                SourceExtFileId = GetNullableInt(reader, 35),
                SourceExtFileGuid = GetNullableGuid(reader, 36),
                SourcePath = GetNullableString(reader, 37),
                SourceUrl = GetNullableString(reader, 38),
                SourceFileCreateDateUtc = GetNullableDateTime(reader, 39),
                SourceExtFileTypeId = GetNullableInt(reader, 40),
                FileType = GetNullableString(reader, 41),
                IsXray = !reader.IsDBNull(42) && reader.GetBoolean(42),
                MimeType = GetNullableString(reader, 43),
                Description = GetNullableString(reader, 44)
            };

            private static int? GetNullableInt(SqlDataReader reader, int ordinal)
                => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

            private static long? GetNullableInt64(SqlDataReader reader, int ordinal)
                => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

            private static Guid? GetNullableGuid(SqlDataReader reader, int ordinal)
                => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

            private static DateTime? GetNullableDateTime(SqlDataReader reader, int ordinal)
                => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

            private static string? GetNullableString(SqlDataReader reader, int ordinal)
                => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }
}
