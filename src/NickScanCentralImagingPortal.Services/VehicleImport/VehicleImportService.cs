using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using VehicleImportEntity = NickScanCentralImagingPortal.Core.Models.VehicleImport;

namespace NickScanCentralImagingPortal.Services.VehicleImport
{
    /// <summary>
    /// Service implementation for vehicle import business logic
    /// </summary>
    public class VehicleImportService : IVehicleImportService
    {
        private readonly IVehicleImportRepository _vehicleImportRepository;
        private readonly ILogger<VehicleImportService> _logger;
        private const string SERVICE_ID = "VEHICLE-IMPORT-SERVICE";

        public VehicleImportService(
            IVehicleImportRepository vehicleImportRepository,
            ILogger<VehicleImportService> logger)
        {
            _vehicleImportRepository = vehicleImportRepository;
            _logger = logger;
        }

        public async Task<VehicleImportEntity> ProcessVINRecordAsync(VehicleData vehicleData, BOEDocument boeDocument, VehicleImportType importType, string? containerNumber = null)
        {
            try
            {
                _logger.LogInformation("{ServiceId} Processing VIN record: {VIN}", SERVICE_ID, vehicleData.VIN);

                // Check if VIN already exists
                var existingVehicle = await _vehicleImportRepository.GetVehicleImportByVINAsync(vehicleData.VIN);
                if (existingVehicle != null)
                {
                    // Check if data has actually changed before updating
                    if (HasVehicleDataChanged(existingVehicle, vehicleData, boeDocument, importType, containerNumber))
                    {
                        _logger.LogInformation("{ServiceId} VIN {VIN} data changed, updating record", SERVICE_ID, vehicleData.VIN);

                        // Update existing record
                        UpdateVehicleImportFromData(existingVehicle, vehicleData, boeDocument, importType, containerNumber);
                        await _vehicleImportRepository.UpdateVehicleImportAsync(existingVehicle);
                    }
                    else
                    {
                        _logger.LogDebug("{ServiceId} VIN {VIN} already exists with same data, skipping update", SERVICE_ID, vehicleData.VIN);
                    }

                    return existingVehicle;
                }

                // Create new vehicle import record
                var vehicleImport = new VehicleImportEntity
                {
                    VIN = vehicleData.VIN,
                    BOEDocumentId = boeDocument.Id,
                    DeclarationNumber = boeDocument.DeclarationNumber,
                    ImportType = importType,
                    ContainerNumber = containerNumber,
                    ProcessingStatus = "Pending"
                };

                // Populate vehicle-specific data
                UpdateVehicleImportFromData(vehicleImport, vehicleData, boeDocument, importType, containerNumber);

                // Save to database
                var vehicleImportId = await _vehicleImportRepository.SaveVehicleImportAsync(vehicleImport);
                vehicleImport.Id = vehicleImportId;

                _logger.LogInformation("{ServiceId} Successfully processed VIN record: {VIN} with ID: {Id}",
                    SERVICE_ID, vehicleData.VIN, vehicleImportId);

                return vehicleImport;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error processing VIN record: {VIN}", SERVICE_ID, vehicleData.VIN);
                throw;
            }
        }

        public async Task<List<VehicleImportEntity>> ProcessVINRecordsFromBOEAsync(BOEDocument boeDocument, ManifestDetails? manifestDetails, List<ManifestItem>? manifestItems)
        {
            var processedVehicles = new List<VehicleImportEntity>();

            try
            {
                _logger.LogInformation("{ServiceId} Processing VIN records from BOE document: {DeclarationNumber}",
                    SERVICE_ID, boeDocument.DeclarationNumber);

                // Track all VINs found in this BOE document to avoid duplicates
                var allVinsFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var vinDataMap = new Dictionary<string, (VehicleData Data, VehicleImportType Type, string? Container)>(StringComparer.OrdinalIgnoreCase);

                // Check if this is a direct VIN record (Type 1)
                var containerDetails = new IcumApiContainerDetails
                {
                    ContainerNumber = boeDocument.ContainerNumber,
                    ContainerWeight = boeDocument.ContainerWeight,
                    Remarks = boeDocument.ContainerDescription
                };

                bool isDirectVINRecord = false;
                if (!containerDetails.IsValidContainerNumber())
                {
                    var vin = containerDetails.GetVinNumber();
                    if (!string.IsNullOrEmpty(vin))
                    {
                        _logger.LogInformation("{ServiceId} Found direct VIN record (no container): {VIN}", SERVICE_ID, vin);

                        var vehicleData = containerDetails.ExtractVehicleData();
                        allVinsFound.Add(vin);
                        vinDataMap[vin] = (vehicleData, VehicleImportType.DirectVIN, null);
                        isDirectVINRecord = true;
                    }
                }

                // Check for VINs in manifest details and items (Type 2)
                // ONLY process manifest VINs if this BOE has a valid container number
                // If the BOE's ContainerNumber field IS a VIN (DirectVIN), skip manifest processing
                if (!isDirectVINRecord && manifestDetails != null && manifestItems != null)
                {
                    var vinTexts = new List<string>();

                    // Collect text that might contain VINs
                    if (!string.IsNullOrWhiteSpace(manifestDetails.MarksNumbers))
                        vinTexts.Add(manifestDetails.MarksNumbers);
                    if (!string.IsNullOrWhiteSpace(manifestDetails.GoodsDescription))
                        vinTexts.Add(manifestDetails.GoodsDescription);

                    foreach (var item in manifestItems)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Description))
                            vinTexts.Add(item.Description);
                    }

