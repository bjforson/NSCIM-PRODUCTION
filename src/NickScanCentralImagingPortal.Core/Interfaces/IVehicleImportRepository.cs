using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository interface for VehicleImport data access operations
    /// </summary>
    public interface IVehicleImportRepository
    {
        /// <summary>
        /// Save a new vehicle import record
        /// </summary>
        Task<int> SaveVehicleImportAsync(VehicleImport vehicleImport);

        /// <summary>
        /// Update an existing vehicle import record
        /// </summary>
        Task UpdateVehicleImportAsync(VehicleImport vehicleImport);

        /// <summary>
        /// Get vehicle import by VIN number
        /// </summary>
        Task<VehicleImport?> GetVehicleImportByVINAsync(string vin);

        /// <summary>
        /// Get vehicle import by ID
        /// </summary>
        Task<VehicleImport?> GetVehicleImportByIdAsync(int id);

        /// <summary>
        /// Get all vehicle imports with pagination
        /// </summary>
        Task<(List<VehicleImport> Items, int TotalCount)> GetVehicleImportsAsync(
            int page = 1,
            int pageSize = 50,
            string? searchTerm = null,
            VehicleImportType? importType = null,
            string? processingStatus = null);

        /// <summary>
        /// Get vehicle imports by declaration number
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByDeclarationNumberAsync(string declarationNumber);

        /// <summary>
        /// Get vehicle imports by container number (for Type 2 records)
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByContainerNumberAsync(string containerNumber);

        /// <summary>
        /// Search vehicle imports by various criteria
        /// </summary>
        Task<List<VehicleImport>> SearchVehicleImportsAsync(
            string? vin = null,
            string? chassisNumber = null,
            string? vehicleType = null,
            string? make = null,
            string? model = null,
            string? declarationNumber = null,
            DateTime? fromDate = null,
            DateTime? toDate = null);

        /// <summary>
        /// Check if a VIN already exists
        /// </summary>
        Task<bool> VINExistsAsync(string vin);

        /// <summary>
        /// Get vehicle imports by processing status
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByStatusAsync(string processingStatus);

        /// <summary>
        /// Update processing status of a vehicle import
        /// </summary>
        Task UpdateProcessingStatusAsync(int vehicleImportId, string status, string? errorMessage = null);

        /// <summary>
        /// Delete a vehicle import record
        /// </summary>
        Task DeleteVehicleImportAsync(int vehicleImportId);

        /// <summary>
        /// Get vehicle import statistics
        /// </summary>
        Task<VehicleImportStatistics> GetVehicleImportStatisticsAsync();

        /// <summary>
        /// Get vehicle imports by date range
        /// </summary>
        Task<List<VehicleImport>> GetVehicleImportsByDateRangeAsync(DateTime fromDate, DateTime toDate);
    }

    /// <summary>
    /// Statistics model for vehicle imports
    /// </summary>
    public class VehicleImportStatistics
    {
        public int TotalVehicleImports { get; set; }
        public int DirectVINCount { get; set; }
        public int VINInContainerCount { get; set; }
        public int PendingCount { get; set; }
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
        public int UniqueVINs { get; set; }
        public int UniqueMakes { get; set; }
        public DateTime? LastImportDate { get; set; }
        public Dictionary<string, int> MakeDistribution { get; set; } = new();
        public Dictionary<string, int> CountryDistribution { get; set; } = new();
    }
}
