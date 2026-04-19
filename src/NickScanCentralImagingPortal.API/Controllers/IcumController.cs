using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "CustomsOfficer")]
    [ApiController]
    [Route("api/[controller]")]
    public class IcumController : ControllerBase
    {
        private readonly IIcumApiService _icumApiService;
        private readonly IIcumRepository _icumRepository;
        private readonly ILogger<IcumController> _logger;

        public IcumController(
            IIcumApiService icumApiService,
            IIcumRepository icumRepository,
            ILogger<IcumController> logger)
        {
            _icumApiService = icumApiService;
            _icumRepository = icumRepository;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var response = await _icumApiService.GetApiStatusAsync();
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS status");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("container/{containerNumber}")]
        public async Task<IActionResult> GetContainerData(string containerNumber)
        {
            try
            {
                var data = await _icumRepository.GetContainerDataAsync(containerNumber);
                if (data == null)
                {
                    return NotFound($"Container {containerNumber} not found");
                }
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting container data for {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("batch")]
        public async Task<IActionResult> GetBatchData([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
        {
            try
            {
                var data = await _icumRepository.GetBatchDataAsync(startDate, endDate);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch data from {StartDate} to {EndDate}", startDate, endDate);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("logs")]
        public async Task<IActionResult> GetBatchLogs()
        {
            try
            {
                var logs = await _icumRepository.GetBatchLogsAsync();
                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch logs");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("fetch")]
        public async Task<IActionResult> FetchData([FromBody] FetchDataRequest request)
        {
            try
            {
                var response = await _icumApiService.FetchBatchDataAsync(request.StartDate, request.EndDate);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching data from {StartDate} to {EndDate}", request.StartDate, request.EndDate);
                return StatusCode(500, "Internal server error");
            }
        }
    }

    public class FetchDataRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
