using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace NickScanCentralImagingPortal.API.Controllers
{
#if DEBUG
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class ServiceTestController : ControllerBase
#else
    // ServiceTestController disabled in Production
    [ApiController]
    [Route("api/[controller]")]
    public class ServiceTestController : ControllerBase
#endif
    {
        private readonly ILogger<ServiceTestController> _logger;
        private readonly IEnumerable<IHostedService> _hostedServices;

        public ServiceTestController(
            ILogger<ServiceTestController> logger,
            IEnumerable<IHostedService> hostedServices)
        {
            _logger = logger;
            _hostedServices = hostedServices;
        }

#if DEBUG
        [HttpGet("list-services")]
        public ActionResult<List<string>> ListHostedServices()
        {
            try
            {
                var services = _hostedServices.Select(s => s.GetType().Name).ToList();
                _logger.LogInformation("[SERVICE-TEST] Found {Count} hosted services", services.Count);
                return Ok(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SERVICE-TEST] Error listing services");
                return StatusCode(500, new { error = ex.Message });
            }
        }
#else
        // Service test endpoint disabled in Production
        [HttpGet("list-services")]
        public IActionResult ListHostedServices() => NotFound();
#endif
    }
}