                    // Extract VINs from all text
                    var vins = new List<string>();
                    foreach (var text in vinTexts)
                    {
                        vins.AddRange(ExtractVINsFromText(text));
                    }

                    // Collect unique VINs from manifest (skip if already found as direct VIN)
                    foreach (var vin in vins.Distinct())
                    {
                        if (!string.IsNullOrEmpty(vin) && ValidateVIN(vin))
                        {
                            if (!allVinsFound.Contains(vin))
                            {
                                _logger.LogInformation("{ServiceId} Found VIN in manifest: {VIN}", SERVICE_ID, vin);

                                var vehicleData = new VehicleData { VIN = vin };
                                vehicleData = containerDetails.ExtractVehicleDataFromManifest(manifestDetails, manifestItems);
                                vehicleData.VIN = vin; // Ensure VIN is set correctly

                                allVinsFound.Add(vin);
                                vinDataMap[vin] = (vehicleData, VehicleImportType.VINInContainer, boeDocument.ContainerNumber);
                            }
                            else
                            {
                                _logger.LogDebug("{ServiceId} Skipping duplicate VIN from manifest: {VIN} (already processed as direct VIN)", SERVICE_ID, vin);
                            }
                        }
                    }
                }

                // Now process each unique VIN exactly once
                foreach (var kvp in vinDataMap)
                {
                    var vin = kvp.Key;
                    var (vehicleData, importType, containerNumber) = kvp.Value;

                    var vehicleImport = await ProcessVINRecordAsync(vehicleData, boeDocument, importType, containerNumber);
                    processedVehicles.Add(vehicleImport);
                }

                _logger.LogInformation("{ServiceId} Processed {Count} unique VIN records from BOE document: {DeclarationNumber}",
                    SERVICE_ID, processedVehicles.Count, boeDocument.DeclarationNumber);

