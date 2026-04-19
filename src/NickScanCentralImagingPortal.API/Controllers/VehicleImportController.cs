using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class VehicleImportController : ControllerBase
    {
        private readonly IVehicleImportService _vehicleImportService;
        private readonly ThrottledLogger _logger;
        private const string SERVICE_ID = "VEHICLE-IMPORT-API";

        public VehicleImportController(
            IVehicleImportService vehicleImportService,
            ILogger<VehicleImportController> logger)
        {
            _vehicleImportService = vehicleImportService;
            _logger = new ThrottledLogger(logger, SERVICE_ID);
        }

        /// <summary>
        /// Get vehicle import by VIN number
        /// </summary>
        [HttpGet("vin/{vin}")]
        public async Task<ActionResult<VehicleImport>> GetVehicleImportByVIN(string vin)
        {
            try
            {
                _logger.LogInfo("GetVehicleImportByVIN", "Getting vehicle import for VIN: {VIN}", new { VIN = vin });

                var vehicleImport = await _vehicleImportService.GetVehicleImportByVINAsync(vin);

                if (vehicleImport == null)
                {
                    _logger.LogWarning("Vehicle import not found for VIN: {VIN}", vin);
                    return NotFound($"Vehicle import with VIN {vin} not found");
                }

                return Ok(vehicleImport);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetVehicleImportByVIN", "Error getting vehicle import for VIN: {VIN}", ex, new { VIN = vin });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Search vehicle imports with pagination and filters
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<VehicleImport>>> SearchVehicleImports(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? searchTerm = null,
            [FromQuery] VehicleImportType? importType = null,
            [FromQuery] string? processingStatus = null)
        {
            try
            {
                _logger.LogInfo("SearchVehicleImports", "Searching vehicle imports - Page: {Page}, PageSize: {PageSize}, SearchTerm: {SearchTerm}",
                    new { Page = page, PageSize = pageSize, SearchTerm = searchTerm });

                var (items, totalCount) = await _vehicleImportService.SearchVehicleImportsAsync(
                    page, pageSize, searchTerm, importType, processingStatus);

                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var result = new PagedResult<VehicleImport>
                {
                    Data = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };

                _logger.LogInfo("SearchVehicleImports", "Found {Count} vehicle imports", new { Count = totalCount });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("SearchVehicleImports", "Error searching vehicle imports", ex, new { Page = page, PageSize = pageSize });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get vehicle import statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<VehicleImportStatistics>> GetVehicleImportStatistics()
        {
            try
            {
                _logger.LogInfo("GetVehicleImportStatistics", "Getting vehicle import statistics");

                var statistics = await _vehicleImportService.GetVehicleImportStatisticsAsync();

                _logger.LogInfo("GetVehicleImportStatistics", "Retrieved vehicle import statistics - Total: {Total}",
                    new { Total = statistics.TotalVehicleImports });

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetVehicleImportStatistics", "Error getting vehicle import statistics", ex);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get vehicle imports by container number (for Type 2 records)
        /// </summary>
        [HttpGet("container/{containerNumber}")]
        public async Task<ActionResult<List<VehicleImport>>> GetVehicleImportsByContainer(string containerNumber)
        {
            try
            {
                _logger.LogInfo("GetVehicleImportsByContainer", "Getting vehicle imports for container: {ContainerNumber}",
                    new { ContainerNumber = containerNumber });

                var vehicleImports = await _vehicleImportService.GetVehicleImportsByContainerNumberAsync(containerNumber);

                _logger.LogInfo("GetVehicleImportsByContainer", "Found {Count} vehicle imports for container {ContainerNumber}",
                    new { Count = vehicleImports.Count, ContainerNumber = containerNumber });

                return Ok(vehicleImports);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetVehicleImportsByContainer", "Error getting vehicle imports for container: {ContainerNumber}",
                    ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get vehicle imports by date range
        /// </summary>
        [HttpGet("daterange")]
        public async Task<ActionResult<List<VehicleImport>>> GetVehicleImportsByDateRange(
            [FromQuery] DateTime fromDate,
            [FromQuery] DateTime toDate)
        {
            try
            {
                _logger.LogInfo("GetVehicleImportsByDateRange", "Getting vehicle imports from {FromDate} to {ToDate}",
                    new { FromDate = fromDate, ToDate = toDate });

                var vehicleImports = await _vehicleImportService.GetVehicleImportsByDateRangeAsync(fromDate, toDate);

                _logger.LogInfo("GetVehicleImportsByDateRange", "Found {Count} vehicle imports in date range",
                    new { Count = vehicleImports.Count });

                return Ok(vehicleImports);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetVehicleImportsByDateRange", "Error getting vehicle imports by date range",
                    ex, new { FromDate = fromDate, ToDate = toDate });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update vehicle import processing status
        /// </summary>
        [HttpPut("{vehicleImportId}/status")]
        public async Task<ActionResult> UpdateProcessingStatus(
            int vehicleImportId,
            [FromBody] VehicleImportUpdateStatusRequest request)
        {
            try
            {
                _logger.LogInfo("UpdateProcessingStatus", "Updating processing status for vehicle import {Id} to {Status}",
                    new { Id = vehicleImportId, Status = request.Status });

                await _vehicleImportService.UpdateProcessingStatusAsync(vehicleImportId, request.Status, request.ErrorMessage);

                _logger.LogInfo("UpdateProcessingStatus", "Successfully updated processing status for vehicle import {Id}",
                    new { Id = vehicleImportId });

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError("UpdateProcessingStatus", "Error updating processing status for vehicle import {Id}",
                    ex, new { Id = vehicleImportId });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Delete vehicle import
        /// </summary>
        [HttpDelete("{vehicleImportId}")]
        public async Task<ActionResult> DeleteVehicleImport(int vehicleImportId)
        {
            try
            {
                _logger.LogInfo("DeleteVehicleImport", "Deleting vehicle import {Id}", new { Id = vehicleImportId });

                await _vehicleImportService.DeleteVehicleImportAsync(vehicleImportId);

                _logger.LogInfo("DeleteVehicleImport", "Successfully deleted vehicle import {Id}", new { Id = vehicleImportId });

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError("DeleteVehicleImport", "Error deleting vehicle import {Id}", ex, new { Id = vehicleImportId });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Validate VIN number format
        /// </summary>
        [HttpGet("validate/{vin}")]
        public ActionResult<VinValidationResult> ValidateVIN(string vin)
        {
            try
            {
                var isValid = _vehicleImportService.ValidateVIN(vin);

                var result = new VinValidationResult
                {
                    VIN = vin,
                    IsValid = isValid,
                    Message = isValid ? "Valid VIN format" : "Invalid VIN format - must be 17 characters, alphanumeric, no I, O, Q"
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("ValidateVIN", "Error validating VIN: {VIN}", ex, new { VIN = vin });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Check if VIN exists in the system
        /// </summary>
        [HttpGet("exists/{vin}")]
        public async Task<ActionResult<VinExistsResult>> CheckVINExists(string vin)
        {
            try
            {
                var exists = await _vehicleImportService.VINExistsAsync(vin);

                var result = new VinExistsResult
                {
                    VIN = vin,
                    Exists = exists
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("CheckVINExists", "Error checking if VIN exists: {VIN}", ex, new { VIN = vin });
                return StatusCode(500, "Internal server error");
            }
        }
    }

    // Request/Response models
    public class VehicleImportUpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public class VinValidationResult
    {
        public string VIN { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class VinExistsResult
    {
        public string VIN { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }
}
