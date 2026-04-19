using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ImageProcessing;

namespace NickScanCentralImagingPortal.API.Controllers
{
#if DEBUG
    [Authorize(Policy = "AdminOnly")]
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
#else
    // TestController disabled in Production
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
#endif
    {
        private readonly ILogger<TestController> _logger;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly IICUMSIntegrationService _icumsService;

        public TestController(
            ILogger<TestController> logger,
            IImageProcessingService imageProcessingService,
            IICUMSIntegrationService icumsService)
        {
            _logger = logger;
            _imageProcessingService = imageProcessingService;
            _icumsService = icumsService;
        }

#if DEBUG
        /// <summary>
        /// Test image processing with quality enhancement
        /// </summary>
        [HttpGet("image/{containerNumber}/enhanced")]
        public async Task<IActionResult> GetEnhancedImage(string containerNumber)
        {
            try
            {
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber);

                if (result.Status != "Success")
                {
                    return NotFound($"Image not found for container: {containerNumber}");
                }

                // For now, return a placeholder since we don't have actual image data in the result
                return StatusCode(501, "Image data retrieval not yet implemented in this version");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing enhanced image for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }
#else
        [HttpGet("image/{containerNumber}/enhanced")]
        public IActionResult GetEnhancedImage(string containerNumber) => NotFound();
#endif

#if DEBUG
        /// <summary>
        /// Test ASE DLL integration specifically
        /// </summary>
        [HttpGet("ase/{containerNumber}")]
        public async Task<IActionResult> TestASEDllIntegration(string containerNumber)
        {
            try
            {
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber, ScannerType.ASE);

                if (result.Status != "Success")
                {
                    return NotFound($"ASE image not found for container: {containerNumber}");
                }

                var response = new
                {
                    ContainerNumber = containerNumber,
                    ScannerType = "ASE",
                    ProcessingStatus = result.Status,
                    ProcessingType = result.ProcessingType,
                    ProcessedAt = result.ProcessedAt,
                    ProcessingTime = result.ProcessingTime,
                    Result = result.Result,
                    AnalysisResults = result.AnalysisResults
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing ASE DLL integration for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Test ICUMS integration preparation
        /// </summary>
        [HttpGet("icums/{containerNumber}")]
        public async Task<IActionResult> TestICUMSIntegration(string containerNumber)
        {
            try
            {
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber);

                if (result.Status != "Success")
                {
                    return NotFound($"Image not found for container: {containerNumber}");
                }

                // For now, create placeholder data since we don't have actual image data
                var placeholderImageData = new byte[1024]; // Placeholder
                var placeholderMetadata = new NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata();
                var icumsData = await _icumsService.PrepareImageForICUMSAsync(containerNumber, placeholderImageData, placeholderMetadata);

                return Ok(new
                {
                    ContainerNumber = containerNumber,
                    ICUMSData = icumsData,
                    ValidationPassed = await _icumsService.ValidateICUMSDataAsync(icumsData)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing ICUMS integration for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Test image quality enhancement
        /// </summary>
        [HttpGet("quality/{containerNumber}")]
        public async Task<IActionResult> TestImageQuality(string containerNumber)
        {
            try
            {
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber);

                if (result.Status != "Success")
                {
                    return NotFound($"Image not found for container: {containerNumber}");
                }

                var qualityInfo = new
                {
                    ContainerNumber = containerNumber,
                    ProcessingStatus = result.Status,
                    ProcessingType = result.ProcessingType,
                    ProcessedAt = result.ProcessedAt,
                    ProcessingTime = result.ProcessingTime,
                    QualityScore = result.QualityScore,
                    AnalysisResults = result.AnalysisResults
                };

                return Ok(qualityInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing image quality for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Test all available containers
        /// </summary>
        [HttpGet("containers")]
        public Task<IActionResult> GetAvailableContainers()
        {
            try
            {
                // This would typically query the database for available containers
                var containers = new[]
                {
                    "TCKU7075316",
                    "MRSU3700452",
                    "ARKU8576170",
                    "CORU2437487",
                    "GCNU4713851",
                    "PIDU4318965",
                    "DFSU7132379",
                    "MSDU2337900",
                    "MSBU1315073",
                    "FANU3063499"
                };

                return Task.FromResult<IActionResult>(Ok(new
                {
                    AvailableContainers = containers,
                    TotalCount = containers.Length,
                    TestEndpoints = new[]
                    {
                        "/api/test/image/{containerNumber}/enhanced",
                        "/api/test/ase/{containerNumber}",
                        "/api/test/icums/{containerNumber}",
                        "/api/test/quality/{containerNumber}"
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available containers");
                return Task.FromResult<IActionResult>(StatusCode(500, "Internal server error"));
            }
        }
#else
        // All test endpoints disabled in Production
        [HttpGet("ase/{containerNumber}")]
        public IActionResult TestASEDllIntegration(string containerNumber) => NotFound();

        [HttpGet("icums/{containerNumber}")]
        public IActionResult TestICUMSIntegration(string containerNumber) => NotFound();

        [HttpGet("quality/{containerNumber}")]
        public IActionResult TestImageQuality(string containerNumber) => NotFound();

        [HttpGet("containers")]
        public IActionResult GetAvailableContainers() => NotFound();
#endif
    }
}
