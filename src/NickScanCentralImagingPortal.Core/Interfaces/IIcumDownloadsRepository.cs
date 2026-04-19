using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IIcumDownloadsRepository
    {
        // Downloaded Files
        Task<List<DownloadedFile>> GetPendingFilesAsync();
        Task<DownloadedFile?> GetFileByIdAsync(int id);
        Task<DownloadedFile?> GetFileByNameAsync(string fileName);
        Task<DownloadedFile?> GetFileByHashAsync(string fileHash); // ✅ FIX 4: Get file by content hash
        Task<DownloadedFile?> GetMostRecentDownloadForContainerAsync(string containerNumber); // Deduplication
        Task<int> SaveDownloadedFileAsync(DownloadedFile file);
        Task UpdateFileProcessingStatusAsync(int fileId, string status, string? errorMessage = null, int? recordCount = null);
        Task UpdateFilePathAsync(int fileId, string newFilePath); // ✅ Update file path when found in archive
        Task<bool> FileHasBOEDocumentsAsync(int fileId); // ✅ Check if file was already processed
        Task<int> GetBOEDocumentCountForFileAsync(int fileId); // Get count of BOE documents for a file
        Task<List<BOEDocument>> GetBOEDocumentsByFileIdAsync(int fileId); // Get BOE documents for a file

        // BOE Documents
        Task<int> SaveBOEDocumentAsync(BOEDocument document);
        Task<List<BOEDocument>> GetPendingBOEDocumentsAsync();
        Task<BOEDocument?> GetBOEDocumentByContainerAndDeclarationAsync(string containerNumber, string declarationNumber);
        Task<BOEDocument?> GetCMRByCompositeKeyAsync(string containerNumber, string rotationNumber, string blNumber);
        Task<int> UpgradeCMRToBOEAsync(int cmrId, BOEDocument upgradedDocument);
        Task UpdateBOEDocumentProcessingStatusAsync(int documentId, string status, string? errorMessage = null);
        Task<bool> ContainerHasICUMSDataAsync(string containerNumber);

        // Manifest Items
        Task<int> SaveManifestItemAsync(DownloadedManifestItem item);
        Task<List<DownloadedManifestItem>> GetPendingManifestItemsAsync();
        Task<List<DownloadedManifestItem>> GetManifestItemsByBOEDocumentIdAsync(int boeDocumentId);
        Task UpdateManifestItemProcessingStatusAsync(int itemId, string status, string? errorMessage = null);

        // Bulk Operations - Phase 1 Performance Optimization
        Task<List<int>> SaveManifestItemsBulkAsync(List<DownloadedManifestItem> items);
        Task UpdateManifestItemsStatusBulkAsync(List<int> itemIds, string status);
        Task UpdateBOEDocumentsStatusBulkAsync(List<int> documentIds, string status);

        // Ingestion Logs
        Task<int> SaveIngestionLogAsync(IngestionLog log);
        Task<List<IngestionLog>> GetIngestionLogsAsync(int? fileId = null);

        // Statistics
        Task<DownloadsStatistics> GetStatisticsAsync();

        // Container Download History - Phase 1.2 Deduplication
        Task<ContainerDownloadHistory?> GetRecentDownloadAsync(string containerNumber, int hoursAgo = 24);
        Task<int> SaveDownloadHistoryAsync(ContainerDownloadHistory history);
        Task CleanupOldDownloadHistoryAsync(int daysToKeep = 30);

        // Failed Processing Queue - Phase 2.2 Dead-Letter Queue
        Task<int> AddFailedFileAsync(FailedProcessingQueue failedFile);
        Task<List<FailedProcessingQueue>> GetPendingRetriesAsync(int maxItems = 50);
        Task<List<FailedProcessingQueue>> GetRetryingFilesAsync(int maxItems = 100);
        Task<FailedProcessingQueue?> GetFailedFileByIdAsync(int id);
        Task UpdateFailedFileRetryAsync(int id, int retryCount, DateTime? nextRetryAt, string? errorDetails = null);
        Task MarkFailedFileResolvedAsync(int id);
        Task MarkFailedFileAbandonedAsync(int id, string reason);

        // Ingestion Verification — stores pre-calculated results
        Task SaveVerificationSummaryAsync(int fileId, int verifiedCount, int perfectCount, int partialCount, double avgAccuracy, double lowestAccuracy, string? lowestContainer, string? detailsJson);

        // Archived Files - Archive Solution
        Task<List<DownloadedFile>> GetFilesReadyForArchiveAsync(int hoursOld = 24, int maxFiles = 100);
        Task<int> SaveArchivedFileAsync(ArchivedFile archivedFile);
        Task<ArchivedFile?> GetArchivedFileByIdAsync(int id);
        Task<ArchivedFile?> GetArchivedFileByDownloadedFileIdAsync(int downloadedFileId);
        Task<List<ArchivedFile>> SearchArchivedFilesAsync(string? containerNumber = null, DateTime? startDate = null, DateTime? endDate = null, string? fileType = null, int maxResults = 100);
        Task<List<ArchivedFile>> GetArchivedFilesForRetentionCheckAsync(int retentionYears = 2);
        Task DeleteArchivedFileAsync(int id);
        Task RestoreArchivedFileAsync(int id, string restorePath);

        // BOE Document lookups by container
        Task<List<BOEDocument>> GetBOEDocumentsByContainerNumberAsync(string containerNumber);

        // Cargo search (ILIKE across identifier columns + VIN join)
        Task<List<NickScanCentralImagingPortal.Core.DTOs.CargoGroup.CargoLookupRowDto>> SearchCargoAsync(string query, int limit = 50);
    }
}
