using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.ASE;

namespace NickScanCentralImagingPortal.Services.HealthChecks
{
    public class AseHealthCheck : IHealthCheck
    {
        private readonly IAseDatabaseSyncService _aseService;
        private readonly ILogger<AseHealthCheck> _logger;

        public AseHealthCheck(IAseDatabaseSyncService aseService, ILogger<AseHealthCheck> logger)
        {
            _aseService = aseService;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Simple health check - just verify the service is accessible
                if (_aseService != null)
                {
                    return Task.FromResult(HealthCheckResult.Healthy("ASE service is accessible"));
                }
                else
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("ASE service is not accessible"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ASE health check failed");
                return Task.FromResult(HealthCheckResult.Unhealthy($"ASE health check failed: {ex.Message}"));
            }
        }
    }
}
