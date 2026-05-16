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
                    // 2026-04-19 (v2.9.5): two-stage read to defeat file-lock
                    // contention with file-sync / antivirus / anything else
                    // that may still be touching the .img files in Archive.
                    //
                    //   stage 1: Archive file → our own IngestWorkspace folder
                    //            (retry on IOException, 4 attempts, exp backoff).
                    //   stage 2: read bytes from the workspace copy — we own
                    //            this file, so the read is guaranteed stable.
                    //
                    // The workspace copy is deleted in a finally-block once
                    // we're done with it. If stage 1 still fails after 4
                    // attempts we throw; the worker / backfill will try again
                    // on its next cycle. Never-abandon is preserved: the
                    // candidate query will still list this scan as "missing
                    // channel" next time round.
                    byte[] bytes = await CopyThenReadAsync(file, ct);

                    // v2.14.1 — reject truncated .img files at ingest time.
                    // The FS6000 header declares (Width, Height, BitDepth);
                    // if the file has fewer bytes than Width*Height*(BitDepth/8)
                    // + header, it was written partially (scanner interrupt or
                    // similar). Surveying production found 22 such files
                    // where the scanner wrote a fraction of the LE buffer;
                    // downstream decode throws "channel truncated" when these
                    // reach the viewer. Reject at ingest so bad bytes never
                    // hit the DB — the scan falls through to partial-channel
                    // handling (v2.14.0) instead of looking renderable.
                    if (!IsHeaderConsistent(bytes, imageType, out var reason))
                    {
                        result.InvalidChannels++;
                        result.LastError = $"header-inconsistent: {reason}";
                        _logger.LogWarning(
                            "[FS6000-RAW] Rejecting truncated/inconsistent {ImageType} for scan {ScanId}: {Reason} — file {File} is structurally incomplete and will remain pending until a complete replacement is available",
                            imageType, scanId, reason, Path.GetFileName(file));
                        _db.ChangeTracker.Clear();
                        continue; // skip to next channel; don't pollute DB
                    }

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

        /// <summary>
        /// v2.14.1 — verify a .img blob is self-consistent before we let it
        /// into the DB. Parses the 36-byte FS6000 header, computes the
        /// expected payload size from declared (Width, Height, BitDepth),
        /// and rejects if the actual byte count is short.
        ///
        /// We deliberately do NOT reject "too many bytes" — some vendor
        /// tools pad with extra metadata after the pixel payload, and
        /// accepting over-sized but valid files is benign (the decoder
        /// only reads Width*Height*bpp + header anyway).
        ///
        /// Returns true when the blob looks complete. Returns false +
        /// fills <paramref name="reason"/> otherwise.
        /// </summary>
        private static bool IsHeaderConsistent(byte[] bytes, string imageType, out string reason)
        {
            if (bytes == null || bytes.Length < FS6000FormatDecoder.HeaderSize)
            {
                reason = $"too small ({bytes?.Length ?? 0} bytes) — header needs at least {FS6000FormatDecoder.HeaderSize}";
                return false;
            }

            FS6000FormatDecoder.Fs6000Header hdr;
            try
            {
                hdr = FS6000FormatDecoder.Fs6000Header.Parse(bytes);
            }
            catch (Exception ex)
            {
                reason = $"header parse failed: {ex.Message}";
                return false;
            }

            // Validate bit depth per channel type. HE / LE are 16-bit;
            // Material is 8-bit. Header bit-depth mismatch is a strong
            // indicator of either a truncated file or a wrong-suffix match.
            int expectedBitDepth = imageType == "Material" ? 8 : 16;
            if (hdr.BitDepth != expectedBitDepth)
            {
                reason = $"bit-depth mismatch (header says {hdr.BitDepth}, expected {expectedBitDepth} for {imageType})";
                return false;
            }

            long pixelCount = (long)hdr.Width * hdr.Height;
            long pixelBytes = pixelCount * (hdr.BitDepth / 8);
            long expected   = FS6000FormatDecoder.HeaderSize + pixelBytes;
            if (bytes.Length < expected)
            {
                reason = $"truncated: {bytes.Length} bytes on disk, header declares {hdr.Width}x{hdr.Height}@{hdr.BitDepth}-bit needing {expected} bytes";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>
        /// Ingest-workspace root. Transient copies of .img files land here
        /// just long enough to read them; cleaned up in a finally-block after
        /// the DB write completes (successfully or not). Kept under Data/
        /// alongside Staging/ and Archive/ so the workspace is visible to
        /// ops, backed up by the same rules that cover the rest of the data
        /// tree, and easy to inspect if something goes wrong.
        /// </summary>
        private static readonly string WorkspaceRoot =
            @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\IngestWorkspace";

        /// <summary>
        /// Two-stage read with retry on the copy step. Rather than reading
        /// directly from the Archive file (where we race file-sync /
        /// antivirus / any other reader), we first copy the file into a
        /// local workspace folder we own, then read from that copy. The
        /// read stage is always on a quiet local file — no lock contention
        /// possible.
        ///
        /// Retry lives on the copy, not the read. 4 attempts with
        /// exponential backoff (500ms → 1s → 2s → 4s). On the 4th failure
        /// we throw; the caller (worker or backfill endpoint) will try
        /// again on its next cycle.
        /// </summary>
        private static async Task<byte[]> CopyThenReadAsync(string sourcePath, CancellationToken ct)
        {
            Directory.CreateDirectory(WorkspaceRoot);
            var workspaceFile = Path.Combine(
                WorkspaceRoot,
                Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(sourcePath));

            try
            {
                // Stage 1: Archive → workspace, with retry.
                const int maxCopyAttempts = 4;
                int delayMs = 500;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        await CopyFileWithShareReadWriteAsync(sourcePath, workspaceFile, ct);
                        break;
                    }
                    catch (IOException) when (attempt < maxCopyAttempts)
                    {
                        await Task.Delay(delayMs, ct);
                        delayMs *= 2;
                    }
                }

                // Stage 2: read from workspace. We own this file; no contention.
                return await File.ReadAllBytesAsync(workspaceFile, ct);
            }
            finally
            {
                // Workspace is purely transient — clear the copy whether or
                // not the rest of the call succeeded. If something deleted
                // the file from under us, the best-effort catch just logs
                // nothing; IngestWorkspace should stay small.
                try
                {
                    if (File.Exists(workspaceFile)) File.Delete(workspaceFile);
                }
                catch
                {
                    // best effort; cleanup worker could sweep later
                }
            }
        }

        /// <summary>
        /// Async file copy that explicitly opens the source with
        /// <see cref="FileShare.ReadWrite"/>, which is more forgiving than
        /// <see cref="File.Copy(string,string,bool)"/>'s default share mode.
        /// If file-sync has the source open for writing (with FileShare.Read),
        /// we still get in.
        /// </summary>
        private static async Task CopyFileWithShareReadWriteAsync(string source, string dest, CancellationToken ct)
        {
            using var src = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var dst = new FileStream(
                dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await src.CopyToAsync(dst, ct);
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

        /// <summary>Channels whose file was readable but structurally invalid.</summary>
        public int InvalidChannels { get; set; }

        /// <summary>Channels that threw during read/insert.</summary>
        public int FailedChannels { get; set; }

        /// <summary>Folder-level error (folder not found, etc.).</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Most recent per-channel error, if any.</summary>
        public string? LastError { get; set; }
    }
}