                return processedVehicles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error processing VIN records from BOE document: {DeclarationNumber}",
                    SERVICE_ID, boeDocument.DeclarationNumber);
                throw;
            }
        }

        public bool ValidateVIN(string vin)
        {
            if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
                return false;

            // VIN validation: 17 characters, alphanumeric, no I, O, Q
            return vin.All(c => char.IsLetterOrDigit(c) && c != 'I' && c != 'O' && c != 'Q');
        }

        public List<string> ExtractVINsFromText(string text)
        {
            var vins = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return vins;

            // VIN pattern: 17 characters, alphanumeric, no I, O, Q
            var vinPattern = @"\b[A-HJ-NPR-Z0-9]{17}\b";
            var matches = Regex.Matches(text, vinPattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var vin = match.Value.ToUpper();
                if (ValidateVIN(vin))
                {
                    vins.Add(vin);
                }
            }

            return vins.Distinct().ToList();
        }

        public async Task<VehicleImportEntity?> GetVehicleImportByVINAsync(string vin)
        {
            return await _vehicleImportRepository.GetVehicleImportByVINAsync(vin);
        }

        public async Task<(List<VehicleImportEntity> Items, int TotalCount)> SearchVehicleImportsAsync(
            int page = 1,
            int pageSize = 50,
            string? searchTerm = null,
            VehicleImportType? importType = null,
            string? processingStatus = null)
        {
            return await _vehicleImportRepository.GetVehicleImportsAsync(page, pageSize, searchTerm, importType, processingStatus);
        }

        public async Task<VehicleImportStatistics> GetVehicleImportStatisticsAsync()
        {
            return await _vehicleImportRepository.GetVehicleImportStatisticsAsync();
        }

        public async Task UpdateProcessingStatusAsync(int vehicleImportId, string status, string? errorMessage = null)
        {
            await _vehicleImportRepository.UpdateProcessingStatusAsync(vehicleImportId, status, errorMessage);
        }

        public async Task<bool> VINExistsAsync(string vin)
        {
            return await _vehicleImportRepository.VINExistsAsync(vin);
        }

        public async Task<List<VehicleImportEntity>> GetVehicleImportsByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _vehicleImportRepository.GetVehicleImportsByDateRangeAsync(fromDate, toDate);
        }

        public async Task<List<VehicleImportEntity>> GetVehicleImportsByContainerNumberAsync(string containerNumber)
        {
            return await _vehicleImportRepository.GetVehicleImportsByContainerNumberAsync(containerNumber);
        }

        public async Task DeleteVehicleImportAsync(int vehicleImportId)
        {
            await _vehicleImportRepository.DeleteVehicleImportAsync(vehicleImportId);
        }

        /// <summary>
        /// Updates vehicle import record with data from VehicleData and BOEDocument
        /// </summary>
        /// <summary>
        /// Checks if vehicle data has changed compared to existing record
        /// </summary>
        private bool HasVehicleDataChanged(VehicleImportEntity existing, VehicleData newData, BOEDocument boeDocument, VehicleImportType importType, string? containerNumber)
        {
            // Check key fields that might change
            return existing.ChassisNumber != (newData.ChassisNumber ?? newData.VIN) ||
                   existing.VehicleType != newData.VehicleType ||
                   existing.Make != newData.Make ||
                   existing.Model != newData.Model ||
                   existing.VehicleYear != newData.VehicleYear ||
                   existing.EngineCapacity != newData.EngineCapacity ||
                   existing.Weight != (newData.Weight ?? boeDocument.ContainerWeight) ||
                   existing.Quantity != newData.Quantity ||
                   existing.HSCode != newData.HSCode ||
                   existing.FOBValue != newData.FOBValue ||
                   existing.FOBCurrency != newData.FOBCurrency ||
                   existing.DutyPaid != newData.DutyPaid ||
                   existing.ShipperName != (newData.ShipperName ?? boeDocument.ShipperName) ||
                   existing.ConsigneeName != (newData.ConsigneeName ?? boeDocument.ConsigneeName) ||
                   existing.BLNumber != (newData.BLNumber ?? boeDocument.BlNumber) ||
                   existing.HouseBL != (newData.HouseBL ?? boeDocument.HouseBl) ||
                   existing.RotationNumber != (newData.RotationNumber ?? boeDocument.RotationNumber) ||
                   existing.CountryOfOrigin != (newData.CountryOfOrigin ?? boeDocument.CountryOfOrigin) ||
                   existing.ImporterName != boeDocument.ImpName ||
                   existing.ClearanceType != boeDocument.ClearanceType ||
                   existing.CrmsLevel != boeDocument.CrmsLevel ||
                   existing.ImportType != importType ||
                   existing.ContainerNumber != containerNumber ||
                   existing.ProcessingStatus != "Completed";
        }

        private void UpdateVehicleImportFromData(VehicleImportEntity vehicleImport, VehicleData vehicleData, BOEDocument boeDocument, VehicleImportType importType, string? containerNumber)
        {
            // Basic vehicle data
            vehicleImport.ChassisNumber = vehicleData.ChassisNumber ?? vehicleData.VIN;
            vehicleImport.VehicleType = vehicleData.VehicleType;
            vehicleImport.Make = vehicleData.Make;
            vehicleImport.Model = vehicleData.Model;
            vehicleImport.VehicleYear = vehicleData.VehicleYear;
            vehicleImport.EngineCapacity = vehicleData.EngineCapacity;
            vehicleImport.Weight = vehicleData.Weight ?? boeDocument.ContainerWeight;
            vehicleImport.Quantity = vehicleData.Quantity;
            vehicleImport.HSCode = vehicleData.HSCode;
            vehicleImport.FOBValue = vehicleData.FOBValue;
            vehicleImport.FOBCurrency = vehicleData.FOBCurrency;
            vehicleImport.DutyPaid = vehicleData.DutyPaid;

            // Business data
            vehicleImport.ShipperName = vehicleData.ShipperName ?? boeDocument.ShipperName;
            vehicleImport.ConsigneeName = vehicleData.ConsigneeName ?? boeDocument.ConsigneeName;
            vehicleImport.BLNumber = vehicleData.BLNumber ?? boeDocument.BlNumber;
            vehicleImport.HouseBL = vehicleData.HouseBL ?? boeDocument.HouseBl;
            vehicleImport.RotationNumber = vehicleData.RotationNumber ?? boeDocument.RotationNumber;
            vehicleImport.CountryOfOrigin = vehicleData.CountryOfOrigin ?? boeDocument.CountryOfOrigin;

            // BOE document data
            vehicleImport.ImporterName = boeDocument.ImpName;
            vehicleImport.ClearanceType = boeDocument.ClearanceType;
            vehicleImport.CrmsLevel = boeDocument.CrmsLevel;
            vehicleImport.ImportType = importType;
            vehicleImport.ContainerNumber = containerNumber;

            // Processing status
            vehicleImport.ProcessingStatus = "Completed";
            vehicleImport.ProcessedAt = DateTime.UtcNow;
            vehicleImport.UpdatedAt = DateTime.UtcNow;
        }
    }
}
