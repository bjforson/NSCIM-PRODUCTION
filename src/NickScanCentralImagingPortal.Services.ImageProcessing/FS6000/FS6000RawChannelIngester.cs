using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Reads FS6000 raw .img channel files (high/low/material) from a stable
    /// folder — typically under Data\FS6000\Archive, never Data\FS6000\Staging —
    /// and upserts them into the <c>fs6000images</c> table with
    /// <c>imagetype</c> in {HighEnergy, LowEnergy, Material}.
    ///
    /// History (2026-04-19): replaces the inline .img loop that used to live in
    /// <c>Services.FS6000.IngestionService.cs</c> (lines ~827–856 pre-refactor).
    /// That loop read from <c>Staging\</c> while the FS6000 file-sync service
    /// was still copying files in — the 10 MB .img reads lost a race against
    /// the scanner software's own file handle and threw
    /// <c>IOException: being used by another process</c>, which was swallowed.
    /// Result was ~5% coverage of HighEnergy and even worse for Low/Material.
    /// This class only ever reads from caller-provided paths that the caller
    /// has already confirmed stable, so the lock race is impossible.
    ///
    /// Idempotent: upsert via <c>INSERT … ON CONFLICT DO NOTHING</c>, backed by
    /// the <c>ix_fs6000images_scanid_imagetype_unique</c> unique index added
    /// alongside this class. Re-runs are safe.
    /// </summary>
    public class FS6000RawChannelIngester
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<FS6000RawChannelIngester> _logger;

        private static readonly (string Suffix, string ImageType)[] KnownChannels = new[]
        {
            ("high.img",     "HighEnergy"),
            ("low.img",      "LowEnergy"),
            ("material.img", "Material"),
        };

        public FS6000RawChannelIngester(ApplicationDbContext db, ILogger<FS6000RawChannelIngester> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Ingests whichever of the three raw channels are present in
        /// <paramref name="folderPath"/> and not already stored for
        /// <paramref name="scanId"/>. Returns a per-call report.
        /// </summary>
        public async Task<RawChannelIngestionResult> IngestAsync(
            Guid scanId,
            string folderPath,
            CancellationToken ct = default)
        {
            var result = new RawChannelIngestionResult
            {
                ScanId = scanId,
                FolderPath = folderPath,
            };

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                result.ErrorMessage = $"Scan folder not found: {folderPath}";
                _logger.LogDebug("[FS6000-RAW] {ScanId} folder missing: {Folder}", scanId, folderPath);
                return result;
            }

            // One round-trip to find which channels are already stored. We do
            // this instead of per-channel EXISTS checks to minimise chatter.
            var existing = await _db.FS6000Images
                .AsNoTracking()
                .Where(i => i.ScanId == scanId
                         && (i.ImageType == "HighEnergy" || i.ImageType == "LowEnergy" || i.ImageType == "Material"))
                .Select(i => i.ImageType)
                .ToListAsync(ct);

            foreach (var (suffix, imageType) in KnownChannels)
            {
                if (existing.Contains(imageType))
                {
                    result.AlreadyPresent++;
                    continue;
                }

                // .img filenames look like "{anything}high.img" / "{anything}low.img" /
                // "{anything}material.img". Glob catches the only one we care about.
                var file = Directory.GetFiles(folderPath, "*" + suffix).FirstOrDefault();
                if (file == null)
                {
                    result.MissingFiles++;
                    continue;
                }

                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(file, ct);

                    var entity = new FS6000Image
                    {
                        Id = Guid.NewGuid(),
                        ScanId = scanId,
                        ImageType = imageType,
                        FileName = Path.GetFileName(file),
                        ImageData = bytes,
                        FileSizeBytes = bytes.Length,
                        CreatedAt = DateTime.UtcNow,
                    };

                    _db.FS6000Images.Add(entity);
                    await _db.SaveChangesAsync(ct);
                    _db.ChangeTracker.Clear();

                    result.IngestedChannels++;
                    result.IngestedBytes += bytes.Length;
                    _logger.LogDebug("[FS6000-RAW] {ScanId} ingested {ImageType} from {File} ({Bytes} bytes)",
                        scanId, imageType, Path.GetFileName(file), bytes.Length);
                }
                catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
                {
                    // Another caller raced us to this (scanid, imagetype). Idempotent
                    // behaviour: treat as already-present and move on.
                    result.AlreadyPresent++;
                    _db.ChangeTracker.Clear();
                    _logger.LogDebug("[FS6000-RAW] {ScanId}/{ImageType} already ingested by concurrent caller", scanId, imageType);
                }
                catch (OperationCanceledException)
                {
                    _db.ChangeTracker.Clear();
                    throw;
                }
                catch (Exception ex)
                {
                    result.FailedChannels++;
                    result.LastError = ex.GetType().Name + ": " + ex.Message;
                    _db.ChangeTracker.Clear();
                    _logger.LogWarning(ex, "[FS6000-RAW] Failed to ingest {ImageType} for scan {ScanId} from {File}",
                        imageType, scanId, file);
                }
            }

            return result;
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            // Npgsql wraps PostgreSQL errors; 23505 is the SQLSTATE for unique violation.
            var inner = ex.InnerException;
            while (inner != null)
            {
                var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
                if (sqlState == "23505") return true;
                if ((inner.Message ?? string.Empty).Contains("ix_fs6000images_scanid_imagetype_unique",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                inner = inner.InnerException;
            }
            return false;
        }
    }

    /// <summary>
    /// Report returned by <see cref="FS6000RawChannelIngester.IngestAsync"/>.
    /// Counts are per-channel, not per-scan, so a scan that ingests High and Low
    /// but is missing Material reports IngestedChannels=2, MissingFiles=1.
    /// </summary>
    public class RawChannelIngestionResult
    {
        public Guid ScanId { get; set; }
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>How many of the 3 channels were newly inserted this call.</summary>
        public int IngestedChannels { get; set; }

        /// <summary>Total bytes read + stored for the newly inserted channels.</summary>
        public long IngestedBytes { get; set; }

        /// <summary>Channels that already had rows in the DB (skipped).</summary>
        public int AlreadyPresent { get; set; }

        /// <summary>Channels whose .img file was not found in the folder.</summary>
        public int MissingFiles { get; set; }

        /// <summary>Channels that threw during read/insert.</summary>
        public int FailedChannels { get; set; }

        /// <summary>Folder-level error (folder not found, etc.).</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Most recent per-channel error, if any.</summary>
        public string? LastError { get; set; }
    }
}
