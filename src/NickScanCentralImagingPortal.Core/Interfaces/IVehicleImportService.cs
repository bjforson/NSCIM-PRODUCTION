using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for vehicle import business logic
    /// </summary>
    public interface IVehicleImportService
    {
        /// <summary>
        /// Process a VIN record and create a vehicle import entry
        /// </summary>
        Task<VehicleImport> ProcessVINRecordAsync(VehicleData vehicleData, BOEDocument boeDocument, VehicleImportType importType, string? containerNumber = null);

        /// <summary>
        /// Process VIN records from ICUMS JSON data
        /// </summary>
        Task<List<VehicleImport>> ProcessVINRecordsFromBOEAsync(BOEDocument boeDocument, ManifestDetails? manifestDetails, List<ManifestItem>? manifestItems);

        /// <summary>
        /// Validate VIN number format
        /// </summary>
        bool ValidateVIN(string vin);

        /// <summary>
        /// Extract VIN numbers from text content
        /// </summary>
        List<string> ExtractVINsFromText(string text);

        /// <summary>
        /// Get vehicle import by VIN
        /// </summary>
        Task<VehicleImport?> GetVehicleImportByVINAsync(string vin);

        /// <summary>
        /// Search vehicle imports
        /// </summary>
        Task<(List<VehicleImport> Items, int TotalCount)> SearchVehicleImportsAsync(
            int page = 1,
            int pageSize = 50,
            string? searchTerm = null,
            VehicleImportType? importType = null,
            string? processingStatus = null);

        /// <summary>
        /// Get vehicle import statistics
        /// </summary>
        Task<VehicleImportStatistics> GetVehicleImportStatisticsAsync();

        /// <summary>
        /// Update vehicle import processing status
        /// </summary>
        Task UpdateProcessingStatusAsync(int vehicleImportId, string status, string? errorMessage = null);

        /// <summary>
        /// Check if VIN already exists
        /// </summary>
        Task<bool> VINExistsAsync(string vin);

        /// <summary>
        /// Get vehicle imports by date range
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByDateRangeAsync(DateTime fromDate, DateTime toDate);

        /// <summary>
        /// Get vehicle imports by container number
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByContainerNumberAsync(string containerNumber);

        /// <summary>
        /// Delete vehicle import
        /// </summary>
        Task DeleteVehicleImportAsync(int vehicleImportId);
    }
}
