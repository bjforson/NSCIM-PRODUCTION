using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class IcumDownloadsRepository : IIcumDownloadsRepository
    {
        private readonly IcumDownloadsDbContext _context;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _keyLocks = new();

        public IcumDownloadsRepository(IcumDownloadsDbContext context)
        {
            _context = context;
        }

        // Downloaded Files
        public async Task<List<DownloadedFile>> GetPendingFilesAsync()
        {
            return await _context.DownloadedFiles
                .Where(f => f.ProcessingStatus == "Pending")
                .OrderBy(f => f.DownloadDate)
                .ToListAsync();
        }

        public async Task<DownloadedFile?> GetFileByIdAsync(int id)
        {
            return await _context.DownloadedFiles
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task<DownloadedFile?> GetFileByNameAsync(string fileName)
        {
            return await _context.DownloadedFiles
                .FirstOrDefaultAsync(f => f.FileName == fileName);
        }

        // ✅ FIX 4: Get file by content hash for deduplication
        public async Task<DownloadedFile?> GetFileByHashAsync(string fileHash)
        {
            return await _context.DownloadedFiles
                .FirstOrDefaultAsync(f => f.FileHash == fileHash);
        }

        // ✅ NEW: Check if container was downloaded recently (deduplication)
        public async Task<DownloadedFile?> GetMostRecentDownloadForContainerAsync(string containerNumber)
        {
            return await _context.DownloadedFiles
                .Where(f => f.FileName.Contains(containerNumber))
                .OrderByDescending(f => f.DownloadDate)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveDownloadedFileAsync(DownloadedFile file)
        {
            file.CreatedAt = DateTime.UtcNow;
            file.UpdatedAt = DateTime.UtcNow;
            _context.DownloadedFiles.Add(file);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear(); // ✅ MEMORY FIX
            return file.Id;
        }

        public async Task UpdateFileProcessingStatusAsync(int fileId, string status, string? errorMessage = null, int? recordCount = null)
        {
            var file = await _context.DownloadedFiles.FindAsync(fileId);
            if (file != null)
            {
                var oldStatus = file.ProcessingStatus;
                file.ProcessingStatus = status;
                file.ErrorMessage = errorMessage;
                file.RecordCount = recordCount;
                file.UpdatedAt = DateTime.UtcNow;

                if (status == "Completed")
                {
                    file.ProcessedDate = DateTime.UtcNow;
                }

                // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                // This fixes the "Changes=0" bug where status updates were not persisting
                _context.Entry(file).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                var changeCount = await _context.SaveChangesAsync();

                Console.WriteLine($"[ICUMS-DOWNLOADS-REPO] UpdateFileStatus: FileId={fileId}, {oldStatus}→{status}, Changes={changeCount}, File={file.FileName}");

                // ✅ CRITICAL BUG FIX: Clear change tracker to release tracked entity
                // Without this, files remain "Pending" and get reprocessed infinitely!
                _context.ChangeTracker.Clear();
            }
            else
            {
                Console.WriteLine($"[ICUMS-DOWNLOADS-REPO] UpdateFileStatus: FileId={fileId} NOT FOUND!");
            }
        }

        public async Task SaveVerificationSummaryAsync(int fileId, int verifiedCount, int perfectCount, int partialCount, double avgAccuracy, double lowestAccuracy, string? lowestContainer, string? detailsJson)
        {
            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
UPDATE downloadedfiles SET
    verifieddocumentcount = $1, perfectdocumentcount = $2, partialdocumentcount = $3,
    averageaccuracypercent = $4, lowestaccuracypercent = $5,
    lowestaccuracycontainer = $6, verificationdetails = $7, updatedat = now()
WHERE id = $8;";
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = verifiedCount });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = perfectCount });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = partialCount });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = avgAccuracy });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = lowestAccuracy });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)lowestContainer ?? DBNull.Value });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)detailsJson ?? DBNull.Value });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = fileId });
                await cmd.ExecuteNonQueryAsync();
                _context.ChangeTracker.Clear();
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        // Update file path when found in archive
        public async Task UpdateFilePathAsync(int fileId, string newFilePath)
        {
            var file = await _context.DownloadedFiles.FindAsync(fileId);
            if (file != null)
            {
                file.FilePath = newFilePath;
                file.UpdatedAt = DateTime.UtcNow;
                _context.Entry(file).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        // ✅ NEW: Check if file was already processed (has BOE documents)
        public async Task<bool> FileHasBOEDocumentsAsync(int fileId)
        {
            return await _context.BOEDocuments
                .AnyAsync(b => b.DownloadedFileId == fileId);
        }

        public async Task<int> GetBOEDocumentCountForFileAsync(int fileId)
        {
            return await _context.BOEDocuments
                .CountAsync(b => b.DownloadedFileId == fileId);
        }

        public async Task<List<BOEDocument>> GetBOEDocumentsByFileIdAsync(int fileId)
        {
            return await _context.BOEDocuments
                .Where(b => b.DownloadedFileId == fileId)
                .ToListAsync();
        }

        // BOE Documents
        public async Task<int> SaveBOEDocumentAsync(BOEDocument document)
        {
            // Normalize keys up-front to ensure consistent comparisons
            if (!string.IsNullOrWhiteSpace(document.ContainerNumber))
            {
                document.ContainerNumber = document.ContainerNumber.Trim().ToUpper();
            }
            if (!string.IsNullOrWhiteSpace(document.DeclarationNumber))
            {
                document.DeclarationNumber = document.DeclarationNumber.Trim();
            }

            // In-process per-key lock to avoid concurrent create races in the same service instance
            var lockKey = $"{document.ContainerNumber}|{document.DeclarationNumber}";
            var keyLock = _keyLocks.GetOrAdd(lockKey, _ => new System.Threading.SemaphoreSlim(1, 1));
            await keyLock.WaitAsync();
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    const int maxRetries = 3;
                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                        try
                        {
                            var ensuredId = await EnsureBoeKeyRowAsync(document.ContainerNumber!, document.DeclarationNumber, document.DownloadedFileId, document.DocumentIndex);
                            var entity = await _context.BOEDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == ensuredId);
                            if (entity == null)
                            {
                                await tx.RollbackAsync();
                                await Task.Delay(100 * (attempt + 1));
                                continue;
                            }

                            var resultId = await UpdateExistingDocumentAsync(entity, document, tx);
                            await tx.CommitAsync();
                            return resultId;
                        }
                        catch (PostgresException ex) when (ex.SqlState == "40P01")
                        {
                            await tx.RollbackAsync();
                            await Task.Delay(100 * (attempt + 1));
                            continue;
                        }
                        catch
                        {
                            await tx.RollbackAsync();
                            throw;
                        }
                    }

                    throw new InvalidOperationException($"Failed to upsert BOE after {maxRetries} attempts for container {document.ContainerNumber}, declaration {document.DeclarationNumber}");
                });
            }
            finally
            {
                keyLock.Release();
            }
        }

        private async Task<int> UpdateExistingDocumentAsync(BOEDocument existing, BOEDocument document, IDbContextTransaction? transaction = null)
        {
            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 120;
                var efTx = _context.Database.CurrentTransaction;
                if (efTx != null) cmd.Transaction = efTx.GetDbTransaction();

                cmd.CommandText = @"
UPDATE boedocuments SET
    containerdescription = $1, containeriso = $2, containerquantity = $3, containerweight = $4,
    containersize = $5, sealnumber = $6, truckplatenumber = $7, drivername = $8,
    driverlicense = $9, containerstatus = $10, containerremarks = $11,
    impname = $12, totaldutypaid = $13, crmslevel = $14, expaddress = $15,
    regimecode = $16, noofcontainers = $17, compoffremarks = $18, declarantname = $19,
    expname = $20, impaddress = $21, impexpname = $22, ccvrintelremarks = $23,
    declarationversion = $24, impexpaddress = $25, declarationdate = $26,
    clearancetype = $27, declarantaddress = $28,
    rotationnumber = $29, consigneename = $30, countryoforigin = $31, marksnumbers = $32,
    shippername = $33, shipperaddress = $34, blnumber = $35, deliveryplace = $36,
    housebl = $37, consigneeaddress = $38, goodsdescription = $39,
    isconsolidated = $40, processingstatus = $41, errormessage = $42,
    rawjsondata = CASE WHEN $43 IS NOT NULL AND $43 != '' THEN $43 ELSE rawjsondata END,
    downloadedfileid = CASE WHEN downloadedfileid = 0 AND $44 > 0 THEN $44 ELSE downloadedfileid END,
    declarationnumber = COALESCE($46, declarationnumber),
    -- 1.13.0: CMR upgrade provenance — set ONCE, never overwritten on subsequent updates.
    originalclearancetype = COALESCE(originalclearancetype, $47),
    cmrupgradedat = COALESCE(cmrupgradedat, $48),
    updatedat = now()
WHERE id = $45;";

                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerDescription ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerISO ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerQuantity ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerWeight ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerSize ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.SealNumber ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.TruckPlateNumber ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DriverName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DriverLicense ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerStatus ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ContainerRemarks ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ImpName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.TotalDutyPaid ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.CrmsLevel ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ExpAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.RegimeCode ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.NoOfContainers ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.CompOffRemarks ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeclarantName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ExpName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ImpAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ImpExpName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.CcvrIntelRemarks ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeclarationVersion ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ImpExpAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeclarationDate ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ClearanceType ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeclarantAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.RotationNumber ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ConsigneeName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.CountryOfOrigin ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.MarksNumbers ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ShipperName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ShipperAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.BlNumber ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeliveryPlace ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.HouseBl ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ConsigneeAddress ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.GoodsDescription ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = document.IsConsolidated });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ProcessingStatus ?? "Pending" });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.ErrorMessage ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.RawJsonData ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = document.DownloadedFileId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = existing.Id });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.DeclarationNumber ?? DBNull.Value });
                // 1.13.0: provenance params (positions 47, 48 in the SQL above).
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.OriginalClearanceType ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)document.CmrUpgradedAt ?? DBNull.Value });

                await cmd.ExecuteNonQueryAsync();
                _context.ChangeTracker.Clear();

                return existing.Id;
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        private async Task<int> CreateNewDocumentAsync(BOEDocument document)
        {
            try
            {
                // Create new document
                document.CreatedAt = DateTime.UtcNow;
                document.UpdatedAt = DateTime.UtcNow;
                _context.BOEDocuments.Add(document);
                await _context.SaveChangesAsync();
                // ✅ MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();
                return document.Id;
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException sqlEx
                && sqlEx.SqlState == "23505")
            {
                // Duplicate key during insert - another thread inserted it first
                // Remove the tracked entity and fetch the existing one with normalized keys and fallbacks
                _context.Entry(document).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                var container = (document.ContainerNumber ?? string.Empty).Trim().ToUpper();
                var decl = document.DeclarationNumber?.Trim();

                // Primary lookup: by container + declaration
                var existing = await _context.BOEDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.ContainerNumber == container && d.DeclarationNumber == decl);

                // Fallback for cases where declaration is null/empty (CMR/VIN)
                if (existing == null && string.IsNullOrEmpty(decl))
                {
                    existing = await _context.BOEDocuments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.ContainerNumber == container);
                }

                // Brief delay and retry to allow concurrent insert visibility across contexts
                if (existing == null)
                {
                    for (int i = 0; i < 3 && existing == null; i++)
                    {
                        await Task.Delay(100);
                        existing = await _context.BOEDocuments
                            .AsNoTracking()
                            .FirstOrDefaultAsync(d => d.ContainerNumber == container && d.DeclarationNumber == decl);
                        if (existing == null && string.IsNullOrEmpty(decl))
                        {
                            existing = await _context.BOEDocuments
                                .AsNoTracking()
                                .FirstOrDefaultAsync(d => d.ContainerNumber == container);
                        }
                    }
                }

                return existing?.Id ?? throw new InvalidOperationException($"Container {document.ContainerNumber} was inserted by another thread but cannot be found");
            }
        }

        private async Task<int> EnsureBoeKeyRowAsync(string containerNumber, string? declarationNumber, int downloadedFileId, int documentIndex)
        {
            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 300; // 5 minutes for MERGE operations (may be slow with table locks)
            var efTx = _context.Database.CurrentTransaction;
            if (efTx != null)
            {
                cmd.Transaction = efTx.GetDbTransaction();
            }

            // PostgreSQL upsert using INSERT ... ON CONFLICT
            // Two unique indexes exist: one for (containernumber, declarationnumber) and one for containernumber WHERE declarationnumber IS NULL
            if (declarationNumber != null)
            {
                cmd.CommandText = @"
INSERT INTO boedocuments (containernumber, declarationnumber, downloadedfileid, documentindex, processingstatus, isconsolidated, unmappedfieldsoverflow, createdat, updatedat)
VALUES ($1, $2, $3, $4, 'Pending', false, false, now(), now())
ON CONFLICT (containernumber, declarationnumber)
DO UPDATE SET
    updatedat = now(),
    downloadedfileid = CASE 
        WHEN boedocuments.downloadedfileid IS NULL AND $3 > 0 THEN $3 
        ELSE boedocuments.downloadedfileid 
    END,
    documentindex = CASE 
        WHEN (boedocuments.documentindex IS NULL OR boedocuments.documentindex < 0) AND $4 >= 0 THEN $4
        ELSE boedocuments.documentindex
    END
RETURNING id;";
            }
            else
            {
                cmd.CommandText = @"
INSERT INTO boedocuments (containernumber, declarationnumber, downloadedfileid, documentindex, processingstatus, isconsolidated, unmappedfieldsoverflow, createdat, updatedat)
VALUES ($1, NULL, $3, $4, 'Pending', false, false, now(), now())
ON CONFLICT (containernumber) WHERE declarationnumber IS NULL
DO UPDATE SET
    updatedat = now(),
    downloadedfileid = CASE 
        WHEN boedocuments.downloadedfileid IS NULL AND $3 > 0 THEN $3 
        ELSE boedocuments.downloadedfileid 
    END,
    documentindex = CASE 
        WHEN (boedocuments.documentindex IS NULL OR boedocuments.documentindex < 0) AND $4 >= 0 THEN $4
        ELSE boedocuments.documentindex
    END
RETURNING id;";
            }

            cmd.Parameters.Add(new NpgsqlParameter { Value = containerNumber });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)declarationNumber ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = downloadedFileId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = documentIndex });

            var result = await cmd.ExecuteScalarAsync();
            if (result is int id && id > 0)
            {
                if (shouldClose) await conn.CloseAsync();
                return id;
            }

            // Fallback: select existing
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandTimeout = 300;
            if (efTx != null) cmd2.Transaction = efTx.GetDbTransaction();
            cmd2.CommandText = @"SELECT id FROM boedocuments WHERE containernumber = $1 AND (declarationnumber = $2 OR (declarationnumber IS NULL AND $2 IS NULL)) LIMIT 1";
            cmd2.Parameters.Add(new NpgsqlParameter { Value = containerNumber });
            cmd2.Parameters.Add(new NpgsqlParameter { Value = (object?)declarationNumber ?? DBNull.Value });
            var fallback = await cmd2.ExecuteScalarAsync();
            if (fallback is int id2 && id2 > 0)
            {
                if (shouldClose) await conn.CloseAsync();
                return id2;
            }

            if (shouldClose) await conn.CloseAsync();
            throw new InvalidOperationException($"Failed to ensure BOE key row for container {containerNumber} / declaration {declarationNumber}");
        }

        public async Task<List<BOEDocument>> GetPendingBOEDocumentsAsync()
        {
            // ✅ CRITICAL MEMORY FIX: Limit batch size to prevent loading thousands of containers
            // Process max 100 containers per cycle to prevent memory explosion
            // ✅ FIX REPROCESSING: Exclude documents that have already been transferred
            return await _context.BOEDocuments
                .Where(d => d.ProcessingStatus == "Completed" &&
                           d.ProcessingStatus != "Transferred" &&
                           d.ProcessingStatus != "TransferFailed")
                .OrderBy(d => d.CreatedAt)
                .Take(100) // ✅ HARD LIMIT - prevents 4,000+ containers loading at once
                .ToListAsync();
        }

        public async Task<BOEDocument?> GetBOEDocumentByContainerAndDeclarationAsync(string containerNumber, string declarationNumber)
        {
            return await _context.BOEDocuments
                .FirstOrDefaultAsync(d => d.ContainerNumber == containerNumber && d.DeclarationNumber == declarationNumber);
        }

        public async Task<BOEDocument?> GetCMRByCompositeKeyAsync(string containerNumber, string rotationNumber, string blNumber)
        {
            return await _context.BOEDocuments
                .FirstOrDefaultAsync(d => d.ContainerNumber == containerNumber
                    && d.RotationNumber == rotationNumber
                    && d.BlNumber == blNumber
                    && d.ClearanceType == "CMR");
        }

        public async Task<int> UpgradeCMRToBOEAsync(int cmrId, BOEDocument upgradedDocument)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                try
                {
                    var existing = await _context.BOEDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == cmrId);
                    if (existing == null)
                    {
                        throw new InvalidOperationException($"CMR record {cmrId} not found for upgrade");
                    }

                    // 1.13.0: capture upgrade provenance. Stamp the *existing* row's
                    // clearance type as the "original" before we overwrite it. The
                    // UPDATE statement uses COALESCE on these fields so a second
                    // upgrade (which shouldn't happen, but is theoretically possible
                    // if ICUMS resends a corrected IM message) won't clobber the
                    // first upgrade's provenance.
                    if (string.IsNullOrWhiteSpace(upgradedDocument.OriginalClearanceType))
                    {
                        upgradedDocument.OriginalClearanceType = existing.ClearanceType;
                    }
                    if (!upgradedDocument.CmrUpgradedAt.HasValue)
                    {
                        upgradedDocument.CmrUpgradedAt = DateTime.UtcNow;
                    }

                    var resultId = await UpdateExistingDocumentAsync(existing, upgradedDocument, tx);
                    await tx.CommitAsync();
                    return resultId;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task UpdateBOEDocumentProcessingStatusAsync(int documentId, string status, string? errorMessage = null)
        {
            // ✅ DEADLOCK FIX: Add retry logic for transient SQL deadlock errors
            // Deadlocks are transient and can be safely retried
            const int maxRetries = 3;
            var retryDelay = TimeSpan.FromMilliseconds(100);

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var document = await _context.BOEDocuments.AsTracking().FirstOrDefaultAsync(d => d.Id == documentId);
                    if (document != null)
                    {
                        document.ProcessingStatus = status;
                        document.ErrorMessage = errorMessage;
                        document.UpdatedAt = DateTime.UtcNow;

                        if (status == "Transferred")
                        {
                            document.ProcessedAt = DateTime.UtcNow;
                        }

                        await _context.SaveChangesAsync();
                        _context.ChangeTracker.Clear(); // ✅ MEMORY FIX
                        return; // Success - exit retry loop
                    }
                    else
                    {
                        // Document not found - no need to retry
                        return;
                    }
                }
                catch (Npgsql.PostgresException sqlEx) when (sqlEx.SqlState == "40P01") // Deadlock victim (Error 1205)
                {
                    if (attempt < maxRetries - 1)
                    {
                        // Clear change tracker before retry to avoid stale entity state
                        _context.ChangeTracker.Clear();

                        // Exponential backoff: 100ms, 200ms, 400ms
                        await Task.Delay(retryDelay);
                        retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);

                        // Retry - deadlocks are transient
                        continue;
                    }
                    else
                    {
                        // Final attempt failed - rethrow (service layer will log)
                        throw;
                    }
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException sqlEx && sqlEx.SqlState == "40P01")
                {
                    // Handle deadlock wrapped in DbUpdateException
                    if (attempt < maxRetries - 1)
                    {
                        _context.ChangeTracker.Clear();
                        await Task.Delay(retryDelay);
                        retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);

                        // Retry - deadlocks are transient
                        continue;
                    }
                    else
                    {
                        // Final attempt failed - rethrow (service layer will log)
                        throw;
                    }
                }
            }
        }

        // Manifest Items
        public async Task<int> SaveManifestItemAsync(DownloadedManifestItem item)
        {
            item.CreatedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            _context.ManifestItems.Add(item);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear(); // ✅ MEMORY FIX
            return item.Id;
        }

        public async Task<List<DownloadedManifestItem>> GetPendingManifestItemsAsync()
        {
            // ✅ CRITICAL FIX: Add date filter to prevent loading ALL ManifestItems into buffer pool (was using 7 GB!)
            // Only load manifest items from the last 30 days to reduce memory usage
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            return await _context.ManifestItems
                .Where(m => m.ProcessingStatus == "Completed" && m.CreatedAt >= thirtyDaysAgo)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<DownloadedManifestItem>> GetManifestItemsByBOEDocumentIdAsync(int boeDocumentId)
        {
            return await _context.ManifestItems
                .Where(m => m.BOEDocumentId == boeDocumentId && m.ProcessingStatus == "Completed")
                .OrderBy(m => m.ItemIndex)
                .ToListAsync();
        }

        public async Task UpdateManifestItemProcessingStatusAsync(int itemId, string status, string? errorMessage = null)
        {
            var item = await _context.ManifestItems.AsTracking().FirstOrDefaultAsync(m => m.Id == itemId);
            if (item != null)
            {
                item.ProcessingStatus = status;
                item.ErrorMessage = errorMessage;
                item.UpdatedAt = DateTime.UtcNow;

                if (status == "Transferred")
                {
                    item.ProcessedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                // ✅ MEMORY FIX: Clear change tracker
                _context.ChangeTracker.Clear();
            }
        }

        // ===================================================================
        // BULK OPERATIONS - Phase 1 Performance Optimization
        // ===================================================================

        public async Task<List<int>> SaveManifestItemsBulkAsync(List<DownloadedManifestItem> items)
        {
            if (items == null || !items.Any())
                return new List<int>();

            var now = DateTime.UtcNow;

            // Set timestamps for all items
            foreach (var item in items)
            {
                item.CreatedAt = now;
                item.UpdatedAt = now;
            }

            // Bulk add all items in a single operation
            _context.ManifestItems.AddRange(items);
            await _context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked manifest items
            // This prevents accumulation of thousands of tracked entities
            var ids = items.Select(i => i.Id).ToList();
            _context.ChangeTracker.Clear();

            // Return all IDs
            return ids;
        }

        public async Task UpdateManifestItemsStatusBulkAsync(List<int> itemIds, string status)
        {
            if (itemIds == null || !itemIds.Any())
                return;

            var now = DateTime.UtcNow;

            // ✅ FIX: Use direct SQL UPDATE to avoid EF Core CTE generation issues with SQL Server 2014
            // SQL Server 2014 requires semicolons before CTEs, which EF Core doesn't always generate
            // Use batched updates to avoid parameter limit issues (SQL Server max 2100 parameters)
            const int batchSize = 1000;
            var batches = itemIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();

            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }

            try
            {
                foreach (var batch in batches)
                {
                    // Build parameterized WHERE IN clause
                    var parameters = new List<NpgsqlParameter>();
                    var paramNames = new List<string>();

                    for (int i = 0; i < batch.Count; i++)
                    {
                        var paramName = $"@id{i}";
                        paramNames.Add(paramName);
                        parameters.Add(new NpgsqlParameter(paramName, batch[i]));
                    }

                    var whereClause = string.Join(",", paramNames);
                    var processedAtClause = status == "Transferred" ? ", ProcessedAt = @now" : "";

                    // Use direct UPDATE with semicolon prefix to ensure proper SQL Server 2014 syntax
                    var sql = $@";UPDATE ManifestItems 
SET ProcessingStatus = @status, UpdatedAt = @now{processedAtClause}
WHERE Id IN ({whereClause})";

                    parameters.Add(new NpgsqlParameter("@status", status));
                    parameters.Add(new NpgsqlParameter("@now", now));

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.AddRange(parameters.ToArray());
                    // ✅ FIX: Set command timeout to prevent timeout errors on large bulk updates
                    // Default 30 seconds may not be enough for 1000+ record updates
                    cmd.CommandTimeout = 300; // 5 minutes for bulk operations

                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }

            // Clear change tracker since we used raw SQL
            _context.ChangeTracker.Clear();
        }

        public async Task UpdateBOEDocumentsStatusBulkAsync(List<int> documentIds, string status)
        {
            if (documentIds == null || !documentIds.Any())
                return;

            var now = DateTime.UtcNow;

            // ✅ SQL Server 2014 FIX: Use direct SQL UPDATE to avoid EF Core CTE generation issues
            // SQL Server 2014 requires semicolons before CTEs, which EF Core doesn't always generate
            // Use batched updates to avoid parameter limit issues (SQL Server max 2100 parameters)
            const int batchSize = 1000;
            var batches = documentIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();

            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }

            try
            {
                foreach (var batch in batches)
                {
                    // Build parameterized WHERE IN clause
                    var parameters = new List<NpgsqlParameter>();
                    var paramNames = new List<string>();

                    for (int i = 0; i < batch.Count; i++)
                    {
                        var paramName = $"@id{i}";
                        paramNames.Add(paramName);
                        parameters.Add(new NpgsqlParameter(paramName, batch[i]));
                    }

                    var whereClause = string.Join(",", paramNames);
                    var processedAtClause = status == "Transferred" ? ", ProcessedAt = @now" : "";

                    // Use direct UPDATE with semicolon prefix to ensure proper SQL Server 2014 syntax
                    var sql = $@";UPDATE BOEDocuments 
SET ProcessingStatus = @status, UpdatedAt = @now{processedAtClause}
WHERE Id IN ({whereClause})";

                    parameters.Add(new NpgsqlParameter("@status", status));
                    parameters.Add(new NpgsqlParameter("@now", now));

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.AddRange(parameters.ToArray());
                    // ✅ FIX: Set command timeout to prevent timeout errors on large bulk updates
                    // Default 30 seconds may not be enough for 1000+ record updates
                    cmd.CommandTimeout = 300; // 5 minutes for bulk operations

                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }

            // Clear change tracker since we used raw SQL
            _context.ChangeTracker.Clear();
            return;
        }

        // Ingestion Logs
        public async Task<int> SaveIngestionLogAsync(IngestionLog log)
        {
            _context.IngestionLogs.Add(log);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear(); // ✅ MEMORY FIX
            return log.Id;
        }

        public async Task UpdateIngestionLogAsync(int logId, string status, DateTime endTime, int? recordsProcessed = null, string? errorMessage = null, string? details = null)
        {
            var log = await _context.IngestionLogs.FindAsync(logId);
            if (log == null) return;
            log.Status = status;
            log.EndTime = endTime;
            if (recordsProcessed.HasValue) log.RecordsProcessed = recordsProcessed;
            if (errorMessage != null) log.ErrorMessage = errorMessage;
            if (details != null) log.Details = details;
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }

        public async Task AddIngestionWarningsAsync(int boeDocumentId, IEnumerable<string> warnings)
        {
            var list = warnings?.Where(w => !string.IsNullOrWhiteSpace(w)).ToList() ?? new List<string>();
            if (list.Count == 0) return;

            var boe = await _context.BOEDocuments.FindAsync(boeDocumentId);
            if (boe == null) return;

            // Merge with any existing warnings, dedupe, cap at column max (4000).
            var existing = string.IsNullOrWhiteSpace(boe.IngestionWarnings)
                ? new List<string>()
                : boe.IngestionWarnings.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var merged = existing.Concat(list).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var joined = string.Join("\n", merged);
            if (joined.Length > 4000) joined = joined.Substring(0, 4000);

            boe.IngestionWarnings = joined;
            boe.HasIngestionWarnings = true;
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }

        public async Task<List<IngestionLog>> GetIngestionLogsAsync(int? fileId = null)
        {
            var query = _context.IngestionLogs.AsQueryable();

            if (fileId.HasValue)
            {
                query = query.Where(l => l.DownloadedFileId == fileId.Value);
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
        }

        // Statistics
        public async Task<DownloadsStatistics> GetStatisticsAsync()
        {
            // ✅ CRITICAL FIX: Use database aggregation instead of loading all data into memory
            // This prevents loading 7 GB of ManifestItems just to count them
            var totalFiles = await _context.DownloadedFiles.CountAsync();
            var pendingFiles = await _context.DownloadedFiles.CountAsync(f => f.ProcessingStatus == "Pending");
            var processingFiles = await _context.DownloadedFiles.CountAsync(f => f.ProcessingStatus == "Processing");
            var completedFiles = await _context.DownloadedFiles.CountAsync(f => f.ProcessingStatus == "Completed");
            var failedFiles = await _context.DownloadedFiles.CountAsync(f => f.ProcessingStatus == "Failed");
            var totalBOEDocuments = await _context.BOEDocuments.CountAsync();
            var totalManifestItems = await _context.ManifestItems.CountAsync();
            var lastProcessedDate = await _context.DownloadedFiles
                .MaxAsync(f => (DateTime?)f.ProcessedDate);
            var totalDataSize = await _context.DownloadedFiles.SumAsync(f => (long)f.FileSize);

            return new DownloadsStatistics
            {
                TotalFiles = totalFiles,
                PendingFiles = pendingFiles,
                ProcessingFiles = processingFiles,
                CompletedFiles = completedFiles,
                FailedFiles = failedFiles,
                TotalBOEDocuments = totalBOEDocuments,
                TotalManifestItems = totalManifestItems,
                LastProcessedDate = lastProcessedDate,
                TotalDataSize = totalDataSize
            };
        }

        public async Task<bool> ContainerHasICUMSDataAsync(string containerNumber)
        {
            return await _context.BOEDocuments
                .AnyAsync(b => b.ContainerNumber == containerNumber);
        }

        // ===================================================================
        // CONTAINER DOWNLOAD HISTORY - Phase 1.2 Deduplication
        // ===================================================================

        public async Task<ContainerDownloadHistory?> GetRecentDownloadAsync(string containerNumber, int hoursAgo = 24)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-hoursAgo);

            return await _context.ContainerDownloadHistory
                .Where(h => h.ContainerNumber == containerNumber && h.DownloadedAt >= cutoffTime)
                .OrderByDescending(h => h.DownloadedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveDownloadHistoryAsync(ContainerDownloadHistory history)
        {
            history.CreatedAt = DateTime.UtcNow;
            _context.ContainerDownloadHistory.Add(history);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
            return history.Id;
        }

        public async Task CleanupOldDownloadHistoryAsync(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

            var oldRecords = await _context.ContainerDownloadHistory
                .Where(h => h.DownloadedAt < cutoffDate)
                .ToListAsync();

            if (oldRecords.Any())
            {
                _context.ContainerDownloadHistory.RemoveRange(oldRecords);
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        // ===================================================================
        // FAILED PROCESSING QUEUE - Phase 2.2 Dead-Letter Queue
        // ===================================================================

        public async Task<int> AddFailedFileAsync(FailedProcessingQueue failedFile)
        {
            failedFile.CreatedAt = DateTime.UtcNow;
            failedFile.UpdatedAt = DateTime.UtcNow;
            _context.FailedProcessingQueue.Add(failedFile);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
            return failedFile.Id;
        }

        public async Task<List<FailedProcessingQueue>> GetPendingRetriesAsync(int maxItems = 50)
        {
            var now = DateTime.UtcNow;

            return await _context.FailedProcessingQueue
                .Where(f => f.Status == "Pending" &&
                           (f.NextRetryAt == null || f.NextRetryAt <= now) &&
                           f.RetryCount < f.MaxRetries)
                .OrderBy(f => f.NextRetryAt ?? f.FailedAt)
                .Take(maxItems)
                .ToListAsync();
        }

        public async Task<List<FailedProcessingQueue>> GetRetryingFilesAsync(int maxItems = 100)
        {
            return await _context.FailedProcessingQueue
                .Where(f => f.Status == "Retrying")
                .OrderBy(f => f.UpdatedAt)
                .Take(maxItems)
                .ToListAsync();
        }

        public async Task<FailedProcessingQueue?> GetFailedFileByIdAsync(int id)
        {
            return await _context.FailedProcessingQueue.FindAsync(id);
        }

        public async Task UpdateFailedFileRetryAsync(int id, int retryCount, DateTime? nextRetryAt, string? errorDetails = null)
        {
            var failedFile = await _context.FailedProcessingQueue.FindAsync(id);
            if (failedFile != null)
            {
                failedFile.RetryCount = retryCount;
                failedFile.NextRetryAt = nextRetryAt;
                failedFile.Status = "Retrying";
                failedFile.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(errorDetails))
                {
                    failedFile.ErrorDetails = errorDetails;
                }

                // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                _context.Entry(failedFile).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        public async Task MarkFailedFileResolvedAsync(int id)
        {
            var failedFile = await _context.FailedProcessingQueue.FindAsync(id);
            if (failedFile != null)
            {
                failedFile.Status = "Resolved";
                failedFile.ResolvedAt = DateTime.UtcNow;
                failedFile.UpdatedAt = DateTime.UtcNow;

                // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                _context.Entry(failedFile).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        public async Task MarkFailedFileAbandonedAsync(int id, string reason)
        {
            var failedFile = await _context.FailedProcessingQueue.FindAsync(id);
            if (failedFile != null)
            {
                failedFile.Status = "Abandoned";
                failedFile.ErrorDetails = $"{failedFile.ErrorDetails}\nAbandoned: {reason}";
                failedFile.UpdatedAt = DateTime.UtcNow;

                // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                _context.Entry(failedFile).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        // ===================================================================
        // ARCHIVED FILES - Archive Solution
        // ===================================================================

        public async Task<List<DownloadedFile>> GetFilesReadyForArchiveAsync(int hoursOld = 24, int maxFiles = 100)
        {
            var cutoffDate = DateTime.UtcNow.AddHours(-hoursOld);

            return await _context.DownloadedFiles
                .Where(f => (f.ProcessingStatus == "Completed" || f.ProcessingStatus == "Transferred") &&
                           f.ProcessedDate.HasValue &&
                           f.ProcessedDate.Value < cutoffDate &&
                           !_context.ArchivedFiles.Any(a => a.DownloadedFileId == f.Id))
                .OrderBy(f => f.ProcessedDate)
                .Take(maxFiles)
                .ToListAsync();
        }

        public async Task<int> SaveArchivedFileAsync(ArchivedFile archivedFile)
        {
            archivedFile.CreatedAt = DateTime.UtcNow;
            archivedFile.UpdatedAt = DateTime.UtcNow;
            _context.ArchivedFiles.Add(archivedFile);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
            return archivedFile.Id;
        }

        public async Task<ArchivedFile?> GetArchivedFileByIdAsync(int id)
        {
            return await _context.ArchivedFiles.FindAsync(id);
        }

        public async Task<ArchivedFile?> GetArchivedFileByDownloadedFileIdAsync(int downloadedFileId)
        {
            return await _context.ArchivedFiles
                .Where(a => a.DownloadedFileId == downloadedFileId && !a.IsRestored)
                .OrderByDescending(a => a.ArchivedDate)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ArchivedFile>> SearchArchivedFilesAsync(string? containerNumber = null, DateTime? startDate = null, DateTime? endDate = null, string? fileType = null, int maxResults = 100)
        {
            var query = _context.ArchivedFiles.AsQueryable();

            if (!string.IsNullOrEmpty(containerNumber))
            {
                query = query.Where(a => a.ContainerNumbers != null && a.ContainerNumbers.Contains(containerNumber));
            }

            if (startDate.HasValue)
            {
                query = query.Where(a => a.ArchivedDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(a => a.ArchivedDate <= endDate.Value);
            }

            if (!string.IsNullOrEmpty(fileType))
            {
                query = query.Where(a => a.FileType == fileType);
            }

            return await query
                .OrderByDescending(a => a.ArchivedDate)
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<List<ArchivedFile>> GetArchivedFilesForRetentionCheckAsync(int retentionYears = 2)
        {
            var cutoffDate = DateTime.UtcNow.AddYears(-retentionYears);

            return await _context.ArchivedFiles
                .Where(a => a.ArchivedDate < cutoffDate && !a.IsRestored)
                .ToListAsync();
        }

        public async Task DeleteArchivedFileAsync(int id)
        {
            var archivedFile = await _context.ArchivedFiles.FindAsync(id);
            if (archivedFile != null)
            {
                _context.ArchivedFiles.Remove(archivedFile);
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        public async Task RestoreArchivedFileAsync(int id, string restorePath)
        {
            var archivedFile = await _context.ArchivedFiles.AsTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (archivedFile != null)
            {
                archivedFile.IsRestored = true;
                archivedFile.RestoredDate = DateTime.UtcNow;
                archivedFile.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        public async Task<List<BOEDocument>> GetBOEDocumentsByContainerNumberAsync(string containerNumber)
        {
            return await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => b.ContainerNumber == containerNumber)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<NickScanCentralImagingPortal.Core.DTOs.CargoGroup.CargoLookupRowDto>> SearchCargoAsync(string query, int limit = 50)
        {
            var pattern = $"%{query.Trim()}%";

            // BOE document matches across identifier columns
            var boeMatches = await _context.BOEDocuments
                .AsNoTracking()
                .Where(b => EF.Functions.ILike(b.ContainerNumber, pattern)
                         || EF.Functions.ILike(b.DeclarationNumber ?? "", pattern)
                         || EF.Functions.ILike(b.BlNumber ?? "", pattern)
                         || EF.Functions.ILike(b.MasterBlNumber ?? "", pattern)
                         || EF.Functions.ILike(b.HouseBl ?? "", pattern)
                         || EF.Functions.ILike(b.RotationNumber ?? "", pattern))
                .OrderByDescending(b => b.UpdatedAt)
                .Take(limit)
                .Select(b => new NickScanCentralImagingPortal.Core.DTOs.CargoGroup.CargoLookupRowDto
                {
                    BoeDocumentId = b.Id,
                    ContainerNumber = b.ContainerNumber,
                    BlNumber = b.BlNumber,
                    MasterBlNumber = b.MasterBlNumber,
                    HouseBl = b.HouseBl,
                    DeclarationNumber = b.DeclarationNumber,
                    RotationNumber = b.RotationNumber,
                    ClearanceType = b.ClearanceType,
                    IsConsolidated = b.IsConsolidated,
                    ImpExpName = b.ImpExpName,
                    DeclarantName = b.DeclarantName,
                    UpdatedAt = b.UpdatedAt,
                    MatchedField =
                        EF.Functions.ILike(b.ContainerNumber, pattern) ? "ContainerNumber" :
                        EF.Functions.ILike(b.MasterBlNumber ?? "", pattern) ? "MasterBl" :
                        EF.Functions.ILike(b.HouseBl ?? "", pattern) ? "HouseBl" :
                        EF.Functions.ILike(b.BlNumber ?? "", pattern) ? "BlNumber" :
                        EF.Functions.ILike(b.DeclarationNumber ?? "", pattern) ? "DeclarationNumber" :
                        EF.Functions.ILike(b.RotationNumber ?? "", pattern) ? "RotationNumber" :
                        "Other",
                })
                .ToListAsync();

            // VIN matches — join back to BOE
            var remaining = Math.Max(0, limit - boeMatches.Count);
            if (remaining > 0)
            {
                var alreadyMatchedIds = boeMatches.Select(m => m.BoeDocumentId).ToHashSet();

                var vinJoined = await (
                    from v in _context.VehicleImports.AsNoTracking()
                    join b in _context.BOEDocuments.AsNoTracking()
                        on v.BOEDocumentId equals b.Id
                    where EF.Functions.ILike(v.VIN, pattern)
                    orderby b.UpdatedAt descending
                    select new NickScanCentralImagingPortal.Core.DTOs.CargoGroup.CargoLookupRowDto
                    {
                        BoeDocumentId = b.Id,
                        ContainerNumber = b.ContainerNumber,
                        BlNumber = b.BlNumber,
                        MasterBlNumber = b.MasterBlNumber,
                        HouseBl = b.HouseBl,
                        DeclarationNumber = b.DeclarationNumber,
                        RotationNumber = b.RotationNumber,
                        ClearanceType = b.ClearanceType,
                        IsConsolidated = b.IsConsolidated,
                        ImpExpName = b.ImpExpName,
                        DeclarantName = b.DeclarantName,
                        UpdatedAt = b.UpdatedAt,
                        MatchedField = "VIN",
                        MatchedValue = v.VIN,
                    })
                    .Take(remaining * 2)
                    .ToListAsync();

                // Deduplicate against BOE matches
                var deduplicated = vinJoined
                    .Where(v => !alreadyMatchedIds.Contains(v.BoeDocumentId))
                    .DistinctBy(v => v.BoeDocumentId)
                    .Take(remaining);

                boeMatches.AddRange(deduplicated);
            }

            return boeMatches;
        }
    }
}
